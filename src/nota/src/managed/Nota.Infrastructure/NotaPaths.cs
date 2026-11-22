// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Single source of truth for Nota's per-user data directory, shared by the
// settings, recovery and logging services. Pushed to native at startup via
// NotaEngine.SetDataDir (nota_set_data_dir); the engine itself never infers
// platform paths — besides this value only NOTA_DATA_DIR (tests) is honoured.
// Platform conventions:
//   macOS   -> ~/Library/Application Support/NotAopenVJ
//   Windows -> %APPDATA%\NotAopenVJ                  (Roaming AppData)
//   Linux   -> $XDG_CONFIG_HOME/notaopenvj or ~/.config/notaopenvj

using Nota.Application;

namespace Nota.Infrastructure;

public static class NotaPaths
{
    /// <summary>The per-user Nota data root, created if missing.</summary>
    public static string DataDir { get; } = ResolveDataDir();

    /// <summary>User home directory.</summary>
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Ensures and returns a subfolder of <see cref="DataDir"/> (e.g. "logs", "presets").</summary>
    public static string SubDir(string name)
    {
        var dir = Path.Combine(DataDir, name);
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        return dir;
    }

    private static string ResolveDataDir()
    {
        string root;
        // NOTA_DATA_DIR (tests) relocates the data root; the native plugin host honours it too.
        if (Environment.GetEnvironmentVariable("NOTA_DATA_DIR") is { Length: > 0 } over)
            root = over;
        else if (OperatingSystem.IsWindows())
        {
            // %APPDATA% == Roaming AppData; on Windows SpecialFolder.ApplicationData maps here.
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Branding.DataFolder);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // The fork keeps its own data root so a stock Nota install is untouched.
            root = Path.Combine(Home, "Library", "Application Support", Branding.DataFolder);
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = string.IsNullOrEmpty(xdg) ? Path.Combine(Home, ".config") : xdg;
            root = Path.Combine(baseDir, Branding.DataFolder.Replace(' ', '-').ToLowerInvariant());
        }

        // First launch after the rebrand: bring the old profile across. The old folder stays
        // as a backup; the user deletes it once the new one is confirmed working.
        MigrateOldData(root);
        try { Directory.CreateDirectory(root); } catch { /* best-effort */ }
        return root;
    }

    /// <summary>Copies the pre-rebrand data folder once (macOS path only today).</summary>
    private static void MigrateOldData(string root)
    {
        if (!OperatingSystem.IsMacOS()) return;
        string old = Path.Combine(Home, "Library", "Application Support", "NotAopenVJ");
        if (Directory.Exists(root) || !Directory.Exists(old)) return;
        try
        {
            CopyTree(old, root);
            File.WriteAllText(Path.Combine(root, ".migrated-from-notaopenvj"), DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "syntetika-migration.log"),
                    DateTime.UtcNow.ToString("O") + " " + ex + Environment.NewLine);
            }
            catch { /* best-effort */ }
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            string name = Path.GetFileName(dir);
            if (name is "cache" or "tmp") continue;   // rebuildable, and can be large
            CopyTree(dir, Path.Combine(to, name));
        }
        foreach (var file in Directory.EnumerateFiles(from))
        {
            string name = Path.GetFileName(file);
            if (name is "app.lock" or ".DS_Store") continue;
            try { File.Copy(file, Path.Combine(to, name), overwrite: false); }
            catch (IOException) { /* present already */ }
        }
    }
}
