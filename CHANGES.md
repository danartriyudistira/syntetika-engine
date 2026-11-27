# Changes against upstream

Base revisions are listed in [ATTRIBUTION.md](ATTRIBUTION.md). This is a summary; the exact
diffs are in `patches/`.

## Session loop export

- **File → Export Session Loops…** renders the current deck scene by scene: one folder per
  filled scene, one WAV per track and an optional Standard MIDI File per instrument track.
  Files are loop length (no tail), 16/24-bit PCM or 32-bit float at 44.1/48/96 kHz; master
  devices are bypassed, track/group/return processing stays, and the scene tempo is used.
- Each slot is rendered in isolation (other tracks muted, solo off, launch quant 0, route MIDI
  out off); mute/solo, BPM, loop, metronome, launch quant, transport, deck and master bypass
  are restored afterwards. `AudioExporter` + `MidiFileWriter`; UI `SessionExportWindow`.
- Tests: `--export-session` (folder and file naming, loop frame count at 120 and 140 BPM, no
  leading silence, valid SMF, state restored).

## Global MIDI out offset

- **A/V Offset → MIDI out offset (all tracks)**: one slider (±200 ms, 1 ms steps) shifts every
  external MIDI route, added on top of each track's own trim in *Inspector → MIDI out*.
  Positive delays the synth send, negative advances it within the current block. The value is
  stored in app settings, applied at startup and while dragging, and noted in the status line.
- The picture offset keeps its own slider in the same window; the metronome/visual calibration
  behaviour is unchanged. Files: `VisualOffsetWindow`, `Settings.MidiOutOffsetMs`,
  `IAudioEngine.SetMidiOutOffsetMs` (engine-wide, added to `Track::midiOutOffsetMs`).
- Tests: `--midi-out` (global value reads back, survives an edit, and a positive offset holds
  the note until its due time).

## MIDI hot-plug

- **Ports connected while the app runs appear at once**: CoreMIDI keeps a per-process snapshot
  of the endpoint list until a client's notification run loop processes add/remove messages,
  so a synth or controller plugged in after launch never appeared in *Midi To* / *MIDI From* —
  not even after reopening the picker. `watchMidiPorts()` now starts one notification client
  on its own run-loop thread; output enumeration, reopening a saved route and input
  enumeration call it. The listener itself does nothing: receiving the add/remove message is
  what keeps `MIDIGetNumberOfDestinations` / `MIDIGetNumberOfSources` live.
- macOS only (CoreMIDI); the shared engine ABI is unchanged. Verified with a virtual CoreMIDI
  destination: a port created after the engine started appears in `MidiOutputs()` in ~250 ms
  and disappears when its process ends.

## Random MIDI parts

- **Random… in the MIDI clip editor**: next to Tools, it opens a dialog that writes a
  **bassline**, **pad** or **melody** from a genre, the clip's key and mode, and a seed. The
  20 genres are the same book as the drum Random dialog (House … Lo-Fi), with an optional
  second genre and a Mix amount.
- `MidiPartGenerator` is pure and deterministic: the same settings and seed produce the same
  notes, so Generate previews a take in the roll, Reroll auditions another, and Apply commits
  exactly the preview as one undo step. The bass and melody are monophonic; the pad holds
  chords and moves by voice leading. Density keeps the signature notes at 0 and fills the
  part at 1; Variation re-rolls bars, adds a bass walk-up every four bars and answers every
  second melody phrase.
- Length stops the take after 1/2/4/8 bars or fills the clip; Octave shifts each part's
  register; Chromatic is folded to natural minor so chords stay diatonic. Replace writes
  over the clip, Add layers the take without doubling a note already at that pitch and
  instant.
- Tests: `--midi-part` (genre-book parity with drums, legal notes in every genre × part,
  determinism, key/mode, chromatic fallback, monophony and registers, pad chords, motif
  repetition, density, octave, length, mix, 3/4 meter).

## Realtime tempo

- **BPM changes no longer freeze on warped audio**: changing the tempo used to rebuild every
  warped clip's offline stretch cache synchronously on the message thread. A BPM drag fires
  many changes, so a stem project could stall for seconds per move. `SetBpm` now only updates
  the transport; a current cache still plays the cheap, sample-exact path, and once the cache
  is stale the clip renders through its live `ClipWarpStream`, which reads the new tempo every
  block. No cache rebuild happens on a tempo change. The BPM field's arrangement refresh is
  throttled to ~30 Hz while dragging.
- `Engine::ensureLiveWarps` gives every playing warped clip a configured, unshared live voice;
  split/trim pieces on one track may share the same continuous stream (seamless), while
  duplicate tracks/clips get their own. A device sample-rate change reconfigures the voices.
- Tests: `--tempo-live` (four warped tracks, four BPM changes under 50 ms, audio after the
  change, duplicate track) and the existing streaming-warp/split/range-cut/1/8-meter suites.

## Branding and packaging

- Product is **Syntetika Engine** (`studio.syntetika.engine`); the app menu, window title,
  Welcome, Splash and About use it. Product version **0.1.6** now drives
  `CFBundleShortVersionString`/`CFBundleGetInfoString` (Nota's version stays the upstream base).
- Notarization is **deferred**: releases use an ad-hoc DMG with a one-time "Open Anyway".
  `scripts/notarize-mac.sh` + `docs/release.md` remain ready for a future Developer ID.
- Profile folder is `~/Library/Application Support/Syntetika Engine`; an existing
  `NotAopenVJ` profile is copied once on first launch (the old folder is left untouched).
- New original icon (SVG source, rendered to `logo.png`/`.icns`) and a new About credit block.
- The self-updater is disabled: it would replace this fork with a stock Nota release.
- `scripts/package-dmg-syntetika.sh` wraps the built bundle in a drag-to-Applications
  `.dmg` (hdiutil only) with a checksum; it carries the product version and a Gatekeeper
  note. Ad-hoc signed and not notarized, so another Mac needs **Open Anyway** once.
- ISF shaders (the Ghost Arcade pack and Syntetika's own) ship inside the bundle at
  `Contents/Resources/shaders`; `bundle-mac.sh` copies them next to the app.
- MCP server name is `Syntetika Engine`; default port 3910 (3900 is a stock Nota install).

## Nota fork

- **Random drum patterns by genre**: Pattern ▸ **Random…** fills the open Drum Rack clip
  from 20 genre sketches (House, Techno, Hip-Hop, Trap, Drum & Bass, Breakbeat, Disco, Funk,
  Rock, Pop, Reggaeton, Latin, Afrobeat, Amapiano, Jazz, UK Garage, Lo-Fi and more). Pad roles
  come from their names and the GM drum map; a kit with unrelated names still gets Kick,
  Snare, Closed Hat and Open Hat from its lowest pads. Choose a second genre and either blend
  it step by step or alternate bars, then set **Density** (signature hits only → busy) and
  **Variation** (one repeated bar → re-rolled bars and four-bar fills). **Replace** overwrites
  the pad notes in one undo step; off, it only fills empty steps. **Generate** keeps the
  dialog open for another roll, with the result in its status line; **Close** ends the session.
  The window is non-modal, so the transport can play while it is open, and it closes with the
  grid if the tab or clip changes. The grid switches to 1/16, and open hats replace closed hats,
  ghost snares and phrase crashes are built in. Tests cover role mapping, the signature of each
  genre, mixes, alternate bars, odd meters and every genre.

- **Genre Drum Rack presets**: 20 one-click presets (House, Techno, Tech House, Deep House,
  Hip-Hop, Trap, Drill, Drum & Bass, Breakbeat, Disco, Funk, Rock, Pop, Reggaeton, Latin,
  Afrobeat, Amapiano, Jazz, UK Garage, Lo-Fi) under **Instruments → Drum Rack → Genres**.
  Each borrows a matching factory kit's pads — the samples are rendered once and shared — and
  gives the rack that genre's swing, humanize and FX space. The same presets appear under Nota
  Rhythm, which remembers the preset it was loaded from. The base kits stay exactly as they
  were; the rack's timing names the preset again after loading.

- **One-file projects (`.syntetika`)**: a project can be a single ZIP container holding the
  bundle Nota already wrote — `project.json`, sidecars, samples, plugin states, visuals/media
  and the version history — plus a small `.package/manifest.json`. Opening extracts it to a
  working folder and the rest of the app keeps using a bundle; saving writes the folder and
  packs it back atomically (temp + rename). New projects and Save As use the file; a legacy
  `.nota` folder opens from File → **Open Legacy .nota Folder…**, saves as a folder, and is
  copied into a working folder when Save As migrates it to a container (the old folder stays as
  a backup). Recent Projects, the Projects browser, version switches and crash recovery know
  both forms. Media already compressed is stored in the zip; JSON/samples deflate.

- **Session decks**: a Nota deck is a page of scene rows that shares the tracks, instruments,
  devices and mixer. The deck bar sits under the Session grid (tabs left, **+ Deck** right;
  right-click a tab for Rename / Duplicate / Remove; the last deck stays). Switching decks
  never stops a playing clip, and Follow Actions stay inside the open deck. The project format
  is v36 (`session.decks` + `currentDeck`); a v35 file loads as one deck. openVJ mirrors the
  deck list (`vizDeck*` + `vizDecks`): one visual deck per Nota deck, one column per scene,
  duplicate copies the visual deck, remove drops it, and a deletion's undo restores the audio
  deck while its visual content starts empty (documented limitation).
- **Deck-aware triggers**: every scene ↔ openVJ column, clip ↔ slot and deck ↔ deck trigger
  now maps through the open deck (engine scenes are global, openVJ columns are per deck). A
  scene launched from Nota schedules each layer's visual on its clip quantum; a column clicked
  in openVJ asks Nota to launch that scene (audio + visuals) instead of starting a picture with
  no music; an openVJ deck tab switches Nota's deck (and vice versa) without an echo loop.
  Scheduled launches carry their deck, so a launch queued just before a deck switch can't fire
  on the new deck's same column. The OpenVJ tab compares replies against the open deck's
  column (a deck-2 slot no longer sits at "Loading the visual").
- **Video/image clips reload with the project**: opening a project (or switching to a deck)
  only deserialized the composition — the media behind video/image clips was never requested,
  so the clip was on the grid but black until it was replaced. Every load and deck switch now
  asks for the missing media (thumbnails, video lengths and decoders included).
- **New projects start with a blank picture**: New Project sends `vizReset` (clips, effects,
  media, history and extra decks dropped; resolution kept), and opening a project whose
  `visuals/composition.json` is missing loads blank instead of inheriting the previous
  project's effects. Undo/redo of a deck add/remove/rename/switch is reconciled by name (and by
  position for a rename), so the visual deck list follows the engine again.

- **Visuals ride with version history and crash recovery**: a save records
  `visuals/composition.json` beside the manifest, and the content-named media files a
  composition references are pinned by the versions that use them (the save's prune keeps
  them; the engine's own save-time GC is off for project saves so it cannot delete a file an
  older version still needs). Switching versions or opening one as a copy reloads the matching
  visuals. The periodic crash snapshot now copies the composition and media as well, and a
  restore loads them with the media placed somewhere durable (the project's media folder, or
  the staging cache for a never-saved project) before the snapshot is deleted.
- **Visual bridge** (`Nota.Infrastructure/Visuals/VisualBridge.cs`): loopback TCP, ~125 Hz
  transport, beat-scheduled launch/stop, layout snapshots, selection mirroring, viz parameter
  API, `vizSave`/`vizLoad`, single-instance file lock.
- **Follow Actions drive visuals**: the bridge mirrors the engine's playing scene onto openVJ
  layers, so Follow Action section changes launch (and clear) visuals too. UI launches are
  recorded as expected, so the mirror never retriggers a clip that was just scheduled.
- **Stable track UIDs** (`TrackUids`, `TrackDto.Uid`): a track's visual layer follows the track
  across save/load and reorder.
- **Layout mirroring**: one openVJ layer per playable track and one column per scene, live
  add/remove/reorder, group/return/master skipped.
- **OpenVJ tab** in the detail panel (`VisualParamsView`): source parameters, transport,
  layer controls, transform and the full effect chain, using Nota's slider controls; two-way
  with openVJ.
- **OpenVJ tab layout**: Undo/Redo, Replace/Remove and the Speed slider sit in the Transport
  card above its controls; the source name/kind moved into the Source card. Effect headers
  keep only their title and buttons, with Mix on its own row, so narrow columns no longer clip.
- **Persistence**: visuals are written to `visuals/composition.json` in the project bundle on
  save/quit and restored on open, together with the layer→track uid map.
- **Visual engine lifecycle**: the app starts the bundled `syntetika-visual` process (pointing
  it at the bridge), restarts it up to three times after a crash, and stops it on quit. The
  engine also exits by itself when NotA disappears, so no invisible orphan is left behind.
- **Video as a clip**: right-click a Session slot (audio track) → **Import video (audio + visual)…**,
  or drop a video from the library. Its sound is extracted with the bundled FFmpeg into the
  slot and its picture is assigned to the matching visual, beat-synced to the clip's length.
  Images drop the same way and become the visual only.
- **Media GC on save**: after a project saves its visuals, files in `visuals/media` that the
  composition no longer references are deleted, so removing a clip or replacing its media
  doesn't leave the bundle growing.
- **Video follows the audio trim**: shortening/looping an audio slot retimes its video to the
  same musical loop at normal speed (it trims the video instead of squeezing the whole file),
  a trimmed start moves the video's in-point to the same source time (e.g. audio 10–20 s plays
  video 10–20 s), turning the audio's loop off makes the video play once and clear, and the
  timing is refreshed after a project load or when a video is assigned to a slot that already
  has audio.
- **Audio-reactive visuals follow Nota's master**: while Nota hosts the engine it writes the
  final master (tracks, plugins, master chain) into a lock-free shared ring; openVJ analyses
  it with the same FFT as its input device, so MilkDrop, ISF audio inputs and audio-modulated
  parameters react to the DAW's sound without any extra routing. The tap costs two memcpys
  per audio block and starts/stops with the device.
- **Copy/paste carries the visual**: copying, cutting, duplicating or drag-copying a Session
  clip also copies or moves its openVJ visual (source, effects, playback, trim), including the
  media behind the new clip id.
- **Scene structure stays aligned**: inserting, removing, moving or duplicating a scene moves
  or copies the matching visual columns (duplicate copies each layer's visual), and the
  selected slot is re-sent by track UID after a layout change, so a track reorder cannot leave
  openVJ pointing at another layer.
- **Reconnect recovery**: a restarted visual engine reloads the saved project composition,
  then gets the layout, slot timing and a relaunch of every Session slot whose audio is still
  playing. Deleting a playing/queued clip clears its visual on the same quantum as the audio
  stop.
- **openVJ click launches Nota**: clicking a clip in openVJ's editor asks Nota to launch the
  matching Session slot, so its audio and visual start on Nota's launch quantum together. A
  visual-only clip still launches; the bridge falls back to a local visual launch when Nota is
  not connected, and the status line says when a slot has no audio clip.
- **Video drops pair the sound**: a video dropped on a Session slot's visual (OpenVJ tab or
  openVJ's own editor) also puts its audio into that slot when the slot is still empty, and
  beat-syncs the two. A slot that already has audio keeps it; a silent video stays silent.
- **Start/finish match**: visual launches use each clip's own Quantize, scene launches are
  scheduled per layer, stops (stop button, Stop All, Toggle, Gate/Repeat release) land on the
  same quantum as the audio, and the playing slot's local beat phase corrects the video's
  start/loop seam (latency-compensated).
- The browser's Files view and sample-folder tree list videos and images (not only audio),
  they are draggable onto Session slots, and the audio sample chips (Loops / One-shots / BPM /
  key) no longer hide them.
- **Visual output window**: View → **Visual Output** toggles openVJ's output window, kept
  always on top for a projector or second screen; its checkmark follows the engine's `vizOut`
  reports, and the last request is replayed when the engine reconnects. It opens at **480p
  (854×480)** so it leaves the DAW workspace visible. Maximizing the output window (green zoom
  button or title-bar double-click) means real fullscreen; **Esc** or **F** returns to the
  previous windowed frame, still on top.
- **Background engine**: Nota starts openVJ with its editor hidden, and View → **OpenVJ**
  shows or hides that window. Showing activates and raises it once (it is not kept above
  other apps), and closing the editor hides it instead of quitting the engine.
- **Projection mapping**: View → **Projection Mapping** opens openVJ's mapping window
  (input selection, output transformation, warp/slice/mask). Its checkmark follows the
  engine's `vizMappingState` reports and the request is replayed on reconnect.
- **Idle cost**: while the editor, output and mapping windows are hidden (and no Syphon client
  is receiving), openVJ skips the GPU frame, the UI sync and the redraw work, and drops its
  tick to 10 fps. Sharing the screen (output or editor) returns to 60 fps immediately.
- **Media sources**: the assign dialog offers video/image files, cameras and Syphon servers.
  Picked files are copied content-addressed into `visuals/media/<sha256>.<ext>` (staged in the
  data folder until the project is saved), and `composition.json` stores `media/<file>` so a
  bundle stays portable. **FFmpeg (decode-only, LGPL) and its licence notices ship inside the
  app**, next to the visual engine, so video plays without a separate install. The bundled
  build also enables the WAV muxer and `pcm_s16le` encoder used to extract a video's sound. The bundle also
  ships `Syphon.framework` and declares camera usage.
- **Performance**: static grid thumbnails (no GPU readback during a show), pipeline prewarm,
  PLL clock smoothing, stopped-transport freeze.
- **Engine log rotation**: `visual-engine.log` rolls to `.1` at 10 MB when the engine restarts,
  so a long-running installation cannot grow it without bound.
- **Measured performance**: hidden editor/output ≈3% CPU with the audio tap (folder watchers at
  5 s, 10 fps
  tick), output-only ≈24% at 60 fps, editor+output ≈48% (the editor window's renderer
  dominates; engine render stays <2 ms). The output-only path skips the editor model sync and
  preview target, and editor property sync is capped at 30 fps.
- **Slow-visual guard**: a generator or effect that needs far more than a frame's budget no
  longer freezes the app. While frames stay over 200 ms the engine halves its render size (down
  to 12.5%) and restores it below 6 ms; the composition label shows the percentage. The log
  names the exact source (`isf:<id>`, `milk:<preset>`, `gen:<kind>`), its effects and the size.
  The bridge also stopped queueing per-track phase messages while the OpenVJ tab is closed —
  that queue used to grow for as long as audio played.
- **Visual undo/redo**: the engine keeps the last 100 visual edits (clip source, parameters,
  playback, effects, and the layer's opacity/blend/bypass) and the OpenVJ tab gets Undo/Redo
  buttons. Every visual command marks automatically; slider drags coalesce into one step.
  Layout, composition changes and a successful save clear the history (before unused media
  is pruned), and undo never changes what is playing.
- **A/V offset & calibration**: View → A/V Offset… shifts every visual launch and the video
  phase by ±200 ms (positive delays the picture) and turns on a white beat strobe on the
  output; drag until it lands on Nota's metronome click (the strobe only runs while the
  transport advances). The value is saved and replayed to the engine before it starts, so
  the first launch already uses it.
- **Visual performance profiles**: View → Visual Performance offers Eco (30 fps, preview 15),
  Balanced (45/30) and Quality (60/60). The profile is saved in Nota and applied live to the
  visual engine; a hidden editor renders no preview at all. Measured on M1: output-only
  Quality ~24% CPU, Eco ~6.5%; editor+output Quality ~48%, Eco ~9.3% (30 fps, drops 0).

## openVJ fork

- **Nota bridge** (`nota_bridge.rs`): TCP client with reconnect, clock source, beat-scheduled
  launches, layout reconciliation, viz schema/editing, composition save/load.
- **Performance**: `set_live_thumbnails(false)` in performance mode with a one-shot static
  paint; prewarm queue; slow-render diagnostics; stopped-transport PLL fix.
- **Branding**: window title and wordmark “Syntetika Visual”; binary `syntetika-visual`.
- **Shader pack**: Ghost Arcade ISF plus Syntetika's `ink-kaleido` — an eight-fold monochrome
  ink kaleidoscope (rings, shards, snake bands, star, smoke, grain) used by the industrial
  demo; each section sets its own ring count, shard/snake mix, star size, smoke and contrast.
- **Output window**: always on top, and Nota can show/hide it over the bridge (`vizOutput`),
  with `vizOut` reports keeping the menu checkmark in step.

## Fixed before the first release

- **Audio → MIDI is now a dialog with timing and density controls**: Melody / Harmony open an
  Audio to MIDI dialog first. **Timing** snaps note starts (and, optionally, ends) to a grid —
  straight or triplet, defaulting to the piano roll's Quantize step — so a stem lands on the
  project tempo instead of a few frames late. **Density** adds a minimum strength, a shortest
  note and a voice limit per chord, so a dense basic-pitch result (weak neighbours, harmonics,
  re-attacks) arrives as playable notes. Independently, Convert still merges fragments, drops
  exact duplicates, trims same-pitch overlaps, suppresses weak harmonics and semitone ghosts
  and keeps real chords (octaves, fifths, semitones of comparable strength). Tests cover each
  rule and each control.

- **AI model downloads failed every time**: the pinned ONNX Runtime archive lived on
  `github.com/microsoft/onnxruntime` release assets, which now 404. The runtime comes from the
  immutable `Microsoft.ML.OnnxRuntime` 1.23.2 NuGet package (same native libraries, all six
  RIDs, pinned size and SHA); the first model installs it as before. Download failures now reach
  the log, not only the status line. Verified live: runtime + basic-pitch install and transcribe
  an A4 tone.
- **Save As repeated the container extension**: the suggested name already ended in
  `.syntetika` and the dialog appended another. The name is suggested without an extension.

- **Visual writes could resurrect a deleted bundle**: if the project folder was deleted or
  moved while open, the bridge recreated `visuals/` and the quit-save wrote an empty
  composition into it, leaving a folder that looked like a project but had no `project.json`.
  Layout and quit-save writes now require the manifest and are skipped (and logged) otherwise.
- **A save could claim success without the visuals**: `VizSave` is asynchronous, so Save now
  waits up to 1.2 s for the engine's acknowledgement and reports "visual changes not
  confirmed by the visual engine" when it never arrives.
- **Recovery restore listed a temporary bundle**: `OpenProject` learned to skip Recent
  Projects for the crash snapshot, which is deleted right after the restore; the real project
  is recorded instead.
- **Closing Visual Output could lose unsaved visuals**: the hosted engine used Slint's
  default event loop, which ends once the last visible window is hidden. Closing the output
  window therefore killed the helper, Nota restarted it, and the fresh process reloaded the
  last saved composition — so any visual assigned since the last Save disappeared. A hosted
  engine now runs `run_event_loop_until_quit()`: hiding the output keeps the process, the
  bridge and the composition alive, while a hand-started openVJ still quits with its window.
  Crash recovery also points the visual bridge at the real bundle (`SetProjectDir`/`VizLoad`)
  instead of the audio-recovery snapshot.
- **Quit could lose the composition**: the visual process was killed before its `vizSave`
  finished. The app now waits for the `vizSaved` acknowledgement (1.2 s) before stopping it.
- **Quit-save could lose every visual**: while Nota's engine was shutting down it could send
  an empty `layout_uids`, and the saved file then had no track map. Reopening rebuilt the
  layers empty and dropped all clips. The engine now falls back to its live uid map when the
  message is empty, and adopts the saved layer order when a file has no map but the layer
  count still matches the tracks.
- **Visuals stopped after a few crashes**: the restart budget was lifetime and never refilled,
  so three engine exits over a long session left the visuals down until the app restarted.
  A run of 15 s now refills the budget, and an explicit View start resets it; a sustained
  startup crash loop is still capped at three restarts.
- **Large media froze the UI**: hashing/copying a picked file now runs off the UI thread.
- **Empty camera/Syphon lists** showed a blank dialog; they now say what is missing.
- The FFmpeg availability probe is cached, so it is not re-run on every state poll.

## Known limitations

- Undoing a **deck removal** restores the audio deck; its visual clips start empty.
- Follow/stop while a deck is not open shows when that deck is opened again; visual-only
  clips (no audio) are not resumed automatically.
- MCP/remote scene tools address the global scene index, not a deck-local one.
- Project format v36 (decks) needs 0.1.4 or newer; older builds refuse it.

- macOS Apple Silicon is the only tested target.
- Visuals exist per Session slot; arrangement clips show a hint.
- Media/camera/Syphon sources are not yet in the assign picker (generators and ISF are).
- Undo/redo covers visual edits only; audio uses Nota's own history.
- `.nota` project bundles keep the upstream `"app": "Nota"` marker for compatibility.
