// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nota.Application;

namespace Nota.Infrastructure;

/// <summary>Writes a one-track Standard MIDI File (format 0, 480 PPQ) for a Session clip:
/// tempo, time signature, track name and the clip's notes. Audio loops are the primary
/// artifact; this gives the same slot a MIDI companion for re-using the part elsewhere.</summary>
public static class MidiFileWriter
{
    private const int Ppq = 480;

    public static void Write(string path, string name, double bpm, int sigNum, int sigDen,
                             double lengthBeats, IReadOnlyList<NotaNote> notes)
    {
        var body = new MemoryStream();
        WriteVariable(body, 0);
        WriteMeta(body, 0x03, Encoding.UTF8.GetBytes(name ?? ""));   // track name
        long tempo = (long)Math.Round(60_000_000.0 / Math.Max(1.0, bpm));
        WriteVariable(body, 0);
        WriteMeta(body, 0x51, new[] { (byte)(tempo >> 16), (byte)(tempo >> 8), (byte)tempo });
        int denPow = 0;
        for (int d = sigDen > 0 ? sigDen : 4; d > 1; d >>= 1) denPow++;
        WriteVariable(body, 0);
        WriteMeta(body, 0x58, new[] { (byte)Math.Clamp(sigNum, 1, 255), (byte)denPow, (byte)24, (byte)8 });

        int lengthTicks = Math.Max(1, BeatsToTicks(lengthBeats));
        var events = new List<(int Tick, int Order, byte Status, byte Data1, byte Data2)>();
        foreach (var n in notes)
        {
            int start = Math.Clamp(BeatsToTicks(n.StartBeat), 0, Math.Max(0, lengthTicks - 1));
            int end = Math.Clamp(BeatsToTicks(n.StartBeat + n.LengthBeats), start + 1, lengthTicks);
            int pitch = Math.Clamp(n.Pitch, 0, 127);
            int velocity = Math.Clamp((int)Math.Round(n.Velocity * 127f), 1, 127);
            // Note-offs sort before note-ons at the same tick: a repeated pitch releases first.
            events.Add((start, 1, 0x90, (byte)pitch, (byte)velocity));
            events.Add((end, 0, 0x80, (byte)pitch, 0));
        }
        events.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));

        int last = 0;
        foreach (var e in events)
        {
            WriteVariable(body, Math.Max(0, e.Tick - last));
            body.WriteByte(e.Status);
            body.WriteByte(e.Data1);
            body.WriteByte(e.Data2);
            last = e.Tick;
        }
        WriteVariable(body, Math.Max(0, lengthTicks - last));
        WriteMeta(body, 0x2F, Array.Empty<byte>());   // end of track

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        WriteAscii(fs, "MThd");
        WriteInt(fs, 6);
        WriteShort(fs, 0);          // format 0
        WriteShort(fs, 1);          // one track
        WriteShort(fs, Ppq);
        WriteAscii(fs, "MTrk");
        WriteInt(fs, (int)body.Length);
        body.Position = 0;
        body.CopyTo(fs);
    }

    private static int BeatsToTicks(double beats) => (int)Math.Round(Math.Max(0.0, beats) * Ppq);

    private static void WriteMeta(Stream s, byte type, byte[] data)
    {
        s.WriteByte(0xFF);
        s.WriteByte(type);
        WriteVariable(s, data.Length);
        s.Write(data, 0, data.Length);
    }

    private static void WriteVariable(Stream s, int value)
    {
        value = Math.Max(0, value);
        Span<byte> bytes = stackalloc byte[4];
        int n = 0;
        bytes[n++] = (byte)(value & 0x7F);
        while ((value >>= 7) > 0) bytes[n++] = (byte)((value & 0x7F) | 0x80);
        for (int i = n - 1; i >= 0; --i) s.WriteByte(bytes[i]);
    }

    private static void WriteAscii(Stream s, string text)
    {
        foreach (char c in text) s.WriteByte((byte)c);
    }

    private static void WriteShort(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void WriteInt(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }
}
