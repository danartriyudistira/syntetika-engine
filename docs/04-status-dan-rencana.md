# Status & Rencana Pengembangan

Terakhir diperbarui: 22 November 2026.
Repo: https://github.com/danartriyudistira/syntetika-engine
Commit terakhir: `1ba41dd` — *Start and stop the visual engine from the app*

---

## 1. Ringkasan

Syntetika Engine adalah **workspace audio-visual live**: Session View Nota dengan visual openVJ
di setiap clip. Audio dan visual diluncurkan pada **beat yang sama** dari satu aplikasi.

Ini adalah proyek **integrasi**, bukan DAW atau VJ engine baru:

- DAW, audio engine, Session/Arrangement, plugin hosting: **Nota** (AGPL-3.0-only, Egor
  Khindikaynen), basis `d8b0be3` (v0.60.4).
- VJ engine, renderer wgpu, ISF, video, mapping, Syphon: **openVJ** (GPL-3.0-or-later,
  openwave-studio), basis `d383b9b` (v0.1.0).
- Lapisan integrasi, branding, dan dokumen: milik proyek ini (AGPL-3.0-only).

Tidak berafiliasi dengan/endorse oleh kedua proyek upstream. Detail di `ATTRIBUTION.md`.

---

## 2. Yang sudah selesai

### 2.1 Fondasi & build (M0)

- Toolchain user-local tanpa sudo: Rust, .NET 10, CMake 4.5.1, Ninja.
- Nota engine (arm64) + app managed berhasil dibangun dengan CLT/clang 14; satu patch
  portability (P1091) disimpan di `patches/0001`.
- openVJ berhasil dibangun dan berjalan di Metal M1.
- Smoke test Nota: 7019 PASS / 5 FAIL (kegagalan lingkungan: halaman Remote tidak dibangun,
  fixture VST3, timing audio di mesin 8 GB).

### 2.2 Branding

- Nama produk **Syntetika Engine**, bundle id `studio.syntetika.engine`.
- Ikon asli (SVG → PNG/ICNS), About dengan versi produk `0.1.0` + versi engine.
- Data folder `~/Library/Application Support/Syntetika Engine` dengan **migrasi sekali** dari
  `NotAopenVJ` (disalin 92 MB / 493 file, 0 perbedaan, folder lama dihapus setelah verifikasi).
- Mesin visual: jendela **Syntetika Visual**, binary `syntetika-visual`.
- Auto-update dimatikan (tidak boleh menimpa fork dengan rilis Nota).

### 2.3 Integrasi inti

- **Bridge** TCP loopback `127.0.0.1:47810`, JSON per baris, dua arah.
- **Clock**: NotA mengirim `beat/bpm/playing` ~112–125 Hz; openVJ memakai PLL (bukan hard
  sync) sehingga animasi beat-synced halus walau sampel clock bertahap. Saat transport stop,
  beat dibekukan.
- **Launch**: `scene`/`slot` dikirim dengan **beat absolut**; openVJ mengantre dan mengeksekusi
  saat beat tercapai. Drift terukur 1–17 ms (satu frame).
- **Layout mirror**: 1 layer = 1 track audio/MIDI, 1 kolom = 1 scene; tambah/hapus/urut/rename
  live (≤250 ms). Track memakai **UID permanen** (`TrackDto.Uid` + `TrackUids`), jadi isi
  visual mengikuti track saat reorder/save/reload.
- **Seleksi**: memilih slot di NotA memilih slot yang sama di openVJ (Preview + Properties).
- **Tab OpenVJ** (3 kolom sejajar):
  - **Transport**: Speed, Beat sync + beats, Mode, Trigger.
  - **Source & Playback**: parameter sumber + Layer (opacity/blend/bypass).
  - **Effects**: Transform + seluruh rantai efek (add/remove/urut/bypass/mix) + parameter.
  - Slider memakai kontrol Nota (`SliderTrack` Shell), kartu memakai frame Nota.
  - Assign/ganti/hapus sumber lewat katalog (generator + ISF) dengan pencarian.
  - Dua arah: edit di openVJ ikut tampil di tab (poll 250 ms, guard saat drag).
- **Persistence**: `visuals/composition.json` + `layout_uids` di bundle project; ditulis saat
  save/quit, dimuat saat open.
- **Lifecycle engine**: NotA men-start/menghentikan `syntetika-visual` (bundle
  `Contents/Helpers`), restart maks 3× jika crash, dan engine keluar sendiri jika NotA hilang.
- **Performa**: thumbnail grid statis (tanpa readback GPU), prewarm pipeline, `slow render`
  monitor; 59–60 fps dengan ~0 slow render saat show.
- **Single-instance**: file lock `app.lock`; instance kedua memunculkan window yang ada.

### 2.4 Repo

- Publik, commit `67c7f7f` + `1ba41dd`.
- 3.498 file, `.git` ~29 MB, 24 patch upstream.
- `README.md`, `ATTRIBUTION.md`, `TRADEMARKS.md`, `CHANGES.md`, `LICENSE`, `LICENSES/`,
  `.gitignore`.
- Description & topics terpasang; GitHub mendeteksi AGPL-3.0.

---

## 3. Arsitektur

```
┌──────────────────────────── Syntetika Engine.app ────────────────────────────┐
│  Nota (C#/Avalonia)                            Syntetika Visual (Rust)        │
│  ├─ Session View + tab OpenVJ                  ├─ openVJ core/render/media    │
│  ├─ Transport (master clock)                    ├─ clock NOTA (PLL)           │
│  ├─ VisualBridge (server :47810)  ── TCP ──►    ├─ nota_bridge (client)       │
│  ├─ VisualEngineProcess (spawn/stop)            ├─ launch scheduler           │
│  └─ Bundle visuals/                             └─ output window / Syphon     │
└───────────────────────────────────────────────────────────────────────────────┘
```

Pesan utama:

| Arah | Pesan |
|---|---|
| N→V | `hello`, `clock`, `scene`/`slot` (+`atBeat`), `stopLayer`/`stopAll` |
| N→V | `layout` (tracks+scenes), `select`, `vizGet`, `vizSet`, `vizPlayback`, `vizFx`, `vizAssign`, `vizClear`, `vizCatalog`, `vizLayer`, `vizSave`, `vizLoad` |
| V→N | `vizState` (grup source/transform/effects/playback/layer_state), `vizCatalog` |

Data project: `<project>.nota/visuals/composition.json` (komposisi openVJ + `layout_uids`).

---

## 4. Cara menjalankan

```bash
source "<repo>/scripts/env.sh"

# Build visual engine
cd "$SYNTETIKA_ROOT/src/openvj" && cargo build --release

# Build + bundle app (engine ikut di Contents/Helpers)
cd "$SYNTETIKA_ROOT/src/nota"
dotnet publish src/managed/Nota.App -c Release -r osx-arm64 --self-contained true \
  -p:NotaSkipWebBuild=true -p:NotaNativeDir="$PWD/build-arm64" -o "$PWD/dist/publish"
NOTA_SKIP_BUILD=1 NOTA_APP_DIR="$PWD/dist" bash scripts/bundle-mac.sh

# Jalankan
open "$SYNTETIKA_ROOT/src/nota/dist/Syntetika Engine.app"
```

Log yang berguna:

- `~/Library/Application Support/Syntetika Engine/logs/nota-*.log`
- `~/Library/Application Support/Syntetika Engine/logs/visual-engine.log`

Hook uji (env, dev build): `NOTA_VISUAL_TEST=1`, `NOTA_LAYOUT_TEST=1`, `NOTA_SELECT_TEST=1`,
`NOTA_VIZ_TEST=1`.

---

## 5. Batasan saat ini

- macOS Apple Silicon saja yang teruji.
- Visual hanya untuk slot Session; clip Arrangement hanya menampilkan petunjuk.
- Pemilih sumber: generator + ISF. Media/kamera/Syphon belum; belum ada copy media ke bundle.
- Belum ada undo untuk parameter visual, preset visual, atau MIDI mapping visual.
- Belum ada offset A/V, kalibrasi, atau audio reactivity.
- Effect color/point2D tampil sebagai slider float (belum color picker/XY).
- Project belum disimpan → visual hanya di memori.
- Bundle unsigned/notarized; repo source-only (tanpa binary); belum ada CI.
- Mesin 8 GB + swap: sesekali gap frame 20–40 ms dari pacing macOS/WindowServer.

---

## 6. Bug penting yang pernah ditemukan (jangan terulang)

1. **Infinite loop `apply_layout`** saat layout 0 track → render loop mati. Perbaikan: trim
   berhenti saat `remove_layer` menolak (minimal 1 layer) + guard perubahan panjang.
2. **Key JSON `layer` ganda** di `vizState` (index vs objek) → tab stuck “Loading”. Sekarang
   `layer_state`.
3. **Beat sync mati** setiap Speed/Mode/Trigger diubah karena `syncBeats:null` selalu dikirim.
   Sekarang ada flag `setSync`.
4. **Single-instance** dengan `Mutex` tidak tahan karena lifetime thread → diganti file lock.
5. **Layout hilang saat reconnect** → client epoch memaksa snapshot baru.
6. **Clock jitter** → PLL 8%/frame, snap hanya jika error >0.25 beat.
7. **Stall thumbnail readback** → thumbnail statis di performance mode.
8. **Command struktural tertahan antrean beat** → sekarang dieksekusi langsung.

---

## 7. Rencana pengembangan

### P1 — Sumber media (SELESAI sebagian)

- [x] Picker video/gambar (dialog file), kamera, Syphon.
- [x] Media disalin content-addressed ke `visuals/media/<hash>.<ext>`; staging sebelum save.
- [x] `composition.json` menyimpan `media/<file>` (portabel); load me-resolve ke bundle.
- [x] Syphon.framework ikut di bundle + izin kamera di Info.plist.
- [ ] Thumbnail media di katalog.
- [ ] **FFmpeg bundled** (video saat ini butuh `ffmpeg`/`ffprobe` di PATH atau di samping
  helper); gambar tidak butuh FFmpeg.
- Kriteria video belum bisa diuji di mesin ini karena FFmpeg belum tersedia.

### P2 — Sinkronisasi A/V & reaktivitas audio (±5–7 hari)

- Slider offset A/V ±200 ms + kalibrasi (flash/klik).
- Tap audio master NotA → shared memory → band/hits openVJ (tanpa BlackHole).
- Kriteria: offset < 20 ms; visual bereaksi ke kick/bass; tidak menambah stall.

### P3 — Paket rilis v0.1.0 (±3–5 hari)

- Bundle helper ditandatangani + notarized; versi produk di Info.plist.
- `release.yml`: build macOS arm64, zip, checksum, catatan rilis.
- Halaman unduhan + panduan “Open Anyway”.
- Kriteria: unduhan bersih bisa dibuka di Mac lain; About menampilkan versi yang benar.

### P4 — Editing lanjutan (±5–8 hari)

- Undo/redo parameter visual (di NotA) dan/atau undo openVJ.
- Preset visual per clip/layer + browser.
- MIDI mapping parameter visual (belajar dari tab).
- Color picker & XY pad untuk ISF color/point2D.
- Kriteria: edit aman diulang; preset tersimpan di bundle.

### P5 — Arrangement & output (±1–2 minggu)

- Visual di timeline Arrangement (clip visual mengikuti clip audio).
- Multi-output/mapping dari tab.
- Layer groups mengikuti track group NotA.

### P6 — Infrastruktur (±1–3 hari + berkelanjutan)

- CI: `cargo check`, `dotnet build` managed, license/header check, `cargo test`.
- Strategi sinkronisasi upstream: rebase tiap 2–4 minggu, perbarui `patches/` dan
  `ATTRIBUTION.md` base commit.
- Opsional: fork repos upstream untuk mengirim perbaikan balik.

---

## 8. Risiko

| Risiko | Mitigasi |
|---|---|
| Upstream berubah cepat (Nota mingguan) | patch kecil, data di sidecar, rebase terjadwal |
| AGPL/GPL | source lengkap, notices utuh, tanpa restriksi tambahan |
| GPU/CPU 8 GB | DXV/HAP, resolusi komposisi, batasi layer |
| Bundle unsigned | notarization sebelum rilis publik |
| Proses yatim | exit-on-disconnect + stop on quit (sudah) |
| Data user | migrasi copy + marker, folder lama tidak dihapus otomatis |

---

## 9. Cara melanjutkan sesi berikutnya

1. `cd "/Volumes/Macintosh HD/Syntetika Engine"` dan `git status`.
2. `pgrep -fl "Syntetika Engine.app|syntetika-visual"` — pastikan hanya satu instance.
3. Cek log di `~/Library/Application Support/Syntetika Engine/logs/`.
4. Pilih P1/P2 dari daftar di atas; tulis rencana singkat sebelum kode.
5. Setelah perubahan: build → bundle → uji → `git add -A && git commit && git push`.

## 10. Keputusan terbuka

- Apakah repo akan menyertakan binary rilis (saat ini source-only)?
- Nama device internal (“Nota Reverb”, dst.) dibiarkan — konfirmasi tetap begitu.
- CI penuh butuh Xcode 26 + .NET 10 di runner; mulai dari CI ringan.
- Apakah visual Arrangement masuk roadmap publik atau ditunda.
