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
using Nota.Infrastructure.Visuals;

namespace Nota.App;

public partial class MainWindow
{
    private static readonly FilePickerFileType VideoPicker = new("Video")
    {
        Patterns = new[] { "*.mp4", "*.m4v", "*.mov", "*.mkv", "*.webm", "*.avi", "*.mxf", "*.mpg", "*.mpeg" },
    };

    /// <summary>File picker, then the import. Bound to a Session slot's context menu.</summary>
    private async Task ImportVideoToSlotAsync(int trackId, int scene)
    {
        if (_vm is null) return;
        if (!VideoAudio.Available)
        {
            _vm.StatusText = "Video import needs FFmpeg (the app ships it; check Contents/Helpers).";
            return;
        }
        var files = await this.PickFilesAsync(new FilePickerOpenOptions
        {
            Title = "Import video (audio + visual)",
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
            _vm.StatusText = $"Image on the visual layer: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Could not add {Path.GetFileName(path)}: {ex.Message}";
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
            _vm.StatusText = "Choose a Session slot on an audio track for a video's sound.";
            return;
        }
        Console.Error.WriteLine($"[video] import start {path} ffmpeg={VideoAudio.FfmpegPath} probe={VideoAudio.FfprobePath}");
        _vm.StatusText = $"Importing {name}…";
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
                _vm.StatusText = $"Audio could not be extracted ({ex.Message}); importing the visual only.";
            }

            if (wav is not null && Engine.AddSessionAudioFile(trackId, scene, wav))
            {
                audio = true;
                string clipName = Path.GetFileNameWithoutExtension(path);
                if (clipName.Length > 0) Engine.SetSessionClipName(trackId, scene, clipName);
            }
            else if (wav is null)
            {
                _vm.StatusText = $"{name} has no usable audio — importing the visual only.";
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
                    ? $"Imported {name}: audio in the slot, video on the visual layer ({length:0.##} beats)."
                    : $"Imported {name}: audio in the slot, video assigned.";
            }
            else
            {
                _vm.StatusText = $"Imported {name}: visual only (no audio).";
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[video] failed: " + ex);
            _vm.StatusText = $"Could not import {name}: {ex.Message}";
        }
    }
}
