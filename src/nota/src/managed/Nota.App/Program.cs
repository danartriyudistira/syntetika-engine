// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Avalonia;
using Nota.Application;
using System.Diagnostics;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nota.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // A project folder/file passed by the shell (file association): opened once the
        // window exists (MainWindow.OpenStartupProject).
        if (args.Length > 0)
        {
            string candidate = args[0];
            if (Directory.Exists(candidate) || File.Exists(candidate)) StartupProject = candidate;
        }
        // One copy only: a second instance would share the same data folder and could take
        // the visual bridge, leaving the visible window with no visuals. A file lock (not a
        // named mutex) is used because the mutex's lifetime is tied to the calling thread,
        // which Avalonia's macOS loop does not keep; the lock file lives as long as the stream.
        if (!HoldInstanceLock())
        {
            // Hand a project path to the running copy (it opens it), then raise its window.
            if (StartupProject is { Length: > 0 } path)
            {
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(Nota.Infrastructure.NotaPaths.DataDir, "open-request.txt"), path);
                }
                catch { /* best effort */ }
            }
            // Bring the running copy forward instead of failing silently (a shortcut launch
            // has no console).
            Console.Error.WriteLine($"{Branding.ProductName} is already running.");
            ActivateRunningCopy();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>A project to open after the window is up, from the command line.</summary>
    internal static string? StartupProject { get; set; }

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

    /// <summary>Raises the copy that holds the lock (macOS: open -b; Windows: the main window).</summary>
    private static void ActivateRunningCopy()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                Process.Start(new ProcessStartInfo("open", "-b " + Branding.BundleId) { UseShellExecute = false });
            }
            catch { /* best effort */ }
            return;
        }
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is null) return;
            foreach (var other in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                if (other.Id == Environment.ProcessId) continue;
                // The running copy may still be building its window: give it a moment.
                IntPtr handle = other.MainWindowHandle;
                for (int i = 0; i < 20 && handle == IntPtr.Zero; i++)
                {
                    Thread.Sleep(100);
                    other.Refresh();
                    handle = other.MainWindowHandle;
                }
                if (handle != IntPtr.Zero)
                {
                    RaiseWindow(handle);
                    return;
                }
            }
        }
        catch { /* best effort */ }
    }

    private const int SW_RESTORE = 9;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

    /// <summary>Restores and focuses a window even when the foreground lock applies.</summary>
    private static void RaiseWindow(IntPtr handle)
    {
        var foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint thisThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread
            && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            ShowWindow(handle, SW_RESTORE);
            if (!SetForegroundWindow(handle)) SwitchToThisWindow(handle, false);
        }
        finally
        {
            if (attached) AttachThreadInput(thisThread, foregroundThread, false);
        }
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
