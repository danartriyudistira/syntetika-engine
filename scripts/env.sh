# Syntetika Engine — environment build (user-local, tanpa sudo)
#
# Pakai:  source scripts/env.sh
#
# Semua toolchain dipasang di HOME supaya tidak butuh sudo:
#   ~/.cargo   rustup/cargo
#   ~/.dotnet  .NET SDK 10
#   ~/.local   cmake, symlink tool lain
#   ~/Library/Python/3.9/bin  ninja (pip --user)

export PATH="$HOME/.cargo/bin:$HOME/.local/bin:$HOME/Library/Python/3.9/bin:$HOME/.dotnet:$PATH"

export DOTNET_ROOT="$HOME/.dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# 8 GB RAM: batasi paralelisme supaya tidak swap.
export CARGO_BUILD_JOBS="${CARGO_BUILD_JOBS:-6}"

# CPM (CMake Package Manager) — Nota mengunduh JUCE/FLAC/lame saat configure.
export CPM_SOURCE_CACHE="${CPM_SOURCE_CACHE:-$HOME/.cache/CPM}"

# Lokasi source: fork berada di dalam workspace proyek.
export SYNTETIKA_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export NOTA_DIR="${NOTA_DIR:-$SYNTETIKA_ROOT/src/nota}"
export OPENVJ_DIR="${OPENVJ_DIR:-$SYNTETIKA_ROOT/src/openvj}"
export NOTA_BUILD="${NOTA_BUILD:-$NOTA_DIR/src/native/nota.engine/build}"

# Nota: lewati build halaman Remote (butuh Node).
export NOTA_SKIP_WEB="-p:NotaSkipWebBuild=true"
