#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Assemble a macOS .app bundle for Nota with a proper Info.plist (so the OS treats
# it as a real app — needed for the microphone TCC permission prompt / M4-3).
#
# Usage: scripts/bundle-mac.sh
# Output: /Applications/Nota.app  (ad-hoc signed, self-contained arm64)
#         Override the destination with NOTA_APP_DIR=/some/dir

set -euo pipefail
cd "$(dirname "$0")/.."

RID="osx-arm64"
APP_NAME="Syntetika Engine"
# The assembly (and therefore the Mach-O executable inside the bundle) keeps its
# Nota.App name; only the product/bundle identity is rebranded.
APP_EXE="Nota.App"
BUNDLE_ID="studio.syntetika.engine"
# App version: single source of truth is the repo-root VERSION file.
# CFBundleVersion must monotonically increase per build;
# derive it from the semver so QA can tell builds apart.
VERSION="$(tr -d '[:space:]' < VERSION)"                 # Nota engine version
# The product version is what Finder/About show; it lives in Branding.cs.
PRODUCT_VERSION="$(sed -n 's/.*public const string ProductVersion = "\([^"]*\)".*/\1/p' \
  src/managed/Nota.Application/Branding.cs | head -1)"
PRODUCT_VERSION="${PRODUCT_VERSION:-0.1.0}"
PRODUCT_BUILD="$(echo "${PRODUCT_VERSION}" | awk -F. '{ printf "%d%03d%03d", $1, $2, $3 }')"
NATIVE_BUILD="$(pwd)/src/native/nota.engine/build"
APP_DIR="${NOTA_APP_DIR:-$PWD/dist}"
APP="${APP_DIR}/${APP_NAME}.app"
CONTENTS="${APP}/Contents"

PUBDIR="${NOTA_PUBLISH_DIR:-$(pwd)/dist/publish}"
if [ "${NOTA_SKIP_BUILD:-0}" != "1" ]; then
  echo "==> Building native engine (universal)…"
  # No -G here: reuse the generator the build directory was configured with (Ninja where
  # available, Unix Makefiles otherwise). Forcing Ninja turns an existing Makefiles cache
  # into a configure error.
  cmake -S src/native/nota.engine -B "${NATIVE_BUILD}" \
    -DCMAKE_BUILD_TYPE=Release >/dev/null
  cmake --build "${NATIVE_BUILD}" >/dev/null

  echo "==> Publishing managed app (${RID}, self-contained)…"
  rm -rf "${PUBDIR}"
  dotnet publish src/managed/Nota.App -c Release -r "${RID}" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false -p:NotaSkipWebBuild=true \
    -p:NotaNativeDir="${NATIVE_BUILD}" -o "${PUBDIR}" >/dev/null
else
  echo "==> Reusing published app in ${PUBDIR}"
fi

echo "==> Assembling ${APP}…"
mkdir -p "${APP_DIR}"
rm -rf "${APP}"
mkdir -p "${CONTENTS}/MacOS" "${CONTENTS}/Resources"
cp -R "${PUBDIR}/." "${CONTENTS}/MacOS/"
chmod +x "${CONTENTS}/MacOS/${APP_EXE}" "${CONTENTS}/MacOS/nota-scanworker" 2>/dev/null || true

# Syntetika Engine: the visual process rides inside the bundle, started by the app.
VISUAL_BIN="${NOTA_VISUAL_BIN:-$(pwd)/../openvj/target/release/syntetika-visual}"
mkdir -p "${CONTENTS}/Helpers"
if [ -f "${VISUAL_BIN}" ]; then
  cp "${VISUAL_BIN}" "${CONTENTS}/Helpers/syntetika-visual"
  chmod +x "${CONTENTS}/Helpers/syntetika-visual"
else
  echo "   (visual engine not found at ${VISUAL_BIN}; the app will look for one at runtime)"
fi

# ISF shaders (the Ghost Arcade pack plus Syntetika's own). The engine looks in
# Contents/Resources/shaders at runtime, so a distributed bundle needs its own copy.
SHADERS_SRC="${NOTA_SHADERS_DIR:-$(pwd)/../openvj/shaders}"
if [ -d "${SHADERS_SRC}" ]; then
  mkdir -p "${CONTENTS}/Resources/shaders"
  ditto "${SHADERS_SRC}" "${CONTENTS}/Resources/shaders"
else
  echo "   (shader folder not found at ${SHADERS_SRC})"
fi

# FFmpeg (decode-only, LGPL) sits next to the visual engine; `tool_path` finds it there.
FFMPEG_SRC="${NOTA_FFMPEG_DIR:-$(pwd)/../openvj/target/ffmpeg}"
if [ -x "${FFMPEG_SRC}/bin/ffmpeg" ]; then
  cp "${FFMPEG_SRC}/bin/ffmpeg" "${FFMPEG_SRC}/bin/ffprobe" "${CONTENTS}/Helpers/"
  chmod +x "${CONTENTS}/Helpers/ffmpeg" "${CONTENTS}/Helpers/ffprobe"
  mkdir -p "${CONTENTS}/Resources/FFmpeg"
  cp "${FFMPEG_SRC}"/licenses/* "${CONTENTS}/Resources/FFmpeg/" 2>/dev/null || true
else
  echo "   (FFmpeg not found; video clips need ffmpeg/ffprobe at runtime)"
fi

echo "==> Generating app icon (${APP_NAME}.icns)…"
ICON_SRC="assets/icons/logo.png"
ICONSET="$(mktemp -d)/${APP_NAME}.iconset"
mkdir -p "${ICONSET}"
# Canonical macOS iconset: each base size plus its @2x Retina variant.
for size in 16 32 128 256 512; do
  sips -z "${size}" "${size}" "${ICON_SRC}" \
    --out "${ICONSET}/icon_${size}x${size}.png" >/dev/null
  sips -z "$((size * 2))" "$((size * 2))" "${ICON_SRC}" \
    --out "${ICONSET}/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "${ICONSET}" -o "${CONTENTS}/Resources/${APP_NAME}.icns"
rm -rf "${ICONSET}"

cat > "${CONTENTS}/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>            <string>${APP_NAME}</string>
  <key>CFBundleDisplayName</key>     <string>${APP_NAME}</string>
  <key>CFBundleIdentifier</key>      <string>${BUNDLE_ID}</string>
  <key>CFBundleExecutable</key>      <string>${APP_EXE}</string>
  <key>CFBundleIconFile</key>        <string>${APP_NAME}</string>
  <key>CFBundlePackageType</key>     <string>APPL</string>
  <key>CFBundleShortVersionString</key> <string>${PRODUCT_VERSION}</string>
  <key>CFBundleVersion</key>         <string>${PRODUCT_BUILD}</string>
  <key>CFBundleGetInfoString</key>   <string>Syntetika Engine ${PRODUCT_VERSION} · Nota ${VERSION}</string>
  <key>CFBundleDevelopmentRegion</key> <string>en</string>
  <!-- The languages of src/managed/Nota.App/Languages: macOS shows its own text (Hide, Quit, the
       open panels) in one of these when the app asks for it (MacSystemLanguage.cs). -->
  <key>CFBundleLocalizations</key>   <array><string>en</string><string>fr</string></array>
  <key>LSMinimumSystemVersion</key>  <string>12.0</string>
  <key>NSHighResolutionCapable</key> <true/>
  <key>NSCameraUsageDescription</key>
  <string>Syntetika Visual plays cameras as live visuals.</string>
  <key>NSMicrophoneUsageDescription</key>
  <string>NotAopenVJ records audio from your selected input device.</string>
</dict>
</plist>
PLIST

# The visual helper links Syphon; ship the framework next to it so the bundle works on
# another Mac (the build tree's absolute rpath is only a local fallback).
SYPHON="$(ls -d ../openvj/target/release/build/syphon-core/*/out/frameworks/Syphon.framework 2>/dev/null | head -1)"
if [ -n "${SYPHON}" ]; then
  mkdir -p "${CONTENTS}/Frameworks"
  rm -rf "${CONTENTS}/Frameworks/Syphon.framework"
  cp -R "${SYPHON}" "${CONTENTS}/Frameworks/"
fi

echo "==> Ad-hoc signing…"
codesign --force --deep --sign - "${APP}" >/dev/null 2>&1 || \
  echo "   (codesign warning ignored)"

echo "==> Done: ${APP}"
echo "    Launch: open ${APP}"
