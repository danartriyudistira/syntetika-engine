// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A project can be one file: a ZIP container holding the same bundle Nota always wrote
// (project.json, sidecars, samples, plugin-states, visuals, .history). Opening it extracts to
// a working folder and the rest of the app keeps using a bundle directory; saving packs the
// folder back atomically. `.nota` bundles stay readable and saveable.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Nota.Infrastructure;

public static class ProjectContainer
{
    public const string Extension = ".syntetika";
    private const string PackageDir = ".package";
    private const string PackageManifest = ".package/manifest.json";
    private const int FormatVersion = 1;

    public static bool IsContainer(string path)
        => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>A fresh bundle folder under the app's working folder.</summary>
    public static string NewWorkingDir()
    {
        string root = NotaPaths.SubDir("working");
        string dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>True for a folder this app extracted from a container (a transient working copy).</summary>
    public static bool IsWorkingDir(string path)
    {
        string root = Path.GetFullPath(NotaPaths.SubDir("working")) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root,
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Drops stale working folders (a crash leaves one behind).</summary>
    public static void CleanupWorkingDirs(string? keep = null)
    {
        string root = NotaPaths.SubDir("working");
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                if (keep is { Length: > 0 } && SamePath(dir, keep)) continue;
                try { Directory.Delete(dir, recursive: true); } catch { /* in use */ }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Expands a container into <paramref name="destination"/> (which should be empty).</summary>
    public static void Extract(string file, string destination)
    {
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination);
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        using ZipArchive zip = ZipFile.OpenRead(file);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string rel = entry.FullName.Replace('\\', '/');
            if (rel.Length == 0 || rel.EndsWith('/')) continue;                 // directory entry
            if (rel.StartsWith(PackageDir + "/", StringComparison.Ordinal)) continue;
            if (rel.StartsWith("__MACOSX/", StringComparison.Ordinal)) continue;
            if (rel.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe path in {Path.GetFileName(file)}: {rel}");

            string target = Path.GetFullPath(Path.Combine(root, rel));
            if (!target.StartsWith(prefix, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Path escapes the project folder: {rel}");
            string? parent = Path.GetDirectoryName(target);
            if (parent is { Length: > 0 }) Directory.CreateDirectory(parent);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>Copies a bundle folder (the Save As migration keeps the old folder untouched).</summary>
    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(from, file);
            string target = Path.Combine(to, rel);
            string? parent = Path.GetDirectoryName(target);
            if (parent is { Length: > 0 }) Directory.CreateDirectory(parent);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Packs a bundle folder into a container file, atomically (temp + rename).</summary>
    public static void Pack(string bundleDir, string file)
    {
        string full = Path.GetFullPath(file);
        string? parent = Path.GetDirectoryName(full);
        if (parent is { Length: > 0 }) Directory.CreateDirectory(parent);
        string tmp = full + ".tmp";
        try
        {
            using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                WriteManifest(zip);
                foreach (string path in Directory.EnumerateFiles(bundleDir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(bundleDir, path).Replace('\\', '/');
                    if (rel.StartsWith(PackageDir + "/", StringComparison.Ordinal)) continue;
                    if (Path.GetFileName(rel) == ".DS_Store") continue;
                    if (rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    ZipArchiveEntry entry = zip.CreateEntry(rel, CompressionFor(rel));
                    using Stream dst = entry.Open();
                    using FileStream src = File.OpenRead(path);
                    src.CopyTo(dst);
                }
            }
            File.Move(tmp, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    private static void WriteManifest(ZipArchive zip)
    {
        ZipArchiveEntry entry = zip.CreateEntry(PackageManifest, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", "syntetika-project");
        writer.WriteNumber("version", FormatVersion);
        writer.WriteString("app", Nota.Application.Branding.ProductName);
        writer.WriteString("product", Nota.Application.Branding.ProductVersion);
        writer.WriteString("savedAt", DateTimeOffset.UtcNow.ToString("o"));
        writer.WriteEndObject();
    }

    // Already-compressed media is stored; JSON and samples compress well.
    private static CompressionLevel CompressionFor(string rel)
    {
        string ext = Path.GetExtension(rel).ToLowerInvariant();
        return ext is ".mp4" or ".m4v" or ".mov" or ".mkv" or ".webm" or ".avi" or ".mxf"
            or ".mpg" or ".mpeg" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"
            or ".mp3" or ".flac" or ".ogg" or ".aac" or ".m4a"
            ? CompressionLevel.NoCompression
            : CompressionLevel.Optimal;
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                         Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                         OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
