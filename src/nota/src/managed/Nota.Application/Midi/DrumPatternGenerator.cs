// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Random drum patterns from a genre, or a blend of two genres. The generator is pure: it
// reads the loaded Drum Rack voices (pad note + name), the clip length and the meter, and
// returns notes. The Pattern view writes them into the clip in one undo step.
//
// A genre is a one-bar 16-step sketch for the roles a Drum Rack usually has. The same sketch
// repeats across the clip; Variation re-rolls optional hits bar by bar, adds a fill every
// four bars and changes velocities. Mixing averages two sketches step by step; the alternate
// mode switches the sketch every bar instead. Roles come from pad names first (Kick, Snare,
// Closed Hat, Open Hat, Perc…, with common abbreviations) and from the GM drum map second;
// a kit with unrelated names still gets a usable layout from the four lowest pads.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nota.Application.Midi;

/// <summary>A Drum Rack pad as the generator sees it: the note that triggers it and the name
/// shown in the rack. <see cref="Chain"/> is only for the UI's colour.</summary>
public readonly record struct DrumVoice(int Note, string Name, int Chain = -1);

/// <summary>The roles the generator writes. A role the kit lacks is simply skipped, so a
/// three-pad kit still works.</summary>
public enum DrumRole { Kick, Snare, Clap, Rim, ClosedHat, OpenHat, Tom, Perc, Crash }

/// <summary>The data behind one genre: a 16-step probability sketch per role.</summary>
internal sealed record GenreSpec(
    string Id, string Name,
    double[] Kick, double[] Snare, double[] Ghost,
    double[] Clap, double[] Rim, double[] Hat, double[] Open,
    double[] Perc, double[] Tom, double[] Crash);

/// <summary>One genre as the UI offers it; <see cref="Id"/> is stable, <see cref="Name"/> is the
/// menu text.</summary>
public sealed class DrumGenre
{
    public string Id { get; }
    public string Name { get; }
    internal GenreSpec Spec { get; }

    internal DrumGenre(GenreSpec spec)
    {
        Spec = spec;
        Id = spec.Id;
        Name = spec.Name;
    }
}

/// <summary>What to generate. Genre ids come from <see cref="DrumPatternGenerator.Genres"/>.
/// <see cref="Mix"/> 0 is the first genre and 1 the second; it is ignored when there is no
/// second genre. <see cref="Density"/> 0 keeps only the signature hits, 1 fills the optional
/// ones. <see cref="Variation"/> 0 repeats one bar, 1 re-rolls and fills more.</summary>
public sealed record DrumPatternOptions(
    string GenreA,
    string? GenreB = null,
    double Mix = 0.5,
    bool AlternateBars = false,
    double Density = 0.5,
    double Variation = 0.25);

public static class DrumPatternGenerator
{
    // ---- the genre book ------------------------------------------------------------------

    private static readonly double[] None = new double[16];

    /// <summary>A 16-step probability sketch. Several entries on one step keep the highest.</summary>
    private static double[] P(params (int Step, double P)[] hits)
    {
        var a = new double[16];
        foreach (var (step, p) in hits)
            if (step is >= 0 and < 16) a[step] = Math.Max(a[step], Math.Clamp(p, 0, 1));
        return a;
    }

    private static double[] Every(double p)
    {
        var a = new double[16];
        Array.Fill(a, Math.Clamp(p, 0, 1));
        return a;
    }

    private static readonly GenreSpec[] s_specs =
    {
        new("house", "House",
            P((0, 1), (4, 1), (8, 1), (12, 1), (14, .2)), P((4, 1), (12, 1)),
            P((6, .15), (14, .15)), P((4, .9), (12, .9)), None,
            Every(.9), P((2, .75), (6, .75), (10, .75), (14, .75)), Every(.25), None,
            P((0, 1))),
        new("techno", "Techno",
            P((0, 1), (4, 1), (8, 1), (12, 1), (6, .15), (14, .15)), P((4, .85), (12, .85)),
            None, None, None,
            Every(.95), P((2, .35), (6, .35), (10, .35), (14, .35)), Every(.1), None,
            P((0, .8))),
        new("tech-house", "Tech House",
            P((0, 1), (4, 1), (8, 1), (12, 1), (3, .2), (11, .2)), P((4, 1), (12, 1)),
            P((6, .15), (14, .15)), P((4, .8), (12, .8)), None,
            Every(.85), P((2, .65), (6, .65), (10, .65), (14, .65)), Every(.3), None,
            P((0, .8))),
        new("deep-house", "Deep House",
            P((0, 1), (4, 1), (8, 1), (12, 1), (10, .1)), P((4, 1), (12, 1)),
            P((14, .2)), P((4, .7), (12, .7)), None,
            P((0, .8), (2, .6), (4, .8), (6, .6), (8, .8), (10, .6), (12, .8), (14, .6)),
            P((2, .6), (6, .6), (10, .6), (14, .6)), Every(.2), None,
            P((0, .7))),
        new("hip-hop", "Hip-Hop",
            P((0, 1), (6, .9), (10, .8), (3, .3), (11, .25), (14, .2)), P((4, 1), (12, 1)),
            P((2, .25), (6, .2), (10, .25), (14, .3)), None, None,
            P((0, .9), (2, .8), (4, .9), (6, .8), (8, .9), (10, .8), (12, .9), (14, .8)),
            None, None, None, P((0, .5))),
        new("trap", "Trap",
            P((0, 1), (6, .9), (10, .8), (3, .35), (7, .35), (14, .25)), P((4, 1), (12, 1)),
            P((7, .2), (15, .2)), None, None,
            Every(1), None, None, None, P((0, .8))),
        new("drill", "Drill",
            P((0, 1), (6, .9), (10, .8), (3, .35), (7, .35), (14, .25)), P((4, 1), (12, .9)),
            P((7, .2), (15, .2)), None, None,
            Every(1), None, None, None, P((0, .8))),
        new("dnb", "Drum & Bass",
            P((0, 1), (10, .95), (3, .2), (6, .3), (14, .2)), P((4, 1), (12, 1)),
            P((6, .2), (14, .2)), None, None,
            P((0, .8), (2, .7), (4, .8), (6, .7), (8, .8), (10, .7), (12, .8), (14, .7)),
            None, Every(.2), None, P((0, .9))),
        new("breakbeat", "Breakbeat",
            P((0, 1), (10, .9), (3, .3), (6, .35), (12, .2), (15, .15)), P((4, 1), (12, 1)),
            P((2, .15), (7, .25), (15, .25)), None, None,
            P((0, .85), (2, .7), (4, .85), (6, .7), (8, .85), (10, .7), (12, .85), (14, .7)),
            None, Every(.15), None, P((0, .7))),
        new("disco", "Disco",
            P((0, 1), (4, 1), (8, 1), (12, 1)), P((4, 1), (12, 1)),
            None, P((4, .8), (12, .8)), None,
            Every(.85), P((2, .85), (6, .85), (10, .85), (14, .85)),
            P((2, .5), (6, .5), (10, .5), (14, .5)), None, P((0, .8))),
        new("funk", "Funk",
            P((0, 1), (3, .7), (6, .8), (8, .9), (11, .6), (2, .25), (7, .3), (10, .3), (14, .2)),
            P((4, 1), (12, 1)), P((2, .2), (6, .25), (10, .25), (14, .2)), None, None,
            Every(.7), P((2, .15), (6, .15), (10, .15), (14, .15)),
            P((2, .3), (6, .3), (10, .3), (14, .3)), None, P((0, .6))),
        new("rock", "Rock",
            P((0, 1), (8, 1), (6, .3), (10, .3), (14, .2)), P((4, 1), (12, 1)),
            None, None, None,
            P((0, .9), (2, .8), (4, .9), (6, .8), (8, .9), (10, .8), (12, .9), (14, .8)),
            None, None, None, P((0, .9))),
        new("pop", "Pop",
            P((0, 1), (8, 1), (4, .2), (12, .2)), P((4, 1), (12, 1)),
            None, P((4, .5), (12, .5)), None,
            P((0, .9), (2, .8), (4, .9), (6, .8), (8, .9), (10, .8), (12, .9), (14, .8)),
            P((2, .4), (6, .4), (10, .4), (14, .4)),
            P((2, .3), (6, .3), (10, .3), (14, .3)), None, P((0, .8))),
        new("reggaeton", "Reggaeton",
            P((0, 1), (4, .85), (8, 1), (12, .85), (6, .2), (14, .2)), P((4, .7), (12, .7)),
            None, P((3, .5), (6, .5), (11, .5), (14, .5)), P((3, .75), (6, .75), (11, .75), (14, .75)),
            P((0, .8), (2, .7), (4, .8), (6, .7), (8, .8), (10, .7), (12, .8), (14, .7)),
            None, None, None, P((0, .7))),
        new("latin", "Latin",
            P((0, 1), (6, .8), (8, .8), (11, .6), (3, .3), (10, .25), (14, .2)),
            P((4, .5), (12, .5)), None, None, P((0, .5), (3, .6), (6, .5), (10, .6), (12, .5)),
            P((0, .7), (2, .6), (4, .7), (6, .6), (8, .7), (10, .6), (12, .7), (14, .6)),
            None, P((2, .4), (5, .5), (8, .4), (11, .5), (14, .4)), None, P((0, .6))),
        new("afrobeat", "Afrobeat",
            P((0, 1), (4, .9), (8, 1), (12, .85), (3, .3), (6, .25), (10, .3), (14, .2)),
            P((4, .6), (12, .6)), None, P((4, .5), (12, .5)), P((2, .4), (5, .4), (8, .3), (11, .4), (14, .4)),
            Every(.7), P((2, .35), (6, .35), (10, .35), (14, .35)), Every(.35), None, P((0, .7))),
        new("amapiano", "Amapiano",
            P((0, 1), (6, .8), (8, .9), (12, .8), (3, .25), (10, .3), (14, .2)),
            P((4, .8), (12, .8)), None, P((4, .5), (12, .5)), P((3, .3), (7, .3), (11, .3), (15, .3)),
            Every(.55), P((2, .4), (6, .4), (10, .4), (14, .4)),
            P((0, .3), (3, .5), (6, .4), (8, .3), (11, .5), (14, .4)), None, P((0, .7))),
        new("jazz", "Jazz",
            P((0, .7), (8, .5), (6, .2), (10, .2)), P((4, .6), (12, .6)),
            P((2, .2), (6, .2), (10, .2), (14, .2)), None, None,
            P((0, .9), (2, .7), (4, .9), (6, .7), (8, .9), (10, .7), (12, .9), (14, .7)),
            None, None, None, P((0, .5))),
        new("uk-garage", "UK Garage",
            P((0, 1), (8, .9), (6, .3), (14, .2), (3, .2)), P((4, 1), (12, 1)),
            P((6, .2), (14, .2)), None, None,
            Every(.7), P((2, .35), (6, .35), (10, .35), (14, .35)), Every(.25), None, P((0, .7))),
        new("lo-fi", "Lo-Fi",
            P((0, 1), (6, .9), (10, .8), (3, .25), (14, .2)), P((4, 1), (12, 1)),
            P((2, .2), (6, .2), (10, .2), (14, .25)), None, None,
            P((0, .7), (2, .6), (4, .7), (6, .6), (8, .7), (10, .6), (12, .7), (14, .6)),
            None, None, None, P((0, .4))),
    };

    /// <summary>Every genre, in menu order (popular first).</summary>
    public static IReadOnlyList<DrumGenre> Genres { get; } =
        s_specs.Select(s => new DrumGenre(s)).ToArray();

    public static DrumGenre? Find(string? id) => string.IsNullOrWhiteSpace(id)
        ? null
        : Genres.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string NameOf(string id) => Find(id)?.Name ?? id;

    // ---- roles ---------------------------------------------------------------------------

    /// <summary>Pad → role. A name wins over the note; the GM drum map is the fallback, and a
    /// kit with unrelated names gets its lowest pads as Kick, Snare, Closed Hat and Open Hat.</summary>
    public static IReadOnlyDictionary<DrumRole, DrumVoice> MapRoles(IReadOnlyList<DrumVoice> voices)
    {
        var map = new Dictionary<DrumRole, DrumVoice>();
        var spare = new List<DrumVoice>();
        foreach (var v in voices.OrderBy(v => v.Note))
        {
            var role = DetectRole(v);
            if (role is { } r)
            {
                if (!map.ContainsKey(r)) map[r] = v;
            }
            else spare.Add(v);
        }

        if (map.Count == 0)
        {
            var order = new[]
            {
                DrumRole.Kick, DrumRole.Snare, DrumRole.ClosedHat, DrumRole.OpenHat,
                DrumRole.Perc, DrumRole.Tom, DrumRole.Clap, DrumRole.Rim, DrumRole.Crash,
            };
            for (int i = 0; i < spare.Count && i < order.Length; i++) map[order[i]] = spare[i];
        }
        else
        {
            // An incomplete kit: the lowest unused pads cover the core roles, so a kick and a
            // snare still exist even when the kit names only some of its pads.
            foreach (var role in new[] { DrumRole.Kick, DrumRole.Snare, DrumRole.ClosedHat, DrumRole.Perc })
            {
                if (map.ContainsKey(role) || spare.Count == 0) continue;
                map[role] = spare[0];
                spare.RemoveAt(0);
            }
        }
        return map;
    }

    /// <summary>The pads the generator will use, by role, for the dialog's "Using" line.</summary>
    public static string RoleSummary(IReadOnlyList<DrumVoice> voices)
        => string.Join(" · ", MapRoles(voices).OrderBy(kv => kv.Key).Select(kv =>
            string.IsNullOrWhiteSpace(kv.Value.Name) ? kv.Key.ToString() : kv.Value.Name));

    private static DrumRole? DetectRole(DrumVoice v)
    {
        string n = Normalize(v.Name);
        if (n.Length > 0)
        {
            if (n.Contains("open") && (n.Contains("hat") || n.Contains("hh"))) return DrumRole.OpenHat;
            if (n == "oh" || n == "o h") return DrumRole.OpenHat;
            if (n.Contains("closed") && n.Contains("hat")) return DrumRole.ClosedHat;
            if (n.Contains("hihat") || n.Contains("hi hat") || n.Contains("hat") || n == "ch" || n == "hh")
                return DrumRole.ClosedHat;
            if (n.Contains("kick") || n == "bd" || n.Contains("bass drum") || n.Contains("808")) return DrumRole.Kick;
            if (n.Contains("snare") || n == "sd" || n == "snr") return DrumRole.Snare;
            if (n.Contains("clap") || n == "cp" || n.Contains("snap")) return DrumRole.Clap;
            if (n.Contains("crash") || n.Contains("china") || n.Contains("splash") || n.Contains("cymbal"))
                return DrumRole.Crash;
            if (n.Contains("rim") || n.Contains("stick")) return DrumRole.Rim;
            if (n.Contains("tom") || n.Contains("floor")) return DrumRole.Tom;
            if (n.Contains("perc") || n.Contains("shaker") || n.Contains("tamb") || n.Contains("conga") ||
                n.Contains("bongo") || n.Contains("cowbell") || n.Contains("clave") || n.Contains("ride") ||
                n.Contains("bell") || n.Contains("wood") || n.Contains("block"))
                return DrumRole.Perc;
        }

        return v.Note switch
        {
            35 or 36 => DrumRole.Kick,
            37 => DrumRole.Rim,
            38 or 40 => DrumRole.Snare,
            39 => DrumRole.Clap,
            41 or 43 or 45 or 47 or 48 or 50 => DrumRole.Tom,
            42 or 44 => DrumRole.ClosedHat,
            46 => DrumRole.OpenHat,
            49 or 52 or 55 or 57 => DrumRole.Crash,
            51 or 53 or 54 or 56 or 59 or 60 or 61 or 62 or 63 or 64 or 70 or 75 or 76 or 77 or 81 or 82
                => DrumRole.Perc,
            _ => null,
        };
    }

    private static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- generation ----------------------------------------------------------------------

    private sealed record RolePlan(bool[] Hit, double[] P);

    private sealed class BarPlan
    {
        public Dictionary<DrumRole, RolePlan> Roles { get; } = new();
        public bool[] GhostHit { get; init; } = new bool[16];
        public double[] GhostP { get; init; } = new double[16];
    }

    private static double Scale(double p, double density, double weight)
    {
        if (p <= 0) return 0;
        if (p >= 1) return 1;
        double s = 0.35 + 1.3 * Math.Clamp(density, 0, 1);
        return Math.Clamp(p * (1 + (s - 1) * weight), 0, 1);
    }

    private static int TemplateIndex(int stepInBar, int stepsPerBar)
        => stepsPerBar == 16
            ? stepInBar
            : Math.Clamp((int)Math.Floor(stepInBar * 16.0 / Math.Max(1, stepsPerBar)), 0, 15);

    private static BarPlan BuildPlan(GenreSpec a, GenreSpec? b, double mix,
                                     IReadOnlyDictionary<DrumRole, DrumVoice> roles, Random rng, double density)
    {
        var plan = new BarPlan();
        void Add(DrumRole role, double[] pa, double[]? pb, double weight = 1.0)
        {
            if (!roles.ContainsKey(role)) return;
            var hit = new bool[16];
            var prob = new double[16];
            for (int t = 0; t < 16; t++)
            {
                double p = pb is null ? pa[t] : pa[t] * (1 - mix) + pb[t] * mix;
                prob[t] = Scale(p, density, weight);
                hit[t] = rng.NextDouble() < prob[t];
            }
            plan.Roles[role] = new RolePlan(hit, prob);
        }

        Add(DrumRole.Kick, a.Kick, b?.Kick);
        Add(DrumRole.Snare, a.Snare, b?.Snare);
        Add(DrumRole.Clap, a.Clap, b?.Clap);
        Add(DrumRole.Rim, a.Rim, b?.Rim);
        Add(DrumRole.OpenHat, a.Open, b?.Open);
        Add(DrumRole.ClosedHat, a.Hat, b?.Hat);
        Add(DrumRole.Perc, a.Perc, b?.Perc);

        // A ghost snare lives on the snare pad but at a low velocity. It is a separate plan so
        // a normal snare at the same step always wins.
        if (roles.ContainsKey(DrumRole.Snare))
        {
            for (int t = 0; t < 16; t++)
            {
                double p = b is null ? a.Ghost[t] : a.Ghost[t] * (1 - mix) + b.Ghost[t] * mix;
                plan.GhostP[t] = Scale(p, density, 1.5);
                plan.GhostHit[t] = rng.NextDouble() < plan.GhostP[t];
            }
        }

        // One hat speaks at a time: an open hat on a step replaces the closed hat there.
        if (roles.ContainsKey(DrumRole.OpenHat) && roles.ContainsKey(DrumRole.ClosedHat)
            && plan.Roles.TryGetValue(DrumRole.OpenHat, out var open)
            && plan.Roles.TryGetValue(DrumRole.ClosedHat, out var closed))
        {
            for (int t = 0; t < 16; t++) if (open.Hit[t]) closed.Hit[t] = false;
        }
        return plan;
    }

    /// <summary>Notes for a clip: one bar sketch repeated, with variation and fills. Every note
    /// starts on a 16th and stays inside the clip; a pitch is never doubled on one step.</summary>
    public static List<NotaNote> Generate(IReadOnlyList<DrumVoice> voices, double lengthBeats, int beatsPerBar,
                                          DrumPatternOptions options, int seed)
    {
        var roles = MapRoles(voices);
        if (roles.Count == 0 || lengthBeats <= 0) return new List<NotaNote>();

        var a = Find(options.GenreA) ?? Genres[0];
        var b = Find(options.GenreB);
        if (b is not null && b.Id == a.Id) b = null;

        double mix = Math.Clamp(options.Mix, 0, 1);
        double density = Math.Clamp(options.Density, 0, 1);
        double variation = Math.Clamp(options.Variation, 0, 1);
        var rng = new Random(seed);

        int stepsPerBar = Math.Max(4, (int)Math.Round(Math.Max(1, beatsPerBar) * 4.0));
        double barBeats = stepsPerBar * 0.25;
        int bars = Math.Max(1, (int)Math.Ceiling(lengthBeats / barBeats - 1e-9));

        BarPlan plan;
        BarPlan? planB = null;
        if (b is not null && options.AlternateBars)
        {
            plan = BuildPlan(a.Spec, null, 0, roles, rng, density);
            planB = BuildPlan(b.Spec, null, 0, roles, rng, density);
        }
        else
        {
            plan = BuildPlan(a.Spec, b?.Spec, mix, roles, rng, density);
        }

        var cells = new Dictionary<(int Note, int Step), NotaNote>();

        void AddNote(DrumVoice voice, double start, double length, double velocity)
        {
            if (start >= lengthBeats - 1e-9) return;
            length = Math.Min(length, lengthBeats - start);
            if (length < 0.05) return;
            int step = (int)Math.Round(start * 4);
            var key = (voice.Note, step);
            var note = new NotaNote(voice.Note, start, length, (float)Math.Clamp(velocity, 0.02, 1.0));
            if (cells.TryGetValue(key, out var had) && had.Velocity >= note.Velocity) return;
            cells[key] = note;
        }

        double Velocity(DrumRole role, int t, bool ghost = false)
        {
            double v = role switch
            {
                DrumRole.Kick => t % 4 == 0 ? .98 : .82,
                DrumRole.Snare => .95,
                DrumRole.Clap => .85,
                DrumRole.Rim => .62,
                DrumRole.ClosedHat => t % 4 == 0 ? .8 : .55,
                DrumRole.OpenHat => .7,
                DrumRole.Perc => t % 4 == 0 ? .55 : .42,
                DrumRole.Tom => .8,
                DrumRole.Crash => .9,
                _ => .7,
            };
            if (ghost) v = .3;
            v += (rng.NextDouble() * 2 - 1) * (0.03 + variation * 0.1);
            return Math.Clamp(v, .2, 1.0);
        }

        bool Hit(BarPlan p, DrumRole role, int t)
        {
            if (!p.Roles.TryGetValue(role, out var rp)) return false;
            bool hit = rp.Hit[t];
            if (variation <= 0 || rp.P[t] >= 1) return hit;
            if (hit && rng.NextDouble() < variation * 0.3) return false;
            if (!hit && rng.NextDouble() < variation * 0.15) return true;
            return hit;
        }

        void Voice(DrumRole role, double start, double length, double velocity)
        {
            if (roles.TryGetValue(role, out var v)) AddNote(v, start, length, velocity);
        }

        for (int bar = 0; bar < bars; bar++)
        {
            var p = planB is not null && bar % 2 == 1 ? planB : plan;
            for (int i = 0; i < stepsPerBar; i++)
            {
                double start = bar * barBeats + i * 0.25;
                if (start >= lengthBeats - 1e-9) break;
                int t = TemplateIndex(i, stepsPerBar);

                bool open = Hit(p, DrumRole.OpenHat, t);
                bool snare = Hit(p, DrumRole.Snare, t);

                if (Hit(p, DrumRole.Kick, t)) Voice(DrumRole.Kick, start, 0.25, Velocity(DrumRole.Kick, t));
                if (snare) Voice(DrumRole.Snare, start, 0.25, Velocity(DrumRole.Snare, t));
                else if (p.GhostHit[t] && (variation <= 0 || rng.NextDouble() > variation * 0.25))
                    Voice(DrumRole.Snare, start, 0.25, Velocity(DrumRole.Snare, t, ghost: true));
                if (Hit(p, DrumRole.Clap, t)) Voice(DrumRole.Clap, start, 0.25, Velocity(DrumRole.Clap, t));
                if (Hit(p, DrumRole.Rim, t)) Voice(DrumRole.Rim, start, 0.25, Velocity(DrumRole.Rim, t));
                if (open) Voice(DrumRole.OpenHat, start, 0.5, Velocity(DrumRole.OpenHat, t));
                else if (Hit(p, DrumRole.ClosedHat, t)) Voice(DrumRole.ClosedHat, start, 0.25, Velocity(DrumRole.ClosedHat, t));
                if (Hit(p, DrumRole.Perc, t)) Voice(DrumRole.Perc, start, 0.25, Velocity(DrumRole.Perc, t));
            }

            // A crash marks the top of the clip and every eight bars (if the genre wants one).
            if (roles.TryGetValue(DrumRole.Crash, out var crash) && (bar == 0 || bar % 8 == 0))
            {
                double chance = bar == 0 ? a.Spec.Crash[0] : a.Spec.Crash[0] * 0.7;
                if (rng.NextDouble() < chance) AddNote(crash, bar * barBeats, 1.0, .9);
            }

            // A fill every four bars, only when Variation asks for one. Toms carry it; a kit
            // without toms uses its snare.
            if (bars >= 2 && (bar + 1) % 4 == 0 && variation > 0 && rng.NextDouble() < variation * 0.8)
            {
                DrumVoice? fill = roles.TryGetValue(DrumRole.Tom, out var tom) ? tom
                    : roles.TryGetValue(DrumRole.Snare, out var sn) ? sn : (DrumVoice?)null;
                if (fill is { } fv)
                    for (int k = 0; k < 4; k++)
                    {
                        int step = stepsPerBar - 4 + k;
                        if (step < 0) continue;
                        AddNote(fv, bar * barBeats + step * 0.25, 0.25, 0.5 + k * 0.13);
                    }
            }
        }

        return cells.Values.OrderBy(n => n.StartBeat).ThenBy(n => n.Pitch).ToList();
    }
}
