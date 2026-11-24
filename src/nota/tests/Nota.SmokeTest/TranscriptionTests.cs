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

        // The Convert dialog's controls.
        var quiet = Transcription.ToClipNotes([N(0, 1, 60, 0.9f), N(0, 1, 64, 0.2f)], 10, 10,
            new TranscriptionOptions(MinStrength: 0.4));
        yield return (quiet.Length == 1 && quiet[0].Pitch == 60,
            "a note below the strength floor is left out");

        var crowded = Transcription.ToClipNotes(
            [N(0, 1, 60, 0.9f), N(0, 1, 64, 0.6f), N(0, 1, 67, 0.3f)], 10, 10,
            new TranscriptionOptions(MaxVoices: 2));
        yield return (crowded.Length == 2 && crowded.Any(n => n.Pitch == 60) && crowded.Any(n => n.Pitch == 64),
            "a chord keeps its strongest voices when the voice limit is lower");

        var shortNote = Transcription.ToClipNotes([N(0, 0.1, 60), N(1, 2, 64)], 10, 10,
            new TranscriptionOptions(MinLengthBeats: 0.25));
        yield return (shortNote.Length == 1 && shortNote[0].Pitch == 64,
            "a note shorter than the shortest-note setting is left out");

        var onGrid = Transcription.ToClipNotes([N(0.03, 0.22, 60), N(1.27, 1.48, 64)], 10, 10,
            new TranscriptionOptions(QuantizeGridBeats: 0.25, QuantizeEnds: true));
        yield return (onGrid.Length == 2
                      && Math.Abs(onGrid[0].StartBeat - 0) < 1e-9 && Math.Abs(onGrid[0].LengthBeats - 0.25) < 1e-9
                      && Math.Abs(onGrid[1].StartBeat - 1.25) < 1e-9 && Math.Abs(onGrid[1].LengthBeats - 0.25) < 1e-9,
            "quantize pulls starts and ends onto the grid");

        var halfWay = Transcription.ToClipNotes([N(0.1, 0.6, 60)], 10, 10,
            new TranscriptionOptions(QuantizeGridBeats: 0.5, QuantizeAmount: 0.5));
        yield return (halfWay.Length == 1 && Math.Abs(halfWay[0].StartBeat - 0.05) < 1e-9,
            "a partial quantize keeps half of the original timing");

        var lastNote = Transcription.ToClipNotes([N(9.96, 12, 60)], 10, 10,
            new TranscriptionOptions(QuantizeGridBeats: 0.25, QuantizeEnds: true));
        yield return (lastNote.Length == 1 && lastNote[0].StartBeat < 10 && lastNote[0].LengthBeats > 0,
            "a quantized note at the clip end stays inside the clip");

        var monophonic = Transcription.Monophonic([N(0, 1, 60, 0.4f), N(0, 1, 64, 0.9f), N(1, 2, 62, 0.5f)]);
        var line = Transcription.ToClipNotes(monophonic, 10, 10);
        yield return (line.All(n => n.Pitch != 60) && line.Any(n => n.Pitch == 64) && line.Any(n => n.Pitch == 62),
            "melody keeps the strongest note where the model heard two at once");
    }
}
