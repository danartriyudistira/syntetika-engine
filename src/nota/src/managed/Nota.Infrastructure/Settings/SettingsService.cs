// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// JSON-on-disk implementation of the ISettingsService port (contract lives in
// Application). Persists next to the engine's data, in NotaPaths.DataDir
// (settings.json).

using System.Linq;
using System.Text.Json;
using Nota.Application.Browser;

namespace Nota.Infrastructure;

public sealed class SettingsService : ISettingsService
{
    /// <summary>Default sample library folder name (the pre-1.0.1 name was "Nota Samples").</summary>
    public const string DefaultSamplesFolderName = "Syntetika Samples";

    /// <summary>Default projects folder name (the pre-1.0.1 name was "Nota Projects").</summary>
    public const string DefaultProjectsFolderName = "Syntetika Projects";

    private const string LegacySamplesFolderName = "Nota Samples";
    private const string LegacyProjectsFolderName = "Nota Projects";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public Settings Current { get; private set; } = new();

    public event Action? Changed;

    public event Action? SampleFoldersChanged;

    public SettingsService() : this(Path.Combine(NotaPaths.DataDir, "settings.json"), migrateLegacy: true) { }

    /// <summary>Tests: settings kept in <paramref name="path"/>. The rebrand migration is off,
    /// so a test never renames the machine's real Music/Documents folders.</summary>
    public SettingsService(string path) : this(path, migrateLegacy: false) { }

    private SettingsService(string path, bool migrateLegacy)
    {
        _path = path;

        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? new Settings();
        }
        catch
        {
            Current = new Settings(); // corrupt/older file → defaults
        }
        RepairSampleFolders();
        if (migrateLegacy && MigrateLegacyFolders())
        {
            // Write once, without firing Changed: this runs before any view subscribes.
            MirrorDefaultSampleFolder();
            try { File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts)); } catch { }
        }
    }

    public void Save()
    {
        MirrorDefaultSampleFolder();
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts)); }
        catch { /* best-effort; not fatal */ }
        Changed?.Invoke();
    }

    /// <summary>Where samples live when the user never chose a folder.</summary>
    public static string DefaultSamplesLocation()
        => Path.Combine(UserDirs.GetMusicDir(), DefaultSamplesFolderName);

    /// <summary>Where projects live when the user never chose a folder.</summary>
    public static string DefaultProjectsLocation()
        => Path.Combine(UserDirs.GetDocumentsDir(), DefaultProjectsFolderName);

    private static string LegacySamplesLocation()
        => Path.Combine(UserDirs.GetMusicDir(), LegacySamplesFolderName);

    private static string LegacyProjectsLocation()
        => Path.Combine(UserDirs.GetDocumentsDir(), LegacyProjectsFolderName);

    /// <summary>Rebrand migration: the 1.0.0 defaults were "Nota Samples"/"Nota Projects".
    /// A folder still at the old name is renamed on disk, and the settings — including the
    /// recent-projects list — follow it. A failed rename keeps the old path, so nothing
    /// disappears. Idempotent: once the old folder is gone, later starts change nothing.</summary>
    private bool MigrateLegacyFolders()
        => MigrateLegacyFolders(Current, LegacySamplesLocation(), DefaultSamplesLocation(),
            LegacyProjectsLocation(), DefaultProjectsLocation());

    /// <summary>The migration with explicit roots, so the smoke test can drive it against a
    /// temporary folder tree instead of the user's real Music/Documents.</summary>
    internal static bool MigrateLegacyFolders(Settings current,
        string legacySamples, string newSamples, string legacyProjects, string newProjects)
    {
        bool changed = false;

        foreach (var folder in current.SampleFolders ?? [])
        {
            if (!SamePath(folder.Path, legacySamples)) continue;
            folder.Path = MigrateFolder(legacySamples, newSamples);
            changed = true;
        }
        if (current.SamplesFolder is { Length: > 0 } samples && SamePath(samples, legacySamples))
        {
            current.SamplesFolder = MigrateFolder(legacySamples, newSamples);
            changed = true;
        }

        // Rename the projects folder only when the app actually uses the default one.
        bool projectsDefault = string.IsNullOrWhiteSpace(current.ProjectsFolder)
            || SamePath(current.ProjectsFolder, legacyProjects);
        if (projectsDefault)
        {
            string moved = MigrateFolder(legacyProjects, newProjects);
            if (!SamePath(moved, newProjects))
            {
                // The rename failed: pin the old folder, or the new default would quietly
                // create a second, empty projects folder beside it.
                current.ProjectsFolder = moved;
                changed = true;
            }
            else if (!string.IsNullOrWhiteSpace(current.ProjectsFolder))
            {
                current.ProjectsFolder = newProjects;
                changed = true;
            }
            if (current.RecentProjects is { Count: > 0 } recents)
            {
                for (int i = 0; i < recents.Count; i++)
                {
                    if (RewriteRoot(recents[i], legacyProjects, newProjects) is not { } movedEntry) continue;
                    recents[i] = movedEntry;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static string MigrateFolder(string oldPath, string newPath)
    {
        try
        {
            if (SamePath(oldPath, newPath)) return newPath;
            if (!Directory.Exists(oldPath)) return newPath;   // nothing to move: the new default applies
            if (Directory.Exists(newPath))
            {
                // Both exist. An empty new folder (made by a first run of the new default) gives
                // way to the old library; two non-empty folders are left for the user to merge.
                if (Directory.EnumerateFileSystemEntries(newPath).Any())
                {
                    Console.Error.WriteLine($"[settings] both '{oldPath}' and '{newPath}' exist; keeping the old folder");
                    return oldPath;
                }
                Directory.Delete(newPath);
            }
            Directory.Move(oldPath, newPath);
            Console.Error.WriteLine($"[settings] renamed '{oldPath}' to '{newPath}'");
            return newPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[settings] could not rename '{oldPath}' to '{newPath}': {ex.Message}; keeping the old folder");
            return oldPath;
        }
    }

    private static bool SamePath(string a, string b)
        => SampleFolderRules.PathComparer.Equals(
            SampleFolderRules.Normalize(a), SampleFolderRules.Normalize(b));

    /// <summary><paramref name="path"/> moved from under <paramref name="oldRoot"/> to
    /// <paramref name="newRoot"/>, or null when it is not below the old root.</summary>
    private static string? RewriteRoot(string path, string oldRoot, string newRoot)
    {
        string normalized = SampleFolderRules.Normalize(path);
        string root = SampleFolderRules.Normalize(oldRoot);
        if (!SampleFolderRules.IsSameOrInside(normalized, root)) return null;
        return newRoot + normalized[root.Length..];
    }

    public string ResolvedSamplesFolder()
    {
        FollowLegacyField();
        var def = Current.SampleFolders?.Find(e => e.IsDefault)?.Path;
        if (string.IsNullOrWhiteSpace(def)) def = string.IsNullOrWhiteSpace(Current.SamplesFolder) ? DefaultSamplesLocation() : Current.SamplesFolder;
        return EnsureDir(def);
    }

    // --- sample folders (spec 092) ---------------------------------------------------------

    // A pre-092 file has only SamplesFolder: it becomes the one (default) entry. Bad entries
    // (nested, duplicate) are dropped with a log line.
    private void RepairSampleFolders()
    {
        string legacy = string.IsNullOrWhiteSpace(Current.SamplesFolder) ? DefaultSamplesLocation() : Current.SamplesFolder;
        var (list, dropped) = SampleFolderRules.Repair(Current.SampleFolders, legacy);
        Current.SampleFolders = list;
        foreach (var d in dropped) Console.Error.WriteLine($"[settings] Sample folders: dropped '{d}' (empty, duplicate or nested in another folder)");
        MirrorDefaultSampleFolder();
    }

    // SamplesFolder stays the default folder's path, for older versions and Mosaic refs.
    private void MirrorDefaultSampleFolder()
    {
        var def = Current.SampleFolders?.Find(e => e.IsDefault);
        if (def is not null) Current.SamplesFolder = def.Path;
    }

    // Code that still sets the single field (older harnesses, scripts) moves the default
    // folder with it: a folder already in the list becomes the default, any other one
    // takes the default entry's place.
    private void FollowLegacyField()
    {
        var list = Current.SampleFolders;
        if (list is null || string.IsNullOrWhiteSpace(Current.SamplesFolder)) return;
        var legacy = SampleFolderRules.Normalize(Current.SamplesFolder);
        var def = list.Find(e => e.IsDefault);
        if (def is null || SampleFolderRules.PathComparer.Equals(def.Path, legacy)) return;
        var hit = list.Find(e => SampleFolderRules.PathComparer.Equals(e.Path, legacy));
        if (hit is not null) { foreach (var e in list) e.IsDefault = ReferenceEquals(e, hit); }
        else { def.Path = legacy; def.Name = null; }
    }

    private List<SampleFolderEntry> Folders
    {
        get
        {
            FollowLegacyField();
            return Current.SampleFolders ??= SampleFolderRules.Repair(null, DefaultSamplesLocation()).List;
        }
    }

    public IReadOnlyList<ResolvedSampleFolder> ResolvedSampleFolders()
    {
        var list = Folders;
        var result = new List<ResolvedSampleFolder>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            string path = e.IsDefault ? ResolvedSamplesFolder() : e.Path;   // only the default is created
            bool ok;
            try { ok = Directory.Exists(path); } catch { ok = false; }
            result.Add(new ResolvedSampleFolder(i, path, SampleFolderRules.Label(e), e.IsDefault, ok));
        }
        return result;
    }

    public SampleFolderResult AddSampleFolder(string path)
    {
        path = SampleFolderRules.Normalize(path);
        bool isDir;
        try { isDir = path.Length > 0 && Directory.Exists(path); } catch { isDir = false; }
        if (!isDir) return new(SampleFolderOutcome.NotADirectory);
        var r = SampleFolderRules.Check(Folders.ConvertAll(e => e.Path), path);
        if (!r.Ok) return r;
        Folders.Add(new SampleFolderEntry { Path = path });
        Save();
        SampleFoldersChanged?.Invoke();
        return r;
    }

    public bool RemoveSampleFolder(int index)
    {
        var list = Folders;
        if (list.Count <= 1 || index < 0 || index >= list.Count) return false;
        bool wasDefault = list[index].IsDefault;
        list.RemoveAt(index);
        if (wasDefault) list[Math.Min(index, list.Count - 1)].IsDefault = true;
        Save();
        SampleFoldersChanged?.Invoke();
        return true;
    }

    public void RenameSampleFolder(int index, string? name)
    {
        var list = Folders;
        if (index < 0 || index >= list.Count) return;
        list[index].Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Save();
        SampleFoldersChanged?.Invoke();
    }

    public void SetDefaultSampleFolder(int index)
    {
        var list = Folders;
        if (index < 0 || index >= list.Count || list[index].IsDefault) return;
        for (int i = 0; i < list.Count; i++) list[i].IsDefault = i == index;
        Save();
        SampleFoldersChanged?.Invoke();
    }

    public string ResolvedProjectsFolder()
        => EnsureDir(string.IsNullOrWhiteSpace(Current.ProjectsFolder)
            ? DefaultProjectsLocation() : Current.ProjectsFolder);

    public string PresetsFolder() => NotaPaths.SubDir("presets");

    private static string EnsureDir(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        return dir;
    }
}
