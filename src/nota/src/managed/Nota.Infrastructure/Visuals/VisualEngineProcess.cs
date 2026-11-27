// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Syntetika Engine — lifecycle of the visual process.
//
// NotA owns the visual engine: it starts `syntetika-visual` with OPENVJ_NOTA pointing at the
// bridge, restarts it a few times if it crashes, and stops it when the app quits. If a visual
// engine is already connected (started by hand), nothing is spawned and nothing is killed.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Nota.Infrastructure.Visuals;

public sealed class VisualEngineProcess : IDisposable
{
    public const string BinaryName = "syntetika-visual";
    /// <summary>The executable as it is named on this platform (<c>.exe</c> on Windows).</summary>
    public static string BinaryFile { get; } =
        OperatingSystem.IsWindows() ? BinaryName + ".exe" : BinaryName;
    private const int MaxRestarts = 3;
    /// A run this long is not part of a startup crash loop, so the restart budget refills.
    private static readonly TimeSpan HealthyRun = TimeSpan.FromSeconds(15);
    /// A hidden visual engine above this private footprint is stopped; the next Visual
    /// Output/OpenVJ request starts a fresh one. Fresh idle is ~300 MB; long sessions leaked
    /// several GB before this guard.
    private const long IdleMemoryLimit = 1_200_000_000;

    private readonly VisualBridge _bridge;
    private readonly object _gate = new();
    private readonly Timer _memoryTimer;
    private Process? _process;
    private bool _stopping;
    private volatile bool _shouldRun;
    private volatile bool _active;
    private int _restarts;
    private long _startedAtTicks;
    private Timer? _restartTimer;
    private StreamWriter? _log;

    public VisualEngineProcess(VisualBridge bridge)
    {
        _bridge = bridge;
        // Visuals are on demand: the engine starts only when a visual surface is requested,
        // then is checked while hidden so a long audio-only session does not keep old media.
        StartAtLaunch = Environment.GetEnvironmentVariable("SYNTETIKA_VISUAL_ALWAYS") == "1";
        _memoryTimer = new Timer(_ => CheckIdleMemory(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        // The app creates the engine before any window; assign it here so the on-demand paths
        // (Visual Output, OpenVJ, mapping, OpenVJ tab) find it before a process exists.
        Current = this;
    }

    public static VisualEngineProcess? Current { get; private set; }

    /// <summary>Start at app launch. Off by default; View → Visual Output/OpenVJ/Mapping
    /// (or the OpenVJ tab) starts the engine on demand. <c>SYNTETIKA_VISUAL_ALWAYS=1</c>
    /// restores the old always-on behaviour.</summary>
    public bool StartAtLaunch { get; set; }

    public bool Running
    {
        get
        {
            lock (_gate) return _process is { HasExited: false };
        }
    }

    /// <summary>The visual binary: env override, bundle helper, then a dev build.</summary>
    public static string? FindBinary()
    {
        string? env = Environment.GetEnvironmentVariable("SYNTETIKA_VISUAL");
        if (env is { Length: > 0 } && File.Exists(env)) return env;

        string baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
        {
            Path.Combine(baseDir, BinaryFile),
            Path.Combine(baseDir, "..", "Helpers", BinaryFile),
            Path.Combine(baseDir, "..", "Resources", BinaryFile),
        })
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        // Dev tree: walk up looking for src/openvj/target/release/syntetika-visual.
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            string dev = Path.Combine(dir.FullName, "src", "openvj", "target", "release", BinaryFile);
            if (File.Exists(dev)) return dev;
        }
        return null;
    }

    public bool IsAvailable => FindBinary() is not null;

    public void Start()
    {
        lock (_gate)
        {
            if (_process is { HasExited: false }) return;
            if (_bridge.Connected) return;   // already following a hand-started engine
            string? path = FindBinary();
            if (path is null) return;
            _stopping = false;
            _shouldRun = true;
            // An explicit start (app launch or View ▸ Visual Output) is a fresh intent: give
            // the engine a full restart budget even after a crash loop used it up.
            Interlocked.Exchange(ref _restarts, 0);
            StartCore(path);
        }
    }

    private void StartCore(string path)
    {
        try
        {
            // A previous writer may still hold the log (the engine crashed and the same
            // process is starting it again): close it first, or the new StreamWriter fails.
            CloseLog();
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path)!,
            };
            psi.Environment["OPENVJ_NOTA"] = $"127.0.0.1:{_bridge.Port}";
            // The engine is a background visual backend: its editor window stays hidden until
            // View ▸ OpenVJ asks for it. The output window has its own View menu item.
            psi.Environment["SYNTETIKA_UI_HIDDEN"] = "1";
            // The master-output ring openVJ analyses for audio-reactive visuals.
            psi.Environment["SYNTETIKA_AUDIO_TAP"] = NotaPaths.AudioTapFile;
            // If NotA disappears (quit or crash), the visual engine exits instead of lingering.
            psi.Environment["SYNTETIKA_EXIT_ON_DISCONNECT"] = "1";
            // Keep GPU diagnostics in the app's log folder, not on the system console.
            string logs = NotaPaths.SubDir("logs");
            string logPath = Path.Combine(logs, "visual-engine.log");
            try
            {
                var info = new FileInfo(logPath);
                if (info.Exists && info.Length >= 10 * 1024 * 1024)
                    File.Move(logPath, logPath + ".1", overwrite: true);
            }
            catch { /* a held or read-only log must not stop the engine */ }
            _log = new StreamWriter(logPath, append: true) { AutoFlush = true };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => WriteLog(e.Data);
            process.ErrorDataReceived += (_, e) => WriteLog(e.Data);
            process.Exited += (_, _) => OnExited();
            if (!process.Start())
            {
                process.Dispose();
                return;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            Interlocked.Exchange(ref _startedAtTicks, DateTime.UtcNow.Ticks);
            ApplyPriority();
            WriteLog($"started {path} (pid {process.Id})");
            Current = this;
        }
        catch (Exception ex)
        {
            WriteLog("could not start: " + ex.Message);
        }
    }

    private void WriteLog(string? line)
    {
        if (line is null) return;
        try { _log?.WriteLine($"{DateTime.UtcNow:O} {line}"); } catch { }
    }

    /// <summary>Closes the engine log so a restart can open it again.</summary>
    private void CloseLog()
    {
        try { _log?.Dispose(); } catch { }
        _log = null;
    }

    private void OnExited()
    {
        Process? dead;
        lock (_gate)
        {
            dead = _process;
            _process = null;
        }
        dead?.Dispose();
        if (_stopping || !_shouldRun) return;
        // A crash loop at startup must not hammer the GPU, but a long healthy run is not part
        // of that loop: refill the budget so three crashes over a session never leave the
        // visuals permanently down.
        long started = Interlocked.Exchange(ref _startedAtTicks, 0);
        if (started != 0 && DateTime.UtcNow.Ticks - started >= HealthyRun.Ticks)
            Interlocked.Exchange(ref _restarts, 0);
        if (Interlocked.Increment(ref _restarts) > MaxRestarts)
        {
            WriteLog($"visual engine exited {MaxRestarts + 1} times without a healthy run; staying down");
            CloseLog();
            return;
        }
        WriteLog($"visual engine exited; restarting ({_restarts}/{MaxRestarts}) in 1.5 s");
        CloseLog();
        _restartTimer?.Dispose();
        _restartTimer = new Timer(_ =>
        {
            lock (_gate)
            {
                if (_stopping) return;
                if (_bridge.Connected) return;
                string? path = FindBinary();
                if (path is not null) StartCore(path);
            }
        }, null, TimeSpan.FromSeconds(1.5), Timeout.InfiniteTimeSpan);
    }

    /// <summary>Called when a visual surface (output, OpenVJ window, mapping or the OpenVJ
    /// tab) becomes visible. While hidden the process runs BelowNormal and may be stopped if
    /// its private memory grows; the next visual request starts a fresh engine.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        ApplyPriority();
        if (!active) CheckIdleMemory();
    }

    private void ApplyPriority()
    {
        if (!OperatingSystem.IsWindows()) return; // no portable Process priority on Unix
        Process? p;
        lock (_gate) p = _process is { HasExited: false } ? _process : null;
        if (p is null) return;
        try { p.PriorityClass = _active ? ProcessPriorityClass.Normal : ProcessPriorityClass.BelowNormal; }
        catch { /* the process may have exited between the check and the set */ }
    }

    private void CheckIdleMemory()
    {
        if (_active) return;
        Process? p;
        lock (_gate) p = _process is { HasExited: false } ? _process : null;
        if (p is null) return;
        try
        {
            long limit = IdleMemoryLimit;
            // SYNTETIKA_VISUAL_IDLE_LIMIT_MB overrides the cap (tests and small machines).
            if (long.TryParse(Environment.GetEnvironmentVariable("SYNTETIKA_VISUAL_IDLE_LIMIT_MB"), out long mb) && mb > 0)
                limit = mb * 1024 * 1024;
            long bytes = p.PrivateMemorySize64;
            if (bytes <= limit) return;
            WriteLog($"visual engine idle at {bytes / (1024 * 1024)} MB; stopping (limit {limit / (1024 * 1024)} MB)");
            // Visual edits live in the engine: write the composition to the project before
            // the process goes away, so a later save does not fall back to older visuals.
            try
            {
                _bridge.SaveProject();
                _bridge.WaitForSave(1500);
            }
            catch { /* the composition on disk stays the last known good one */ }
            Stop();
        }
        catch { /* diagnostics must never break the app */ }
    }

    public void Stop()
    {
        _shouldRun = false;
        _active = false;
        _stopping = true;
        Interlocked.Exchange(ref _startedAtTicks, 0);
        _restartTimer?.Dispose();
        _restartTimer = null;
        Process? p;
        lock (_gate)
        {
            p = _process;
            _process = null;
        }
        try { p?.Kill(entireProcessTree: true); } catch { }
        try { p?.WaitForExit(2000); } catch { }
        p?.Dispose();
        WriteLog("stopped");
        CloseLog();
    }

    public void Dispose()
    {
        _memoryTimer.Dispose();
        Stop();
    }
}
