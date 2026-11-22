# Syntetika Engine

**Audio and visuals, on the same beat.**

Syntetika Engine is a live audio-visual workspace: Nota's Session view, with an openVJ visual on every clip. Launch a clip and its visual starts on the same musical beat — one performance surface for sound and picture.

> Status: **alpha (0.1.0)**. macOS Apple Silicon first. Expect rough edges.
> What works, known limits and the roadmap: [docs/04-status-dan-rencana.md](docs/04-status-dan-rencana.md).

## Built on two excellent open-source projects

This project is an **integration**, not a new DAW or a new VJ engine:

| Project | What it is | Author / licence |
|---|---|---|
| [Nota](https://github.com/nota-daw/nota) | Cross-platform DAW: audio engine, Session/Arrangement, plugin hosting | © Egor Khindikaynen — **AGPL-3.0-only** |
| [openVJ](https://github.com/openwave-studio/openvj) | GPU VJ mixer: layers, ISF, video, mapping, Syphon | openwave-studio and contributors — **GPL-3.0-or-later** |

We did not write the DAW or the VJ engine. Our work is the bridge between them, released under
the same licences. **Not affiliated with or endorsed by either project.** Exact upstream
revisions and file-level changes are listed in [ATTRIBUTION.md](ATTRIBUTION.md).

## What we added

- **Beat-accurate launches** — NotA is the master clock; launches are scheduled on absolute
  beats, with a PLL so visual playback stays smooth even when the clock source is steppy.
- **Structure mirroring** — one openVJ layer per playable track, one column per scene, live
  add/remove/reorder, with stable track UIDs across save/reload.
- **Selection mirroring** — selecting a clip in NotA selects and previews it in openVJ.
- **OpenVJ tab** — the selected clip's visual parameters, transform and full effect chain,
  edited with Nota's own sliders; two-way with openVJ's UI.
- **Media sources** — generators, ISF, images, video (bundled decode-only FFmpeg), cameras and
  Syphon; media is copied into the project bundle so it travels with the `.nota` file.
- **Persistence** — visuals are stored in the project bundle (`visuals/composition.json`) and
  restored with the track identity map.
- **Performance work** — static grid thumbnails, pipeline prewarm, and no GPU readback during
  a show.

## Run it

```bash
open "./src/nota/dist/Syntetika Engine.app"
```

The app starts its bundled visual engine (`Contents/Helpers/syntetika-visual`), restarts it if
it crashes, and stops it on quit. Then open **Session View**, select a clip, and use the
**OpenVJ** tab to assign a visual. Build instructions live in
[docs/02-m0-setup.md](docs/02-m0-setup.md).

## Repository layout

```
src/nota/      fork of Nota (AGPL-3.0-only)
src/openvj/    fork of openVJ (GPL-3.0-or-later)
docs/          research, setup and bridge notes
patches/       our diffs against the upstream revisions
scripts/       build environment
```

## Data & migration

This build keeps its profile in `~/Library/Application Support/Syntetika Engine`. On first
launch it copies an existing `NotAopenVJ` profile once and leaves the old folder untouched.

## Credits & licences

- Nota — © Egor Khindikaynen, AGPL-3.0-only. The Nota part of this repository remains AGPL.
- openVJ — openwave-studio and contributors, GPL-3.0-or-later. The openVJ part remains GPL.
- Integration layer and documentation — AGPL-3.0-only.
- Third-party notices (JUCE, Avalonia, Slint, FFmpeg, Ghost Arcade, fonts) are kept in each
  tree's `LICENSES/` and `THIRD_PARTY_NOTICES.md`; see [ATTRIBUTION.md](ATTRIBUTION.md).

“Nota” and “openVJ” are the marks of their respective owners and are used here descriptively.
