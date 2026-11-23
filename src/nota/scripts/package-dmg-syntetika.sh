#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Package an already-built Syntetika Engine.app into a compressed .dmg with the usual
# drag-to-Applications layout. Works offline with hdiutil only (the upstream dmgbuild
# path is not required).
#
# Usage:  scripts/package-dmg-syntetika.sh ["dist/Syntetika Engine.app"]
# Output: dist/Syntetika-Engine-<version>-<arch>.dmg
#         and its .sha256
# Set NOTA_DMG_DIR to write somewhere else.
#
# The bundle is ad-hoc signed (no Developer ID) and not notarized: on another Mac the
# Finder asks for right-click -> Open, or System Settings -> Privacy & Security -> Open
# Anyway. Shipping a warning-free DMG needs a Developer ID certificate + notarytool.

set -euo pipefail
cd "$(dirname "$0")/.."

APP="${1:-dist/Syntetika Engine.app}"
if [ ! -d "${APP}" ]; then
  echo "error: no app bundle at ${APP}" >&2
  echo "       build it first with scripts/bundle-mac.sh" >&2
  exit 1
fi

APP_NAME="$(basename "${APP}" .app)"
OUT_DIR="${NOTA_DMG_DIR:-dist}"
# The installer carries the product version (About); the bundle keeps Nota's upstream
# version. Override either with NOTA_DMG_VERSION.
APP_VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "${APP}/Contents/Info.plist")"
PRODUCT_VERSION="$(sed -n 's/.*public const string ProductVersion = "\([^"]*\)".*/\1/p' \
  src/managed/Nota.Application/Branding.cs | head -1)"
VERSION="${NOTA_DMG_VERSION:-${PRODUCT_VERSION:-${APP_VERSION}}}"
ARCH="$(lipo -archs "${APP}/Contents/MacOS/Nota.App" 2>/dev/null | tr ' ' '-')"
[ -n "${ARCH}" ] || ARCH="$(uname -m)"
DMG="${OUT_DIR}/Syntetika-Engine-${VERSION}-${ARCH}.dmg"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/syntetika-dmg.XXXXXX")"
MNT="${WORK}/mnt"
cleanup() {
  if [ -d "${MNT}" ]; then hdiutil detach "${MNT}" >/dev/null 2>&1 || true; fi
  rm -rf "${WORK}"
}
trap cleanup EXIT

echo "==> Staging ${APP_NAME}.app"
mkdir -p "${WORK}/root"
ditto "${APP}" "${WORK}/root/${APP_NAME}.app"
ln -s /Applications "${WORK}/root/Applications"

# The standard unsigned-build note: Finder on another Mac needs one manual approval.
cat > "${WORK}/root/Read me.txt" <<NOTE
Syntetika Engine ${VERSION} (Nota ${APP_VERSION})

Drag Syntetika Engine.app onto Applications.

This build is not notarized. On another Mac, macOS may say the app is from an
unidentified developer: right-click the app -> Open, or allow it under
System Settings -> Privacy & Security -> Open Anyway.

Nota is the host: Session clips carry the visuals and the bundled
syntetika-visual process plays them. The first launch creates
~/Library/Application Support/Syntetika Engine.
NOTE

echo "==> Building ${DMG}"
mkdir -p "${OUT_DIR}"
rm -f "${DMG}"
hdiutil create -volname "${APP_NAME}" -srcfolder "${WORK}/root" -fs HFS+ -format UDZO \
  -ov "${DMG}" >/dev/null

echo "==> Verifying the image"
mkdir -p "${MNT}"
hdiutil attach "${DMG}" -nobrowse -readonly -mountpoint "${MNT}" >/dev/null
if [ ! -d "${MNT}/${APP_NAME}.app" ] || [ ! -L "${MNT}/Applications" ]; then
  echo "error: the image is missing the app or the Applications link" >&2
  exit 1
fi
if ! codesign --verify --deep --strict "${MNT}/${APP_NAME}.app" >/dev/null 2>&1; then
  echo "   (ad-hoc signature check: warning — expected for a local build)"
fi
hdiutil detach "${MNT}" >/dev/null

HASH="$(shasum -a 256 "${DMG}" | awk '{print $1}')"
printf '%s  %s\n' "${HASH}" "$(basename "${DMG}")" > "${DMG}.sha256"

echo "==> Done: ${DMG} ($(du -h "${DMG}" | awk '{print $1}'))"
echo "    sha256: ${HASH}"
