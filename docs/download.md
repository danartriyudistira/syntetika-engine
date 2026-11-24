# Download Syntetika Engine

The latest installer is on GitHub:

**[Syntetika Engine releases](https://github.com/danartriyudistira/syntetika-engine/releases/latest)**

| | |
|---|---|
| Latest release | **0.1.4** |
| File | `Syntetika-Engine-0.1.4-arm64.dmg` (112 MB) |
| Requires | macOS 12 or newer, Apple Silicon (arm64) |
| SHA-256 | `f9899939a4945da9a36a089aaf32a55b66b773800317c41910403f1bc673a2aa` |

## Install

1. Download the `.dmg` and its `.sha256` from the release.
2. Verify the download:

   ```bash
   shasum -a 256 Syntetika-Engine-0.1.4-arm64.dmg
   ```

3. Open the image and drag **Syntetika Engine** onto **Applications**.
4. Launch it.

### Gatekeeper

The current DMG is **ad-hoc signed, not notarized** (no Developer ID certificate on the build
machine). On another Mac, macOS may say the app is from an unidentified developer. Approve it
once:

- Right-click the app in Finder → **Open** → **Open**, or
- System Settings → Privacy & Security → **Open Anyway**.

Notarization is not planned for now; the build stays ad-hoc. The signing/notary scripts are
kept in [release.md](release.md) for when a Developer ID becomes available.

## What's inside

One app bundle: Nota is the DAW host and `syntetika-visual` (openVJ) runs from
`Contents/Helpers`, together with FFmpeg and Syphon. Session clips carry visuals; the DAW's
transport, launches and master audio drive the picture.

First launch creates `~/Library/Application Support/Syntetika Engine`. An older
`NotAopenVJ` profile is copied once, without deleting the original.

## Staying up to date

The self-updater is disabled on purpose: it would replace this fork with a stock Nota release.
Install a new DMG over the old app; projects and settings stay in the application-support
folder.

## Older builds

All releases: <https://github.com/danartriyudistira/syntetika-engine/releases>
