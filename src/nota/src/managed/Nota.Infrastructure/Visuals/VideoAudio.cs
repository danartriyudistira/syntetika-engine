// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Syntetika Engine — video to audio for Nota.
//
// A video clip is performed by openVJ; Nota performs its sound. The audio track is extracted
// once with the FFmpeg that ships in the app (decode-only, LGPL) into a content-addressed WAV
// in the profile folder. Once the project is saved, Nota's normal bundle handling copies the
// sample into `samples/` like any other import.

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Nota.Infrastructure.Visuals;

public static class VideoAudio
{
    /// <summary>The FFmpeg shipped with the app, the env override, or the usual locations.</summary>
    public static string? FfmpegPath { get; } = Resolve("ffmpeg");
    public static string? FfprobePath { get; } = Resolve("ffprobe");

    private static string? Resolve(string tool)
    {
        string exe = tool;
        var dirs = new System.Collections.Generic.List<string>();
        if (Environment.GetEnvironmentVariable("SYNTETIKA_FFMPEG") is { Length: > 0 } env) dirs.Add(env);
        if (Environment.GetEnvironmentVariable("OPENVJ_FFMPEG_DIR") is { Length: > 0 } ov) dirs.Add(ov);
        if (VisualEngineProcess.FindBinary() is { } visual)
        {
            string? visualDir = Path.GetDirectoryName(visual);
            if (visualDir is not null)
            {
                dirs.Add(visualDir);
                dirs.Add(Path.Combine(visualDir, "..", "ffmpeg", "bin"));   // dev tree
            }
        }
        dirs.Add(AppContext.BaseDirectory);
        dirs.Add(Path.Combine(AppContext.BaseDirectory, "..", "Helpers"));
        dirs.Add("/opt/homebrew/bin");
        dirs.Add("/usr/local/bin");
        dirs.Add("/usr/bin");
        foreach (string dir in dirs)
        {
            string candidate = Path.GetFullPath(Path.Combine(dir, exe));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static bool Available => FfmpegPath is not null && FfprobePath is not null;

    /// <summary>True when the file has at least one audio stream.</summary>
    public static async Task<bool> HasAudioAsync(string video, CancellationToken ct = default)
    {
        if (FfprobePath is null) return false;
        var psi = new ProcessStartInfo(FfprobePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams"); psi.ArgumentList.Add("a");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("stream=codec_type");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("csv=p=0");
        psi.ArgumentList.Add(video);
        using var p = Process.Start(psi);
        if (p is null) return false;
        string output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return output.Contains("audio", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the audio to a cached WAV, or null when the file has no audio.</summary>
    public static async Task<string?> ExtractAsync(string video, CancellationToken ct = default)
    {
        if (FfmpegPath is null) throw new InvalidOperationException("FFmpeg is not available.");
        if (!await HasAudioAsync(video, ct)) return null;

        string hash = await Task.Run(() =>
        {
            using var stream = File.OpenRead(video);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }, ct);

        string dir = NotaPaths.SubDir("video-audio-staging");
        string target = Path.Combine(dir, hash + ".wav");
        if (File.Exists(target) && new FileInfo(target).Length > 44) return target;

        string tmp = target + ".part.wav";
        var psi = new ProcessStartInfo(FfmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
        {
            "-hide_banner", "-nostdin", "-y", "-i", video,
            "-vn", "-sn", "-dn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", tmp,
        }) psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        string errors = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 || !File.Exists(tmp))
            throw new InvalidOperationException("FFmpeg could not extract the audio: " + LastLine(errors));
        File.Move(tmp, target, overwrite: true);
        return target;
    }

    private static string LastLine(string text)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? "" : lines[^1].Trim();
    }
}
