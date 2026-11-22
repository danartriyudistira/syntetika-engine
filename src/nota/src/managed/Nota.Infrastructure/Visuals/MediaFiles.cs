// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Media files for visual clips (Syntetika Engine).
//
// A visual media file is copied into the project bundle as
// `visuals/media/<sha256>.<ext>` (content-addressed, like Nota's own samples), so a project
// stays portable and the same file is stored once. Projects that are not saved yet stage
// their files in the app's data folder; saving moves the composition to bundle-relative
// paths and the files themselves into the bundle.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nota.Infrastructure.Visuals;

public static class MediaFiles
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".mxf", ".mpg", ".mpeg",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));
    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));
    public static bool IsMedia(string path) => IsVideo(path) || IsImage(path);

    public static string KindOf(string path) => IsVideo(path) ? "video" : "image";

    /// <summary>Copies a media file once and returns its absolute path (bundle or staging).</summary>
    public static string Prepare(string source, string? bundleDir)
    {
        string hash;
        using (var stream = File.OpenRead(source))
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        string ext = Path.GetExtension(source).ToLowerInvariant();
        string name = hash + ext;

        string dir = bundleDir is { Length: > 0 }
            ? Path.Combine(bundleDir, "visuals", "media")
            : NotaPaths.SubDir("media-staging");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, name);
        if (!File.Exists(target)) File.Copy(source, target);
        return target;
    }
}
