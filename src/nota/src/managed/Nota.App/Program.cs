// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Avalonia;
using System.Diagnostics;
using System;

namespace Nota.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // One copy only: a second instance would share the same data folder and could take
        // the visual bridge, leaving the visible window with no visuals. A file lock (not a
        // named mutex) is used because the mutex's lifetime is tied to the calling thread,
        // which Avalonia's macOS loop does not keep; the lock file lives as long as the stream.
        if (!HoldInstanceLock())
        {
            // Bring the running copy forward instead of failing silently (Finder launches have
            // no console). `open -b` needs the bundle id; a dev build just exits.
            Console.Error.WriteLine("NotAopenVJ is already running.");
            try
            {
                Process.Start(new ProcessStartInfo("open", "-b studio.syntetika.notaopenvj")
                {
                    UseShellExecute = false,
                });
            }
            catch { /* best effort */ }
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    /// <summary>Held for the whole process; released by the OS when it exits or crashes.</summary>
    private static System.IO.FileStream? _instanceLock;

    private static bool HoldInstanceLock()
    {
        try
        {
            var dir = Nota.Infrastructure.NotaPaths.DataDir;
            System.IO.Directory.CreateDirectory(dir);
            _instanceLock = new System.IO.FileStream(
                System.IO.Path.Combine(dir, "app.lock"),
                System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite,
                System.IO.FileShare.None, 1, System.IO.FileOptions.None);
            return true;
        }
        catch (System.IO.IOException) { return false; }          // another copy holds it
        catch (System.UnauthorizedAccessException) { return true; } // don't block on a permissions quirk
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithNotaFonts()
            .WithLinuxFileDialogFallback()
            .LogToTrace()
            .WithImeLogNoiseFiltered();
}
