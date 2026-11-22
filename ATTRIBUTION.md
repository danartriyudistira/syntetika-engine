# Attribution

Syntetika Engine is an integration project built on two open-source works. This file records
who wrote what, the exact revisions this repository is based on, and how our changes relate to
the upstream projects.

## Upstream projects

| Project | Repository | Author / maintainer | Licence |
|---|---|---|---|
| **Nota** | https://github.com/nota-daw/nota | Egor Khindikaynen (Ambertape) | AGPL-3.0-only |
| **openVJ** | https://github.com/openwave-studio/openvj | openwave-studio and contributors | GPL-3.0-or-later |

Upstream revisions this fork started from:

- Nota `develop` @ `d8b0be3` (2026-11-21), version 0.60.4
- openVJ `main` @ `d383b9b` (2026-11-17), version 0.1.0

## What is upstream

All of the DAW (audio engine, DSP, instruments, effects, plugin hosting, Arrangement/Session,
piano roll, project format) is Nota's work. All of the VJ engine (composition model, wgpu
renderer, ISF, video decoding, output mapping, Syphon) is openVJ's work. Their copyright
notices and SPDX headers are kept in every file.

## What this project added

- `src/nota`: a Nota fork with a visual bridge (master clock + beat-scheduled launches), stable
  track UIDs, layout mirroring, selection mirroring, an **OpenVJ** tab in the detail panel,
  visual persistence in the project bundle, single-instance and rebranding changes, plus
  performance fixes (PLL clock smoothing, static thumbnails, pipeline prewarm).
- `src/openvj`: an openVJ fork with a NotA bridge (TCP JSON protocol, launch scheduler, clock
  source), layout reconciliation, an effect-parameter schema for the tab, composition
  save/load in the project bundle, and rebranding of the window/binary.
- Repository glue: docs, scripts and the integration notes in `docs/`.

File-level diffs against the upstream revisions are kept in `patches/`.

## Not affiliated

This project is not affiliated with, endorsed by, or sponsored by the Nota or openVJ authors.
“Nota” and “openVJ” are used descriptively to credit the upstream work; see
[TRADEMARKS.md](TRADEMARKS.md).

## Licence of our changes

Changes inside `src/nota` are AGPL-3.0-only, changes inside `src/openvj` are GPL-3.0-or-later,
and the integration glue in this repository is AGPL-3.0-only. Nothing is relicensed.
