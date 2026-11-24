// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application.Midi;

namespace Nota.Application;

/// <summary>A transcribed note: times in seconds from the start of the audio, MIDI pitch, and
/// the model's mean activation over the note (0..1).</summary>
public readonly record struct TranscribedNote(double Start, double End, int Pitch, float Amplitude)
{
    public int Velocity => Math.Clamp((int)Math.Round(127 * Amplitude), 1, 127);
}

/// <summary>Turning transcribed notes into a clip's notes; shared by Convert and its MCP tool.</summary>
public static class Transcription
{
    /// <summary>One line out of a transcription: wherever notes overlap, the strongest one
    /// wins and the others are dropped (Convert Melody).</summary>
    public static List<TranscribedNote> Monophonic(IReadOnlyList<TranscribedNote> notes)
    {
        var kept = new List<TranscribedNote>();
        foreach (var n in notes.OrderByDescending(n => n.Amplitude * (n.End - n.Start)))
            if (!kept.Any(k => k.Start < n.End && n.Start < k.End)) kept.Add(n);
        kept.Sort((a, b) => a.Start.CompareTo(b.Start));
        return kept;
    }

    /// <summary>Transcribed notes as a MIDI clip's notes: <paramref name="audioSeconds"/> of
    /// audio spans <paramref name="clipBeats"/> (so a warped clip's notes follow its tempo);
    /// notes are kept inside the clip. The model can return the same pitch twice (a held note
    /// split by a confidence dip) or an octave overtone of a note it also heard; those are
    /// merged/dropped and same-pitch overlaps are trimmed, so the clip never plays a stacked
    /// note. Different pitches may still overlap — that is the harmony.</summary>
    public static NotaNote[] ToClipNotes(IReadOnlyList<TranscribedNote> notes, double audioSeconds, double clipBeats)
    {
        double toBeat = audioSeconds > 0 ? clipBeats / audioSeconds : 0;
        var mapped = new List<NotaNote>(notes.Count);
        foreach (var n in notes)
        {
            double start = Math.Max(0, n.Start) * toBeat;
            if (start >= clipBeats) continue;
            double end = Math.Min(n.End * toBeat, clipBeats);
            mapped.Add(new NotaNote(n.Pitch, start, Math.Max(0.05, end - start), n.Velocity / 127f));
        }
        var cleaned = MidiToolUtil.Deoverlap(DropOvertones(DropSemitoneGhosts(MergeFragments(mapped, 1.0 / 32))));
        return MidiToolUtil.Tidy(cleaned, clipBeats).ToArray();
    }

    // A sustained note can fall just below the model's frame threshold for a frame or two and
    // come back as two notes. Close the tiny gap when the pitch is the same and nothing else of
    // that pitch lies between them.
    private static List<NotaNote> MergeFragments(List<NotaNote> notes, double gapBeats)
    {
        var sorted = MidiToolUtil.Sorted(notes);
        var outp = new List<NotaNote>(sorted.Count);
        foreach (var n in sorted)
        {
            double prevEnd = outp.Count > 0 ? outp[^1].StartBeat + outp[^1].LengthBeats : 0;
            if (outp.Count > 0 && outp[^1].Pitch == n.Pitch
                && n.StartBeat >= prevEnd - 1e-6          // a gap, not two overlapping takes
                && n.StartBeat <= prevEnd + gapBeats)
            {
                var p = outp[^1];
                double end = Math.Max(p.StartBeat + p.LengthBeats, n.StartBeat + n.LengthBeats);
                p.LengthBeats = end - p.StartBeat;
                p.Velocity = Math.Max(p.Velocity, n.Velocity);
                outp[^1] = p;
                continue;
            }
            outp.Add(n);
        }
        return outp;
    }

    // Basic Pitch sometimes hears a harmonic above a note as its own note, weaker and starting
    // together with the fundamental: the 2nd harmonic (+12), 3rd (+19), 4th (+24), 5th (+28),
    // 6th (+31). A genuine chord tone has comparable strength; a quiet, same-length harmonic of
    // a louder note is an overtone and is dropped. A perfect fifth (+7) is a chord interval, not
    // a harmonic here, and stays.
    private static List<NotaNote> DropOvertones(List<NotaNote> notes)
    {
        if (notes.Count < 2) return notes;
        var drop = new bool[notes.Count];
        for (int i = 0; i < notes.Count; i++)
            for (int j = 0; j < notes.Count; j++)
            {
                if (i == j || drop[i] || drop[j]) continue;
                var low = notes[i];
                var high = notes[j];
                int interval = high.Pitch - low.Pitch;
                if (interval is not (12 or 19 or 24 or 28 or 31)) continue;
                if (Math.Abs(high.StartBeat - low.StartBeat) > MidiToolUtil.ChordTolerance) continue;
                if (high.Velocity >= low.Velocity * 0.8f) continue;
                if (high.LengthBeats > low.LengthBeats * 1.25 + MidiToolUtil.MinLength) continue;
                drop[j] = true;
            }
        var kept = new List<NotaNote>(notes.Count);
        for (int i = 0; i < notes.Count; i++) if (!drop[i]) kept.Add(notes[i]);
        return kept;
    }

    // A single pitch can split across two neighbouring semitones at the same onset when the
    // model is unsure; the weaker one is a ghost, not a minor second. A real semitone cluster is
    // close in strength and stays.
    private static List<NotaNote> DropSemitoneGhosts(List<NotaNote> notes)
    {
        if (notes.Count < 2) return notes;
        var drop = new bool[notes.Count];
        for (int i = 0; i < notes.Count; i++)
            for (int j = i + 1; j < notes.Count; j++)
            {
                if (drop[i] || drop[j]) continue;
                if (Math.Abs(notes[i].Pitch - notes[j].Pitch) != 1) continue;
                if (Math.Abs(notes[i].StartBeat - notes[j].StartBeat) > MidiToolUtil.ChordTolerance) continue;
                if (notes[i].Velocity >= notes[j].Velocity * 0.5f
                    && notes[j].Velocity >= notes[i].Velocity * 0.5f) continue;   // comparable: real cluster
                drop[notes[i].Velocity < notes[j].Velocity ? i : j] = true;
            }
        var kept = new List<NotaNote>(notes.Count);
        for (int i = 0; i < notes.Count; i++) if (!drop[i]) kept.Add(notes[i]);
        return kept;
    }
}

/// <summary>An audio clip read for Separate Stems (<see cref="IClipAi.ReadStemSource"/>).</summary>
public abstract class StemJob
{
    public required int TrackId { get; init; }
    public required int ClipIndex { get; init; }
    /// <summary>The clip's name, else its track's: names the stem clips and their group.</summary>
    public required string Name { get; init; }
}

/// <summary>Separated stems waiting to be applied; disposing deletes their temp files.</summary>
public abstract class StemResult : IDisposable
{
    public abstract void Dispose();
}

/// <summary>The AI features on audio clips (models from <see cref="IModelStore"/>). Separate
/// Stems runs in three steps so the engine stays on its thread: read (engine thread), separate
/// (any thread; tens of seconds), apply (engine thread, one undo step). Failures throw
/// <see cref="InvalidOperationException"/> with a user-facing message.</summary>
public interface IClipAi
{
    bool CanSeparate { get; }
    bool CanTranscribe { get; }

    StemJob ReadStemSource(int trackId, int clipIndex);

    StemResult Separate(StemJob job, IProgress<(double Fraction, string Label)>? progress = null, CancellationToken ct = default);

    /// <summary>Puts a group of stem tracks (Drums, Bass, Other, Vocals) under the clip's track,
    /// each with a clip shaped like the original, and switches the original off. Returns the
    /// group's track id.</summary>
    int ApplyStems(StemJob job, StemResult stems);

    /// <summary>Notes in <paramref name="mono"/> (any sample rate), by start time.</summary>
    IReadOnlyList<TranscribedNote> Transcribe(float[] mono, double sampleRate, IProgress<double>? progress = null, CancellationToken ct = default);
}
