// SPDX-License-Identifier: AGPL-3.0-only

// Session loop export: the current deck's filled slots become one folder per scene, a WAV
// per track (exact loop length) and a .mid for instrument clips.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SessionExportTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "nota-session-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            e.SetSceneName(0, "Intro");
            e.SetSceneName(1, "Drop");
            if (e.TryGetSceneProps(1, out var dropProps)) { dropProps.Tempo = 140; e.SetSceneProps(1, dropProps); }

            int t1 = e.AddConsortTrack();
            e.SetTrackName(t1, "Kobot");
            e.AddSessionMidiClip(t1, 0, 4);
            e.SetSessionNotes(t1, 0, new[] { new NotaNote(60, 0, 1, 1f) });
            e.SetSessionClipName(t1, 0, "Eerie Riser");
            e.AddSessionMidiClip(t1, 1, 2);
            e.SetSessionNotes(t1, 1, new[] { new NotaNote(64, 0, 1, 1f) });

            int t2 = e.AddConsortTrack();
            e.SetTrackName(t2, "Model D");
            e.AddSessionMidiClip(t2, 0, 2);
            e.SetSessionNotes(t2, 0, new[] { new NotaNote(48, 0, 1, 1f) });
            e.SetTrackMute(t2, true);   // export must not leave it muted
            e.AddAudioTrack();          // empty slot: no file
            int master = e.MasterTrackId;
            int masterFx = e.AddBuiltinDevice(master, 0);

            var request = new SessionExportRequest(root, 48000, WavBitDepth.Pcm24, true, false);
            var result = new AudioExporter().ExportSessionLoops(e, request);
            bool restoredMute = false;
            for (int i = 0; i < e.TrackCount; i++)
                if (e.TryGetTrackInfo(i, out var ti) && ti.Id == t2) restoredMute = ti.Muted != 0;
            yield return (result.AudioFiles == 3 && result.MidiFiles == 3 && result.Scenes == 2,
                $"session export wrote 3 loops, 3 MIDI files in 2 scene folders (got {result.AudioFiles}/{result.MidiFiles}/{result.Scenes})");

            string intro = Path.Combine(root, "01 Intro");
            string dropFolder = Path.Combine(root, "02 Drop");
            yield return (Directory.Exists(intro) && Directory.Exists(dropFolder),
                "session export creates one folder per filled scene");
            yield return (File.Exists(Path.Combine(intro, "01 Kobot - Eerie Riser.wav")) &&
                          File.Exists(Path.Combine(intro, "02 Model D.wav")) &&
                          File.Exists(Path.Combine(dropFolder, "01 Kobot.wav")),
                "session export names files by track and clip");

            long Frames(string path)
            {
                var bytes = File.ReadAllBytes(path);
                return bytes.Length >= 44 ? (bytes.Length - 44) / (2 * 3) : 0;
            }
            long introFrames = Frames(Path.Combine(intro, "01 Kobot - Eerie Riser.wav"));
            long dropFrames = Frames(Path.Combine(dropFolder, "01 Kobot.wav"));
            yield return (Math.Abs(introFrames - 96000) <= 1,
                $"a 4-beat loop at 120 BPM is 96 000 frames ({introFrames})");
            yield return (Math.Abs(dropFrames - 41143) <= 1,
                $"a 2-beat loop at the scene's 140 BPM is 41 143 frames ({dropFrames})");

            static double FirstPeak(string path)
            {
                var bytes = File.ReadAllBytes(path);
                double peak = 0;
                int at = 44;
                for (int i = 0; i < 512 && at + 2 < bytes.Length; i++, at += 3)
                {
                    int v = bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16);
                    if ((v & 0x800000) != 0) v |= ~0xFFFFFF;
                    peak = Math.Max(peak, Math.Abs(v) / 8388608.0);
                }
                return peak;
            }
            yield return (FirstPeak(Path.Combine(intro, "01 Kobot - Eerie Riser.wav")) > 1e-4,
                "a loop starts at its first sample (no launch block of silence)");
            string midi = Path.Combine(intro, "01 Kobot - Eerie Riser.mid");
            var mb = File.ReadAllBytes(midi);
            yield return (mb.Length > 14 && mb[0] == (byte)'M' && mb[1] == (byte)'T' && mb[2] == (byte)'h' && mb[3] == (byte)'d',
                "session export writes a Standard MIDI File per instrument clip");
            yield return (restoredMute && Math.Abs(e.Bpm - 120) < 1e-6 && e.CurrentDeck == 0 &&
                          !e.DeviceBypassed(master, masterFx),
                "session export restores mutes, tempo, deck and master effects afterwards");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { /* temp */ }
        }
    }
}
