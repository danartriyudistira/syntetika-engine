// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Live tempo changes with warped audio: SetBpm must not rebuild stretch caches on the
// message thread. A current cache is still used at rest; after a tempo change the live
// ClipWarpStream reads the new tempo. Also runnable alone: `--tempo-live`.

using System.Diagnostics;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LiveTempoTests
{
    private const int Sr = 44100;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_tempo_live.wav");
        WavWriter.WriteSine(wav, seconds: 4.0, freq: 220, sampleRate: Sr);

        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetTimeSignature(4, 4);
        var tracks = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            int t = e.AddAudioTrack();
            int c = e.AddAudioClip(t, wav, 0.0);
            e.SetClipWarp(t, c, true, 3);
            e.SetClipWarpLength(t, c, 32);
            tracks.Add(t);
        }
        // Materialize the caches once, exactly as a project load does, so the measurement
        // covers only the tempo change.
        while (e.WarpBuildPoll() > 0) Thread.Sleep(1);
        yield return (e.WarpBuildRemaining == 0, "live tempo: warped clips start from a current cache");

        var times = new List<long>();
        bool landed = true;
        foreach (double bpm in new[] { 124.0, 128.0, 132.0, 126.0 })
        {
            var sw = Stopwatch.StartNew();
            e.SetBpm(bpm);
            times.Add(sw.ElapsedMilliseconds);
            e.RenderOffline(new float[2], 1, Sr);   // drain the SetBpm command
            landed &= Math.Abs(e.Bpm - bpm) < 1e-9;
        }
        yield return (landed, "live tempo: every SetBpm lands");
        yield return (times.Max() < 50, $"live tempo: a BPM change is not a cache rebuild (max {times.Max()} ms, all {string.Join('/', times)} ms)");
        yield return (e.WarpBuildRemaining > 0, "live tempo: the old cache is left stale, not rebuilt on the UI thread");

        e.Seek(0); e.Play();
        var buf = new float[512 * 2];
        bool finite = true; double rms = 0;
        for (int i = 0; i < 16; i++)
        {
            e.RenderOffline(buf, 512);
            for (int k = 0; k < buf.Length; k++)
            {
                if (!float.IsFinite(buf[k])) finite = false;
                rms += buf[k] * buf[k];
            }
        }
        e.StopTransport();
        rms = Math.Sqrt(rms / (16 * 512 * 2));
        yield return (finite && rms > 1e-3, $"live tempo: warped audio plays after the tempo changes (rms={rms:F4})");

        // A copy made while live must get its own voice, and still play.
        int dup = e.DuplicateTrack(tracks[0]);
        e.Seek(0); e.Play();
        e.RenderOffline(buf, 512);
        e.StopTransport();
        yield return (dup > 0 && buf.All(float.IsFinite), "live tempo: a duplicated warped track gets a clean voice");

        try { File.Delete(wav); } catch { }
    }
}
