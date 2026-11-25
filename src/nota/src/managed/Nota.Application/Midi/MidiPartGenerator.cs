// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Random instrument parts: a bassline, a pad or a melody, from a genre, the clip's key and
// mode, and a seed. Pure and deterministic — the same options and seed always give the same
// notes — so the Random MIDI dialog can preview a take in the roll, reroll it, and commit it
// as one undo step. The genre book is the same vocabulary as the Random drum dialog; each
// entry carries its own groove for all three parts, so "Amapiano pad" and "Jazz bassline"
// are different jobs rather than one template with a different name.

namespace Nota.Application.Midi;

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application.Localization;

/// <summary>The instrument part a random take writes.</summary>
public enum MidiPart
{
    Bassline,
    Pad,
    Melody,
}

/// <summary>One genre as the Random MIDI dialog offers it. <see cref="Id"/> is shared with
/// the drum genre book; <see cref="Name"/> is the menu text.</summary>
public sealed class MidiPartGenre
{
    public string Id { get; }
    public string Name { get; }
    internal MidiPartStyle Style { get; }

    internal MidiPartGenre(MidiPartStyle style)
    {
        Style = style;
        Id = style.Id;
        // The drum book owns the genre names, so the two random dialogs cannot drift apart.
        Name = DrumPatternGenerator.NameOf(style.Id);
    }
}

/// <summary>What to generate. <see cref="GenreA"/> is the main genre; <see cref="GenreB"/>
/// blends a second one, weighted by <see cref="Mix"/> (0 = A, 1 = B). <see cref="Root"/> is
/// a pitch class 0..11, <see cref="ScaleIndex"/> an index into <see cref="MidiScales.Names"/>.
/// <see cref="LengthBars"/> 0 fills the whole clip; otherwise the take stops after that many
/// bars. <see cref="Octave"/> shifts the register by an octave (-1..1).</summary>
public sealed record MidiPartOptions(
    MidiPart Part,
    string GenreA = "house",
    string? GenreB = null,
    double Mix = 0.5,
    int Root = 0,
    int ScaleIndex = 1,
    double Density = 0.5,
    double Variation = 0.25,
    int Octave = 0,
    int LengthBars = 0);

/// <summary>The data behind one genre, per part: a 16-step bass pulse, a chord-progression
/// book (scale degrees, one root per chord slot), a pad gait, and a melody gait.</summary>
internal sealed record MidiPartStyle(
    string Id,
    double[] BassPulse, double BassGate, double BassJump,
    int[][] Progressions,
    int PadVoices, bool PadSeventh, double PadGate, double PadPerBar,
    double[] MelodyRhythm, double MelodyRest, int MelodyRange, int MelodyMotifBars);

public static class MidiPartGenerator
{
    // ---- the genre book ------------------------------------------------------------------

    // 16-step bass pulse probabilities. A step at 1 is a signature hit; lower values are
    // optional hits the Density knob scales in or out.
    private static double[] Pulse(params (int Step, double P)[] hits)
    {
        var a = new double[16];
        foreach (var (step, p) in hits)
            if (step is >= 0 and < 16) a[step] = Math.Max(a[step], Math.Clamp(p, 0, 1));
        return a;
    }

    /// <summary>A 16-step profile at <paramref name="floor"/>, with the listed steps lifted.</summary>
    private static double[] Grid(double floor, params (int Step, double P)[] hits)
    {
        var a = new double[16];
        Array.Fill(a, Math.Clamp(floor, 0, 1));
        foreach (var (step, p) in hits)
            if (step is >= 0 and < 16) a[step] = Math.Max(a[step], Math.Clamp(p, 0, 1));
        return a;
    }

    // Chord loops in scale degrees, one entry per chord slot. All loops are diatonic, so
    // they work under any seven-note mode; a major key turns i–VI–III–VII into I–vi–iii–VII
    // rather than breaking it.
    private static readonly int[][] HouseLoops = { new[] { 0, 5, 2, 6 }, new[] { 0, 3, 5, 4 } };
    private static readonly int[][] DeepLoops = { new[] { 0, 3, 5, 4 }, new[] { 0, 5, 2, 6 } };
    private static readonly int[][] HipHopLoops = { new[] { 0, 5, 3, 4 }, new[] { 0, 3, 4, 5 } };
    private static readonly int[][] PopLoops = { new[] { 0, 4, 5, 3 }, new[] { 0, 3, 5, 4 } };
    private static readonly int[][] LatinLoops = { new[] { 0, 3, 5, 4 }, new[] { 0, 4, 3, 5 } };
    private static readonly int[][] JazzLoops = { new[] { 1, 4, 0, 5 }, new[] { 0, 3, 5, 4 } };
    private static readonly int[][] AmapianoLoops = { new[] { 0, 5, 3, 4, 0, 5, 2, 6 } };

    private static MidiPartStyle Style(
        string id,
        double[] bass, double bassGate, double bassJump,
        int[][] progressions,
        int padVoices, bool padSeventh, double padGate, double padPerBar,
        double[] melody, double melodyRest, int melodyRange, int melodyMotifBars) =>
        new(id, bass, bassGate, bassJump, progressions,
            padVoices, padSeventh, padGate, padPerBar, melody, melodyRest, melodyRange, melodyMotifBars);

    private static readonly MidiPartStyle[] s_styles =
    {
        Style("house",
            Pulse((0, .25), (2, .95), (6, .95), (10, .95), (14, .95)), .22, .20, HouseLoops,
            3, false, .95, 1, Pulse((0, .30), (2, .70), (4, .35), (6, .70), (8, .30), (10, .75), (12, .35), (14, .70)), .35, 7, 1),
        Style("techno",
            Grid(.50, (0, 1), (4, .90), (8, 1), (12, .90)), .18, .12, new[] { new[] { 0, 0, 5, 3 } },
            3, false, 1.0, 1, Grid(.30, (0, .90), (4, .60), (8, .85), (12, .60)), .55, 5, 1),
        Style("tech-house",
            Pulse((0, .40), (3, .80), (6, .90), (10, .80), (12, .70), (14, .85)), .20, .22,
            new[] { new[] { 0, 5, 3, 4 } },
            3, true, .60, 2, Pulse((0, .30), (3, .60), (6, .70), (10, .70), (13, .50)), .50, 7, 1),
        Style("deep-house",
            Pulse((0, .30), (2, .85), (6, .90), (10, .90), (12, .40), (14, .80)), .30, .18, DeepLoops,
            4, true, 1.0, 1, Pulse((0, .40), (2, .50), (4, .40), (6, .55), (8, .40), (10, .50), (12, .40), (14, .55)), .50, 7, 2),
        Style("hip-hop",
            Pulse((0, .95), (3, .30), (6, .90), (10, .85), (14, .50)), .55, .12, HipHopLoops,
            3, true, 1.0, 1, Pulse((0, .60), (3, .35), (6, .40), (10, .40), (14, .30)), .60, 5, 1),
        Style("trap",
            Pulse((0, 1), (3, .25), (6, .90), (10, .85), (14, .30)), .80, .05,
            new[] { new[] { 0, 5, 2, 6 } },
            3, true, 1.0, 1, Pulse((0, .80), (4, .30), (7, .40), (10, .50), (14, .35)), .60, 5, 1),
        Style("drill",
            Pulse((0, 1), (3, .50), (6, .90), (10, .85), (14, .40)), .60, .10,
            new[] { new[] { 0, 1, 5, 4 } },
            3, true, 1.0, 1, Pulse((0, .70), (3, .40), (6, .50), (10, .50), (14, .40)), .55, 5, 1),
        Style("dnb",
            Pulse((0, .95), (3, .70), (6, .40), (10, .90), (14, .50)), .35, .25,
            new[] { new[] { 0, 5, 3, 4 } },
            4, true, 1.0, 1, Grid(.35, (0, .90), (3, .60), (6, .70), (10, .85), (14, .60)), .45, 7, 2),
        Style("breakbeat",
            Pulse((0, .90), (4, .50), (6, .80), (10, .80), (12, .50), (14, .70)), .30, .20,
            new[] { new[] { 0, 3, 5, 4 } },
            3, false, .80, 2, Grid(.35, (0, .80), (4, .60), (6, .70), (10, .80), (14, .60)), .50, 7, 1),
        Style("disco",
            Pulse((0, 1), (2, .80), (4, .90), (6, .80), (8, .95), (10, .80), (12, .90), (14, .80)), .22, .35, PopLoops,
            3, true, .35, 2, Pulse((0, .70), (2, .50), (4, .65), (6, .50), (8, .70), (10, .50), (12, .65), (14, .50)), .30, 7, 1),
        Style("funk",
            Pulse((0, .90), (3, .70), (6, .85), (8, .75), (10, .70), (13, .60), (15, .50)), .20, .30,
            new[] { new[] { 0, 3, 5, 4 } },
            3, true, .28, 2, Grid(.40, (0, .80), (3, .60), (6, .70), (8, .60), (11, .50), (14, .60)), .45, 7, 1),
        Style("rock",
            Pulse((0, 1), (2, .70), (4, .90), (6, .70), (8, 1), (10, .70), (12, .90), (14, .70)), .40, .15, PopLoops,
            3, false, 1.0, 1, Grid(.35, (0, .85), (4, .70), (8, .80), (12, .70)), .55, 7, 2),
        Style("pop",
            Pulse((0, 1), (4, .90), (8, .95), (12, .90), (14, .50)), .45, .15, PopLoops,
            4, false, 1.0, 1, Pulse((0, .70), (2, .50), (4, .60), (8, .70), (10, .50), (12, .60)), .40, 7, 2),
        Style("reggaeton",
            Pulse((0, 1), (3, .50), (4, .80), (8, 1), (11, .50), (12, .80)), .40, .12,
            new[] { new[] { 0, 5, 3, 4 } },
            3, false, .80, 2, Pulse((0, .60), (3, .50), (6, .50), (8, .60), (11, .50), (14, .50)), .50, 5, 1),
        Style("latin",
            Pulse((0, .90), (3, .70), (6, .80), (8, .85), (10, .70), (14, .60)), .28, .30, LatinLoops,
            3, true, .50, 2, Grid(.35, (0, .80), (3, .60), (6, .60), (8, .70), (10, .55), (14, .60)), .45, 7, 2),
        Style("afrobeat",
            Pulse((0, .90), (3, .75), (6, .85), (8, .90), (10, .70), (14, .65)), .30, .22,
            new[] { new[] { 0, 3, 5, 4 } },
            3, true, .55, 2, Pulse((0, .70), (2, .50), (3, .55), (6, .60), (8, .70), (10, .50), (11, .55), (14, .60)), .40, 7, 2),
        Style("amapiano",
            Pulse((0, .95), (3, .70), (6, .90), (10, .85), (12, .70), (14, .80)), .35, .18, AmapianoLoops,
            4, true, 1.0, 1, Pulse((0, .60), (3, .50), (6, .50), (10, .50), (12, .40), (14, .50)), .50, 7, 2),
        Style("jazz",
            Pulse((0, .90), (4, .85), (8, .90), (12, .85)), .85, .40, JazzLoops,
            4, true, 1.0, 1, Pulse((0, .70), (3, .60), (4, .50), (7, .50), (8, .70), (11, .60), (12, .50), (15, .50)), .50, 9, 2),
        Style("uk-garage",
            Pulse((0, 1), (3, .45), (6, .85), (10, .80), (14, .70)), .30, .28,
            new[] { new[] { 0, 5, 3, 4 } },
            4, true, .70, 2, Pulse((0, .60), (3, .50), (6, .60), (10, .60), (11, .45), (14, .50)), .50, 7, 1),
        Style("lo-fi",
            Pulse((0, .90), (3, .40), (6, .85), (10, .80), (14, .40)), .50, .10, DeepLoops,
            4, true, 1.0, 1, Pulse((0, .50), (4, .40), (7, .40), (10, .45), (14, .30)), .60, 7, 2),
    };

    /// <summary>Every genre, in menu order (popular first, the same order as the drum book).</summary>
    public static IReadOnlyList<MidiPartGenre> Genres { get; } =
        s_styles.Select(s => new MidiPartGenre(s)).ToArray();

    public static MidiPartGenre? Find(string? id) => string.IsNullOrWhiteSpace(id)
        ? null
        : Genres.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string NameOf(string id) => Find(id)?.Name ?? id;

    public static string PartName(MidiPart part) => part switch
    {
        MidiPart.Bassline => Loc.T("midi_random.bassline"),
        MidiPart.Pad => Loc.T("midi_random.pad"),
        _ => Loc.T("midi_random.melody"),
    };

    // ---- generation ----------------------------------------------------------------------

    // Registers wide enough for the Octave knob to mean something: -1 puts the bass at C1,
    // the pad at C2 and the melody at C3, while +1 lifts each of them an octave.
    private const int BassLow = 21, BassHigh = 55;
    private const int PadLow = 36, PadHigh = 90;
    private const int MelodyLow = 45, MelodyHigh = 100;

    private sealed class Job
    {
        public required MidiPartStyle A;
        public MidiPartStyle? B;
        public required double Mix, Density, Variation, End;
        public required int Root, Bars, StepsPerBar, Octave;
        public required double StepBeats, BarBeats;
        public required int[] Degrees;
        public required Random Rng;
    }

    private readonly record struct MotifNote(int Step, int Degree);

    /// <summary>Notes for a clip, from <paramref name="options"/> and <paramref name="seed"/>.
    /// Every note stays inside the clip; the bass and melody are monophonic, the pad holds
    /// chords. Nothing is written past <see cref="MidiPartOptions.LengthBars"/>.</summary>
    public static List<NotaNote> Generate(double lengthBeats, int beatsPerBar, MidiPartOptions options, int seed)
    {
        if (lengthBeats <= 1e-9) return new List<NotaNote>();

        var a = Find(options.GenreA) ?? Genres[0];
        var b = Find(options.GenreB);
        if (b is not null && b.Id == a.Id) b = null;

        int beats = Math.Max(1, beatsPerBar);
        int stepsPerBar = Math.Max(4, (int)Math.Round(beats * 4.0));
        double barBeats = stepsPerBar * 0.25;   // the take sits on a 16th grid
        int bars = Math.Max(1, (int)Math.Ceiling(lengthBeats / barBeats - 1e-9));
        int limitBars = options.LengthBars > 0 ? Math.Min(bars, options.LengthBars) : bars;
        double end = Math.Min(lengthBeats, limitBars * barBeats);

        var degrees = MidiScales.Degrees(MidiScales.MaskAt(options.ScaleIndex));
        // A mode with more than seven notes (Chromatic) has no usable diatonic thirds, so the
        // take falls back to natural minor instead of stacking random semitones.
        if (degrees.Length is < 5 or > 7) degrees = MidiScales.Degrees(MidiScales.Masks[1]);

        var job = new Job
        {
            A = a.Style,
            B = b?.Style,
            Mix = Math.Clamp(options.Mix, 0, 1),
            Density = Math.Clamp(options.Density, 0, 1),
            Variation = Math.Clamp(options.Variation, 0, 1),
            End = end,
            Root = ((options.Root % 12) + 12) % 12,
            Bars = limitBars,
            StepsPerBar = stepsPerBar,
            Octave = Math.Clamp(options.Octave, -1, 1),
            StepBeats = barBeats / stepsPerBar,
            BarBeats = barBeats,
            Degrees = degrees,
            Rng = new Random(seed),
        };

        var notes = options.Part switch
        {
            MidiPart.Pad => Pad(job),
            MidiPart.Melody => Melody(job),
            _ => Bass(job),
        };

        notes = MidiToolUtil.Tidy(notes, end);
        if (options.Part != MidiPart.Pad) notes = Monophonic(notes);
        return MidiToolUtil.Tidy(MidiToolUtil.Deoverlap(notes), end);
    }

    // ---- bassline ------------------------------------------------------------------------

    private static List<NotaNote> Bass(Job j)
    {
        var notes = new List<NotaNote>();
        int basePitch = Fit(j.Root + 36 + 12 * j.Octave, BassLow, BassHigh);
        int baseDegree = MidiScales.DegreeOfPitch(j.Degrees, j.Root, basePitch);
        var prog = Progression(j);
        var pulse = Blend(j.A.BassPulse, j.B?.BassPulse, j.Mix);
        double gate = Blend(j.A.BassGate, j.B?.BassGate, j.Mix);
        double jump = Blend(j.A.BassJump, j.B?.BassJump, j.Mix);

        for (int bar = 0; bar < j.Bars; bar++)
        {
            double barStart = bar * j.BarBeats;
            if (barStart >= j.End - 1e-9) break;
            int chordRoot = prog[bar % prog.Length];
            // Later four-bar phrases may swap a chord; a loop that never moves is a copy-paste.
            if (bar >= 4 && j.Variation > 0 && j.Rng.NextDouble() < j.Variation * .2)
                chordRoot = (chordRoot + (j.Rng.Next(2) == 0 ? 2 : 5)) % j.Degrees.Length;

            var used = new HashSet<int>();
            for (int i = 0; i < j.StepsPerBar; i++)
            {
                double start = barStart + i * j.StepBeats;
                if (start >= j.End - 1e-9) break;
                double p = PulseAt(pulse, Template(i, j.StepsPerBar));
                // The downbeat always speaks when the genre puts anything there at all: a
                // bassline that drops beat one reads as a mistake however the dice fell.
                bool hit = i == 0 ? p > 0 || j.Density > .1 : j.Rng.NextDouble() < Scaled(p, j.Density);
                if (!hit) continue;
                used.Add(i);

                int degree = baseDegree + chordRoot;
                bool strong = i % Math.Max(1, j.StepsPerBar / 4) == 0;
                if (!strong && j.Rng.NextDouble() < jump * (.35 + j.Variation * .35)) degree += 7;
                else if (!strong && j.Rng.NextDouble() < jump * .3) degree += 4;
                int pitch = Fit(MidiScales.PitchAtDegree(j.Degrees, j.Root, degree, 0), BassLow, BassHigh);
                double len = gate * (strong ? 1.15 : .9) * (.85 + j.Rng.NextDouble() * .3);
                double vel = (strong ? .92 : .70) + (j.Rng.NextDouble() * 2 - 1) * (.05 + j.Variation * .07);
                notes.Add(MidiToolUtil.Note(pitch, start, len, vel));
            }

            // A short walk-up fills the last beat of every fourth bar, when Variation asks.
            if (j.Variation > 0 && bar > 0 && (bar + 1) % 4 == 0 && j.Rng.NextDouble() < j.Variation * .75)
            {
                for (int k = 0; k < 4; k++)
                {
                    int i = j.StepsPerBar - 4 + k;
                    if (i < 0 || !used.Add(i)) continue;
                    double start = barStart + i * j.StepBeats;
                    if (start >= j.End - 1e-9) break;
                    int degree = baseDegree + chordRoot + (k < 3 ? k : 4);
                    int pitch = Fit(MidiScales.PitchAtDegree(j.Degrees, j.Root, degree, 0), BassLow, BassHigh);
                    notes.Add(MidiToolUtil.Note(pitch, start, gate * .8, .55 + k * .08));
                }
            }
        }
        return notes;
    }

    // ---- pad -----------------------------------------------------------------------------

    private static List<NotaNote> Pad(Job j)
    {
        var notes = new List<NotaNote>();
        int basePitch = Fit(j.Root + 48 + 12 * j.Octave, PadLow, PadHigh);
        int baseDegree = MidiScales.DegreeOfPitch(j.Degrees, j.Root, basePitch);
        var prog = Progression(j);
        int voices = Math.Clamp((int)Math.Round(Blend(j.A.PadVoices, j.B?.PadVoices, j.Mix)), 2, 5);
        bool seventh = j.A.PadSeventh || (j.B?.PadSeventh ?? false);
        double gate = Blend(j.A.PadGate, j.B?.PadGate, j.Mix);
        double perBar = Blend(j.A.PadPerBar, j.B?.PadPerBar, j.Mix);
        if (perBar <= 0) perBar = 1;
        double slotBeats = j.BarBeats / perBar;
        int slots = Math.Max(1, (int)Math.Ceiling(j.End / slotBeats - 1e-9));

        List<int>? previous = null;
        for (int slot = 0; slot < slots; slot++)
        {
            double start = slot * slotBeats;
            if (start >= j.End - 1e-9) break;
            int chordRoot = prog[slot % prog.Length];
            if (slot >= prog.Length && j.Variation > 0 && j.Rng.NextDouble() < j.Variation * .25)
                chordRoot = (chordRoot + (j.Rng.Next(2) == 0 ? 2 : 5)) % j.Degrees.Length;

            var chord = Chord(j.Degrees, j.Root, baseDegree + chordRoot, voices, seventh, j.Density, j.Rng);
            if (previous is not null) chord = VoiceLead(previous, chord, PadLow, PadHigh);

            double len = Math.Min(j.End - start, slotBeats * gate);
            if (slotBeats > .3) len = Math.Min(len, slotBeats - .03);   // a gap retriggers the chord
            len = Math.Max(MidiToolUtil.MinLength, len);
            double vel = .46 + (j.Rng.NextDouble() * 2 - 1) * (.05 + j.Variation * .04);
            for (int v = 0; v < chord.Count; v++)
                notes.Add(MidiToolUtil.Note(chord[v], start, len, vel + (v == 0 ? .06 : 0)));
            previous = chord;
        }
        return notes;
    }

    /// <summary>A diatonic chord: thirds stacked on <paramref name="degree"/>, with the
    /// seventh and (at high density) the ninth. Pitches are folded into the pad's register;
    /// <see cref="VoiceLead"/> spreads them again from chord to chord.</summary>
    private static List<int> Chord(int[] degrees, int root, int degree, int voices, bool seventh, double density, Random rng)
    {
        var pitches = new List<int>();
        for (int v = 0; v < voices; v++)
            pitches.Add(MidiScales.PitchAtDegree(degrees, root, degree + 2 * v, 0));
        if (seventh) pitches.Add(MidiScales.PitchAtDegree(degrees, root, degree + 6, 0));
        if (density > .78 && rng.NextDouble() < .5)
            pitches.Add(MidiScales.PitchAtDegree(degrees, root, degree + 8, 0));

        for (int i = 0; i < pitches.Count; i++) pitches[i] = Fit(pitches[i], PadLow, PadHigh);
        pitches.Sort();
        for (int i = pitches.Count - 1; i > 0; i--)
            if (pitches[i] == pitches[i - 1]) pitches.RemoveAt(i);
        if (pitches.Count < 2)
            pitches.Add(Fit(pitches[0] + 7, PadLow, PadHigh));
        return pitches;
    }

    /// <summary>Moves each chord tone to the octave nearest the previous chord, so the
    /// progression changes by step instead of jumping a register every bar.</summary>
    private static List<int> VoiceLead(List<int> previous, List<int> next, int lo, int hi)
    {
        var outp = new List<int>(next.Count);
        foreach (int pitch in next)
        {
            int best = pitch, bestDistance = int.MaxValue;
            for (int octave = -2; octave <= 2; octave++)
            {
                int candidate = pitch + 12 * octave;
                if (candidate < lo || candidate > hi) continue;
                int distance = int.MaxValue;
                foreach (int p in previous) distance = Math.Min(distance, Math.Abs(p - candidate));
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            outp.Add(best);
        }
        outp.Sort();
        for (int i = 1; i < outp.Count; i++)
            if (outp[i] == outp[i - 1]) outp[i] += 12;
        for (int guard = 0; guard < 8 && outp.Count > 0 && outp[^1] > hi; guard++)
            for (int i = 0; i < outp.Count; i++) outp[i] -= 12;
        for (int guard = 0; guard < 8 && outp.Count > 0 && outp[0] < lo; guard++)
            for (int i = 0; i < outp.Count; i++) outp[i] += 12;
        return outp;
    }

    // ---- melody --------------------------------------------------------------------------

    private static List<NotaNote> Melody(Job j)
    {
        var notes = new List<NotaNote>();
        int basePitch = Fit(j.Root + 60 + 12 * j.Octave, MelodyLow, MelodyHigh);
        int baseDegree = MidiScales.DegreeOfPitch(j.Degrees, j.Root, basePitch);
        var prog = Progression(j);
        var rhythm = Blend(j.A.MelodyRhythm, j.B?.MelodyRhythm, j.Mix);
        double rest = Blend(j.A.MelodyRest, j.B?.MelodyRest, j.Mix);
        int range = Math.Clamp((int)Math.Round(Blend(j.A.MelodyRange, j.B?.MelodyRange, j.Mix)), 3, 12);
        int motifBars = Math.Clamp((int)Math.Round(Blend(j.A.MelodyMotifBars, j.B?.MelodyMotifBars, j.Mix)), 1, 2);
        var motif = BuildMotif(j, rhythm, rest, range, motifBars);

        const int phraseBars = 4;
        for (int bar = 0; bar < j.Bars; bar++)
        {
            double barStart = bar * j.BarBeats;
            if (barStart >= j.End - 1e-9) break;
            int chordRoot = prog[bar % prog.Length];
            int motifBar = bar % motifBars;
            // Every second four-bar phrase answers the first: the motif sequenced up a third.
            bool answer = j.Variation > 0 && (bar / phraseBars) % 2 == 1;

            foreach (var m in motif)
            {
                if (m.Step / j.StepsPerBar != motifBar) continue;
                int step = m.Step % j.StepsPerBar;
                double start = barStart + step * j.StepBeats;
                if (start >= j.End - 1e-9) continue;
                if (j.Variation > 0 && j.Rng.NextDouble() < j.Variation * .12) continue;   // a rest on a repeat

                int degree = baseDegree + m.Degree + (answer ? 2 : 0);
                bool strong = step % Math.Max(1, j.StepsPerBar / 4) == 0;
                if (strong) degree = SnapToChord(degree, chordRoot, j.Degrees.Length);
                if (j.Variation > 0 && !strong && j.Rng.NextDouble() < j.Variation * .2)
                    degree += j.Rng.Next(2) == 0 ? 1 : -1;

                int pitch = Fit(MidiScales.PitchAtDegree(j.Degrees, j.Root, degree, 0), MelodyLow, MelodyHigh);
                double len = j.StepBeats * (strong ? .9 : .75) * (.8 + j.Rng.NextDouble() * .4);
                double vel = (strong ? .82 : .64) + (j.Rng.NextDouble() * 2 - 1) * (.05 + j.Variation * .06);
                notes.Add(MidiToolUtil.Note(pitch, start, len, vel));
            }
        }
        return notes;
    }

    /// <summary>A one- or two-bar motif: a contour, the genre's rhythm, and a landing on the
    /// tonic or third. Tiling this is what keeps a generated melody a tune rather than a walk.</summary>
    private static List<MotifNote> BuildMotif(Job j, double[] rhythm, double rest, int range, int motifBars)
    {
        int steps = motifBars * j.StepsPerBar;
        int shape = j.Rng.Next(6);
        int walk = 0;
        var motif = new List<MotifNote>();

        for (int step = 0; step < steps; step++)
        {
            double p = PulseAt(rhythm, Template(step % j.StepsPerBar, j.StepsPerBar)) * (1 - rest * .45);
            bool hit = step == 0 || j.Rng.NextDouble() < Scaled(p, j.Density);
            if (!hit) continue;

            double f = steps == 1 ? 0 : step / (double)(steps - 1);
            int contour = shape switch
            {
                0 => (int)Math.Round(Math.Sin(f * Math.PI) * range),          // arch
                1 => (int)Math.Round((1 - Math.Sin(f * Math.PI)) * range),    // valley
                2 => (int)Math.Round(f * range),                              // up
                3 => (int)Math.Round((1 - f) * range),                        // down
                4 => step < steps / 2 ? 0 : range,                            // call / answer
                _ => walk = Math.Clamp(walk + j.Rng.Next(-2, 3), 0, range),   // random walk
            };
            int degree = Math.Clamp(contour + (j.Rng.NextDouble() < .3 ? j.Rng.Next(-1, 2) : 0), 0, range);
            motif.Add(new MotifNote(step, degree));
        }

        if (motif.Count == 0) motif.Add(new MotifNote(0, 0));
        // A walk that never left the tonic, or a rhythm with one hit, is not a motif. Spread
        // the notes over the range instead of handing back the same pitch for a whole phrase.
        if (motif.Select(m => m.Degree).Distinct().Count() < 3 && range > 1)
            for (int i = 0; i < motif.Count; i++)
                motif[i] = motif[i] with { Degree = (int)Math.Round(i * range / (double)Math.Max(1, motif.Count - 1)) };
        // The phrase lands on the tonic or the third, which is what makes the repeat read
        // as a phrase ending instead of a loop that was cut off.
        if (motif[^1].Step > 0) motif[^1] = motif[^1] with { Degree = j.Rng.Next(2) * 2 };
        return motif;
    }

    /// <summary>The nearest chord tone (root, third, fifth, seventh) to a scale degree, in
    /// the degree's own octave — otherwise a low chord root would pull a melody an octave
    /// down every time it lands on a strong beat.</summary>
    private static int SnapToChord(int degree, int chordRoot, int scaleLength)
    {
        int octave = (int)Math.Round((degree - chordRoot) / (double)scaleLength);
        int best = degree, bestDistance = int.MaxValue;
        foreach (int tone in new[] { 0, 2, 4, 6 })
            for (int step = -1; step <= 1; step++)
            {
                int candidate = chordRoot + tone + (octave + step) * scaleLength;
                int distance = Math.Abs(candidate - degree);
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
        return best;
    }

    // ---- shared --------------------------------------------------------------------------

    private static int[] Progression(Job j)
    {
        var loops = j.A.Progressions;
        if (j.B is not null && j.Mix > 0 && j.Rng.NextDouble() < j.Mix) loops = j.B.Progressions;
        return loops[j.Rng.Next(loops.Length)];
    }

    private static int Fit(int pitch, int lo, int hi)
    {
        for (int guard = 0; guard < 12 && pitch < lo; guard++) pitch += 12;
        for (int guard = 0; guard < 12 && pitch > hi; guard++) pitch -= 12;
        return Math.Clamp(pitch, 0, 127);
    }

    private static int Template(int stepInBar, int stepsPerBar)
        => stepsPerBar == 16
            ? stepInBar
            : Math.Clamp((int)Math.Floor(stepInBar * 16.0 / Math.Max(1, stepsPerBar)), 0, 15);

    private static double PulseAt(double[] profile, int step)
        => profile.Length == 0 ? 0 : profile[Math.Clamp(step, 0, profile.Length - 1)];

    private static double Scaled(double p, double density)
        => p <= 0 ? 0 : Math.Clamp(p * (.30 + 1.5 * density), 0, 1);

    private static double Blend(double a, double? b, double mix) => b is null ? a : a + (b.Value - a) * mix;

    private static double[] Blend(double[] a, double[]? b, double mix)
    {
        if (b is null || mix <= 0) return a;
        int n = Math.Min(a.Length, b.Length);
        var outp = new double[n];
        for (int i = 0; i < n; i++) outp[i] = a[i] + (b[i] - a[i]) * mix;
        return outp;
    }

    /// <summary>Bass and melody are one line: no note runs into the next onset, whatever its
    /// pitch. The pad keeps its chords.</summary>
    private static List<NotaNote> Monophonic(List<NotaNote> notes)
    {
        var sorted = MidiToolUtil.Sorted(notes);
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            double room = sorted[i + 1].StartBeat - sorted[i].StartBeat;
            if (sorted[i].LengthBeats <= room) continue;
            var n = sorted[i];
            n.LengthBeats = Math.Max(MidiToolUtil.MinLength, room);
            sorted[i] = n;
        }
        return sorted;
    }
}
