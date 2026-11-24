// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Infrastructure.Visuals;
using Nota.Presentation;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private async void OnImportClicked(object? sender, RoutedEventArgs e) => await DoImportAsync();

    private async Task DoImportAsync()
    {
        if (_vm is null) return;
        // Bring the app to the front first: if it isn't the active application when the
        // native file panel opens (common under `dotnet run` / launched from an IDE), the
        // panel can appear unfocused and refuse clicks. Activating makes the window key.
        Activate();
        var files = await this.PickFilesAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("main.files.import_audio"),
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(_importFolder ?? _vm.Settings.ResolvedSamplesFolder()),
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc.T("common.audio")) { Patterns = new[] { "*.wav", "*.flac", "*.mp3" } }
            }
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) { _vm.StatusText = Loc.T("main.files.unsupported_file_location"); return; }
        _importFolder = System.IO.Path.GetDirectoryName(path);

        await ImportAudioInBackgroundAsync(path, -1, 0.0);
    }

    // --- Export master to WAV (M6-4) ---------------------------------------

    private async Task DoExportAsync()
    {
        if (_vm is null) return;
        _editorRoll?.SettlePlacement();   // render what the user sees once the notes are let go

        double endBeats = ProjectEndBeats();
        if (endBeats <= 0.0)
        {
            _vm.StatusText = Loc.T("main.files.nothing_to_export_the_arrangement");
            return;
        }

        double bpm = (double)_vm.Transport.Bpm;
        // The transport's loop region; with none set yet, the first four bars (as Loop on uses).
        double loopStart = Engine.LoopStart, loopEnd = Engine.LoopEnd;
        if (loopEnd <= loopStart) (loopStart, loopEnd) = (0, 4 * 4);
        var opts = await new ExportWindow(endBeats, loopStart, loopEnd, bpm).ShowDialog<ExportOptions?>(this);
        if (opts is null) return;

        Activate(); // keep the native panel focused (see DoImportAsync)
        // Stems bounce one file per track into a folder; the master bounce writes
        // one file. The picker follows the chosen format.
        string ext = opts.Format switch { ExportFormat.Flac => "flac", ExportFormat.Mp3 => "mp3", _ => "wav" };
        string? path;
        if (opts.Stems)
        {
            var folders = await this.PickFoldersAsync(new FolderPickerOpenOptions
            {
                Title = Loc.T("main.files.export_stems_to_folder"), AllowMultiple = false,
            });
            path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        else
        {
            var file = await this.PickSaveFileAsync(new FilePickerSaveOptions
            {
                Title = Loc.F("main.files.export_master_to", ext.ToUpperInvariant()),
                DefaultExtension = ext,
                SuggestedFileName = $"Nota Export.{ext}",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(Loc.F("main.files.audio", ext.ToUpperInvariant())) { Patterns = new[] { $"*.{ext}" } }
                },
            });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return;

        // Stop the audio backend and suspend the UI clock so the render thread
        // has exclusive engine access (see MainWindowViewModel.SuspendEnginePolling).
        // A modal progress dialog blocks the UI for the bounce and shows its progress.
        string noun = opts.Stems ? "stems" : "master";
        _vm.SuspendEnginePolling = true;
        try
        {
            var request = new ExportRequest(
                path, opts.RangeBeats + opts.TailBeats, bpm, opts.SampleRate, opts.Depth,
                RestoreLoop: _vm.Transport.LoopOn, RestoreMetronome: _vm.Transport.MetronomeOn,
                Normalize: opts.Normalize, Dither: opts.Dither,
                Format: opts.Format, Mp3BitrateKbps: opts.BitrateKbps, StartBeats: opts.StartBeats);
            await RunBlockingAsync(Loc.T("main.files.export"), opts.Stems ? Loc.T("main.files.exporting_stems") : Loc.T("main.files.exporting"), async prog =>
            {
                var frac = new Progress<double>(f =>
                    prog.Report(ProgressReport.At(f, Loc.F("main.files.exporting_n", noun, f * 100))));
                if (opts.Stems)
                {
                    int count = await Task.Run(() => _exporter.ExportStems(Engine, request, frac));
                    _vm.StatusText = Loc.F("common.pattern_n", Loc.Plural("main.files.exported_stems", count), System.IO.Path.GetFileName(path));
                }
                else
                {
                    await Task.Run(() => _exporter.ExportMaster(Engine, request, frac));
                    _vm.StatusText = Loc.F("main.files.exported", System.IO.Path.GetFileName(path));
                }
            });
        }
        catch (Exception ex)
        {
            _vm.StatusText = Loc.F("main.files.export_failed", ex.Message);
        }
        finally
        {
            _vm.SuspendEnginePolling = false;
        }
    }

    /// <summary>Last clip end across all tracks, in beats (0 if the arrangement is empty).</summary>
    private double ProjectEndBeats()
    {
        double end = 0.0;
        int tracks = Engine.TrackCount;
        for (int i = 0; i < tracks; i++)
        {
            if (!Engine.TryGetTrackInfo(i, out var t)) continue;
            for (int c = 0; c < t.ClipCount; c++)
                if (Engine.TryGetClipInfo(t.Id, c, out var clip))
                    end = Math.Max(end, clip.StartBeat + clip.LengthBeats);
        }
        return end;
    }

    // --- project save / open (M7-6a) ---------------------------------------

    // Folder holding the currently-open bundle (a `.nota` folder or a `.syntetika` container's
    // extracted working folder). Null = never saved.
    private string? _projectPath;
    // The one-file project this working folder belongs to (null = legacy `.nota` folder).
    private string? _projectFile;

    /// <summary>What the user names/reopens: the container file, or the legacy bundle folder.</summary>
    private string? ProjectDisplayPath => _projectFile ?? _projectPath;

    /// <summary>Reflect the open project in the window + title-bar caption
    /// ("Nota — Untitled" until the bundle is named).</summary>
    private void UpdateWindowTitle()
    {
        string title = Loc.F("main.files.nota", ProjectDisplayName());
        Title = title;
        // macOS: the frameless header's caption is the project's name; the OS window title keeps
        // the app too, for the Dock and window switcher. Windows: the caption is the window title
        // itself, "Nota — My song" — with the menu row gone it is where the app is named (#518).
        // (Linux shows the native frame, whose title is Title.)
        DocTitleText.Text = OperatingSystem.IsWindows() ? title : ProjectDisplayName();
    }

    /// <summary>A crash-recovery snapshot to offer on open (set by App at launch). M7-7.</summary>
    public RecoveryInfo? PendingRecovery { get; init; }

    private void OnMenuNew(object? sender, EventArgs e) => _ = DoNewAsync();

    private async Task DoNewAsync()
    {
        if (_vm is null) return;
        if (!await ConfirmLeaveChangesAsync(Loc.T("common.new_project"),
                Loc.T("main.files.save_your_changes_before_starting"))) return;
        _links.Clear();    // no live-freeze links (cleared first: stale ids must not hit the new graph)
        Nota.Application.TrackUids.Clear();   // new project = new track identities
        string? oldWorking = _projectPath;
        bool oldWasContainer = _projectFile is not null;
        Engine.Reset();
        // Reset transport to defaults (these setters drive the engine + UI).
        _vm.Transport.Bpm = 120;
        _vm.Transport.MasterVolume = 1.0;
        _vm.Transport.MetronomeOn = false;
        _vm.Transport.LoopOn = false;
        Engine.SetLoop(false, 0, 0);   // nor the old project's loop region
        _vm.Transport.SyncLoop();
        _vm.Transport.TimeSigNumerator = 4;
        _vm.Transport.TimeSigDenominator = 4;
        _vm.Transport.KeyCode = -1;
        _panLawLegacy = false;     // new projects use the Stereo pan law (set by the template)
        _projectPath = null;
        _projectFile = null;
        VisualBridge.Current?.SetProjectDir(null);
        VisualBridge.Current?.ResetComposition();   // a new project starts with a blank picture
        if (oldWasContainer) ReleaseWorkingDir(oldWorking);
        if (_vm is not null) _vm.Browser.OpenProjectPath = null;
        _learn?.Clear();   // start with a clean MIDI-map for the new project
        _devicePages?.LoadProject(null);   // a new project has no pages of its own
        _controllers?.Overrides?.LoadProject(null, _controllers.TrackIdAt);   // …nor rebinds (Story 7)
        Timeline.ResetTrackView();  // …at the default track sizes
        Nota.Infrastructure.ProjectTemplate.ApplyDefault(Engine);   // instrument, audio, Reverb + Delay returns
        RefreshAfterLoad();
        UpdateWindowTitle();
        MarkProjectClean();
        HistoryChanged?.Invoke();   // a new project has no history yet
        _vm.StatusText = Loc.T("main.files.new_project");
    }

    // The file panels start where their files live (#309): projects in the Projects folder from
    // Preferences, audio in the folder last imported from (the Samples folder at first). Left
    // alone, macOS opens every panel in the folder any of them used last.
    private string? _importFolder;

    private async Task<IStorageFolder?> FolderAsync(string dir)
    {
        try { return System.IO.Directory.Exists(dir) ? await StorageProvider.TryGetFolderFromPathAsync(dir) : null; }
        catch { return null; }   // a provider without paths (or a stale folder): the OS default
    }

    private void OnMenuOpen(object? sender, EventArgs e) => _ = DoOpenAsync();
    private void OnMenuOpenLegacy(object? sender, EventArgs e) => _ = DoOpenLegacyFolderAsync();
    private void OnMenuSave(object? sender, EventArgs e) => _ = DoSaveAsync(saveAs: false);
    private void OnMenuSaveAs(object? sender, EventArgs e) => _ = DoSaveAsync(saveAs: true);
    private void OnMenuSaveVersion(object? sender, EventArgs e) => _ = SaveVersionWithNoteAsync();

    private async Task DoOpenAsync()
    {
        if (_vm is null) return;
        Activate(); // keep the native panel focused (see DoImportAsync)
        var files = await this.PickFilesAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("main.files.open_project"),
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(_vm.Settings.ResolvedProjectsFolder()),
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc.T("main.files.syntetika_project")) { Patterns = new[] { "*" + ProjectContainer.Extension } },
            },
        });
        var file = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (file is null) return;
        if (!await ConfirmLeaveChangesAsync(Loc.T("main.files.open_project"),
                Loc.T("main.files.save_your_changes_before_opening"))) return;
        OpenContainerProject(file);
    }

    /// <summary>Legacy `.nota` bundles are folders; they stay readable and saveable.</summary>
    private async Task DoOpenLegacyFolderAsync()
    {
        if (_vm is null) return;
        Activate();
        var folders = await this.PickFoldersAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("main.files.open_nota_project_nota_folder"),
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(_vm.Settings.ResolvedProjectsFolder()),
        });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath();
        if (dir is null) { _vm.StatusText = Loc.T("main.files.unsupported_project_location"); return; }
        if (!await ConfirmLeaveChangesAsync(Loc.T("main.files.open_project"),
                Loc.T("main.files.save_your_changes_before_opening"))) return;
        OpenProject(dir);
    }

    /// <summary>Opens a `.syntetika` file in a fresh working folder.</summary>
    private void OpenContainerProject(string file)
    {
        string? oldWorking = _projectPath;
        bool oldWasContainer = _projectFile is not null;
        string working = ProjectContainer.NewWorkingDir();
        try
        {
            ProjectContainer.Extract(file, working);
        }
        catch (Exception ex)
        {
            try { System.IO.Directory.Delete(working, true); } catch { }
            App.Services.GetRequiredService<ILogSink>().Error($"Opening container '{file}' failed", ex);
            if (_vm is not null) _vm.StatusText = Loc.F("main.files.open_failed", ex.Message);
            return;
        }
        OpenProject(working, recordRecent: true, recentPath: file, containerFile: file);
        if (_projectPath is { Length: > 0 } opened && SamePath(opened, working))
        {
            if (oldWasContainer) ReleaseWorkingDir(oldWorking);
        }
        else
        {
            try { System.IO.Directory.Delete(working, true); } catch { }
        }
    }

    /// <summary>Recent Projects entries can be a `.syntetika` file or a legacy folder.</summary>
    private async Task OpenRecentAsync(string path)
    {
        if (!await ConfirmLeaveChangesAsync(Loc.T("main.files.open_project"),
                Loc.T("main.files.save_your_changes_before_opening"))) return;
        if (ProjectContainer.IsContainer(path)) OpenContainerProject(path);
        else OpenProject(path);
    }

    /// <summary>Drops a container's extracted folder once another project points the engine elsewhere.</summary>
    private void ReleaseWorkingDir(string? dir)
    {
        if (dir is not { Length: > 0 } || _projectPath is { Length: > 0 } current && SamePath(current, dir)) return;
        string root = System.IO.Path.GetFullPath(NotaPaths.SubDir("working")) + System.IO.Path.DirectorySeparatorChar;
        string full = System.IO.Path.GetFullPath(dir);
        if (!full.StartsWith(root, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) return;
        try { System.IO.Directory.Delete(full, recursive: true); } catch { /* still in use */ }
    }

    // Open another project from the browser, after the unsaved-changes prompt.
    private async Task OpenProjectConfirmedAsync(string dir)
    {
        if (!await ConfirmLeaveChangesAsync(Loc.T("main.files.open_project"),
                Loc.T("main.files.save_your_changes_before_opening"))) return;
        if (ProjectContainer.IsContainer(dir)) OpenContainerProject(dir);
        else OpenProject(dir);
    }

    // Bumped on every load so a warp-cache build from a previous open bows out if the user
    // opens another project before it finished (both loops run on the UI thread).
    private int _warpBuildGen;

    // The open project predates the pan law (specs/006): only drives the Mixer's OLDER PROJECT
    // badge and is saved back as is.
    private bool _panLawLegacy;

    /// <summary>A folder picker may hand back a path with a trailing separator; Path.GetFileName
    /// would then be empty (project name, log line). Bundles are folders, so trim it.</summary>
    private static string NormalizeBundlePath(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return dir;
        string trimmed = dir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? dir : trimmed;
    }

    /// <summary>Loads a bundle folder into the engine. <paramref name="containerFile"/> is the
    /// `.syntetika` file when <paramref name="dir"/> is its extracted working folder.
    /// <paramref name="recentPath"/> is what Recent Projects lists (defaults to the folder).</summary>
    private void OpenProject(string dir, bool recordRecent = true, string? recentPath = null,
                             string? containerFile = null)
    {
        if (_vm is null) return;
        dir = NormalizeBundlePath(dir);
        _warpBuildGen++;
        Engine.StopPreview();
        try
        {
            // Defer the (heavy) warp-clip stretching so applying the project is fast — the
            // arrangement appears at once and the caches then fill in the background below.
            Engine.SetDeferWarpBuild(true);
            ProjectLoadResult result;
            try { result = _projects.Load(Engine, dir); }
            finally { Engine.SetDeferWarpBuild(false); }
            _links.Clear();   // the old project's live-freeze ids mean nothing in the loaded graph
            // Load resets the engine but leaves transport; restore it via the VM.
            _vm.Transport.RestoreBpm(result.Transport.Bpm);
            _vm.Transport.MasterVolume = result.Transport.MasterVolume;
            _vm.Transport.MetronomeOn = result.Transport.MetronomeOn;
            _vm.Transport.SyncLoop();   // the loop (on/off and its region) came with the project (#455)
            if (result.Transport.LoopOn && !Engine.LoopEnabled)
                _vm.Transport.LoopOn = true;   // an older file saved no region: Loop on picks its default
            _vm.Transport.TimeSigNumerator = result.Transport.TimeSigNumerator;
            _vm.Transport.TimeSigDenominator = result.Transport.TimeSigDenominator;
            _vm.Transport.KeyCode = result.Transport.Key;
            _panLawLegacy = result.Transport.PanLawLegacy;
            _projectPath = dir;
            _projectFile = containerFile;
            VisualBridge.Current?.SetProjectDir(dir);
            VisualBridge.Current?.VizLoad(dir);
            VisualBridge.Current?.SyncDecks();   // the project's deck list is the truth
            // Re-apply audio loop lengths after the composition loads, so saved videos whose
            // audio was trimmed later still match their clips.
            VisualBridge.Current?.ResendSlotTiming();
            VisualBridge.Current?.PublishSlotTiming(force: true);
            _learn?.LoadMappings(dir);   // MIDI-learn mappings ride in the bundle sidecar
            _devicePages?.LoadProject(dir);   // the project's copy of edited controller pages (specs/051)
            _controllers?.Overrides?.LoadProject(dir, _controllers.TrackIdAt);   // its controller rebinds (Story 7)
            _modular?.LoadLayout(dir);   // modular-editor node/island positions (sidecar)
            Timeline.LoadTrackView(dir); // track heights + track-list width (sidecar)
            LoadFreezeLinks(dir);        // live-freeze links (v1.1) — re-sleep linked sources
            if (recordRecent) RecordRecentProject(recentPath ?? dir);   // surface it on the welcome screen
            RefreshAfterLoad();
            UpdateWindowTitle();
            MarkProjectClean();
            HistoryChanged?.Invoke();    // the History tab shows this project's versions
            var warnings = result.Warnings;
            string shown = System.IO.Path.GetFileName(ProjectDisplayPath ?? dir);
            App.Services.GetRequiredService<ILogSink>()
                .Info($"Opened project '{shown}' · {warnings.Count} warning(s)");
            _vm.StatusText = warnings.Count == 0
                ? Loc.F("main.files.opened", shown)
                : Loc.F("main.files.opened_item_s_downgraded", shown, warnings.Count, warnings[0]);
            if (result.TempoClamped) _vm.StatusText = Loc.T("project.tempo_clamped");   // specs/099, once: saving drops it
            _ = BuildWarpCachesAsync();   // fill the deferred warp caches in the background
            _ = OfferMissingPluginsAsync(dir, result.MissingPlugins);
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Open project failed", ex);
            _vm.StatusText = Loc.F("main.files.open_failed", ex.Message);
        }
    }

    // Fill the warp-clip caches deferred during load, on the engine's worker thread, so the
    // status-bar strip advances and the app stays responsive. Warped clips are silent until
    // their cache lands (a few seconds for a big project); everything else plays immediately.
    private async Task BuildWarpCachesAsync()
    {
        if (_vm is null) return;
        int gen = _warpBuildGen;
        long total = Engine.WarpBuildRemaining;
        if (total <= 0) return;
        await RunBackgroundAsync(Loc.T("main.files.preparing_warped_clips"), async prog =>
        {
            // The stretch runs on the engine's worker thread; the UI only polls + installs.
            await PollWarpBuildAsync(() => gen == _warpBuildGen, remaining =>   // bail if another project opened
            {
                double frac = Math.Clamp(1.0 - (double)remaining / total, 0.0, 1.0);
                prog.Report(ProgressReport.At(frac, Loc.F("main.files.preparing_warped_clips_n", frac * 100)));
            });
        });
    }

    /// <summary>Saves the project (prompting for a location when unnamed or Save As) and
    /// records a version in its history, with <paramref name="note"/> if given. A
    /// <c>.syntetika</c> project is written to its working folder and then packed into the
    /// one file; a legacy <c>.nota</c> folder keeps saving as a folder.</summary>
    private async Task<bool> DoSaveAsync(bool saveAs, string? note = null)
    {
        if (_vm is null) return false;
        // Notes still being placed in the piano roll overwrite what they cover now, so a saved
        // project never keeps them stacked (specs/007-midi-note-editing).
        _editorRoll?.SettlePlacement();

        string? dir = _projectPath;
        string? container = _projectFile;
        // A recovery working folder without a file is still unnamed: the first save asks for a
        // container instead of writing into the transient folder.
        bool unnamed = dir is null || (container is null && ProjectContainer.IsWorkingDir(dir));
        if (saveAs || unnamed)
        {
            string? file = await PickContainerPathAsync(Loc.T("main.files.save_nota_project"),
                ProjectDisplayName() + ProjectContainer.Extension);
            if (file is null) return false;
            container = file;
            if (dir is null) dir = ProjectContainer.NewWorkingDir();
        }

        string? previousPath = _projectPath;
        string? migratedFrom = null;
        // Save As from a legacy .nota folder: the container must not keep depending on that
        // folder. Copy the bundle (history included) to a working folder and use it from now on;
        // the old folder stays on disk as a backup.
        if (saveAs && container is not null && previousPath is { Length: > 0 } legacy
            && _projectFile is null && !ProjectContainer.IsWorkingDir(legacy))
        {
            string working = ProjectContainer.NewWorkingDir();
            ProjectContainer.CopyDirectory(legacy, working);
            dir = working;
            previousPath = working;   // the history came along; this is not a replaced project
            migratedFrom = legacy;
        }
        try
        {
            // Save As over another project replaces it — its history goes with it.
            if (previousPath is null || !SamePath(previousPath, dir!)) DropReplacedHistory(dir!);
            var warnings = _projects.Save(Engine, TransportSnapshot(), dir!);
            _learn?.SaveMappings(dir!);   // MIDI-learn mappings ride in the bundle sidecar
            _devicePages?.SaveProject(dir!, Nota.Application.Controllers.FocusedDevice.TypeKeysIn(Engine));   // FR-018a
            _controllers?.Overrides?.SaveProject(dir!, _controllers.TrackIndexOf);   // FR-034
            _modular?.SaveLayout(dir!);   // modular-editor node/island positions (sidecar)
            Timeline.SaveTrackView(dir!); // track heights + track-list width (sidecar)
            SaveFreezeLinks(dir!);        // live-freeze links (v1.1) ride in the bundle sidecar
            _analysis.AdoptInto(dir!);    // analysis cached while unsaved moves into the bundle
            // The bundle is written, so it's the open project now: set the path before the
            // version is recorded, since the History tab refreshes against it (#317).
            _projectPath = dir!;
            _projectFile = container;
            var bridge = VisualBridge.Current;
            bridge?.SetProjectDir(dir!);
            bridge?.VizSave(dir!);
            // The engine writes composition.json after it answers, so wait for that
            // acknowledgement before claiming the save. A restart in this window would
            // otherwise keep the old visuals on disk while the audio moved on.
            bool visualsSaved = true;
            if (bridge is not null)
                visualsSaved = bridge.Connected && await Task.Run(() => bridge.WaitForSave(1200));
            string? historyNote = RecordVersion(dir!, previousPath, note);   // after every sidecar is written
            // The container is the actual file: pack only after every sidecar and the new
            // history version are in the working folder.
            if (container is not null) ProjectContainer.Pack(dir!, container);
            string display = ProjectDisplayPath ?? dir!;
            if (migratedFrom is { Length: > 0 }) ForgetRecentProject(migratedFrom);
            RecordRecentProject(display);    // surface it on the welcome screen next launch
            UpdateWindowTitle();
            MarkProjectClean();
            App.Services.GetRequiredService<ILogSink>()
                .Info($"Saved project '{System.IO.Path.GetFileName(display)}' · {warnings.Count} warning(s)");
            if (!visualsSaved)
                App.Services.GetRequiredService<ILogSink>()
                    .Error($"Visuals were not saved to '{dir}': the visual engine did not acknowledge");
            _vm.StatusText = (warnings.Count == 0
                ? Loc.F("main.files.saved", System.IO.Path.GetFileName(display))
                : Loc.F("main.files.saved_unsupported_item_s_skipped", System.IO.Path.GetFileName(display), warnings.Count, warnings[0]))
                + historyNote
                + (visualsSaved ? "" : " · " + Loc.T("main.files.visuals_not_saved"));
            return true;
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Save project failed", ex);
            _vm.StatusText = Loc.F("main.files.save_failed", ex.Message);
            return false;
        }
    }

    // A migrated legacy folder leaves Recent Projects (the file replaces it).
    private static void ForgetRecentProject(string path)
    {
        try
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            if (settings.Current.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
                settings.Save();
        }
        catch { /* best-effort */ }
    }

    /// <summary>Asks where to put a <c>.syntetika</c> project file. Null if dismissed.</summary>
    private async Task<string?> PickContainerPathAsync(string title, string suggestedName)
    {
        Activate();
        var file = await this.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = _vm is null ? null : await FolderAsync(_vm.Settings.ResolvedProjectsFolder()),
            DefaultExtension = "syntetika",
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(Loc.T("main.files.syntetika_project")) { Patterns = new[] { "*" + ProjectContainer.Extension } },
            },
        });
        var chosen = file?.TryGetLocalPath();
        if (chosen is null) return null;
        if (!chosen.EndsWith(ProjectContainer.Extension, StringComparison.OrdinalIgnoreCase))
            chosen += ProjectContainer.Extension;
        return chosen;
    }

    /// <summary>Asks where to put a new <c>.nota</c> bundle. Null if the panel was dismissed.</summary>
    private async Task<string?> PickProjectPathAsync(string title, string suggestedName)
    {
        Activate();
        var file = await this.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = _vm is null ? null : await FolderAsync(_vm.Settings.ResolvedProjectsFolder()),   // #309
            DefaultExtension = "nota",
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(Loc.T("main.files.nota_project")) { Patterns = new[] { "*.nota" } }
            },
        });
        var chosen = file?.TryGetLocalPath();
        if (chosen is null) return null;
        if (!chosen.EndsWith(".nota", StringComparison.OrdinalIgnoreCase)) chosen += ".nota";
        // The save panel may drop an empty placeholder file at the path; the
        // bundle is a folder, so clear it before writing.
        if (System.IO.File.Exists(chosen)) System.IO.File.Delete(chosen);
        return chosen;
    }

    // --- unsaved-changes tracking ------------------------------------------

    // Manifest fingerprint as of the last New / Open / Save (null = never clean, e.g. a
    // recovered session). The project is dirty when the current fingerprint differs.
    private string? _cleanFingerprint;

    private string? CurrentFingerprint()
    {
        if (_vm is null) return null;
        try { return _projects.Fingerprint(Engine, TransportSnapshot()); }
        catch { return null; }
    }

    // When the project first differed from its clean state, as seen by the 30 s autosave
    // tick (NoteDirtySince) — so the quit prompt can say how long changes have waited.
    private DateTime? _dirtySince;

    /// <summary>Record the current state as saved (after New / Open / Save).</summary>
    private void MarkProjectClean()
    {
        _cleanFingerprint = CurrentFingerprint();
        _dirtySince = null;
    }

    private void NoteDirtySince()
    {
        if (_dirtySince is null && HasUnsavedChanges()) _dirtySince = DateTime.Now;
    }

    /// <summary>The open project's name ("Untitled" until the bundle is named).</summary>
    private string ProjectDisplayName()
        => ProjectDisplayPath is { Length: > 0 } p
            ? System.IO.Path.GetFileNameWithoutExtension(p.TrimEnd('/', '\\'))
            : Loc.T("main.files.untitled");

    /// <summary>True if the project changed since it was last created, opened or saved.</summary>
    private bool HasUnsavedChanges()
    {
        if (_vm is null) return false;
        var now = CurrentFingerprint();
        return now is null || now != _cleanFingerprint;
    }

    // A load/new swaps the whole graph and invalidates old track ids, so clear
    // per-clip editor state and rebuild every view (cf. RefreshAfterUndoRedo).
    private void RefreshAfterLoad()
    {
        _controllers?.ResetSessionState();   // first bank / page again (specs/051)
        _importEpoch++;                  // imports still running belong to the old graph
        Timeline.ClearPendingImports();
        Timeline.ForgetAutoTargets();    // lane targets are per-project track/device indices
        ResetClipEditor();               // every clip editor points into the old graph
        _blockMoves.Clear();             // ...and the clip moves it remembers are the old project's
        _blockMovesDone = 0;
        CloseDetail();                   // collapse the docked row too, not just hide the panel
        _deviceChain?.ForgetCardState();   // per-track preset labels belong to the old project, and so do device windows
        _showMe?.Stop();                   // a Show me walkthrough points at the old project's controls (specs/098)
        _modular?.ForgetScopes();          // so do frozen Scope traces and Scope windows
        TrackArming.ForgetSelection();     // the arm-on-select memory too
        PianoRollView.ForgetBendPicks();   // and the Bend lane ranges picked per clip
        Timeline.Refresh();
        _session?.Refresh();
        // Popped out, the window stays up: show the new project's chain, not the old one's.
        if (DetailFloating) ShowDevices(Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : TrackIdAt(0));
    }
}
