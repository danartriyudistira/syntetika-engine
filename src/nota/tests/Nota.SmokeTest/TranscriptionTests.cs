// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio→MIDI note hygiene: one clip note per voice (no stacked same-pitch notes), tiny gaps
// merged, a weak octave overtone dropped, real chords kept. Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --transcription`.

using Nota.Application;

namespace Nota.SmokeTest;

internal static class TranscriptionTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        static TranscribedNote N(double start, double end, int pitch, float amp = 0.8f)
            => new(start, end, pitch, amp);

        // 10 seconds of audio span 10 beats: seconds map 1:1.
        var same = Transcription.ToClipNotes([N(0, 1, 60), N(0.5, 1.5, 60)], 10, 10);
        yield return (same.Length == 2 && same[0].StartBeat == 0 && same[0].LengthBeats <= 0.5 + 1e-9
                      && same[1].StartBeat == 0.5 && same.All(n => n.Pitch == 60),
            "two takes of the same pitch are trimmed into a sequence, not stacked");

        var duplicate = Transcription.ToClipNotes([N(0, 1, 60), N(0, 1, 60)], 10, 10);
        yield return (duplicate.Length == 1, "an exact duplicate (same pitch, same instant) is dropped");

        var octave = Transcription.ToClipNotes([N(0, 1, 57, 0.9f), N(0, 1, 69, 0.2f)], 10, 10);
        yield return (octave.Length == 1 && octave[0].Pitch == 57,
            "a quiet octave above a louder note is treated as an overtone");

        var twelfth = Transcription.ToClipNotes([N(0, 1, 60, 0.9f), N(0, 1, 79, 0.2f)], 10, 10);
        yield return (twelfth.Length == 1 && twelfth[0].Pitch == 60,
            "a weak 12th (the 3rd harmonic) above a note is dropped too");

        var ghost = Transcription.ToClipNotes([N(0, 1, 60, 0.9f), N(0, 1, 61, 0.2f)], 10, 10);
        yield return (ghost.Length == 1 && ghost[0].Pitch == 60,
            "a weak semitone next to a note is a pitch-estimate ghost, not a chord");

        var minorSecond = Transcription.ToClipNotes([N(0, 1, 60, 0.8f), N(0, 1, 61, 0.75f)], 10, 10);
        yield return (minorSecond.Length == 2, "a real semitone cluster (comparable strength) is kept");

        var fifth = Transcription.ToClipNotes([N(0, 1, 60, 0.8f), N(0, 1, 67, 0.8f)], 10, 10);
        yield return (fifth.Length == 2, "a perfect fifth is a chord, not a harmonic, and is kept");

        var chord = Transcription.ToClipNotes([N(0, 1, 57, 0.8f), N(0, 1, 69, 0.8f)], 10, 10);
        yield return (chord.Length == 2, "a genuine octave chord (equal strength) is kept");

        var third = Transcription.ToClipNotes([N(0, 1, 60, 0.8f), N(0, 1, 64, 0.7f)], 10, 10);
        yield return (third.Length == 2, "different pitches may overlap: that is the harmony");

        var fragments = Transcription.ToClipNotes([N(0, 0.5, 60), N(0.51, 1.0, 60)], 10, 10);
        yield return (fragments.Length == 1 && fragments[0].LengthBeats >= 0.99,
            "a confidence dip in a held note is stitched back into one note");

        var overrun = Transcription.ToClipNotes([N(9.8, 12, 60)], 10, 10);
        yield return (overrun.Length == 1 && overrun[0].StartBeat == 9.8 && overrun[0].LengthBeats <= 0.2 + 1e-9,
            "a note running past the clip is trimmed to it");

        var monophonic = Transcription.Monophonic([N(0, 1, 60, 0.4f), N(0, 1, 64, 0.9f), N(1, 2, 62, 0.5f)]);
        var line = Transcription.ToClipNotes(monophonic, 10, 10);
        yield return (line.All(n => n.Pitch != 60) && line.Any(n => n.Pitch == 64) && line.Any(n => n.Pitch == 62),
            "melody keeps the strongest note where the model heard two at once");
    }
}
