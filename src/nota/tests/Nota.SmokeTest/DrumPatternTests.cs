// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Random drum-pattern generator (Pattern ▸ Random): roles from pad names/GM notes, genre
// sketches, a two-genre mix, density/variation, fills and bars. Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --drum-genre`.

using Nota.Application;
using Nota.Application.Midi;

namespace Nota.SmokeTest;

internal static class DrumPatternTests
{
    private const int Seed = 424242;

    private static readonly DrumVoice[] Kit =
    {
        new(36, "Kick"), new(38, "Snare"), new(39, "Clap"), new(42, "Closed Hat"),
        new(46, "Open Hat"), new(70, "Shaker"), new(45, "Tom"), new(37, "Rim"), new(49, "Crash"),
    };

    private static int Step(NotaNote n) => (int)Math.Round(n.StartBeat * 4);
    private static bool Has(IEnumerable<NotaNote> notes, int pitch, int step)
        => notes.Any(n => n.Pitch == pitch && Step(n) == step);
    private static List<NotaNote> Gen(string genre, double length = 4, int bpb = 4,
        string? genreB = null, double mix = 0.5, bool alternate = false,
        double density = 1, double variation = 0, int seed = Seed)
        => DrumPatternGenerator.Generate(Kit, length, bpb,
            new DrumPatternOptions(genre, genreB, mix, alternate, density, variation), seed);

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (DrumPatternGenerator.Genres.Count >= 15, "the genre book has a useful spread");
        yield return (DrumPatternGenerator.Genres.Select(g => g.Id).Distinct().Count()
                      == DrumPatternGenerator.Genres.Count, "genre ids are unique");

        // Roles.
        var named = DrumPatternGenerator.MapRoles(new[]
        {
            new DrumVoice(36, "Kick"), new DrumVoice(38, "Snare"), new DrumVoice(42, "Closed Hat"),
            new DrumVoice(46, "Open Hat"), new DrumVoice(70, "Shaker"),
        });
        yield return (named.Count == 5 && named[DrumRole.Kick].Note == 36
                      && named[DrumRole.Snare].Note == 38 && named[DrumRole.ClosedHat].Note == 42
                      && named[DrumRole.OpenHat].Note == 46 && named[DrumRole.Perc].Note == 70,
            "pad names map to Kick / Snare / Closed Hat / Open Hat / Perc");

        var abbreviations = DrumPatternGenerator.MapRoles(new[]
        {
            new DrumVoice(1, "BD"), new DrumVoice(2, "SD"), new DrumVoice(3, "CP"),
            new DrumVoice(4, "CH"), new DrumVoice(5, "OH"),
        });
        yield return (abbreviations.Count == 5 && abbreviations[DrumRole.Kick].Note == 1
                      && abbreviations[DrumRole.Snare].Note == 2 && abbreviations[DrumRole.Clap].Note == 3
                      && abbreviations[DrumRole.ClosedHat].Note == 4 && abbreviations[DrumRole.OpenHat].Note == 5,
            "common pad abbreviations map too");

        var gm = DrumPatternGenerator.MapRoles(new[]
        {
            new DrumVoice(36, "Pad 1"), new DrumVoice(38, "Pad 2"), new DrumVoice(42, "Pad 3"),
            new DrumVoice(46, "Pad 4"), new DrumVoice(70, "Pad 5"),
        });
        yield return (gm.Count == 5 && gm[DrumRole.Kick].Note == 36 && gm[DrumRole.Snare].Note == 38
                      && gm[DrumRole.ClosedHat].Note == 42 && gm[DrumRole.OpenHat].Note == 46
                      && gm[DrumRole.Perc].Note == 70,
            "unrelated names fall back to the GM drum map");

        var arbitrary = DrumPatternGenerator.MapRoles(new[]
        {
            new DrumVoice(100, "A"), new DrumVoice(101, "B"), new DrumVoice(102, "C"), new DrumVoice(103, "D"),
        });
        yield return (arbitrary.Count == 4 && arbitrary[DrumRole.Kick].Note == 100
                      && arbitrary[DrumRole.Snare].Note == 101 && arbitrary[DrumRole.ClosedHat].Note == 102
                      && arbitrary[DrumRole.OpenHat].Note == 103,
            "a kit with no known names gets its lowest pads as the core four");

        var duplicate = DrumPatternGenerator.MapRoles(new[]
        {
            new DrumVoice(36, "Kick"), new DrumVoice(37, "Kick 2"), new DrumVoice(38, "Snare"),
            new DrumVoice(42, "Closed Hat"),
        });
        yield return (duplicate[DrumRole.ClosedHat].Note == 42,
            "a second kick is not borrowed as another role");
        yield return (DrumPatternGenerator.Generate(Array.Empty<DrumVoice>(), 4, 4,
            new DrumPatternOptions("house"), Seed).Count == 0, "no pads: no notes");

        // House signature: four-on-the-floor and a backbeat.
        var house = Gen("house", variation: 0);
        yield return (house.Count > 0 && house.All(n => Kit.Any(v => v.Note == n.Pitch)),
            "house writes only on loaded pads");
        yield return (new[] { 0, 4, 8, 12 }.All(s => Has(house, 36, s)),
            "house kick lands on every beat");
        yield return (Has(house, 38, 4) && Has(house, 38, 12),
            "house snare lands on the backbeat");

        var openSteps = house.Where(n => n.Pitch == 46).Select(Step).ToHashSet();
        yield return (openSteps.All(s => !Has(house, 42, s)),
            "an open hat replaces the closed hat on its step");
        yield return (house.GroupBy(n => (n.Pitch, Step(n))).All(g => g.Count() == 1),
            "no pitch is doubled on a step");
        yield return (house.All(n => n.StartBeat >= 0 && n.StartBeat < 4
                                    && n.LengthBeats > 0 && n.LengthBeats <= 4 - n.StartBeat
                                    && n.Velocity > 0 && n.Velocity <= 1),
            "every note stays inside the clip with a sane length and velocity");

        // Density and variation.
        var sparse = Gen("house", density: 0, variation: 0);
        yield return (new[] { 0, 4, 8, 12 }.All(s => Has(sparse, 36, s)) && sparse.Count < house.Count,
            "density 0 keeps the signature hits and thins the rest");

        var bars = Gen("house", length: 8, variation: 0);
        var bar0 = bars.Where(n => n.Pitch != 49 && n.StartBeat < 4).Select(n => (n.Pitch, Step(n))).ToHashSet();
        var bar1 = bars.Where(n => n.Pitch != 49 && n.StartBeat >= 4)
            .Select(n => (n.Pitch, Step(n))).ToHashSet();
        yield return (bar0.SetEquals(bar1.Select(x => (x.Item1, x.Item2 - 16))),
            "variation 0 repeats the same bar");
        var varied = Gen("house", length: 8, variation: 1);
        var vbar0 = varied.Where(n => n.Pitch != 49 && n.StartBeat < 4).Select(n => (n.Pitch, Step(n))).ToHashSet();
        var vbar1 = varied.Where(n => n.Pitch != 49 && n.StartBeat >= 4)
            .Select(n => (n.Pitch, Step(n))).ToHashSet();
        yield return (!vbar0.SetEquals(vbar1.Select(x => (x.Item1, x.Item2 - 16))),
            "variation 1 lets the bars breathe");

        // Genres with a different signature.
        var trap = Gen("trap", variation: 0);
        yield return (Has(trap, 36, 0) && Has(trap, 36, 6) && Has(trap, 36, 10),
            "trap kick follows its syncopated sketch");
        yield return (Enumerable.Range(0, 16).Count(s => Has(trap, 42, s)) >= 12,
            "trap keeps the hat rolling");

        var reggaeton = Gen("reggaeton", variation: 0);
        yield return (Has(reggaeton, 37, 3) && Has(reggaeton, 37, 6)
                      && Has(reggaeton, 37, 11) && Has(reggaeton, 37, 14),
            "reggaeton rim carries the dembow");

        var jazz = Gen("jazz", variation: 0);
        yield return (jazz.Count > 0 && jazz.Any(n => n.Pitch == 38), "jazz still has a snare comp");

        // Mixing and alternate bars.
        var pureHouse = Gen("house", genreB: "breakbeat", mix: 0, variation: 0);
        var pureBreak = Gen("house", genreB: "breakbeat", mix: 1, variation: 0);
        yield return (!Has(pureHouse, 36, 10) && Has(pureBreak, 36, 10),
            "mix 0 is the first genre and mix 1 the second");

        var alternate = Gen("house", length: 8, genreB: "breakbeat", mix: .5, alternate: true, variation: 0);
        yield return (Has(alternate, 36, 16) && Has(alternate, 36, 26) && !Has(alternate, 36, 20),
            "alternate bars use the second genre on odd bars");
        var blended = Gen("house", length: 8, genreB: "breakbeat", mix: .5, alternate: false, variation: 0);
        yield return (!alternate.Select(n => (n.Pitch, Step(n))).SequenceEqual(blended.Select(n => (n.Pitch, Step(n)))),
            "alternate bars and a step blend are different mixes");

        // Meters and odd lengths.
        var three = Gen("house", length: 6, bpb: 3, variation: 0);
        yield return (three.Count > 0 && three.All(n => n.StartBeat < 6),
            "a 3/4 bar still stays inside the clip");
        var shortClip = DrumPatternGenerator.Generate(Kit, 2.1, 4,
            new DrumPatternOptions("house", Density: 1, Variation: 0), Seed);
        yield return (shortClip.All(n => n.StartBeat < 2.1 && n.LengthBeats > 0),
            "a clip shorter than a bar is followed exactly");

        // Every genre must produce something useful with the full kit.
        var failures = new List<string>();
        foreach (var genre in DrumPatternGenerator.Genres)
        {
            var notes = Gen(genre.Id, length: 16, density: .5, variation: .25);
            if (notes.Count == 0) failures.Add(genre.Id + ": empty");
            else if (notes.Any(n => Kit.All(v => v.Note != n.Pitch))) failures.Add(genre.Id + ": foreign pitch");
            else if (notes.Any(n => n.StartBeat < 0 || n.StartBeat >= 16)) failures.Add(genre.Id + ": outside clip");
            else if (notes.GroupBy(n => (n.Pitch, Step(n))).Any(g => g.Count() > 1)) failures.Add(genre.Id + ": duplicate");
        }
        yield return (failures.Count == 0, "every genre generates a clean pattern" +
            (failures.Count == 0 ? "" : " (" + string.Join(", ", failures.Take(4)) + ")"));
    }
}
