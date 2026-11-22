# M0 — Setup environment & build dasar

Tanggal mulai: 22 November 2026. Mesin: Apple M1, 8 GB RAM, macOS 27.2.1, 8 core.
Letak proyek: `<repo>` (134 GB bebas).

## Kondisi awal

| Tool | Status |
|---|---|
| Xcode penuh | **tidak ada** (hanya Command Line Tools, Apple clang 14.0.3, macOS SDK 13.3) |
| .NET | tidak ada → dipasang user-local (10.0.404) |
| Rust | tidak ada → dipasang user-local (stable, rustup `--no-modify-path`) |
| CMake | tidak ada → dipasang user-local (4.5.1, universal) |
| Ninja | tidak ada → dipasang via `pip install --user` (1.13.2) |
| Homebrew / Node / FFmpeg | tidak ada (Node dilewati dengan `-p:NotaSkipWebBuild=true`) |
| sudo | tidak tersedia tanpa password → semua instalasi user-local |

## Yang dipasang (tanpa sudo)

```
~/.cargo/bin            rustup + cargo (stable)
~/.dotnet               .NET SDK 10.0.404
~/.local/opt/cmake-4.5.1-macos-universal
~/.local/bin/cmake      symlink
~/Library/Python/3.9/bin/ninja
```

Script environment: `scripts/env.sh` (menambahkan PATH + variabel build).

## Catatan kompatibilitas

- **SDK 13.3 + clang 14** adalah di bawah yang diminta `BUILD.md` (Xcode 26 / macOS 26 SDK).
  Konfigurasi CMake Nota **berhasil** (JUCE 8.0.14, deps CPM, OBJC/OBJCXX terdeteksi).
- Kompilasi penuh berhenti di `Engine_Tracks.cpp`: clang 14 belum punya **P1091** (capture
  structured binding di lambda, C++20; clang ≥ 16 / Xcode 26 menerimanya). Dipatch lokal
  1 baris (`const int32_t rowTrack = tid;`) — kandidat untuk dikirim upstream sebagai
  portability fix, atau diganti dengan LLVM/Xcode baru.
- Nota default-nya membangun **universal (arm64 + x86_64)**. Untuk iterasi dipakai
  `-DCMAKE_OSX_ARCHITECTURES=arm64` agar waktu build separuh.
- 8 GB RAM membatasi paralelisme: cargo `-j6`, clang `-j4`, dan jangan jalankan dua build besar bersamaan.

## Perintah build

```bash
source scripts/env.sh

# Nota — engine saja (arm64)
cmake -G Ninja -S src/nota/src/native/nota.engine -B src/nota/build-arm64 \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64
cmake --build src/nota/build-arm64 -j 4

# Nota — aplikasi (.NET)
dotnet build src/nota/Nota.sln -c Release -p:NotaSkipWebBuild=true

# openVJ
cd src/openvj && cargo build --release
```

> Path `src/...` di atas berlaku setelah repo dipindahkan ke workspace proyek (lihat "Rencana
> relokasi"). Sementara ini repo ada di `~/research/syntetika/`.

## Hasil build

| Target | Status | Catatan |
|---|---|---|
| openVJ `cargo build --release` (default features: link, camera, syphon) | **OK** | 5m29s, binary 52 MB; `OPENVJ_SNAPSHOT` jalan: Metal Apple M1, 1080p, UI 39 fps |
| Nota engine (arm64) | **OK** | 13 MB `libnota_engine.dylib` + 8.7 MB `nota-scanworker`; 1 patch portability |
| Nota app (.NET) | **OK** | 41 s, 0 error / 32 warning; app jalan, memuat dylib hasil build sendiri |
| Nota smoke test | **berjalan** | 3309+ PASS, 0 FAIL saat dicek |

Catatan: `screencapture` ditolak sistem ("could not create image from display") — sesi ini tidak
punya izin Screen Recording, jadi validasi visual Nota tidak bisa lewat screenshot otomatis.
openVJ punya mode `OPENVJ_SNAPSHOT` (render sendiri), Nota tidak.

## Batasan disk

Volume sistem hanya **8.7 GB bebas**; build sudah memakai ±5 GB. Repo + build dipindah ke
`<repo>` (133 GB bebas) sebelum M1.

## Workspace (selesai dipindah)

```
Syntetika Engine/
  README.md
  docs/
  patches/           0001-clang14-structured-binding-lambda.patch
  scripts/           env.sh (PATH + variabel build)
  src/nota/          fork Nota (upstream = nota-daw/nota) + build-arm64/
  src/openvj/        fork openVJ (upstream = openwave-studio/openvj) + target/
```

Volume sistem pulih dari 8.7 GB → 11 GB bebas setelah pemindahan.

## Rebranding: Syntetika Engine

Tujuan: build fork bisa jalan berdampingan dengan Nota 0.60.4 yang sudah terpasang,
tanpa berbagi data project/settings dan tanpa ditimpa updater upstream.

| Titik | Sebelum | Sesudah |
|---|---|---|
| Nama produk (judul window, welcome, splash, About, menu macOS, Dock) | Nota | **Syntetika Engine** |
| `App.axaml` `Application.Name` (dipakai Avalonia untuk nama app di macOS) | Nota | **Syntetika Engine** |
| Data dir macOS | `~/Library/Application Support/Nota` | `~/Library/Application Support/Syntetika Engine` |
| Data dir Windows / Linux | `%APPDATA%\Nota` / `~/.config/nota` | `%APPDATA%\Syntetika Engine` / `~/.config/syntetika-engine` |
| Bundle id (script bundling) | `com.nota.daw` | `studio.syntetika.engine` |
| Nama `.app` | `Nota.app` | `Syntetika Engine.app` |
| MCP server name / port default | `Nota` / `3900` | `Syntetika Engine` / `3910` (3900 dipakai Nota asli) |
| Auto-update | GitHub nota-daw/nota | **dimatikan** (`Branding.UpdatesEnabled = false`) |
| String EN: `common.nota`, `common.about_nota`, `main.files.nota`, `main.files.nota_project`, `history.nota` | Nota | Syntetika Engine |

Semua konstanta ada di `src/managed/Nota.Application/Branding.cs`.

Verifikasi:
- `~/Library/Application Support/Syntetika Engine/` terbentuk saat app jalan (Nota asli tidak tersentuh).
- `dist/Syntetika Engine.app`: `CFBundleName=Syntetika Engine`, `CFBundleIdentifier=studio.syntetika.engine`,
  executable `Nota.App`, ad-hoc signed, 175 MB self-contained.
- `strings Nota.Application.dll` memuat `"common.nota": "Syntetika Engine"` (judul window, welcome, splash,
  About ikut berubah).

Patch tersimpan: `patches/0002-rebrand-notaopenvj.patch` (termasuk `Branding.cs`).

Yang **tidak** diubah (sengaja):
- Nama assembly/namespace (`Nota.*`) — internal, tidak terlihat user.
- `project.json` field `"app": "Nota"` — penanda format, bukan merek.
- Lisensi & header SPDX tetap; atribusi ke Nota/openVJ dipertahankan.
- Ikon, splash art, dan teks marketing lain — menyusul saat aset baru siap.

## Langkah setelah build hijau

1. Jalankan Nota (`dotnet run --project src/managed/Nota.App`) → cek Session View.
2. Jalankan openVJ (`cargo run --release`).
3. Spike #1 (sync): metronome Nota + flash putih openVJ, rekam, ukur offset/jitter.
4. Spike #2 (service mode): openVJ tanpa Slint, 4 layer 1080p, ukur fps.
