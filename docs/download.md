# Download Syntetika Engine

The latest installers are on GitHub:

**[Syntetika Engine releases](https://github.com/danartriyudistira/syntetika-engine/releases/latest)**

| | |
|---|---|
| Latest macOS | **0.1.20** (`Syntetika-Engine-0.1.20-arm64.dmg`, 112 MB — macOS 12+, Apple Silicon) |
| Latest Windows | **0.1.20** |
| Windows | `Syntetika-Engine-Setup-0.1.20-x64.exe` (110 MB) — Windows 10/11 x64 |

macOS SHA-256: `2956150440a5bef6b0b6d0fc6e0a26fc51233d00c1e5f866a4ecd6dee6cfb3ad`

Windows SHA-256: `fb0b996fc185ad413b4e05100d5ee211a0316be37290212c917c08a9a29bdfb2`

These hashes are optional: only the installer/DMG needs to be downloaded. The `.sha256` files are not separate assets.

## Install on Windows

1. Download `Syntetika-Engine-Setup-0.1.20-x64.exe` from the release.
2. (Optional) Verify the download (PowerShell):

   ```powershell
   Get-FileHash .\Syntetika-Engine-Setup-0.1.20-x64.exe -Algorithm SHA256
   ```

3. Run the installer. It installs **per user** (no administrator prompt) into
   `%LOCALAPPDATA%\Programs\Syntetika Engine` and adds Start Menu and desktop shortcuts.
   Choosing "install for all users" on the privileges page uses `Program Files` instead.
4. Launch **Syntetika Engine** from the Start Menu.

The package is self-contained: .NET, the Visual C++ runtime and FFmpeg are bundled, so the
PC needs nothing installed beforehand.

### SmartScreen

The installer is **not code-signed yet** (no certificate on the build machine). Windows may
say "Windows protected your PC". Choose **More info → Run anyway**. A new build installed
over an old one keeps projects and settings.

### Open a project

Nota projects are folders ending in `.nota`. Windows always opens a folder in Explorer, so
right-click the project folder and choose **Open with Syntetika Engine** (added by the
installer), or use **File → Open** in the app.

### Uninstall

Settings → Apps → **Syntetika Engine** → Uninstall (or the Start Menu entry). Projects and
settings in `%APPDATA%\Syntetika Engine` are kept.

## Install on macOS

1. Download the `.dmg` from the release.
2. (Optional) Verify the download:

   ```bash
   shasum -a 256 Syntetika-Engine-0.1.20-arm64.dmg
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

Nota is the DAW host; `syntetika-visual` (openVJ) runs beside it — in
`Contents/Helpers` on macOS, in the app folder as `syntetika-visual.exe` on Windows.
FFmpeg ships with both, so video clips carry their sound. Session clips carry visuals; the
DAW's transport, launches and master audio drive the picture.

Frame sharing is platform-specific: **Syphon** on macOS, **Spout** output on Windows
(Settings → Output, off by default, sender name "Syntetika Engine"). Spout *input* is not
available on Windows yet. Everything else in the Windows build — audio, visuals, mapping,
projection output, audio-reactive parameters — is the same.

First launch creates the profile:

- macOS: `~/Library/Application Support/Syntetika Engine` (an older `NotAopenVJ` profile is
  copied once, without deleting the original).
- Windows: `%APPDATA%\Syntetika Engine`.

## Staying up to date

The self-updater is disabled on purpose: it would replace this fork with a stock Nota release.
Install a new build over the old one; projects and settings stay in the profile folder.

## Older builds

All releases: <https://github.com/danartriyudistira/syntetika-engine/releases>
