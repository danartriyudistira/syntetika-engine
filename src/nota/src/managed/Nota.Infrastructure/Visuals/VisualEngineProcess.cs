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
    private const int MaxRestarts = 3;

    private readonly VisualBridge _bridge;
    private readonly object _gate = new();
    private Process? _process;
    private bool _stopping;
    private int _restarts;
    private Timer? _restartTimer;
    private StreamWriter? _log;

    public VisualEngineProcess(VisualBridge bridge) => _bridge = bridge;

    public static VisualEngineProcess? Current { get; private set; }

    /// <summary>Start automatically when NotA opens (the one-app experience).</summary>
    public bool AutoStart { get; set; } = true;

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
            Path.Combine(baseDir, BinaryName),
            Path.Combine(baseDir, "..", "Helpers", BinaryName),
            Path.Combine(baseDir, "..", "Resources", BinaryName),
        })
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        // Dev tree: walk up looking for src/openvj/target/release/syntetika-visual.
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            string dev = Path.Combine(dir.FullName, "src", "openvj", "target", "release", BinaryName);
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
            StartCore(path);
        }
    }

    private void StartCore(string path)
    {
        try
        {
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
            // If NotA disappears (quit or crash), the visual engine exits instead of lingering.
            psi.Environment["SYNTETIKA_EXIT_ON_DISCONNECT"] = "1";
            // Keep GPU diagnostics in the app's log folder, not on the system console.
            string logs = NotaPaths.SubDir("logs");
            _log = new StreamWriter(Path.Combine(logs, "visual-engine.log"), append: true) { AutoFlush = true };

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

    private void OnExited()
    {
        Process? dead;
        lock (_gate)
        {
            dead = _process;
            _process = null;
        }
        dead?.Dispose();
        if (_stopping || !AutoStart) return;
        // A crashed engine takes the visuals with it; try a few times, then stay down.
        if (Interlocked.Increment(ref _restarts) > MaxRestarts) return;
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

    public void Stop()
    {
        _stopping = true;
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
        try { _log?.Dispose(); } catch { }
        _log = null;
    }

    public void Dispose() => Stop();
}
