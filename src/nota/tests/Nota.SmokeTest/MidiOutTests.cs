// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// External MIDI output (Syntetika Engine): a track's clip notes, controllers and note-offs
// reach the chosen output port on its channel; stopping the transport releases everything.
// Uses the engine's test port, whose loopback output the test can read back.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MidiOutTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        using var e = new NotaEngine();
        int ta = e.AddConsortTrack();
        e.AddMidiClip(ta, 0, 4);
        e.SetClipNotes(ta, 0, new[] { new NotaNote(60, 0, 0.25, 1f) });
        int port = e.AddTestMidiPort("midiout");
        yield return (port >= 0, "a test MIDI port is available");
        if (port < 0) yield break;

        var outs = e.MidiOutputs();
        yield return (outs.Any(o => o.Uid == "test:midiout" && o.Name == "midiout"),
            "the output port is listed by the output enumeration");

        yield return (e.SetTrackMidiOutput(ta, "test:midiout", 3), "routing a track to an output port");
        yield return (e.GetTrackMidiOutputUid(ta) == "test:midiout" && e.GetTrackMidiOutputChannel(ta) == 3,
            "the output route and channel read back");

        var blk = new float[512 * 2];
        e.Seek(0);
        e.Play();
        for (int i = 0; i < 100; i++)
        {
            e.RenderOffline(blk, 512);
            if (i % 10 == 0) Thread.Sleep(5);
        }
        Thread.Sleep(200); // the pump thread sends outside the render call
        var played = e.TakeTestMidiSent(port);
        yield return (Contains(played, 0x92, 60, 127), "the clip's note-on reaches the port on its channel");
        yield return (Contains(played, 0x82, 60, _ => true), "the note-off follows");

        e.StopTransport();
        e.RenderOffline(blk, 512);
        Thread.Sleep(200);
        var stopped = e.TakeTestMidiSent(port);
        yield return (Contains(stopped, 0xB2, 123, 0), "stopping the transport sends all-notes-off");
        yield return (Contains(stopped, 0xB2, 120, 0), "and the controller reset");

        e.SetTrackMidiOutput(ta, null, 3);
        Thread.Sleep(300); // the off route's own all-notes-off drains here
        e.TakeTestMidiSent(port);
        e.Seek(0);
        e.Play();
        for (int i = 0; i < 20; i++) e.RenderOffline(blk, 512);
        Thread.Sleep(200);
        var off = e.TakeTestMidiSent(port);
        yield return (!HasNote(off), "an off route sends no notes");

        // Session clips render on their own path (renderSessionSlotRaw): they must play the
        // external output too, not just arrangement clips.
        using var es = new NotaEngine();
        int ts = es.AddConsortTrack();
        int sport = es.AddTestMidiPort("midiout-session");
        yield return (sport >= 0, "a session test MIDI port is available");
        if (sport < 0) yield break;
        bool sroute = es.SetTrackMidiOutput(ts, "test:midiout-session", 2);
        yield return (sroute, "a session track routes to the output port");
        es.AddSessionMidiClip(ts, 0, 4);
        es.SetSessionNotes(ts, 0, new[] { new NotaNote(65, 0, 0.5, 1f) });
        es.SetLaunchQuant(0);
        es.StopTransport();
        es.Seek(0);
        es.Play();
        es.LaunchSlot(ts, 0);
        for (int i = 0; i < 100; i++)
        {
            es.RenderOffline(blk, 512);
            if (i % 10 == 0) Thread.Sleep(5);
        }
        Thread.Sleep(200);
        var sessionSent = es.TakeTestMidiSent(sport);
        yield return (Contains(sessionSent, 0x91, 65, _ => true),
            "a Session clip's note reaches the output port");
    }

    private static bool Contains(byte[] data, byte status, byte d1, byte d2)
        => Contains(data, status, d1, v => v == d2);

    private static bool Contains(byte[] data, byte status, byte d1, Func<byte, bool> d2)
    {
        for (int i = 0; i + 2 < data.Length; i++)
            if (data[i] == status && data[i + 1] == d1 && d2(data[i + 2])) return true;
        return false;
    }

    private static bool HasNote(byte[] data)
    {
        for (int i = 0; i + 2 < data.Length; i++)
            if ((data[i] & 0xE0) == 0x80 || (data[i] & 0xF0) == 0x90) return true;
        return false;
    }
}
