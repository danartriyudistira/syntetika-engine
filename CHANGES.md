# Changes against upstream

Base revisions are listed in [ATTRIBUTION.md](ATTRIBUTION.md). This is a summary; the exact
diffs are in `patches/`.

## Branding and packaging

- Product is **Syntetika Engine** (`studio.syntetika.engine`); the app menu, window title,
  Welcome, Splash and About use it.
- Profile folder is `~/Library/Application Support/Syntetika Engine`; an existing
  `NotAopenVJ` profile is copied once on first launch (the old folder is left untouched).
- New original icon (SVG source, rendered to `logo.png`/`.icns`) and a new About credit block.
- The self-updater is disabled: it would replace this fork with a stock Nota release.
- MCP server name is `Syntetika Engine`; default port 3910 (3900 is a stock Nota install).

## Nota fork

- **Visual bridge** (`Nota.Infrastructure/Visuals/VisualBridge.cs`): loopback TCP, ~125 Hz
  transport, beat-scheduled launch/stop, layout snapshots, selection mirroring, viz parameter
  API, `vizSave`/`vizLoad`, single-instance file lock.
- **Stable track UIDs** (`TrackUids`, `TrackDto.Uid`): a track's visual layer follows the track
  across save/load and reorder.
- **Layout mirroring**: one openVJ layer per playable track and one column per scene, live
  add/remove/reorder, group/return/master skipped.
- **OpenVJ tab** in the detail panel (`VisualParamsView`): source parameters, transport,
  layer controls, transform and the full effect chain, using Nota's slider controls; two-way
  with openVJ.
- **Persistence**: visuals are written to `visuals/composition.json` in the project bundle on
  save/quit and restored on open, together with the layer→track uid map.
- **Visual engine lifecycle**: the app starts the bundled `syntetika-visual` process (pointing
  it at the bridge), restarts it up to three times after a crash, and stops it on quit. The
  engine also exits by itself when NotA disappears, so no invisible orphan is left behind.
- **Media sources**: the assign dialog offers video/image files, cameras and Syphon servers.
  Picked files are copied content-addressed into `visuals/media/<sha256>.<ext>` (staged in the
  data folder until the project is saved), and `composition.json` stores `media/<file>` so a
  bundle stays portable. Videos need FFmpeg on the machine; images do not. The bundle now ships
  `Syphon.framework` and declares camera usage.
- **Performance**: static grid thumbnails (no GPU readback during a show), pipeline prewarm,
  PLL clock smoothing, stopped-transport freeze.

## openVJ fork

- **Nota bridge** (`nota_bridge.rs`): TCP client with reconnect, clock source, beat-scheduled
  launches, layout reconciliation, viz schema/editing, composition save/load.
- **Performance**: `set_live_thumbnails(false)` in performance mode with a one-shot static
  paint; prewarm queue; slow-render diagnostics; stopped-transport PLL fix.
- **Branding**: window title and wordmark “Syntetika Visual”; binary `syntetika-visual`.

## Known limitations

- macOS Apple Silicon is the only tested target.
- Visuals exist per Session slot; arrangement clips show a hint.
- Media/camera/Syphon sources are not yet in the assign picker (generators and ISF are).
- No undo for visual parameter edits; openVJ's own undo is not exposed yet.
- `.nota` project bundles keep the upstream `"app": "Nota"` marker for compatibility.
