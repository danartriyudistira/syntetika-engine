# Syntetika Engine

**Audio and visuals, on the same beat.**

Syntetika Engine is a live audio-visual workspace: Nota's Session view, with an openVJ visual on every clip. Launch a clip and its visual starts on the same musical beat — one performance surface for sound and picture.

![Syntetika Engine: the Session grid and OpenVJ tab driving a monochrome ink kaleidoscope in the always-on-top visual output](docs/images/syntetika-engine.png)

> **Syntetika Engine 1.0.1 is the latest release** for Windows x64 and macOS Apple Silicon.
> All 0.1.x releases are beta / pre-release and are not supported for daily use.
> What works, known limits and the roadmap: [docs/04-status-dan-rencana.md](docs/04-status-dan-rencana.md).
> Next macOS build priorities: [docs/07-macos-prioritas.md](docs/07-macos-prioritas.md).

## Download

| Platform | Version | File |
|---|---|---|
| **Windows x64** | **1.0.1 stable** | [`Syntetika-Engine-Setup-1.0.1-x64.exe`](https://github.com/danartriyudistira/syntetika-engine/releases/latest) |
| **macOS arm64** | **1.0.1 stable** | [`Syntetika-Engine-1.0.1-arm64.dmg`](https://github.com/danartriyudistira/syntetika-engine/releases/latest) |

Only the installer/DMG needs to be downloaded. SHA-256 hashes are **optional** and listed in
the release notes and [docs/download.md](docs/download.md).

## Built on two excellent open-source projects

This project is an **integration**, not a new DAW or a new VJ engine:

| Project | What it is | Author / licence |
|---|---|---|
| [Nota](https://github.com/nota-daw/nota) | Cross-platform DAW: audio engine, Session/Arrangement, plugin hosting | Ac Egor Khindikaynen — **AGPL-3.0-only** |
| [openVJ](https://github.com/openwave-studio/openvj) | GPU VJ mixer: layers, ISF, video, mapping, Syphon | openwave-studio and contributors — **GPL-3.0-or-later** |

We did not write the DAW or the VJ engine. Our work is the bridge between them, released under
the same licences. **Not affiliated with or endorsed by either project.** Exact upstream
revisions and file-level changes are listed in [ATTRIBUTION.md](ATTRIBUTION.md).

## What we added

### Session, MIDI and export

- **One-file projects** — save audio, visuals, media and version history in a single `.syntetika` file; legacy `.nota` folders still open and migrate on Save As.
- **Ableton-style mixer I/O** — instrument strips show **MIDI From → Monitor In/Auto/Off → MIDI out + Channel → Audio To**; audio strips show **Audio From → Monitor → Audio To**. Session I/O uses the same order: Input → Monitor → MIDI out → Output.
- **External MIDI output per track** — play a hardware or virtual synth (loopMIDI, USB-MIDI) from Arrangement **and** Session clips: notes, CC, pitch bend and channel pressure. Stop, seek, channel change, clip edit, audio-device change and quit send all-notes-off so nothing hangs.
- **MIDI timing** — timestamped send (each event leaves near its sample time, not as one block burst), a global **MIDI out offset** in **View → A/V Offset**, and a per-track trim in the Inspector. Positive delays the synth; negative advances it inside the current block. Routing and offsets are saved with the project.
- **Export Session Loops** — current deck → one folder per scene, a WAV per track and a MIDI (`.mid`) file per instrument clip. Loops are trimmed to the slot length, scene tempo is honoured, and master effects are bypassed for clean files. Empty slots are skipped.
- **Mixer/Session defaults** — I/O on, Sends off, Mixer on, matching a live performance layout.

### Visuals

- **Beat-accurate launches** — Nota is the master clock; launches are scheduled on absolute beats with a PLL, so visual playback stays smooth even when the clock source is steppy.
- **Structure and selection mirroring** — one openVJ layer per playable track, one column per scene, live add/remove/reorder, with stable track UIDs across save/reload. Selecting a clip in Nota selects and previews it in openVJ.
- **OpenVJ tab** — the selected clip's visual parameters, transform and full effect chain, edited with Nota's own controls; two-way with openVJ's UI.
- **Media sources** — generators, ISF, images, video (bundled decode-only FFmpeg), cameras and Syphon (Spout on Windows); media travels inside the project.
- **Video clips with sound** — drop a video on an audio Session slot: its audio is extracted into the slot and its picture becomes the slot's visual, beat-synced and retimed/trimmed with the audio loop.
- **Visual output and mapping** — always-on-top output window (480p by default, maximize for fullscreen, Esc to return) and **View → Projection Mapping** for projector warping, slicing and masking.
- **Clip copy/paste carries the visual** — copy, cut, duplicate or drag a Session clip and its openVJ visual and media move with it; scene insert/remove/move/duplicate keeps visual columns aligned.
- **Reconnect recovery** — a restarted visual engine reloads the project and resumes visuals for Session clips that are still playing.
- **Click-through launching** — clicking a clip in openVJ launches the matching Session slot (audio and visual on the same quantum).
- **Audio-reactive visuals** — the visual engine analyses Nota's master output through a lock-free shared ring, so shaders, MilkDrop and modulated parameters follow the DAW.
- **A/V offset and calibration** — **View → A/V Offset** shifts the picture ±200 ms with a beat strobe; the same window carries the global MIDI out offset.
- **Visual undo/redo** — Undo/Redo for clip sources, parameters, playback, effects and layer settings; a slider drag is one step.
- **Persistence** — visuals live in `visuals/composition.json` inside the project bundle and are restored with the track identity map.

### Performance and Windows

- **Auto visual profile** — integrated GPU → **Eco** (30 fps, preview 15), discrete GPU → **Quality** (60/60); macOS keeps Quality. **View → Visual Performance** also offers Eco/Balanced/Quality manually.
- **Adaptive render scale** — a visual that runs far over budget renders at a lower internal scale (down to 12.5%) instead of freezing the app.
- **Visual engine on demand** — an audio-only session pays no visual cost: openVJ starts only when you open **Visual Output**, the **OpenVJ** window/tab or **Projection Mapping**. Hidden, it runs at **below-normal priority**, stops GPU rendering and slows its tick; if it still grows past **1.2 GB** while hidden it saves the composition and exits, and the next visual request starts a fresh engine. `SYNTETIKA_VISUAL_ALWAYS=1` restores always-on.
- **Windows stability defaults** — 512-frame WASAPI buffer (≈10.7 ms), render workers follow **physical cores** rather than logical processors, and iGPU machines start at Eco.
- **No GPU readback during a show** — static grid thumbnails and pipeline prewarm keep the UI cheap.

## Run it

### Windows (x64, stable 1.0.1)

Download the installer from the
[latest release](https://github.com/danartriyudistira/syntetika-engine/releases/latest), or
build it yourself:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-win.ps1
& src/nota/src/managed/Nota.App/bin/Release/net10.0/Nota.App.exe
```

The Windows installer is self-contained (no .NET, VC++ or FFmpeg needed) and installs per user
by default. Build/packaging details: [docs/05-windows.md](docs/05-windows.md).

### macOS (Apple Silicon, stable 1.0.1)

Download the DMG from the
[latest release](https://github.com/danartriyudistira/syntetika-engine/releases/latest),
open it and drag **Syntetika Engine** to **Applications**. The image is ad-hoc signed, not
notarized: on another Mac, right-click → **Open** once (or System Settings → Privacy & Security
→ **Open Anyway**).

Build it yourself:

```bash
cd src/nota
bash scripts/bundle-mac.sh              # native engine + managed publish + .app
bash scripts/package-dmg-syntetika.sh   # dist/Syntetika-Engine-<version>-arm64.dmg
```

Install, Gatekeeper and checksum details: [docs/download.md](docs/download.md).

The app starts its bundled visual engine (`Contents/Helpers/syntetika-visual` on macOS,
`syntetika-visual.exe` beside the app on Windows) when a visual is first requested, restarts it
if it crashes, and stops it on quit. Then open **Session View**, select a clip, and use the
**OpenVJ** tab to assign a visual. Set `SYNTETIKA_VISUAL_ALWAYS=1` to start the engine with the
app instead.
Build instructions live in [docs/02-m0-setup.md](docs/02-m0-setup.md) and
[docs/05-windows.md](docs/05-windows.md).

## Versioning

- **1.0.1** — Windows x64 and macOS Apple Silicon: visual engine on demand, below-normal
  priority while hidden (Windows), a memory guard that saves and restarts a bloated hidden
  engine, and Syntetika-named library/projects folders with a one-time rename. The Windows
  1.0.1 installer was rebuilt once; the release notes carry the final hash.
- **1.0.0** — Windows x64 and macOS Apple Silicon, first stable release.
- **0.1.0 – 0.1.20** — beta / pre-release; kept for history, marked **Pre-release** on GitHub.

## Repository layout

```
src/nota/      fork of Nota (AGPL-3.0-only)
src/openvj/    fork of openVJ (GPL-3.0-or-later)
docs/          research, setup and bridge notes
patches/       our diffs against the upstream revisions
scripts/       build environment
```

## Data & migration

This build keeps its profile in `~/Library/Application Support/Syntetika Engine` on macOS and
`%APPDATA%\Syntetika Engine` on Windows. On first launch it copies an existing `NotAopenVJ`
profile once (macOS) and leaves the old folder untouched.

The library and projects defaults are `~/Music/Syntetika Samples` and
`~/Documents/Syntetika Projects`. A folder still named `Nota Samples` or `Nota Projects` is
renamed on disk once at startup, and the saved paths and recent-project list follow it (a
folder that cannot be renamed keeps its old path). Logs are `syntetika-YYYYMMDD.log`.

## Credits & licences

- Nota — Ac Egor Khindikaynen, AGPL-3.0-only. The Nota part of this repository remains AGPL.
- openVJ — openwave-studio and contributors, GPL-3.0-or-later. The openVJ part remains GPL.
- Integration layer and documentation — AGPL-3.0-only.
- Third-party notices (JUCE, Avalonia, Slint, FFmpeg, Ghost Arcade, fonts) are kept in each
  tree's `LICENSES/` and `THIRD_PARTY_NOTICES.md`; see [ATTRIBUTION.md](ATTRIBUTION.md).

“Nota” and “openVJ” are the marks of their respective owners and are used here descriptively.
