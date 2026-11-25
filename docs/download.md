# Download Syntetika Engine

The latest installers are on GitHub:

**[Syntetika Engine releases](https://github.com/danartriyudistira/syntetika-engine/releases/latest)**

| | |
|---|---|
| Latest Windows | **0.1.7** |
| Latest macOS | **0.1.6** (`Syntetika-Engine-0.1.6-arm64.dmg`, 112 MB — macOS 12+, Apple Silicon) |
| Windows | `Syntetika-Engine-Setup-0.1.7-x64.exe` (110 MB) — Windows 10/11 x64 |

macOS SHA-256: `bc16054b56441ba80edd52855ee4b985f535014dcbb7cb17b872a5c375b567c8`

Windows SHA-256: `ab9fe309d21b6dcfe0ed6481f024e7ba4f41e33e058fd6fc51a8353ec4967267`

## Install on Windows

1. Download `Syntetika-Engine-Setup-0.1.7-x64.exe` and its `.sha256` from the release.
2. Verify the download (PowerShell):

   ```powershell
   Get-FileHash .\Syntetika-Engine-Setup-0.1.7-x64.exe -Algorithm SHA256
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

1. Download the `.dmg` and its `.sha256` from the release.
2. Verify the download:

   ```bash
   shasum -a 256 Syntetika-Engine-0.1.6-arm64.dmg
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

Syphon is macOS-only; the Windows build cannot send video to other apps yet (Spout is not
integrated). Everything else in the Windows build — audio, visuals, mapping, projection
output, audio-reactive parameters — is the same.

First launch creates the profile:

- macOS: `~/Library/Application Support/Syntetika Engine` (an older `NotAopenVJ` profile is
  copied once, without deleting the original).
- Windows: `%APPDATA%\Syntetika Engine`.

## Staying up to date

The self-updater is disabled on purpose: it would replace this fork with a stock Nota release.
Install a new build over the old one; projects and settings stay in the profile folder.

## Older builds

All releases: <https://github.com/danartriyudistira/syntetika-engine/releases>
