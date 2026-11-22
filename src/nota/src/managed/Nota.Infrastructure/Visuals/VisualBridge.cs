// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Visual engine bridge (Syntetika Engine): Nota is the master clock.
//
// A loopback TCP line-delimited JSON channel carries transport state to the separate
// openVJ-based visual process (~120 Hz) and clip-launch commands stamped with the
// transport beat they must happen on. Commands use absolute beats, never "now": the
// visual engine schedules them against the same musical grid, so network jitter cannot
// move a visual off the beat.
//
// Protocol v1 (one JSON object per line):
//   Nota -> visual : {"t":"hello","v":1}
//                    {"t":"clock","beat":1.5,"bpm":120,"playing":true}
//                    {"t":"scene","col":3,"atBeat":4}
//                    {"t":"slot","layer":0,"col":3,"atBeat":4}
//                    {"t":"stopLayer","layer":0}
//                    {"t":"stopAll"}
//
// No audio ever crosses this channel; only musical time and commands.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Nota.Infrastructure.Visuals;

public sealed class VisualBridge : IDisposable
{
    /// <summary>Loopback port the visual engine connects to. 47810 is not used by Nota itself.</summary>
    public const int DefaultPort = 47810;
    private const int ClockIntervalMs = 8;   // ~125 Hz

    private readonly NotaEngine _engine;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    /// <summary>Lines the visual engine sent back (vizState, vizCatalog). Drained on the UI thread.</summary>
    private readonly ConcurrentQueue<string> _inbox = new();
    private long _seq;

    private TcpListener? _listener;
    /// <summary>Cross-process guard: only one running Nota may own the bridge port.</summary>
    private Mutex? _owner;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _acceptThread;
    private Thread? _clockThread;
    private volatile bool _running;
    private volatile bool _loggedFirstClient;
    /// <summary>Set when a client connects, so it gets the current layout immediately.</summary>
    private volatile bool _layoutDirty = true;
    private string _layoutSignature = "";
    /// <summary>Bumped per accepted client, so each new connection gets a layout even when the
    /// structure did not change (a send racing a disconnect must not lose the snapshot).</summary>
    private volatile int _clientEpoch;
    private int _sentEpoch = -1;
    /// <summary>Current .nota bundle, for the `visuals/layout.json` sidecar (null = unsaved).</summary>
    private string? _projectDir;

    public VisualBridge(NotaEngine engine) => _engine = engine;

    /// <summary>Set by the composition root so UI code can reach the running bridge.</summary>
    public static VisualBridge? Current { get; private set; }

    public bool Running => _running;
    public int Port { get; private set; }
    public bool Connected
    {
        get { lock (_writeGate) return _stream is not null; }
    }

    public void Start(int port = DefaultPort)
    {
        lock (_gate)
        {
            if (_running) return;
            // macOS lets a second listener bind the same port with SO_REUSEADDR (unlike Linux),
            // so a leftover dev instance silently stole openVJ's connection. A named mutex makes
            // ownership explicit across processes: the newest instance does not steal the bridge.
            _owner = new Mutex(initiallyOwned: true, name: "studio.syntetika.notaopenvj.visual-bridge", out bool createdNew);
            if (!createdNew)
            {
                _owner.Dispose();
                _owner = null;
                AppLog("visual bridge already owned by another Nota instance; not starting");
                return;
            }
            Port = port;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _running = true;
            Current = this;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "visual-bridge-accept" };
            _acceptThread.Start();
            _clockThread = new Thread(ClockLoop) { IsBackground = true, Name = "visual-bridge-clock" };
            _clockThread.Start();
            // Dev/self-test hook (like openVJ's OPENVJ_SNAPSHOT): launch two scenes on the
            // musical grid so the bridge can be verified without clicking the UI.
            if (Environment.GetEnvironmentVariable("NOTA_VISUAL_TEST") is { Length: > 0 })
                new Thread(TestLoop) { IsBackground = true, Name = "visual-bridge-test" }.Start();
            // Layout mirror self-test: add tracks, remove one, add a scene — openVJ must follow.
            if (Environment.GetEnvironmentVariable("NOTA_LAYOUT_TEST") is { Length: > 0 })
                new Thread(LayoutTestLoop) { IsBackground = true, Name = "visual-layout-test" }.Start();
            if (Environment.GetEnvironmentVariable("NOTA_VIZ_TEST") is { Length: > 0 })
                new Thread(VizTestLoop) { IsBackground = true, Name = "visual-viz-test" }.Start();
            if (Environment.GetEnvironmentVariable("NOTA_MEDIA_TEST") is { Length: > 0 })
                new Thread(MediaTestLoop) { IsBackground = true, Name = "visual-media-test" }.Start();
        }
    }

    public void Stop()
    {
        _running = false;
        Current = null;
        try { _listener?.Stop(); } catch { /* best effort */ }
        lock (_writeGate)
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            try { _client?.Dispose(); } catch { }
            _client = null;
        }
        try { _acceptThread?.Join(500); } catch { }
        try { _clockThread?.Join(500); } catch { }
        if (_owner is { } owner)
        {
            try { owner.ReleaseMutex(); } catch { }
            owner.Dispose();
            _owner = null;
        }
    }

    public void Dispose() => Stop();

    // ---- layout (UI thread, ~4 Hz) ------------------------------------------

    /// <summary>The open project bundle, or null.</summary>
    public string? ProjectDir => _projectDir;

    /// <summary>The open project bundle, or null. The layout sidecar lives in `visuals/`.</summary>
    public void SetProjectDir(string? dir)
    {
        _projectDir = dir;
        _layoutDirty = true;   // re-send (and re-write) for the new project
    }

    /// <summary>
    /// Mirrors NotA's structure to the visual engine: one layer per playable track (audio /
    /// instrument; group, return and master are skipped) and one column per scene, in NotA's
    /// top-to-bottom order. Sends a full snapshot only when something changed, so reconnects
    /// and undo/redo/load are covered without hooking every edit.
    /// </summary>
    public void PublishLayout()
    {
        if (!_running) return;
        try
        {
            var tracks = new List<object>();
            var scenes = new List<object>();
            var sig = new StringBuilder(512);
            for (int i = 0; i < _engine.TrackCount; i++)
            {
                if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
                if (ti.Type is not (0 or 1)) continue;          // audio + instrument only
                string name = _engine.GetTrackName(ti.Id) ?? "";
                string uid = Nota.Application.TrackUids.UidFor(ti.Id);
                sig.Append(ti.Id).Append(':').Append(uid).Append(':').Append(name).Append('|');
                tracks.Add(new { uid, name, kind = ti.Type, id = ti.Id });
            }
            int sceneCount = _engine.SceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                string name = _engine.GetSceneName(i) ?? "";
                sig.Append('s').Append(i).Append(':').Append(name).Append('|');
                scenes.Add(new { index = i, name });
            }

            string signature = sig.ToString();
            int epoch = _clientEpoch;
            if (!_layoutDirty && signature == _layoutSignature && epoch == _sentEpoch) return;
            _layoutDirty = false;
            _layoutSignature = signature;
            _sentEpoch = epoch;

            string json = JsonSerializer.Serialize(new { t = "layout", v = 1, tracks, scenes });
            AppLog($"layout sent: {tracks.Count} tracks / {scenes.Count} scenes");
            Send(json);
            WriteLayoutFile(json);
        }
        catch (ObjectDisposedException) { /* app closing */ }
    }

    private void WriteLayoutFile(string json)
    {
        if (_projectDir is not { Length: > 0 } dir) return;
        try
        {
            var visuals = Path.Combine(dir, "visuals");
            Directory.CreateDirectory(visuals);
            var target = Path.Combine(visuals, "layout.json");
            var tmp = target + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, target, overwrite: true);
        }
        catch (IOException) { /* a read-only bundle: the mirror still works in memory */ }
        catch (UnauthorizedAccessException) { }
    }

    // ---- commands (UI thread) ------------------------------------------------

    /// <summary>Launch a Session scene's visual column on the next launch quantum.</summary>
    public void Scene(int scene) => SendLaunch("scene", scene, atBeat: NextQuantumBeat());

    /// <summary>Mirror the selected clip: openVJ selects the same slot (Preview + Properties).</summary>
    public void Select(int layer, int scene)
        => Send($"{{\"t\":\"select\",\"layer\":{layer.ToString(CultureInfo.InvariantCulture)},\"col\":{scene.ToString(CultureInfo.InvariantCulture)}}}");

    /// <summary>Launch one track's visual clip (layer, column) on the next launch quantum.</summary>
    public void Slot(int layer, int scene) => SendLaunch("slot", scene, atBeat: NextQuantumBeat(), layer: layer);

    public void StopLayer(int layer)
        => Send($"{{\"t\":\"stopLayer\",\"layer\":{layer.ToString(CultureInfo.InvariantCulture)}}}");

    public void StopAll() => Send("{\"t\":\"stopAll\"}");

    /// <summary>
    /// The beat the engine will actually apply a launch on, matching SessionPlayer's
    /// ceil(blockStart / quant - eps) * quant. quant &lt;= 0 means immediately.
    /// </summary>
    public double NextQuantumBeat()
    {
        try
        {
            double quant = _engine.LaunchQuant;
            double now = _engine.PositionBeats;
            if (quant <= 1e-9) return Math.Round(now, 6);
            // Match SessionPlayer: a beat already on the boundary stays on it.
            double beat = Math.Ceiling(now / quant - 1e-9) * quant;
            return Math.Round(beat < 0 ? 0 : beat, 6);
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    private void SendLaunch(string kind, int col, double atBeat, int layer = 0)
    {
        string b = Num(atBeat);
        string c = col.ToString(CultureInfo.InvariantCulture);
        string json = kind == "scene"
            ? $"{{\"t\":\"scene\",\"col\":{c},\"atBeat\":{b}}}"
            : $"{{\"t\":\"slot\",\"layer\":{layer.ToString(CultureInfo.InvariantCulture)},\"col\":{c},\"atBeat\":{b}}}";
        Send(json);
    }

    private void TestLoop()
    {
        try
        {
            AppLog("test: armed (NOTA_VISUAL_TEST)");
            // Let the engine finish opening its audio device before driving the transport.
            Thread.Sleep(6000);
            if (!_engine.IsPlaying) _engine.Play();
            Thread.Sleep(4000);
            AppLog($"test: scene 0 at beat {NextQuantumBeat():0.###}");
            Scene(0);
            Thread.Sleep(6000);
            AppLog($"test: scene 1 at beat {NextQuantumBeat():0.###}");
            Scene(1);
            Thread.Sleep(8000);
            AppLog("test: stopAll");
            StopAll();
        }
        catch (Exception ex) { AppLog("test loop failed: " + ex.Message); }
    }

    private void MediaTestLoop()
    {
        try
        {
            Thread.Sleep(6000);
            string image = MediaFiles.Prepare("/Volumes/Macintosh HD/Syntetika Engine/assets/syntetika-icon.svg.png", null);
            AppLog($"media test: assign image at {image}");
            VizAssignMedia(0, 0, "image", image);
            Thread.Sleep(1500);
            VizGet(0, 0);
            Thread.Sleep(800);
            string bundle = "/tmp/syntetika-media-test";
            if (File.Exists("/tmp/syntetika-test.mp4"))
            {
                AppLog("media test: assign a video");
                string video = MediaFiles.Prepare("/tmp/syntetika-test.mp4", null);
                VizAssignMedia(0, 0, "video", video);
                Thread.Sleep(1800);
                VizGet(0, 0);
                Thread.Sleep(900);
            }

            AppLog("media test: save through the project path");
            SetProjectDir(bundle);
            SaveProject();
            Thread.Sleep(1500);
            AppLog("media test: clear then load the bundle");
            VizClear(0, 0);
            Thread.Sleep(600);
            VizLoad(bundle);
            Thread.Sleep(1200);
            VizGet(0, 0);
            Thread.Sleep(800);
            AppLog("media test: done");
        }
        catch (Exception ex) { AppLog("media test failed: " + ex.Message); }
    }

    private void VizTestLoop()
    {
        try
        {
            Thread.Sleep(6000);
            AppLog("viz test: assign plasma to 0:0");
            VizAssign(0, 0, "generator", "plasma");
            Thread.Sleep(1500);
            VizGet(0, 0);
            Thread.Sleep(600);
            AppLog("viz test: set p0 = 80%");
            VizSet(0, 0, "source", "p0", 80);
            Thread.Sleep(800);
            AppLog("viz test: add effect kaleido");
            VizFx(0, 0, "add", id: "kaleido");
            Thread.Sleep(800);
            AppLog("viz test: catalog generators 'plasma'");
            VizCatalog("generators", "plasma", DateTime.UtcNow.Ticks);
            Thread.Sleep(1200);
            AppLog("viz test: beat sync 4 beats, then change speed to 2x");
            VizPlayback(0, 0, null, 4, null, null, setSync: true);
            Thread.Sleep(500);
            VizPlayback(0, 0, 2.0, null, null, null);
            Thread.Sleep(900);
            AppLog("viz test: save to a temp file");
            VizSave("/tmp/nota-viz-test.json");
            Thread.Sleep(800);
            AppLog("viz test: clear, then load the saved file");
            VizClear(0, 0);
            Thread.Sleep(600);
            VizLoad("/tmp/nota-viz-test.json");
            Thread.Sleep(800);
            VizGet(0, 0);
            Thread.Sleep(800);
            AppLog("viz test: layer opacity 50%, blend Add, bypass");
            VizLayer(0, 0.5, "Add", true);
            Thread.Sleep(800);
            VizGet(0, 0);
            Thread.Sleep(1200);
            VizGet(0, 0);
            Thread.Sleep(1200);

            AppLog("viz test: assign an image (staged)");
            string image = MediaFiles.Prepare("/Volumes/Macintosh HD/Syntetika Engine/assets/syntetika-icon.svg.png", null);
            VizAssignMedia(0, 0, "image", image);
            Thread.Sleep(900);

            AppLog("viz test: camera catalog");
            VizCatalog("cameras", "", DateTime.UtcNow.Ticks);
            Thread.Sleep(700);
            AppLog("viz test: syphon catalog");
            VizCatalog("syphon", "", DateTime.UtcNow.Ticks);
            Thread.Sleep(700);

            AppLog("viz test: save media project to /tmp/syntetika-bundle-test");
            string bundle = "/tmp/syntetika-bundle-test";
            Directory.CreateDirectory(Path.Combine(bundle, "visuals", "media"));
            foreach (string f in Directory.GetFiles(NotaPaths.SubDir("media-staging")))
                File.Copy(f, Path.Combine(bundle, "visuals", "media", Path.GetFileName(f)), overwrite: true);
            VizSave(bundle);
            Thread.Sleep(1500);

            AppLog("viz test: done");
        }
        catch (Exception ex) { AppLog("viz test failed: " + ex.Message); }
    }

    private void LayoutTestLoop()
    {
        try
        {
            Thread.Sleep(5000);
            AppLog("layout test: add Volt track");
            _engine.AddVoltSynthTrack();
            Thread.Sleep(4000);
            AppLog("layout test: add audio track");
            _engine.AddAudioTrack();
            Thread.Sleep(4000);
            AppLog("layout test: add scene");
            _engine.AddScene();
            Thread.Sleep(4000);
            AppLog("layout test: move the last track to the top");
            if (_engine.TrackCount > 1 && _engine.TryGetTrackInfo(_engine.TrackCount - 1, out var lastTi))
                _engine.MoveTrack(lastTi.Id, 0);
            Thread.Sleep(4000);
            AppLog("layout test: remove the first added track");
            for (int i = 0; i < _engine.TrackCount; i++)
                if (_engine.TryGetTrackInfo(i, out var ti) && ti.Type == 1) { _engine.RemoveTrack(ti.Id); break; }
            Thread.Sleep(4000);
            AppLog($"layout test: done ({_engine.TrackCount} tracks, {_engine.SceneCount} scenes)");
            Thread.Sleep(2000);
            AppLog("layout test: remove every playable track");
            for (int i = _engine.TrackCount - 1; i >= 0; i--)
                if (_engine.TryGetTrackInfo(i, out var ti) && ti.Type is 0 or 1) _engine.RemoveTrack(ti.Id);
            Thread.Sleep(4000);
            AppLog("layout test: add one audio track back");
            _engine.AddAudioTrack();
            Thread.Sleep(4000);
            AppLog($"layout test: end ({_engine.TrackCount} tracks)");
        }
        catch (Exception ex) { AppLog("layout test failed: " + ex.Message); }
    }

    // ---- transport loop ------------------------------------------------------

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = _listener!.AcceptTcpClient();
            }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }

            client.NoDelay = true;
            AppLog($"client accepted ({client.Client.RemoteEndPoint})");
            lock (_writeGate)
            {
                try { _stream?.Dispose(); } catch { }
                try { _client?.Close(); } catch { }
                _client = client;
                _stream = client.GetStream();
            }
            _layoutDirty = true;
            _clientEpoch++;
            Send("{\"t\":\"hello\",\"v\":2}");
            var reader = new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "visual-bridge-read" };
            reader.Start();
            if (!_loggedFirstClient)
            {
                _loggedFirstClient = true;
                try { AppLog($"visual engine connected on 127.0.0.1:{Port}"); } catch { }
            }
        }
    }

    private void ReadLoop(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (_running && reader.ReadLine() is { } line)
            {
                if (line.Length > 0)
                {
                    LogIncoming(line);
                    _inbox.Enqueue(line);
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private void LogIncoming(string line)
    {
        if (!line.Contains("\"vizState\"", StringComparison.Ordinal) && !line.Contains("\"vizCatalog\"", StringComparison.Ordinal))
            return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var v = doc.RootElement;
            string t = v.TryGetProperty("t", out var te) ? te.GetString() ?? "" : "";
            int layer = v.TryGetProperty("layer", out var l) ? l.GetInt32() : -1;
            int col = v.TryGetProperty("col", out var c) ? c.GetInt32() : -1;
            long seq = v.TryGetProperty("seq", out var sq) ? sq.GetInt64() : -1;
            bool has = v.TryGetProperty("has_visual", out var h) && h.GetBoolean();
            string extra = "";
            if (v.TryGetProperty("layer_state", out var lo) && lo.ValueKind == JsonValueKind.Object)
                extra += $" opacity={lo.GetProperty("opacity").GetDouble():0.00} blend={lo.GetProperty("blend").GetString()}";
            if (v.TryGetProperty("playback", out var pl) && pl.ValueKind == JsonValueKind.Object)
                extra += $" speed={pl.GetProperty("speed").GetDouble():0.00} sync={(pl.TryGetProperty("sync_beats", out var sy) && sy.ValueKind == JsonValueKind.Number ? sy.GetDouble().ToString("0.##") : "off")}";
            AppLog($"[viz<-] {t} L{layer} C{col} seq={seq} visual={has}{extra} ({line.Length} bytes)");
        }
        catch (JsonException)
        {
            AppLog("[viz<-] unparseable: " + (line.Length > 120 ? line[..120] : line));
        }
    }

    /// <summary>One line from the visual engine, or null. UI thread only.</summary>
    public string? TakeMessage() => _inbox.TryDequeue(out var line) ? line : null;

    private long NextSeq() => Interlocked.Increment(ref _seq);

    public void VizGet(int layer, int scene)
        => Send($"{{\"t\":\"vizGet\",\"layer\":{layer},\"col\":{scene},\"seq\":{NextSeq()}}}");

    public void VizSet(int layer, int scene, string group, string key, double value)
        => Send($"{{\"t\":\"vizSet\",\"layer\":{layer},\"col\":{scene},\"group\":{JsonSerializer.Serialize(group)},\"key\":{JsonSerializer.Serialize(key)},\"value\":{Num(value)}}}");

    /// <summary>Changes only the named playback fields. Beat sync is cleared only when
    /// <paramref name="setSync"/> is true and <paramref name="syncBeats"/> is null — otherwise a
    /// Speed or Mode edit would silently switch sync off.</summary>
    public void VizPlayback(int layer, int scene, double? speed, double? syncBeats, string? playMode, string? trigger,
        bool setSync = false)
    {
        var sb = new StringBuilder();
        sb.Append("{\"t\":\"vizPlayback\",\"layer\":").Append(layer).Append(",\"col\":").Append(scene);
        if (speed is { } sp) sb.Append(",\"speed\":").Append(Num(sp));
        if (setSync) sb.Append(",\"syncBeats\":").Append(syncBeats is { } sy ? Num(sy) : "null");
        if (playMode is not null) sb.Append(",\"playMode\":").Append(JsonSerializer.Serialize(playMode));
        if (trigger is not null) sb.Append(",\"trigger\":").Append(JsonSerializer.Serialize(trigger));
        sb.Append('}');
        Send(sb.ToString());
    }

    public void VizFx(int layer, int scene, string op, int index = 0, int to = 0, string? id = null, double value = 0)
    {
        var sb = new StringBuilder();
        sb.Append("{\"t\":\"vizFx\",\"layer\":").Append(layer).Append(",\"col\":").Append(scene)
          .Append(",\"op\":").Append(JsonSerializer.Serialize(op)).Append(",\"index\":").Append(index);
        if (to > 0) sb.Append(",\"to\":").Append(to);
        if (id is not null) sb.Append(",\"id\":").Append(JsonSerializer.Serialize(id));
        if (value != 0) sb.Append(",\"value\":").Append(Num(value));
        sb.Append('}');
        Send(sb.ToString());
    }

    public void VizAssign(int layer, int scene, string kind, string id)
        => Send($"{{\"t\":\"vizAssign\",\"layer\":{layer},\"col\":{scene},\"kind\":{JsonSerializer.Serialize(kind)},\"id\":{JsonSerializer.Serialize(id)}}}");

    /// <summary>Assigns a video/image already prepared by <see cref="MediaFiles"/>.</summary>
    public void VizAssignMedia(int layer, int scene, string kind, string path)
        => Send($"{{\"t\":\"vizAssign\",\"layer\":{layer},\"col\":{scene},\"kind\":{JsonSerializer.Serialize(kind)},\"id\":\"\",\"path\":{JsonSerializer.Serialize(path)}}}");

    /// <summary>Assigns a camera or Syphon server by its openVJ name/id.</summary>
    public void VizAssignLive(int layer, int scene, string kind, string id, string app, string server)
        => Send($"{{\"t\":\"vizAssign\",\"layer\":{layer},\"col\":{scene},\"kind\":{JsonSerializer.Serialize(kind)},\"id\":{JsonSerializer.Serialize(id)},\"app\":{JsonSerializer.Serialize(app)},\"server\":{JsonSerializer.Serialize(server)}}}");

    public void VizLayer(int layer, double? opacity, string? blend, bool? bypass)
    {
        var sb = new StringBuilder();
        sb.Append("{\"t\":\"vizLayer\",\"layer\":").Append(layer);
        if (opacity is { } o) sb.Append(",\"opacity\":").Append(Num(o));
        if (blend is not null) sb.Append(",\"blend\":").Append(JsonSerializer.Serialize(blend));
        if (bypass is { } b) sb.Append(",\"bypass\":").Append(b ? "true" : "false");
        sb.Append('}');
        Send(sb.ToString());
    }

    /// <summary>Playable track uids, bottom of the visual stack first (openVJ's layer order).</summary>
    public List<string> CurrentLayoutUids()
    {
        var uids = new List<string>();
        try
        {
            for (int i = 0; i < _engine.TrackCount; i++)
                if (_engine.TryGetTrackInfo(i, out var ti) && ti.Type is 0 or 1)
                    uids.Add(Nota.Application.TrackUids.UidFor(ti.Id));
        }
        catch (ObjectDisposedException) { }
        uids.Reverse();
        return uids;
    }

    /// <summary>Saves the composition into a bundle; media paths become `media/<file>`.</summary>
    public void VizSave(string bundleDir)
    {
        string staging = NotaPaths.SubDir("media-staging");
        string bundleMedia = Path.Combine(bundleDir, "visuals", "media");
        // Staged media becomes part of the bundle: the saved composition refers to
        // `media/<file>` and must find the file there.
        try
        {
            Directory.CreateDirectory(bundleMedia);
            foreach (string file in Directory.GetFiles(staging))
            {
                string target = Path.Combine(bundleMedia, Path.GetFileName(file));
                if (!File.Exists(target)) File.Copy(file, target);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var dirs = new List<string> { staging };
        dirs.Add(bundleMedia);
        Send(JsonSerializer.Serialize(new
        {
            t = "vizSave",
            path = Path.Combine(bundleDir, "visuals", "composition.json"),
            uids = CurrentLayoutUids(),
            mediaDirs = dirs,
        }));
    }

    /// <summary>Loads a bundle's composition; `media/<file>` resolves inside the bundle.</summary>
    public void VizLoad(string bundleDir) => Send(JsonSerializer.Serialize(new
    {
        t = "vizLoad",
        path = Path.Combine(bundleDir, "visuals", "composition.json"),
        mediaDir = Path.Combine(bundleDir, "visuals", "media"),
    }));

    /// <summary>Saves the current bundle's visuals (called on Save and on quit).</summary>
    public void SaveProject()
    {
        if (_projectDir is { Length: > 0 } dir) VizSave(dir);
    }

    public void VizClear(int layer, int scene)
        => Send($"{{\"t\":\"vizClear\",\"layer\":{layer},\"col\":{scene}}}");

    public void VizCatalog(string kind, string query, long tag)
        => Send($"{{\"t\":\"vizCatalog\",\"kind\":{JsonSerializer.Serialize(kind)},\"query\":{JsonSerializer.Serialize(query)},\"seq\":{tag}}}");

    private void ClockLoop()
    {
        while (_running)
        {
            if (Connected)
            {
                try
                {
                    double beat = _engine.PositionBeats;
                    double bpm = _engine.Bpm;
                    bool playing = _engine.IsPlaying;
                    Send("{\"t\":\"clock\",\"beat\":" + Num(beat) +
                         ",\"bpm\":" + Num(bpm) +
                         ",\"playing\":" + (playing ? "true" : "false") + "}");
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (IOException)
                {
                    DropClient();
                }
                catch (SocketException)
                {
                    DropClient();
                }
            }
            Thread.Sleep(ClockIntervalMs);
        }
    }

    private static string Num(double v) => v.ToString("0.#########", CultureInfo.InvariantCulture);

    private void Send(string line)
    {
        lock (_writeGate)
        {
            if (_stream is not { } s) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                s.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                AppLog($"send failed: {ex.GetType().Name}: {ex.Message}");
                DropClient();
            }
        }
    }

    private void DropClient()
    {
        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }

    private static void AppLog(string text) => Console.Error.WriteLine("[visual-bridge] " + text);
}
