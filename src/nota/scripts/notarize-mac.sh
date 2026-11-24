#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Sign and notarize a Syntetika Engine DMG with a Developer ID.
#
# Prerequisites (one-time):
#   1. An Apple Developer Program account and a "Developer ID Application" certificate in the
#      login keychain (Xcode → Settings → Accounts → Manage Certificates, or the developer portal).
#   2. Notary credentials stored in a keychain profile:
#        xcrun notarytool store-credentials syntetika-notary \
#          --apple-id you@example.com --team-id ABCDE12345 --password app-specific-password
#   3. A DMG built by scripts/package-dmg-syntetika.sh and the .app it was made from.
#
# Usage:
#   SYNTETIKA_SIGN_ID="Developer ID Application: Name (TEAMID)" \
#   NOTARY_PROFILE=syntetika-notary \
#     scripts/notarize-mac.sh ["dist/Syntetika-Engine-<version>-arm64.dmg"] ["dist/Syntetika Engine.app"]
#
# Without SYNTETIKA_SIGN_ID the script picks the first Developer ID Application identity.
# It signs every Mach-O inside-out (helpers, engine, FFmpeg), then the bundle and the DMG,
# submits the DMG to notarytool, staples the ticket and verifies Gatekeeper accepts it.

set -euo pipefail
cd "$(dirname "$0")/.."

DMG="${1:-$(ls -t dist/Syntetika-Engine-*.dmg 2>/dev/null | head -1)}"
APP="${2:-dist/Syntetika Engine.app}"
PROFILE="${NOTARY_PROFILE:-syntetika-notary}"

if [ -z "${DMG}" ] || [ ! -f "${DMG}" ]; then
  echo "error: no DMG found (run scripts/bundle-mac.sh and scripts/package-dmg-syntetika.sh)" >&2
  exit 1
fi
if [ ! -d "${APP}" ]; then
  echo "error: no app bundle at ${APP}" >&2
  exit 1
fi

IDENTITY="${SYNTETIKA_SIGN_ID:-}"
if [ -z "${IDENTITY}" ]; then
  IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
    | sed -n 's/.*"\(Developer ID Application: [^"]*\)".*/\1/p' | head -1)"
fi
if [ -z "${IDENTITY}" ]; then
  cat >&2 <<'EOF'
error: no "Developer ID Application" identity in the keychain.

Notarization needs an Apple Developer Program account and a Developer ID certificate.
Without it the DMG stays ad-hoc signed and macOS asks for "Open Anyway" on other Macs.

Create one in Xcode → Settings → Accounts → Manage Certificates, then store the notary
credentials:
  xcrun notarytool store-credentials syntetika-notary \
    --apple-id you@example.com --team-id ABCDE12345 --password app-specific-password
EOF
  exit 2
fi

echo "==> Signing ${APP} with ${IDENTITY}"
# Inside-out: every Mach-O file first, then the bundle that contains them.
while IFS= read -r -d '' file; do
  if file -b "${file}" | grep -q "Mach-O"; then
    codesign --force --options runtime --timestamp --sign "${IDENTITY}" "${file}"
  fi
done < <(find "${APP}/Contents" -type f -print0)
codesign --force --options runtime --timestamp --sign "${IDENTITY}" "${APP}"
codesign --verify --deep --strict --verbose=2 "${APP}"

echo "==> Signing ${DMG}"
codesign --force --timestamp --sign "${IDENTITY}" "${DMG}"

echo "==> Submitting to notarytool (profile ${PROFILE})"
xcrun notarytool submit "${DMG}" --keychain-profile "${PROFILE}" --wait

echo "==> Stapling the ticket"
xcrun stapler staple "${DMG}"
xcrun stapler validate "${DMG}"

echo "==> Gatekeeper check"
spctl --assess --type open --context context:primary-signature -v "${DMG}"

echo "==> Done: ${DMG} is signed, notarized and stapled."
