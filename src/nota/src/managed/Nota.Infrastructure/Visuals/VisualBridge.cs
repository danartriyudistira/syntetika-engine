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
//                    {"t":"vizOutput","show":true}
//                    {"t":"vizUi","show":true}
//                    {"t":"vizMapping","show":true}
//                    {"t":"vizPerf","mode":2}
//                    {"t":"vizSlotTiming","layer":0,"col":0,"beats":8,"bpm":120,"loop":true,"start":10}
//                    {"t":"vizSlotPhase","layer":0,"col":0,"phase":3.5,"beat":12.25}
//                    {"t":"vizCopy","fromLayer":0,"fromCol":0,"toLayer":0,"toCol":1,"move":false}
//                    {"t":"vizColumnInsert","index":2}
//   visual -> Nota : {"t":"vizOut","open":true}
//                    {"t":"vizUiState","open":true}
//                    {"t":"vizMappingState","open":true}
//                    {"t":"launchSlot","layer":0,"col":3}
//                    {"t":"vizMediaAudio","layer":0,"col":3,"path":"/path/clip.mp4"}
//                    {"t":"vizOffset","ms":100}
//                    {"t":"vizCalibrate","on":true}
//                    {"t":"vizUndo"}
//                    {"t":"vizRedo"}
//   visual -> Nota : {"t":"vizHistory","undo":1,"redo":0}
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
    /// <summary>Raised on a bridge thread when openVJ asks Nota to launch a Session slot.</summary>
    public event Action<int, int>? SlotLaunchRequested;

    /// <summary>Raised on a bridge thread when openVJ dropped a video that Nota should pair
    /// with the same Session slot (layer, scene, media path).</summary>
    public event Action<int, int, string>? SlotAudioRequested;

    /// <summary>Raised on the UI thread after a layout snapshot was sent.</summary>
    public event Action? LayoutChanged;

    private volatile bool _canUndo;
    private volatile bool _canRedo;

    /// <summary>Whether the visual engine has a visual edit to undo/redo.</summary>
    public bool CanUndo => _canUndo;
    public bool CanRedo => _canRedo;

    /// <summary>Loopback port the visual engine connects to. 47810 is not used by Nota itself.</summary>
    public const int DefaultPort = 47810;
    private const int ClockIntervalMs = 8;   // ~125 Hz

    private readonly NotaEngine _engine;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    /// <summary>Lines the visual engine sent back (vizState, vizCatalog). Drained on the UI thread.</summary>
    private readonly ConcurrentQueue<string> _inbox = new();
    private readonly ManualResetEventSlim _saved = new(false);
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
    /// <summary>Selected Session slot, kept as a track UID so a layer reorder can resend it.</summary>
    private string? _selectedUid;
    private int _selectedScene = -1;
    /// <summary>Current .nota bundle, for the `visuals/layout.json` sidecar (null = unsaved).</summary>
    private string? _projectDir;
    /// <summary>Last audio slot length/loop/tempo/trim sent per (track, scene), so only edits travel.</summary>
    private readonly Dictionary<(int TrackId, int Scene), (double Beats, double Bpm, bool Loop, double Start)> _slotTiming = new();
    /// <summary>A new engine connected: forget what was sent so every slot syncs again.</summary>
    private volatile bool _slotTimingDirty;
    /// <summary>A new engine connected while audio may be playing: launch its visuals again.</summary>
    private volatile bool _resumePending;
    /// <summary>Last playing scene sent per track, so follow-action changes launch visuals.</summary>
    private readonly Dictionary<int, int> _playingScene = new();
    /// <summary>Scene a UI launch already scheduled per layer (scene + tick), so the sync does
    /// not retrigger it; stale entries expire so a cancelled launch can't mute a later one.</summary>
    private readonly Dictionary<int, (int Scene, long At)> _expectedLayerScene = new();
    /// <summary>Last visual performance profile (0 Eco, 1 Balanced, 2 Quality); replayed on connect.</summary>
    private volatile int _perfMode = 2;
    /// <summary>A/V offset in ms (+ delays the picture); replays on connect for the UI.</summary>
    private volatile int _avOffsetMs;
    private long _slotTimingAt;
    /// <summary>openVJ reported that its output window is on screen (`vizOut`).</summary>
    private volatile bool _outputOpen;
    /// <summary>Last show/hide Nota asked for; replayed when the visual engine reconnects.</summary>
    private volatile bool _outputWanted;
    /// <summary>openVJ reported that its editor window is on screen (`vizUiState`).</summary>
    private volatile bool _uiOpen;
    /// <summary>Last editor show/hide Nota asked for; replayed on reconnect.</summary>
    private volatile bool _uiWanted;
    /// <summary>openVJ reported that its projection-mapping window is on screen (`vizMappingState`).</summary>
    private volatile bool _mappingOpen;
    /// <summary>Last mapping show/hide Nota asked for; replayed on reconnect.</summary>
    private volatile bool _mappingWanted;

    /// <summary>Whether the visual engine's output window is open, as it last reported.</summary>
    public bool OutputOpen => _outputOpen;

    /// <summary>Whether openVJ's editor window is open, as it last reported.</summary>
    public bool UiOpen => _uiOpen;

    /// <summary>Whether openVJ's projection-mapping window is open, as it last reported.</summary>
    public bool MappingOpen => _mappingOpen;

    /// <summary>Raised on a bridge thread when the output window opens or closes.</summary>
    public event Action<bool>? OutputOpenChanged;

    /// <summary>Raised on a bridge thread when openVJ's editor window opens or closes.</summary>
    public event Action<bool>? UiOpenChanged;

    /// <summary>Raised on a bridge thread when the mapping window opens or closes.</summary>
    public event Action<bool>? MappingOpenChanged;

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
            // A non-default test port gets its own mutex so a dev instance can run beside the app.
            string ownerName = port == DefaultPort
                ? "studio.syntetika.notaopenvj.visual-bridge"
                : $"studio.syntetika.notaopenvj.visual-bridge-{port}";
            _owner = new Mutex(initiallyOwned: true, name: ownerName, out bool createdNew);
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
            // A layer can move while the selected track stays put: resend it with the new layer.
            SendSelection();
            try { LayoutChanged?.Invoke(); }
            catch (Exception ex) { AppLog("layout handler failed: " + ex.Message); }
        }
        catch (ObjectDisposedException) { /* app closing */ }
    }

    /// <summary>Mirror the selected slot, remembering the track by UID for layout changes.</summary>
    public void Select(int trackId, int scene)
    {
        _selectedUid = Nota.Application.TrackUids.UidFor(trackId);
        _selectedScene = scene;
        SendSelection();
    }

    private void SendSelection()
    {
        if (_selectedUid is not { Length: > 0 } uid || _selectedScene < 0) return;
        int layer = CurrentLayoutUids().IndexOf(uid);
        if (layer < 0) return;
        Send($"{{\"t\":\"select\",\"layer\":{layer.ToString(CultureInfo.InvariantCulture)},\"col\":{_selectedScene.ToString(CultureInfo.InvariantCulture)}}}");
    }

    /// <summary>Re-sends every slot on the next poll (after a project load or a new media assignment).</summary>
    public void ResendSlotTiming()
    {
        _slotTimingDirty = true;
        _slotTimingAt = 0;
    }

    /// <summary>While an audio slot plays, its local beat position, so the video's phase (start
    /// and loop seam) follows the audio even if a launch landed a few milliseconds off.</summary>
    public void PublishSlotPhase()
    {
        if (!Connected) return;
        try
        {
            var uids = CurrentLayoutUids();
            var layerOf = new Dictionary<string, int>(uids.Count, StringComparer.Ordinal);
            for (int i = 0; i < uids.Count; i++) layerOf[uids[i]] = i;

            for (int i = 0; i < _engine.TrackCount; i++)
            {
                if (!_engine.TryGetTrackInfo(i, out var ti) || ti.Type != 0) continue;
                if (!layerOf.TryGetValue(Nota.Application.TrackUids.UidFor(ti.Id), out int layer)) continue;
                int playing = -1;
                for (int scene = 0; scene < _engine.SceneCount; scene++)
                    if (_engine.SessionSlotState(ti.Id, scene) == 3) { playing = scene; break; }
                if (playing < 0) continue;
                double len = _engine.SessionSlotLength(ti.Id, playing);
                if (len <= 0) continue;
                double pos = _engine.SessionSlotPosition(ti.Id);
                double phase = pos - Math.Floor(pos / len) * len;
                phase = (phase - OffsetBeats) % len;
                if (phase < 0) phase += len;
                Send($"{{\"t\":\"vizSlotPhase\",\"layer\":{layer},\"col\":{playing},\"phase\":{Num(phase)},\"beat\":{Num(_engine.PositionBeats)}}}");
            }
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>After a (re)connect, launches the visual of every Session slot whose audio is
    /// already playing. Without this a restarted engine stays blank until the next launch.</summary>
    public void ResumePlayingVisuals()
    {
        if (!_resumePending || !Connected) return;
        _resumePending = false;
        _playingScene.Clear();
        _expectedLayerScene.Clear();
        SyncPlayingVisuals(force: true);
    }

    /// <summary>
    /// Mirrors the audio engine's playing scene to the visual layers. UI launches send their own
    /// `slot` messages (recorded in <see cref="_expectedLayerScene"/>); this catches every other
    /// way a scene starts — Follow Actions, controller launch, undo — within one UI poll.
    /// </summary>
    public void SyncPlayingVisuals(bool force = false)
    {
        if (!Connected) return;
        try
        {
            var uids = CurrentLayoutUids();
            var layerOf = new Dictionary<string, int>(uids.Count, StringComparer.Ordinal);
            for (int i = 0; i < uids.Count; i++) layerOf[uids[i]] = i;
            double at = Math.Round(_engine.PositionBeats, 6);
            for (int i = 0; i < _engine.TrackCount; i++)
            {
                if (!_engine.TryGetTrackInfo(i, out var ti) || ti.Type is not (0 or 1)) continue;
                if (!layerOf.TryGetValue(Nota.Application.TrackUids.UidFor(ti.Id), out int layer)) continue;
                int playing = -1;
                for (int scene = 0; scene < _engine.SceneCount; scene++)
                    if (_engine.SessionSlotState(ti.Id, scene) == 3) { playing = scene; break; }
                bool known = _playingScene.TryGetValue(ti.Id, out int prev);
                if (playing >= 0)
                {
                    bool expected = false;
                    if (_expectedLayerScene.TryGetValue(layer, out var ex))
                    {
                        expected = ex.Scene == playing && Environment.TickCount64 - ex.At < 5000;
                        if (expected || Environment.TickCount64 - ex.At >= 5000) _expectedLayerScene.Remove(layer);
                    }
                    if (!expected && (force || !known || prev != playing))
                    {
                        SendLaunch("slot", playing, atBeat: at, layer);
                        AppLog($"[playing] slot {ti.Id}:{playing} layer={layer}");
                    }
                    _playingScene[ti.Id] = playing;
                }
                else
                {
                    if (known && prev >= 0)
                    {
                        StopLayer(layer, atBeat: at);
                        AppLog($"[playing] stop {ti.Id} layer={layer}");
                    }
                    _playingScene.Remove(ti.Id);
                }
            }
        }
        catch (ObjectDisposedException) { _resumePending = true; }
    }

    /// <summary>Audio loop length and loop state of every non-empty audio slot, so a video's
    /// beat sync follows the clip's trim. Only changed slots travel; force after an edit.</summary>
    public void PublishSlotTiming(bool force = false)
    {
        if (!Connected) return;
        long now = Environment.TickCount64;
        if (!force && now - _slotTimingAt < 500) return;
        _slotTimingAt = now;
        if (_slotTimingDirty)
        {
            _slotTiming.Clear();
            _slotTimingDirty = false;
        }
        try
        {
            var uids = CurrentLayoutUids();
            var layerOf = new Dictionary<string, int>(uids.Count, StringComparer.Ordinal);
            for (int i = 0; i < uids.Count; i++) layerOf[uids[i]] = i;

            var seen = new HashSet<(int, int)>();
            double bpm = _engine.Bpm;
            for (int i = 0; i < _engine.TrackCount; i++)
            {
                if (!_engine.TryGetTrackInfo(i, out var ti) || ti.Type != 0) continue;
                if (!layerOf.TryGetValue(Nota.Application.TrackUids.UidFor(ti.Id), out int layer)) continue;
                for (int scene = 0; scene < _engine.SceneCount; scene++)
                {
                    double beats = _engine.SessionSlotLength(ti.Id, scene);
                    if (beats <= 0) continue;
                    bool loop = !_engine.TryGetSessionClipProps(ti.Id, scene, out var props) || props.Loop != 0;
                    // A trimmed audio clip starts inside its source: the video must start there too.
                    double start = 0;
                    if (_engine.TryGetSessionAudioSlot(ti.Id, scene, out var audioSlot)
                        && _engine.TryGetSampleInfo(audioSlot.SampleId, out var sampleInfo)
                        && sampleInfo.SampleRate > 0)
                        start = Math.Max(0, audioSlot.SourceOffsetFrames / sampleInfo.SampleRate);
                    var key = (ti.Id, scene);
                    seen.Add(key);
                    if (_slotTiming.TryGetValue(key, out var prev)
                        && Math.Abs(prev.Beats - beats) < 1e-6
                        && Math.Abs(prev.Bpm - bpm) < 0.05
                        && Math.Abs(prev.Start - start) < 1e-4
                        && prev.Loop == loop) continue;
                    _slotTiming[key] = (beats, bpm, loop, start);
                    Send($"{{\"t\":\"vizSlotTiming\",\"layer\":{layer},\"col\":{scene},\"beats\":{Num(beats)},\"bpm\":{Num(bpm)},\"loop\":{(loop ? "true" : "false")},\"start\":{Num(start)}}}");
                    AppLog($"[sync] slot {ti.Id}:{scene} layer={layer} beats={beats:0.###} bpm={bpm:0.##} loop={loop} start={start:0.###}s");
                }
            }
            // Forget cleared slots, so a later clip in the same place is sent again.
            var stale = new List<(int, int)>();
            foreach (var key in _slotTiming.Keys)
                if (!seen.Contains(key)) stale.Add(key);
            foreach (var key in stale) _slotTiming.Remove(key);
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>True while the open bundle still exists as a project. Once the user deletes
    /// or moves it, a later visual write must not recreate the folder as a visual-only shell.</summary>
    private static bool BundlePresent(string dir)
        => File.Exists(Path.Combine(dir, ProjectService.ManifestName));

    private void WriteLayoutFile(string json)
    {
        if (_projectDir is not { Length: > 0 } dir || !BundlePresent(dir)) return;
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
    public void Scene(int scene)
    {
        try
        {
            for (int layer = 0; layer < CurrentLayoutUids().Count; layer++)
                _expectedLayerScene[layer] = (scene, Environment.TickCount64);
        }
        catch (ObjectDisposedException) { }
        SendLaunch("scene", scene, atBeat: NextQuantumBeat());
    }

    /// <summary>Mirror the selected clip: openVJ selects the same slot (Preview + Properties).</summary>
    /// <summary>Launch one track's visual clip (layer, column) on the next launch quantum.</summary>
    public void Slot(int layer, int scene, double? atBeat = null)
    {
        _expectedLayerScene[layer] = (scene, Environment.TickCount64);
        SendLaunch("slot", scene, atBeat ?? NextQuantumBeat(), layer);
    }

    public void StopLayer(int layer, double? atBeat = null)
    {
        _expectedLayerScene.Remove(layer);
        Send(atBeat is { } b
            ? $"{{\"t\":\"stopLayer\",\"layer\":{layer},\"atBeat\":{Num(VisualBeat(b))}}}"
            : $"{{\"t\":\"stopLayer\",\"layer\":{layer.ToString(CultureInfo.InvariantCulture)}}}");
    }

    public void StopAll(double? atBeat = null)
    {
        _expectedLayerScene.Clear();
        Send(atBeat is { } b ? $"{{\"t\":\"stopAll\",\"atBeat\":{Num(VisualBeat(b))}}}" : "{\"t\":\"stopAll\"}");
    }

    /// <summary>The A/V offset in beats at the current tempo (+ delays the picture).</summary>
    private double OffsetBeats
    {
        get
        {
            try { return _avOffsetMs / 1000.0 * _engine.Bpm / 60.0; }
            catch (ObjectDisposedException) { return 0; }
        }
    }

    /// <summary>An audio beat shifted to when the picture should show it.</summary>
    private double VisualBeat(double beat) => beat + OffsetBeats;

    /// <summary>
    /// A/V offset in milliseconds, -200..+200. Positive delays the picture relative to the
    /// sound; the visual launches and the video phase are shifted by it.
    /// </summary>
    public void VizOffset(int milliseconds)
    {
        _avOffsetMs = Math.Clamp(milliseconds, -200, 200);
        Send($"{{\"t\":\"vizOffset\",\"ms\":{_avOffsetMs.ToString(CultureInfo.InvariantCulture)}}}");
    }

    /// <summary>Calibration strobe on openVJ's output (metronome click stays in Nota).</summary>
    public void VizCalibrate(bool on)
        => Send($"{{\"t\":\"vizCalibrate\",\"on\":{(on ? "true" : "false")}}}");

    /// <summary>
    /// The beat the engine will actually apply a launch on, matching SessionPlayer's
    /// ceil(blockStart / quant - eps) * quant. quant &lt;= 0 means immediately.
    /// </summary>
    public double NextQuantumBeat()
    {
        try { return NextQuantumBeat(_engine.LaunchQuant); }
        catch (ObjectDisposedException) { return 0; }
    }

    /// <summary>The launch boundary of one Session clip: its Quantize overrides the global.</summary>
    public double NextQuantumBeat(int trackId, int scene)
    {
        try
        {
            double quant = _engine.LaunchQuant;
            if (_engine.TryGetSessionClipProps(trackId, scene, out var p) && p.QuantBeats >= 0)
                quant = p.QuantBeats;
            return NextQuantumBeat(quant);
        }
        catch (ObjectDisposedException) { return 0; }
    }

    private double NextQuantumBeat(double quant)
    {
        try
        {
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
        string b = Num(VisualBeat(atBeat));
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
            Directory.CreateDirectory(bundle);
            // SaveProject only writes into a real bundle (a missing manifest means the project
            // was deleted); give the test harness one so the media path still exercises a save.
            File.WriteAllText(Path.Combine(bundle, "project.json"), "{}");
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
            AppLog($"media test: save ack={WaitForSave(1500)}");
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
            _slotTimingDirty = true;
            _resumePending = true;
            _canUndo = false;
            _canRedo = false;
            Send("{\"t\":\"hello\",\"v\":2}");
            Send($"{{\"t\":\"vizPerf\",\"mode\":{_perfMode}}}");
            Send($"{{\"t\":\"vizOffset\",\"ms\":{_avOffsetMs.ToString(CultureInfo.InvariantCulture)}}}");
            // A fresh engine starts with its output window hidden. Restore the state the user
            // asked for, so "View ▸ Visual Output" survives an engine restart.
            SetOutputOpen(false);
            // A restarted visual engine starts with an empty/demo composition. Reload the saved
            // visuals for the open project before layout/timing/resume reach it.
            if (_projectDir is { Length: > 0 } dir) VizLoad(dir);
            if (_outputWanted) Send("{\"t\":\"vizOutput\",\"show\":true}");
            if (_uiWanted) Send("{\"t\":\"vizUi\",\"show\":true}");
            if (_mappingWanted) Send("{\"t\":\"vizMapping\",\"show\":true}");
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
                    if (HandleControlMessage(line)) continue;
                    if (line.Contains("\"vizSaved\"", StringComparison.Ordinal)) _saved.Set();
                    LogIncoming(line);
                    // Only the OpenVJ tab's view reads the inbox, and it handles just these two
                    // shapes. Queuing per-track phase messages while that tab is closed used to
                    // grow the queue for as long as audio played.
                    if (!line.Contains("\"vizState\"", StringComparison.Ordinal)
                        && !line.Contains("\"vizCatalog\"", StringComparison.Ordinal)) continue;
                    if (_inbox.Count > 512) _inbox.TryDequeue(out _);
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
                extra += $" speed={pl.GetProperty("speed").GetDouble():0.00} sync={(pl.TryGetProperty("sync_beats", out var sy) && sy.ValueKind == JsonValueKind.Number ? sy.GetDouble().ToString("0.##") : "off")}"
                       + $" trim={(pl.TryGetProperty("sync_secs", out var ss) && ss.ValueKind == JsonValueKind.Number ? ss.GetDouble().ToString("0.##") + "s" : "full")}"
                       + $" in={(pl.TryGetProperty("sync_start", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetDouble().ToString("0.##") + "s" : "0s")}"
                       + $" mode={(pl.TryGetProperty("play_mode", out var pm) ? pm.GetString() : "?")}";
            AppLog($"[viz<-] {t} L{layer} C{col} seq={seq} visual={has}{extra} ({line.Length} bytes)");
        }
        catch (JsonException)
        {
            AppLog("[viz<-] unparseable: " + (line.Length > 120 ? line[..120] : line));
        }
    }

    /// <summary>One line from the visual engine, or null. UI thread only.</summary>
    public string? TakeMessage() => _inbox.TryDequeue(out var line) ? line : null;

    /// <summary>Shows or hides openVJ's output window; replayed if the engine reconnects.</summary>
    public void VizOutput(bool show)
    {
        _outputWanted = show;
        Send($"{{\"t\":\"vizOutput\",\"show\":{(show ? "true" : "false")}}}");
    }

    /// <summary>Shows or hides openVJ's editor window; replayed if the engine reconnects.</summary>
    public void VizUi(bool show)
    {
        _uiWanted = show;
        Send($"{{\"t\":\"vizUi\",\"show\":{(show ? "true" : "false")}}}");
    }

    /// <summary>Shows or hides openVJ's projection-mapping window; replayed on reconnect.</summary>
    public void VizMapping(bool show)
    {
        _mappingWanted = show;
        Send($"{{\"t\":\"vizMapping\",\"show\":{(show ? "true" : "false")}}}");
    }

    /// <summary>openVJ performance profile: 0 Eco 30, 1 Balanced 45, 2 Quality 60.</summary>
    public void VizPerf(int mode)
    {
        _perfMode = Math.Clamp(mode, 0, 2);
        Send($"{{\"t\":\"vizPerf\",\"mode\":{_perfMode.ToString(CultureInfo.InvariantCulture)}}}");
    }

    /// <summary>Undo or redo the last visual edit (the engine owns the history).</summary>
    public void VisualUndo() => Send("{\"t\":\"vizUndo\"}");
    public void VisualRedo() => Send("{\"t\":\"vizRedo\"}");

    /// <summary>Copy or move a slot's visual with its Session clip (copy/paste, duplicate, drag).</summary>
    public void VizCopy(int fromLayer, int fromCol, int toLayer, int toCol, bool move)
        => Send($"{{\"t\":\"vizCopy\",\"fromLayer\":{fromLayer.ToString(CultureInfo.InvariantCulture)},\"fromCol\":{fromCol.ToString(CultureInfo.InvariantCulture)},\"toLayer\":{toLayer.ToString(CultureInfo.InvariantCulture)},\"toCol\":{toCol.ToString(CultureInfo.InvariantCulture)},\"move\":{(move ? "true" : "false")}}}");

    /// <summary>Scene structure: keep visual columns aligned with Nota's scenes.</summary>
    public void InsertColumn(int index)
        => Send($"{{\"t\":\"vizColumnInsert\",\"index\":{index.ToString(CultureInfo.InvariantCulture)}}}");

    public void RemoveColumn(int index)
        => Send($"{{\"t\":\"vizColumnRemove\",\"index\":{index.ToString(CultureInfo.InvariantCulture)}}}");

    public void MoveColumn(int from, int to)
        => Send($"{{\"t\":\"vizColumnMove\",\"from\":{from.ToString(CultureInfo.InvariantCulture)},\"to\":{to.ToString(CultureInfo.InvariantCulture)}}}");

    /// <summary>Handles messages about the engine itself; true when consumed here.</summary>
    private bool HandleControlMessage(string line)
    {
        // openVJ's own clip click: launch the matching Session slot (audio + visual) in Nota.
        if (line.Contains("\"launchSlot\"", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var v = doc.RootElement;
                if (!v.TryGetProperty("t", out var t) || t.GetString() != "launchSlot") return false;
                int layer = v.TryGetProperty("layer", out var l) ? l.GetInt32() : -1;
                int scene = v.TryGetProperty("col", out var c) ? c.GetInt32() : -1;
                if (layer < 0 || scene < 0) return true;
                AppLog($"[viz<-] launchSlot L{layer} C{scene}");
                try { SlotLaunchRequested?.Invoke(layer, scene); }
                catch (Exception ex) { AppLog("launch request handler failed: " + ex.Message); }
                return true;
            }
            catch (JsonException) { return false; }
        }
        // Undo/redo availability from the engine (it owns the visual history).
        if (line.Contains("\"vizHistory\"", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var v = doc.RootElement;
                if (!v.TryGetProperty("t", out var t) || t.GetString() != "vizHistory") return false;
                _canUndo = v.TryGetProperty("undo", out var u) && u.GetInt32() > 0;
                _canRedo = v.TryGetProperty("redo", out var r) && r.GetInt32() > 0;
                return true;
            }
            catch (JsonException) { return false; }
        }
        // openVJ dropped a video: take its sound into the matching Session slot, when the slot
        // has no audio of its own.
        if (line.Contains("\"vizMediaAudio\"", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var v = doc.RootElement;
                if (!v.TryGetProperty("t", out var t) || t.GetString() != "vizMediaAudio") return false;
                int layer = v.TryGetProperty("layer", out var l) ? l.GetInt32() : -1;
                int scene = v.TryGetProperty("col", out var c) ? c.GetInt32() : -1;
                string path = v.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                if (layer < 0 || scene < 0 || path.Length == 0) return true;
                AppLog($"[viz<-] vizMediaAudio L{layer} C{scene} {Path.GetFileName(path)}");
                try { SlotAudioRequested?.Invoke(layer, scene, path); }
                catch (Exception ex) { AppLog("media audio handler failed: " + ex.Message); }
                return true;
            }
            catch (JsonException) { return false; }
        }
        bool maybeOutput = line.Contains("\"vizOut\"", StringComparison.Ordinal);
        bool maybeUi = line.Contains("\"vizUiState\"", StringComparison.Ordinal);
        bool maybeMapping = line.Contains("\"vizMappingState\"", StringComparison.Ordinal);
        if (!maybeOutput && !maybeUi && !maybeMapping) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var v = doc.RootElement;
            if (!v.TryGetProperty("t", out var t)) return false;
            string? kind = t.GetString();
            bool open = v.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.True;
            if (kind == "vizOut")
            {
                _outputWanted = open;
                SetOutputOpen(open);
                AppLog($"[viz<-] vizOut open={open}");
                return true;
            }
            if (kind == "vizUiState")
            {
                _uiWanted = open;
                SetUiOpen(open);
                AppLog($"[viz<-] vizUiState open={open}");
                return true;
            }
            if (kind == "vizMappingState")
            {
                _mappingWanted = open;
                SetMappingOpen(open);
                AppLog($"[viz<-] vizMappingState open={open}");
                return true;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }

    private void SetOutputOpen(bool open)
    {
        if (_outputOpen == open) return;
        _outputOpen = open;
        try { OutputOpenChanged?.Invoke(open); }
        catch (Exception ex) { AppLog("output state handler failed: " + ex.Message); }
    }

    private void SetUiOpen(bool open)
    {
        if (_uiOpen == open) return;
        _uiOpen = open;
        try { UiOpenChanged?.Invoke(open); }
        catch (Exception ex) { AppLog("editor state handler failed: " + ex.Message); }
    }

    private void SetMappingOpen(bool open)
    {
        if (_mappingOpen == open) return;
        _mappingOpen = open;
        try { MappingOpenChanged?.Invoke(open); }
        catch (Exception ex) { AppLog("mapping state handler failed: " + ex.Message); }
    }

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
        _saved.Reset();
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

    /// <summary>Waits for the visual engine to confirm a save (used on quit).</summary>
    public bool WaitForSave(int milliseconds) => _saved.Wait(milliseconds);

    /// <summary>Saves the current bundle's visuals (called on Save and on quit).</summary>
    public void SaveProject()
    {
        if (_projectDir is not { Length: > 0 } dir) return;
        // The folder may have been deleted or moved while the project was open. Writing here
        // would recreate it as a visual-only shell (no project.json) and mask the loss.
        if (!BundlePresent(dir))
        {
            AppLog($"save skipped: '{dir}' is no longer a project bundle");
            return;
        }
        VizSave(dir);
    }

    /// <summary>Clears a slot's visual; with atBeat, waits for that beat (audio stop).</summary>
    public void VizClear(int layer, int scene, double? atBeat = null)
        => Send(atBeat is { } b
            ? $"{{\"t\":\"vizClear\",\"layer\":{layer},\"col\":{scene},\"atBeat\":{Num(VisualBeat(b))}}}"
            : $"{{\"t\":\"vizClear\",\"layer\":{layer},\"col\":{scene}}}");

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
        SetOutputOpen(false);
        SetUiOpen(false);
        SetMappingOpen(false);
    }

    private static void AppLog(string text) => Console.Error.WriteLine("[visual-bridge] " + text);
}
