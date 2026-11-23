// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Syntetika Engine — one action for a video: Nota performs its sound, openVJ its picture.
//
// The audio track is extracted with the bundled FFmpeg, placed in the selected Session slot,
// and the video is assigned to the matching visual slot. The video is beat-synced to the
// audio clip's length, so launching the clip starts both on the same beat and loops them
// together.
//
// v1 assumes the tempo stays put while the clip plays; changing it would change the audio
// slot's musical length and needs a re-sync (planned).

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Infrastructure.Visuals;

namespace Nota.App;

public partial class MainWindow
{
    private static readonly FilePickerFileType VideoPicker = new(Loc.T("visual.video_files"))
    {
        Patterns = new[] { "*.mp4", "*.m4v", "*.mov", "*.mkv", "*.webm", "*.avi", "*.mxf", "*.mpg", "*.mpeg" },
    };

    /// <summary>File picker, then the import. Bound to a Session slot's context menu.</summary>
    private async Task ImportVideoToSlotAsync(int trackId, int scene)
    {
        if (_vm is null) return;
        if (!VideoAudio.Available)
        {
            _vm.StatusText = Loc.T("visual.ffmpeg_needed");
            return;
        }
        var files = await this.PickFilesAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("visual.import_video_title"),
            AllowMultiple = false,
            FileTypeFilter = new[] { VideoPicker },
        });
        if (files.Count == 0) return;
        string? path = files[0].TryGetLocalPath();
        if (path is null) return;
        await ImportVideoAsync(path, trackId, scene);
    }

    /// <summary>An image dropped on a Session slot becomes the slot's visual.</summary>
    private async Task ImportImageAsync(string path, int trackId, int scene)
    {
        if (_vm is null) return;
        try
        {
            string media = await Task.Run(() => MediaFiles.Prepare(path, VisualBridge.Current?.ProjectDir));
            int layer = _session?.VisualLayerFor(trackId) ?? 0;
            VisualBridge.Current?.VizAssignMedia(layer, scene, "image", media);
            _session?.Refresh();
            _session?.SelectSlot(trackId, scene);
            ShowVisualPanel(layer, scene);   // show the assigned visual straight away
            _vm.StatusText = Loc.F("visual.image_on_layer", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _vm.StatusText = Loc.F("visual.could_not_add", Path.GetFileName(path), ex.Message);
        }
    }

    /// <summary>Extracts the audio into the slot, assigns the video, and beat-syncs them.</summary>
    private async Task ImportVideoAsync(string path, int trackId, int scene)
    {
        if (_vm is null) return;
        string name = Path.GetFileName(path);
        // TryGetTrackInfo takes a list index, not a track id — resolve the id first.
        bool audioTrack = false;
        for (int i = 0; i < Engine.TrackCount; i++)
            if (Engine.TryGetTrackInfo(i, out var info) && info.Id == trackId)
            {
                audioTrack = info.Type == 0;
                break;
            }
        if (!audioTrack)
        {
            _vm.StatusText = Loc.T("visual.choose_audio_slot");
            return;
        }
        Console.Error.WriteLine($"[video] import start {path} ffmpeg={VideoAudio.FfmpegPath} probe={VideoAudio.FfprobePath}");
        _vm.StatusText = Loc.F("visual.importing", name);
        try
        {
            string? wav = null;
            bool audio = false;
            try
            {
                wav = await VideoAudio.ExtractAsync(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[video] audio extraction failed: " + ex.Message);
                _vm.StatusText = Loc.F("visual.audio_extract_failed", ex.Message);
            }

            if (wav is not null && Engine.AddSessionAudioFile(trackId, scene, wav))
            {
                audio = true;
                string clipName = Path.GetFileNameWithoutExtension(path);
                if (clipName.Length > 0) Engine.SetSessionClipName(trackId, scene, clipName);
            }
            else if (wav is null)
            {
                _vm.StatusText = Loc.F("visual.no_usable_audio", name);
            }

            // Copy the video content-addressed (bundle when saved, staging until then).
            string media = await Task.Run(() => MediaFiles.Prepare(path, VisualBridge.Current?.ProjectDir));
            int layer = _session?.VisualLayerFor(trackId) ?? 0;
            VisualBridge.Current?.VizAssignMedia(layer, scene, "video", media);

            // Beat sync: one video pass lasts exactly the audio slot's musical length.
            double length = audio ? Engine.SessionSlotLength(trackId, scene) : 0;
            if (length > 0)
                VisualBridge.Current?.VizPlayback(layer, scene, null, length, "Loop", null, setSync: true);

            Console.Error.WriteLine(
                $"[video] {name}: wav={wav is not null} audio={audio} length={length:0.###} layer={layer}");
            _session?.Refresh();
            _session?.SelectSlot(trackId, scene);
            ShowVisualPanel(layer, scene);   // show the audio + visual pair
            if (audio)
            {
                _vm.StatusText = length > 0
                    ? Loc.F("visual.imported_pair_beats", name, length)
                    : Loc.F("visual.imported_pair", name);
            }
            else
            {
                _vm.StatusText = Loc.F("visual.imported_visual_only", name);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[video] failed: " + ex);
            _vm.StatusText = Loc.F("visual.could_not_import", name, ex.Message);
        }
    }

    /// <summary>The OpenVJ tab's media picker: a video's sound joins its visual in the same
    /// Session slot, so launching the clip brings both. An image, or a video on a slot that
    /// already has audio, only changes the visual.</summary>
    internal async Task ImportVisualMediaAsync(int layer, int scene, string source)
    {
        if (_vm is null) return;
        string name = Path.GetFileName(source);
        string kind = MediaFiles.KindOf(source);
        int trackId = _session?.TrackForLayer(layer) ?? 0;
        bool video = kind == "video";
        if (video && trackId > 0 && TrackIsAudio(trackId)
            && _session is { } s && !s.SlotFilled(trackId, scene))
        {
            await ImportVideoAsync(source, trackId, scene);
            return;
        }
        try
        {
            string media = await Task.Run(() => MediaFiles.Prepare(source, VisualBridge.Current?.ProjectDir));
            VisualBridge.Current?.VizAssignMedia(layer, scene, kind, media);
            // A video assigned to a slot that already has audio takes that audio's timing.
            VisualBridge.Current?.ResendSlotTiming();
            VisualBridge.Current?.PublishSlotTiming(force: true);
            _session?.Refresh();
            _vm.StatusText = !video
                ? Loc.F("visual.image_on_layer", name)
                : trackId <= 0 || !TrackIsAudio(trackId)
                    ? Loc.F("visual.sound_needs_audio_track", name)
                    : Loc.T("visual.kept_existing_audio");
        }
        catch (Exception ex)
        {
            _vm.StatusText = Loc.F("visual.could_not_add", name, ex.Message);
        }
    }

    /// <summary>A video dropped in openVJ's own editor: put its sound in the Session slot that
    /// already carries the picture, unless the slot has audio of its own. The engine placed and
    /// loaded the clip; this only adds the audio and beat-syncs the pair.</summary>
    internal async Task PairSlotAudioAsync(int layer, int scene, string path)
    {
        if (_vm is null || _session is null) return;
        int trackId = _session.TrackForLayer(layer);
        if (trackId <= 0 || scene < 0 || scene >= Engine.SceneCount) return;
        if (!TrackIsAudio(trackId)) return;          // a MIDI track has no audio slot to fill
        if (_session.SlotFilled(trackId, scene)) return; // keep the slot's own audio
        if (!VideoAudio.Available) return;
        string name = Path.GetFileName(path);
        try
        {
            string? wav = await VideoAudio.ExtractAsync(path);
            if (wav is null)
            {
                _vm.StatusText = Loc.F("visual.video_no_audio", name);
                return;
            }
            if (!Engine.AddSessionAudioFile(trackId, scene, wav)) return;
            Engine.SetSessionClipName(trackId, scene, Path.GetFileNameWithoutExtension(path));
            double length = Engine.SessionSlotLength(trackId, scene);
            if (length > 0)
                VisualBridge.Current?.VizPlayback(layer, scene, null, length, "Loop", null, setSync: true);
            _session.Refresh();
            Console.Error.WriteLine($"[video] paired {name} with layer {layer} scene {scene} ({length:0.###} beats)");
            _vm.StatusText = length > 0
                ? Loc.F("visual.sound_joined_beats", length)
                : Loc.T("visual.sound_joined");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[video] pairing failed: " + ex.Message);
            _vm.StatusText = Loc.F("visual.could_not_take_sound", name, ex.Message);
        }
    }
}
