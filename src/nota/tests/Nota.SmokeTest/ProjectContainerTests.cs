// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The one-file project: a bundle packs into a .syntetika ZIP and extracts back byte-for-byte,
// media and history included; unsafe paths are refused.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --container`.

using System.IO.Compression;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ProjectContainerTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (ProjectContainer.IsContainer("My Song.syntetika") && !ProjectContainer.IsContainer("My Song.nota"),
            "the container extension is recognized");

        string tmp = Path.Combine(Path.GetTempPath(), "nota-container-" + Guid.NewGuid().ToString("N"));
        string bundle = Path.Combine(tmp, "song.nota");
        string file = Path.Combine(tmp, "song.syntetika");
        string outDir = Path.Combine(tmp, "opened");
        try
        {
            Directory.CreateDirectory(Path.Combine(bundle, "visuals", "media"));
            Directory.CreateDirectory(Path.Combine(bundle, "samples"));
            Directory.CreateDirectory(Path.Combine(bundle, ".history"));
            File.WriteAllText(Path.Combine(bundle, "project.json"),
                "{\"formatVersion\":36,\"transport\":{\"bpm\":123},\"tracks\":[{\"type\":1,\"midiClips\":[{\"startBeat\":0,\"lengthBeats\":8}]}]}");
            File.WriteAllText(Path.Combine(bundle, "track-view.json"), "{\"tracks\":[]}");
            File.WriteAllText(Path.Combine(bundle, "visuals", "composition.json"), "{\"composition\":{}}");
            File.WriteAllBytes(Path.Combine(bundle, "visuals", "media", "clip.mp4"), [1, 2, 3, 4, 5]);
            File.WriteAllBytes(Path.Combine(bundle, "samples", "kick.wav"), [9, 8, 7]);
            File.WriteAllText(Path.Combine(bundle, ".history", "versions.json"), "{\"versions\":[]}");
            File.WriteAllText(Path.Combine(bundle, ".DS_Store"), "junk");
            File.WriteAllText(Path.Combine(bundle, "leftover.tmp"), "junk");

            ProjectContainer.Pack(bundle, file);
            yield return (File.Exists(file) && new FileInfo(file).Length > 0, "a bundle packs into one file");

            ProjectContainer.Extract(file, outDir);
            bool Same(string rel) => File.ReadAllBytes(Path.Combine(bundle, rel))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(outDir, rel)));
            yield return (Same("project.json") && Same("track-view.json") && Same("visuals/composition.json"),
                "the manifest and sidecars come back unchanged");
            yield return (Same("visuals/media/clip.mp4") && Same("samples/kick.wav"),
                "media and samples come back unchanged");
            yield return (Same(".history/versions.json"), "the version history travels inside the file");
            yield return (!File.Exists(Path.Combine(outDir, ".DS_Store")) && !File.Exists(Path.Combine(outDir, "leftover.tmp")),
                "Finder/temp junk is left out");
            yield return (!Directory.Exists(Path.Combine(outDir, ".package")), "the package manifest stays inside the zip");

            using (ZipArchive zip = ZipFile.OpenRead(file))
                yield return (zip.GetEntry(".package/manifest.json") is not null, "the container carries a small manifest");

            var summary = new ProjectSummaryReader(Path.Combine(tmp, "summaries.json")).Get(file);
            yield return (summary is { Bpm: 123, TrackCount: 1 }, "the browser reads a container's summary");

            // Zip-slip: an entry that climbs out of the target must be refused.
            string evil = Path.Combine(tmp, "evil.syntetika");
            using (ZipArchive zip = ZipFile.Open(evil, ZipArchiveMode.Create))
            {
                using StreamWriter w = new(zip.CreateEntry("../escaped.txt").Open());
                w.Write("no");
            }
            bool refused = false;
            try { ProjectContainer.Extract(evil, Path.Combine(tmp, "evil-out")); }
            catch (InvalidDataException) { refused = true; }
            yield return (refused, "an entry that escapes the project folder is refused");

            string work = ProjectContainer.NewWorkingDir();
            yield return (ProjectContainer.IsWorkingDir(work) && !ProjectContainer.IsWorkingDir(bundle),
                "working folders are recognized");
            try { Directory.Delete(work, recursive: true); } catch { }
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
