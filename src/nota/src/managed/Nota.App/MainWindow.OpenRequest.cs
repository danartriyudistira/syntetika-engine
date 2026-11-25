// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Syntetika Engine: a second launch (double-clicking a .nota folder, "Open with") cannot
// take the instance lock, so it leaves the path in the profile and raises the window. The
// running copy watches for that file and opens the project, instead of ignoring the click.

using System;
using System.IO;
using Avalonia.Threading;

namespace Nota.App;

public partial class MainWindow
{
    private const string OpenRequestFile = "open-request.txt";
    private FileSystemWatcher? _openRequestWatcher;

    /// <summary>Watches the profile for project paths handed over by a second launch.</summary>
    private void StartOpenRequestWatcher()
    {
        try
        {
            string dir = Nota.Infrastructure.NotaPaths.DataDir;
            _openRequestWatcher = new FileSystemWatcher(dir, OpenRequestFile)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _openRequestWatcher.Created += (_, _) => HandleOpenRequest();
            _openRequestWatcher.Changed += (_, _) => HandleOpenRequest();
        }
        catch { /* the watcher is a convenience; the app still works without it */ }
        // A request written before the watcher existed (fast second launch) still counts.
        HandleOpenRequest();
    }

    private void HandleOpenRequest()
    {
        try
        {
            string file = Path.Combine(Nota.Infrastructure.NotaPaths.DataDir, OpenRequestFile);
            if (!File.Exists(file)) return;
            string path = File.ReadAllText(file).Trim();
            File.Delete(file);
            if (path.Length == 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                try { OpenProject(path); } catch { /* a stale path must not break the app */ }
            });
        }
        catch { /* a half-written or held file is retried on the next event */ }
    }

    private void StopOpenRequestWatcher()
    {
        try { _openRequestWatcher?.Dispose(); } catch { }
        _openRequestWatcher = null;
    }
}
