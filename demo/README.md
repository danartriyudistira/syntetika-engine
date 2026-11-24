# Syntetika Demo

The demo project shown in the README and the release notes: **Ink Industrial**, 105 BPM,
2:59, three synth layers (DrumBrute, Model D, Kobol) with five Session scenes that advance
through Follow Actions, plus one ink-kaleido visual per section on three openVJ layers.

## Rebuild it

```sh
scripts/make-demo.sh
```

Default destination: `~/Documents/Nota Projects/Syntetika Demo.nota`. Pass a path to put it
somewhere else. The script:

1. copies `demo/project.json` (the canonical audio manifest: tracks, instruments, patterns,
   mixer, Session scenes and Follow Actions);
2. runs the `demo_visuals` example from `src/openvj`, which writes
   `visuals/composition.json` from the ink-kaleido recipes (15 clips, 3 layers, 5 looks).

Open the bundle in Syntetika Engine and press **⌘S** once: the save seeds the bundle's
version history and the remaining sidecars (`visuals/layout.json`, mixer/track view state).

## Notes

- No samples or plugin states are required: every layer is a built-in instrument, so the
  bundle is fully portable.
- `demo/project.json` is portable JSON (relative/none asset paths only). Keep it in sync when
  you intentionally change the demo's music; otherwise treat it as a fixture.
- Visual regeneration needs the repository checkout (shader + recipes), not the installed app.
- The visuals are deterministic: same file, same clips. If you change `demo_visuals`, the
  demo's visuals change with it.
