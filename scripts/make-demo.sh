#!/usr/bin/env bash
# Build the Ink Industrial demo project ("Syntetika Demo").
#
# The audio is the canonical Nota manifest in demo/project.json; the visuals are
# regenerated from the ink-kaleido recipes in the openvj-app `demo_visuals` example.
# Running this again always produces the same bundle, so the demo never depends on
# a hand-maintained folder in Documents.
#
# Usage: scripts/make-demo.sh ["/path/to/Syntetika Demo.nota"]
#   Default: ~/Documents/Nota Projects/Syntetika Demo.nota
#
# The bundle is written from scratch (an existing manifest is kept as
# project.json.bak). Open it in Syntetika Engine and press Save once to seed the
# bundle's version history and sidecars.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${1:-$HOME/Documents/Nota Projects/Syntetika Demo.nota}"

mkdir -p "$DEST/visuals"
if [ -f "$DEST/project.json" ]; then
  cp "$DEST/project.json" "$DEST/project.json.bak"
fi
cp "$ROOT/demo/project.json" "$DEST/project.json"

echo "==> Generating visuals in ${DEST}"
cd "$ROOT/src/openvj"
"${CARGO:-$HOME/.cargo/bin/cargo}" run --release -p openvj-app --example demo_visuals -- "$DEST"

echo "==> Demo ready: ${DEST}"
