// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Genre drum-rack presets: a named, one-click starting point for each genre the Pattern
// generator knows. A preset borrows a factory kit's pads — so the samples are rendered once
// and shared — and gives it that genre's rack timing and FX space. The browser files the
// presets under a "Genres" folder on the Drum Rack (and on Nota Rhythm, which shares the
// kits). Keeping the base kit's pad names means the existing kit identification still works;
// the swing/humanize pair is unique enough to name the preset again after it is loaded.

using System.Collections.Generic;

namespace Nota.Infrastructure.Kits;

/// <summary>One genre preset: a base kit plus the rack-level settings that give it the
/// genre's feel. Pad recipes are never changed here, so the base kit's rendered samples are
/// reused; only timing, FX space and (optionally) the tape/tube choice differ.</summary>
public sealed record KitPreset(
    string Id,
    string Name,
    string Blurb,
    string BaseKitId,
    float Swing,
    float Humanize,
    double? FxSpace = null,
    double? FxDrive = null,
    bool? FxTape = null);

public static partial class KitCatalog
{
    private static readonly KitPreset[] s_presets =
    {
        new("genre-house", "House", "house · punchy analog kit, four-on-the-floor, light swing",
            "kompakt", .06f, .03f, FxSpace: .9),
        new("genre-techno", "Techno", "techno · warehouse kick and rumble, dry and driving",
            "bunker", 0f, .01f, FxSpace: 1.4, FxDrive: 1.4),
        new("genre-tech-house", "Tech House", "tech house · punchy kit, shuffling hats, tight swing",
            "kompakt", .08f, .04f, FxSpace: 1.0, FxDrive: 1.1),
        new("genre-deep-house", "Deep House", "deep house · warm analog boom, lazy swing, soft edges",
            "volta", .12f, .08f, FxSpace: 1.0, FxDrive: .9),
        new("genre-hip-hop", "Hip-Hop", "hip-hop · 90s boom bap, swung and dusty",
            "crate", .18f, .10f, FxSpace: .8),
        new("genre-trap", "Trap", "trap · sub 808, tight snares, rolling hats",
            "neon", 0f, .02f, FxSpace: .6, FxDrive: .9),
        new("genre-drill", "Drill", "drill · sub 808, sliding hats, light swing",
            "neon", .04f, .03f, FxSpace: .55, FxDrive: 1.0),
        new("genre-dnb", "Drum & Bass", "drum & bass · tuned-up break, ghosts and flams",
            "breakline", 0f, .06f, FxSpace: .9, FxDrive: 1.1),
        new("genre-breakbeat", "Breakbeat", "breakbeat · chopped break, ghost snares, loose feel",
            "breakline", .03f, .08f, FxSpace: .8, FxDrive: 1.1),
        new("genre-disco", "Disco", "disco · dry 70s kit, sizzling open hat",
            "mirrorball", .04f, .08f, FxSpace: .9),
        new("genre-funk", "Funk", "funk · live studio kit, syncopated and human",
            "atelier", .10f, .12f, FxSpace: .8),
        new("genre-rock", "Rock", "rock · big live room, panned toms, china",
            "brass-room", .02f, .10f, FxSpace: 1.1),
        new("genre-pop", "Pop", "pop · clean studio kit, tight and bright",
            "atelier", .02f, .04f, FxSpace: .8),
        new("genre-reggaeton", "Reggaeton", "reggaeton · log drums and hand percussion, dembow space",
            "lagoon", .05f, .08f, FxSpace: .8),
        new("genre-latin", "Latin", "latin · congas, bongos and shakers, loose swing",
            "terra", .10f, .15f, FxSpace: .9),
        new("genre-afrobeat", "Afrobeat", "afrobeat · log drums and shakers, deep pocket",
            "lagoon", .08f, .10f, FxSpace: .9),
        new("genre-amapiano", "Amapiano", "amapiano · log drums and shakers, laid-back swing",
            "lagoon", .13f, .11f, FxSpace: 1.0),
        new("genre-jazz", "Jazz", "jazz · brushes, soft kick, singing ride",
            "velvet", .30f, .20f, FxSpace: .8, FxDrive: .9),
        new("genre-uk-garage", "UK Garage", "UK garage · crisp 2-step kit, heavy shuffle",
            "shuffle", .26f, .06f, FxSpace: .8),
        new("genre-lo-fi", "Lo-Fi", "lo-fi · dusty vinyl break, tape and swing",
            "cellar", .14f, .14f, FxSpace: .7, FxTape: true),
    };

    /// <summary>The genre presets, in menu order. Each borrows its base kit's samples.</summary>
    public static IReadOnlyList<KitPreset> Presets { get; } = s_presets;

    public static KitPreset? PresetById(string id)
    {
        foreach (var p in s_presets) if (p.Id == id) return p;
        return null;
    }

    /// <summary>Resolves a base kit id or a genre preset id to the kit whose samples are
    /// loaded and the optional preset that shapes its rack settings.</summary>
    public static (KitDefinition Kit, KitPreset? Preset)? Resolve(string id)
    {
        if (ById(id) is { } kit) return (kit, null);
        if (PresetById(id) is { } preset && ById(preset.BaseKitId) is { } baseKit)
            return (baseKit, preset);
        return null;
    }
}
