# Release a Syntetika Engine build

The release is a macOS **arm64** DMG (Apple Silicon) plus its SHA-256. The app is a single
bundle: Nota is the host and `syntetika-visual` (openVJ) rides in `Contents/Helpers`.

## 1. Bump the product version

`src/managed/Nota.Application/Branding.cs` → `ProductVersion` (e.g. `0.1.1`). The bundle script
reads it for `CFBundleShortVersionString`; the DMG script uses it for the file name. Nota's own
version (`src/nota/VERSION`, e.g. `0.60.4`) stays as the upstream base.

## 2. Build

```bash
cd src/openvj && cargo build --release
cd ../nota
bash scripts/bundle-mac.sh                 # native engine, managed publish, .app, ad-hoc sign
bash scripts/package-dmg-syntetika.sh      # dist/Syntetika-Engine-<version>-arm64.dmg + .sha256
```

Check `About` and `Get Info` show the same product version.

## 3. Sign and notarize (optional; currently skipped)

The project currently ships **ad-hoc** DMGs and asks users to approve the app once. Keep this
section for when a Developer ID becomes available.

Without these steps the DMG is **ad-hoc signed**: it runs on this Mac, but another Mac needs
right-click → Open or System Settings → Privacy & Security → Open Anyway.

One-time setup:

1. Apple Developer Program account and a **Developer ID Application** certificate in the login
   keychain.
2. Store notary credentials:

```bash
xcrun notarytool store-credentials syntetika-notary \
  --apple-id you@example.com --team-id ABCDE12345 --password app-specific-password
```

Then:

```bash
SYNTETIKA_SIGN_ID="Developer ID Application: Your Name (TEAMID)" \
NOTARY_PROFILE=syntetika-notary \
  bash scripts/notarize-mac.sh
```

The script signs every Mach-O inside-out (app, helper, `libnota_engine.dylib`, FFmpeg), signs
the DMG, submits it, staples the ticket and runs `spctl`.

Verify on a **different** Mac (or a clean user):

```bash
spctl --assess --type open --context context:primary-signature -v Syntetika-Engine-<v>-arm64.dmg
shasum -a 256 Syntetika-Engine-<v>-arm64.dmg
```

## 4. Publish

```bash
gh release create v<version> \
  "src/nota/dist/Syntetika-Engine-<version>-arm64.dmg" \
  --title "Syntetika Engine <version> (macOS arm64)" \
  --notes-file release-notes/<version>.md
```

Release notes must state the requirements (macOS 12+, Apple Silicon) and, for an ad-hoc build,
the Gatekeeper "Open Anyway" step. Do not attach the `.sha256` as a release asset; put its hash
in the notes instead (one download is clearer).

## CI

A GitHub Actions release build is still open (P6): it needs an arm64 macOS runner and the
Developer ID/notary secrets. Until then the local scripts above are the release path.
