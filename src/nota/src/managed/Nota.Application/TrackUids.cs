// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Stable track identity for the Syntetika Engine visual layout.
//
// Engine track ids are only unique inside one engine session: loading a project creates new
// tracks with new ids. The visual engine needs an identity that survives save/load so a
// track's visual layer follows the track, not its position. `ProjectDto.Track.Uid` carries
// that id in the project file; this registry maps it to the live engine id.
//
// Lives in the Application layer because both persistence (Infrastructure) and the visual
// bridge need it, and Application is the only shared dependency.

using System;
using System.Collections.Generic;

namespace Nota.Application;

public static class TrackUids
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, string> ByTrack = new();
    private static readonly Dictionary<string, int> ByUid = new(StringComparer.Ordinal);

    /// <summary>The persistent id for a live track, generated on first use (new tracks).</summary>
    public static string UidFor(int trackId)
    {
        lock (Gate)
        {
            if (ByTrack.TryGetValue(trackId, out var uid)) return uid;
            uid = Guid.NewGuid().ToString("N");
            ByTrack[trackId] = uid;
            ByUid[uid] = trackId;
            return uid;
        }
    }

    /// <summary>Re-attaches a saved id to the track the loader just created.</summary>
    public static void Bind(string? uid, int trackId)
    {
        if (string.IsNullOrEmpty(uid)) return;
        lock (Gate)
        {
            ByTrack[trackId] = uid;
            ByUid[uid] = trackId;
        }
    }

    public static bool TryGetUid(int trackId, out string uid)
    {
        lock (Gate) return ByTrack.TryGetValue(trackId, out uid!);
    }

    /// <summary>A new project or a project load starts a fresh id space.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            ByTrack.Clear();
            ByUid.Clear();
        }
    }
}
