// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Random MIDI parts (Random… in the clip editor): the genre book, determinism, the key /
// mode contract, part registers, bass/melody monophony, pad chords, the motif, density,
// octave, length trimming and genre blending. Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --midi-part`.

using Nota.Application;
using Nota.Application.Midi;

namespace Nota.SmokeTest;

internal static class MidiPartTests
{
    private const int Seed = 424242;

    private static List<NotaNote> Gen(MidiPart part, string genre, int bars = 4, double density = .6,
        double variation = .25, int root = 0, int scale = 1, int octave = 0,
        string? genreB = null, double mix = .5, int lengthBars = 0, int seed = Seed, int beatsPerBar = 4)
        => MidiPartGenerator.Generate(beatsPerBar * bars, beatsPerBar,
            new MidiPartOptions(part, genre, genreB, mix, root, scale, density, variation, octave, lengthBars), seed);

    private static bool Same(IReadOnlyList<NotaNote> a, IReadOnlyList<NotaNote> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Pitch != b[i].Pitch
                || Math.Abs(a[i].StartBeat - b[i].StartBeat) > 1e-9
                || Math.Abs(a[i].LengthBeats - b[i].LengthBeats) > 1e-9
                || Math.Abs(a[i].Velocity - b[i].Velocity) > 1e-6) return false;
        return true;
    }

    private static bool SameShape(IReadOnlyList<NotaNote> a, IReadOnlyList<NotaNote> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Pitch != b[i].Pitch || Math.Abs(a[i].StartBeat - b[i].StartBeat) > 1e-9) return false;
        return true;
    }

    private static List<NotaNote> Bar(IReadOnlyList<NotaNote> notes, int bar, int beats = 4)
        => notes.Where(n => n.StartBeat >= bar * beats - 1e-9 && n.StartBeat < (bar + 1) * beats - 1e-9)
            .Select(n => { var m = n; m.StartBeat -= bar * beats; return m; })
            .ToList();

    private static List<List<NotaNote>> Onsets(IReadOnlyList<NotaNote> notes)
    {
        var groups = new List<List<NotaNote>>();
        foreach (var n in notes.OrderBy(x => x.StartBeat).ThenBy(x => x.Pitch))
        {
            if (groups.Count > 0 && Math.Abs(n.StartBeat - groups[^1][0].StartBeat) < 1e-6) groups[^1].Add(n);
            else groups.Add(new List<NotaNote> { n });
        }
        return groups;
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var drumIds = DrumPatternGenerator.Genres.Select(g => g.Id).ToArray();
        var partIds = MidiPartGenerator.Genres.Select(g => g.Id).ToArray();
        yield return (partIds.SequenceEqual(drumIds),
            "the MIDI genre book matches the drum book (same ids, same order)");

        var parts = new[] { MidiPart.Bassline, MidiPart.Pad, MidiPart.Melody };

        // Every genre × every part writes something legal inside the clip.
        int empty = 0, illegal = 0;
        foreach (var genre in MidiPartGenerator.Genres)
            foreach (var part in parts)
            {
                var notes = Gen(part, genre.Id);
                if (notes.Count == 0) { empty++; continue; }
                foreach (var n in notes)
                    if (n.Pitch is < 0 or > 127 || n.Velocity is <= 0 or > 1 || n.LengthBeats <= 0
                        || n.StartBeat < -1e-9 || n.StartBeat + n.LengthBeats > 16.000001)
                    {
                        illegal++;
                        break;
                    }
            }
        yield return (empty == 0, $"every genre writes notes for all three parts ({empty} empty)");
        yield return (illegal == 0, $"every note stays inside the clip and inside MIDI range ({illegal} illegal)");

        // Determinism: same settings and seed, same notes; a different seed varies.
        int nondeterministic = 0, identical = 0;
        foreach (var part in parts)
        {
            var a = Gen(part, "house");
            var b = Gen(part, "house");
            if (!Same(a, b)) nondeterministic++;
            var c = Gen(part, "house", seed: Seed + 1);
            if (Same(a, c)) identical++;
        }
        yield return (nondeterministic == 0, $"the same seed gives the same take ({nondeterministic} differed)");
        yield return (identical == 0, $"a different seed changes the take ({identical} did not)");

        // Key and mode: every pitch belongs to the scale, in every genre and part.
        var mask = MidiScales.MaskAt(1);   // natural minor
        int offScale = 0;
        foreach (var genre in MidiPartGenerator.Genres)
            foreach (var part in parts)
                foreach (var n in Gen(part, genre.Id))
                    if (!MidiScales.Contains(mask, 0, n.Pitch)) offScale++;
        yield return (offScale == 0, $"every note follows the key and mode ({offScale} off-scale)");

        // Chromatic is folded back to a tonal scale, not stacked as semitones.
        int chromaticOff = 0;
        foreach (var part in parts)
            foreach (var n in Gen(part, "house", scale: 9))
                if (!MidiScales.Contains(mask, 0, n.Pitch)) chromaticOff++;
        yield return (chromaticOff == 0,
            $"Chromatic falls back to natural minor instead of stacking semitones ({chromaticOff} off-scale)");

        // Bass and melody are one line, in their own register.
        int overlaps = 0, bassRange = 0, melodyRange = 0, melodyLeaps = 0;
        foreach (var genre in MidiPartGenerator.Genres)
        {
            var bass = Gen(MidiPart.Bassline, genre.Id, bars: 8);
            for (int i = 0; i + 1 < bass.Count; i++)
                if (bass[i].StartBeat + bass[i].LengthBeats > bass[i + 1].StartBeat + 1e-6) overlaps++;
            if (bass.Any(n => n.Pitch is < 18 or > 58)) bassRange++;

            var melody = Gen(MidiPart.Melody, genre.Id, bars: 8);
            for (int i = 0; i + 1 < melody.Count; i++)
                if (melody[i].StartBeat + melody[i].LengthBeats > melody[i + 1].StartBeat + 1e-6) overlaps++;
            if (melody.Any(n => n.Pitch is < 43 or > 102)) melodyRange++;
            if (melody.Max(n => n.Pitch) - melody.Min(n => n.Pitch) > 26) melodyLeaps++;
        }
        yield return (overlaps == 0, $"bass and melody are monophonic ({overlaps} overlaps)");
        yield return (bassRange == 0, $"the bassline stays in the bass register ({bassRange} genres outside)");
        yield return (melodyRange == 0, $"the melody stays in the melody register ({melodyRange} genres outside)");
        yield return (melodyLeaps == 0, $"the melody stays within two octaves, not jumping registers ({melodyLeaps} genres wandered)");

        // Pad: chords, not single notes, and at least two changes across four bars.
        int thin = 0, staticPads = 0, padRange = 0;
        foreach (var genre in MidiPartGenerator.Genres)
        {
            var pad = Gen(MidiPart.Pad, genre.Id);
            var onsets = Onsets(pad);
            if (onsets.Count < 2) staticPads++;
            foreach (var chord in onsets)
                if (chord.Select(n => n.Pitch).Distinct().Count() < 2) { thin++; break; }
            if (pad.Any(n => n.Pitch is < 34 or > 92)) padRange++;
        }
        yield return (thin == 0, $"every pad onset is a chord ({thin} genres had a single note)");
        yield return (staticPads == 0, $"the pad moves during the clip ({staticPads} genres sat still)");
        yield return (padRange == 0, $"the pad stays in its register ({padRange} genres outside)");

        // The melody repeats its motif: with no variation, bar 8 is bar 0 again
        // (eight bars apart, so the progression is back at its first chord in both
        // four- and eight-bar loops).
        int noRepeat = 0;
        foreach (var genre in MidiPartGenerator.Genres)
        {
            var melody = Gen(MidiPart.Melody, genre.Id, bars: 12, variation: 0);
            if (!SameShape(Bar(melody, 8), Bar(melody, 0))) noRepeat++;
        }
        yield return (noRepeat == 0, $"the melody repeats its motif instead of wandering ({noRepeat} genres did not)");

        // Density keeps the take alive at 0 and fills it at 1.
        int collapsed = 0, densityIgnored = 0;
        foreach (var part in parts)
        {
            var low = Gen(part, "house", density: 0);
            var high = Gen(part, "house", density: 1);
            if (low.Count == 0) collapsed++;
            if (part != MidiPart.Pad && high.Count <= low.Count) densityIgnored++;
            if (part == MidiPart.Pad && high.Count < low.Count) densityIgnored++;
        }
        yield return (collapsed == 0, $"density 0 still writes the signature notes ({collapsed} empty)");
        yield return (densityIgnored == 0, $"density fills the part at 1 ({densityIgnored} parts ignored it)");

        // Octave lifts the whole part.
        int notHigher = 0;
        foreach (var part in parts)
        {
            var low = Gen(part, "house", octave: -1);
            var high = Gen(part, "house", octave: 1);
            if (low.Count == 0 || high.Count == 0 || high.Average(n => n.Pitch) <= low.Average(n => n.Pitch)) notHigher++;
        }
        yield return (notHigher == 0, $"Octave moves the part up a register ({notHigher} parts did not)");

        // Length bars trim the take; the whole-clip option fills the clip.
        int overrun = 0, shortTake = 0;
        foreach (var part in parts)
        {
            var two = MidiPartGenerator.Generate(32, 4,
                new MidiPartOptions(part, "house", LengthBars: 2), Seed);
            if (two.Any(n => n.StartBeat >= 8 + 1e-9 || n.StartBeat + n.LengthBeats > 8 + 1e-6)) overrun++;

            var whole = MidiPartGenerator.Generate(32, 4,
                new MidiPartOptions(part, "house", LengthBars: 0), Seed);
            if (!whole.Any(n => n.StartBeat >= 8)) shortTake++;
        }
        yield return (overrun == 0, $"Length stops the take at the chosen bars ({overrun} overran)");
        yield return (shortTake == 0, $"Whole clip keeps writing past bar 2 ({shortTake} stopped)");

        // Mixing a second genre: 0% is the first genre exactly, 100% is a different take.
        int mixDrift = 0, mixMute = 0;
        foreach (var part in parts)
        {
            var plain = Gen(part, "house");
            var zero = Gen(part, "house", genreB: "techno", mix: 0);
            var full = Gen(part, "house", genreB: "techno", mix: 1);
            if (!Same(plain, zero)) mixDrift++;
            if (Same(plain, full)) mixMute++;
        }
        yield return (mixDrift == 0, $"Mix at 0% is the first genre untouched ({mixDrift} parts drifted)");
        yield return (mixMute == 0, $"Mix at 100% actually changes the take ({mixMute} parts ignored it)");

        // A different meter still lands on the grid and inside the clip.
        int meterBad = 0;
        foreach (var part in parts)
        {
            var notes = Gen(part, "house", bars: 4, beatsPerBar: 3);
            if (notes.Count == 0 || notes.Any(n => n.StartBeat < -1e-9 || n.StartBeat + n.LengthBeats > 12.000001)) meterBad++;
        }
        yield return (meterBad == 0, $"a 3/4 clip is handled too ({meterBad} parts failed)");

        yield return (new[] { MidiPart.Bassline, MidiPart.Pad, MidiPart.Melody }
            .Select(MidiPartGenerator.PartName).Distinct().Count() == 3, "the three part names are localized and distinct");
    }
}
