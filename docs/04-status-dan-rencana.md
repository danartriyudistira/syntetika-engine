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
- **Audio tap**: master NotA ditulis ke ring file-backed (`audio-tap.shm`) tanpa lock; openVJ
  menganalisisnya dengan FFT yang sama seperti input device, jadi parameter audio-reactive
  mengikuti suara DAW (track, plugin, master chain) tanpa routing tambahan.
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
- [x] **FFmpeg decode-only (LGPL) dibundel** di `Contents/Helpers` + lisensi di
  `Contents/Resources/FFmpeg`; video jalan tanpa install terpisah.
- [ ] Thumbnail media di katalog.
- [ ] GC media tak terpakai saat save.
- Bukti: image 720×720 dan video 640×360 ter-decode dari bundle; save menyimpan
  `media/<hash>.mp4`, load memulihkannya.

### P2 — Sinkronisasi A/V & reaktivitas audio (±5–7 hari)

- [ ] Slider offset A/V ±200 ms + kalibrasi (flash/klik).
- [x] Tap audio master NotA → shared memory → band/hits openVJ (tanpa BlackHole).
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

## 7a. Video dengan audio di Nota (23 Nov)

- Menu slot (audio track): **Import video (audio + visual)…**; drag video dari browser/Finder
  ke slot Session (audio) melakukan hal yang sama.
- Audio diekstrak FFmpeg (`pcm_s16le`, 48 kHz stereo) ke `<data>/video-audio-staging`, lalu
  `AddSessionAudioFile` menaruhnya di slot.
- Video disalin ke `visuals/media`, di-assign ke layer slot, dan `syncBeats` = panjang audio.
- Browser Files (dan tree sample folder) kini mendaftar video/gambar dan bisa di-drag.
- Bukti: Sintel (51,9 s) → audio 103,85 beat, video `sync=103.85`, decode 854×480.
- Browser tree terbukti memuat media: 79 audio + 4 video/image (folder `Movies` dan
  `Nota Samples/Visuals`).
- FFmpeg bundled ditambah muxer **wav** + encoder **pcm_s16le**.

## 7b. Bug hunt pra-rilis (23 Nov)

| Bug | Perbaikan |
|---|---|
| Save saat quit bisa hilang karena child dibunuh sebelum `vizSave` selesai | `vizSaved` ack + tunggu 1,2 s sebelum stop |
| Hash/copy media besar di UI thread | `Task.Run` saat memilih file |
| Katalog kamera/Syphon kosong tanpa pesan | Pesan “no cameras/Syphon servers found” |
| `ffmpeg_available()` men-spawn 2 proses tiap poll | Di-cache (`OnceLock`) |
| `TryGetTrackInfo(trackId)` dipakai dengan **id** (bukan index) saat impor video | Track audio ditolak; resolve id→index dulu |
| Filter “Loops/One-shots/BPM” menyembunyikan video/image | Media selalu lolos filter sample |
| Video/foto tampil sebagai `OtherFile` (abu-abu, tak bisa di-drag) | Kind `VideoFile`/`ImageFile` + drag/drop |

Uji ulang dari bundle: start bersih, image 720×720 + video 640×360 ter-decode, `save ack=True`,
save/load media benar, `codesign --verify --deep` lolos, tidak ada error baru di log.

## 7c. Menu View → Visual Output (23 Nov)

- Menu **View → Visual Output** (checkbox) membuka jendela output openVJ; jendela itu
  **always on top** di atas DAW dan aplikasi lain, cocok untuk proyektor/layar kedua.
- Klik lagi menyembunyikan; tombol X di openVJ juga mematikan centang lewat pesan balik
  `vizOut`.
- Permintaan terakhir disimpan di bridge dan dikirim ulang saat engine reconnect, jadi klik
  sebelum engine siap tidak hilang.
- Perintah yang sama tersedia di Command Palette.
- Bukti uji: `vizOut open=true`, log engine `output window shown`, window “Syntetika Visual
  Output” muncul; toggle kedua menghasilkan `open=false`.

## 7d. openVJ sebagai background engine (23 Nov)

- Nota menjalankan `syntetika-visual` dengan `SYNTETIKA_UI_HIDDEN=1`; window editor tidak
  tampil saat start.
- **View → OpenVJ** menampilkan/menyembunyikan editor; saat ditampilkan, window
  **di-raise dan diaktifkan sekali** sehingga langsung terlihat, tetapi **tidak** selalu
  di atas aplikasi lain (tidak selalu-on-top). Tombol X hanya menyembunyikan selama
  dijalankan Nota, engine tetap hidup dan bridge tetap tersambung.
- **Maximize = fullscreen**: klik tombol hijau/maximize (atau double-click title bar) pada
  window output langsung masuk fullscreen penuh. **Esc** atau **F** kembali ke window output
  dengan ukuran/posisi terakhir; always-on-top dipasang lagi setelah keluar fullscreen.
  Tombol ditangani di level winit window, jadi tetap bekerja walau tidak ada kontrol fokus.
- Bukti uji: zoom → `1920×1080` (`maximize -> fullscreen`); Esc → kembali `960×568` di posisi
  semula (`fullscreen off`), layer always-on-top 3, tanpa masuk fullscreen lagi.
- Window output default sekarang **854×480 (480p)** agar tidak menutupi area kerja Nota;
  restore setelah fullscreen memakai ukuran windowed terakhir.
- State dikirim dua arah: `vizUi` dari Nota, `vizUiState` dari openVJ; centang menu ikut
  berubah walau window ditutup dari openVJ.
- **Optimalisasi**: saat editor, output, dan mapping tersembunyi (tidak ada klien Syphon),
  openVJ melewati `engine.render`, `sync` UI, thumbnail/library pump, dan redraw; tick turun
  dari 60 ke 10 fps. Begitu jendela tampil, kembali 60 fps.
- Bukti: CPU idle turun dari **18–27%** (UI selalu tampil) menjadi **~6–8%** saat semua
  tersembunyi; window list hanya menampilkan Nota, `render=off` di log, dan output tetap
  60 fps dengan `max_render` > 0 saat dibuka.
- Sisa biaya idle berasal dari thread internal engine (watcher folder efek/MilkDrop, Link,
  driver GPU), bukan render atau tick; kandidat optimasi berikutnya adalah memperlambat
  pemindaian folder.
- Syphon discovery tetap hidup saat hidden: server dibuat ringan, dan render otomatis
  berjalan begitu ada klien yang menerima.

## 7e. Menu View → Projection Mapping (23 Nov)

- Menu **View → Projection Mapping** (checkbox) membuka window **openVJ Output mapping**:
  pilih input, atur transformasi output, slice/warp/mask untuk proyektor.
- State dua arah: `vizMapping` dari Nota, `vizMappingState` dari openVJ; centang menu ikut
  berubah saat window ditutup lewat X, dan permintaan terakhir dikirim ulang saat engine
  reconnect.
- Window mapping langsung di-raise dan mendapat fokus saat dibuka.
- Render otomatis aktif selama window mapping terbuka (kebutuhan canvas/preview);
  saat ditutup dan editor/output juga hidden, engine kembali hemat.
- Bukti uji: menu → window “openVJ Output mapping” + centang ✓; X → tertutup + centang mati;
  kill engine → mapping terbuka lagi otomatis; log `mapping window shown/hidden`.

## 7f. Video mengikuti trim/loop audio (23 Nov)

- Nota memantau panjang loop dan status **Loop** setiap slot audio (2×/detik) dan mengirim
  `vizSlotTiming` hanya saat berubah. Edit panjang/loop di Session Inspector memaksa kirim
  seketika.
- openVJ menyimpan `sync_beats` (loop audio) dan `sync_secs` (durasi detik loop). Video yang
  lebih panjang dipotong sepanjang loop audio dan diputar **pada kecepatan normal**, bukan
  seluruh file dipadatkan ke loop. Video yang lebih pendek loop alami di dalam slot.
- Loop audio **off** → video `Play once & hold`; loop on → `Loop`.
- Timing dikirim ulang saat engine reconnect, setelah project load, dan setelah video
  di-assign ke slot yang sudah punya audio.
- Bukti uji (Sintel 51,93 dtk, 120 BPM): impor `sync=103.85 trim=full in=0s`; region audio
  dipotong **10s–20s** → `sync=20 trim=10s in=10s` (video memutar 10–20 dtk, loop tiap 20
  beat); loop off → `mode=Play once & clear`. Unit test
  `a_trimmed_audio_loop_trims_the_video_at_normal_speed` dan
  `a_trimmed_audio_start_moves_the_video_in_point` lulus.

## 7g. Start/finish visual = audio (23 Nov)

- **Launch**: visual slot memakai `QuantBeats` clip (bukan hanya quant global); scene launch
  dijadwalkan per layer dengan quant masing-masing, dan slot kosong mengirim stop terjadwal.
- **Stop**: tombol Stop, Stop All, Toggle, serta Gate/Repeat saat tombol dilepas mengirim
  `stopLayer` dengan `atBeat` yang sama dengan quantum global audio (bukan langsung).
- **Phase**: selama audio slot playing, Nota mengirim `vizSlotPhase` (posisi lokal + beat
  sampel). openVJ mengoreksi `started_beat` video dengan kompensasi latensi, jadi start dan
  loop seam mengikuti audio walau launch meleset beberapa frame.
- **One-shot**: loop audio off → video `Play once & clear` (hilang saat audio selesai).
- Bukti uji: phase terkorreksi sekali setelah launch (`corrected≈0.215 beat`), lalu tidak
  berulang; `sync=20 trim=10s in=10s mode=Play once & clear`.

## 7h. Copy/paste clip ikut menyalin visual (23 Nov)

- Semua jalur copy/paste/duplicate/drag lewat `SessionView.MoveOrCopySlot`; sekarang method itu
  mengirim `vizCopy` ke openVJ setelah audio berhasil disalin.
- openVJ meng-clone clip visual (source, parameter, efek, play mode, sync/trim) dengan **ID
  baru**, memuat ulang media untuk ID itu, dan mengganti visual tujuan. `move` menghapus visual
  sumber; sumber tanpa visual mengosongkan visual tujuan.
- Bukti uji: Sintel di slot 0 di-copy ke slot 1 →
  `NOTA copy visual 0:0 -> 0:1`, `media ready for ClipId(14)`, `vizState L0 C1 visual=True
  sync=103.85 trim=51.93s`; unit test copy/move lulus.
- Catatan: undo Nota belum mengembalikan salinan visual; paste dari Arrangement tidak membuat
  visual baru (tidak ada visual sumber).

## 7i. Sinkronisasi struktur scene & selection (23 Nov)

- Operasi scene di Nota kini memindahkan/menyalin **column visual** yang sesuai:
  insert scene → `vizColumnInsert`, remove → `vizColumnRemove`, drag/move →
  `vizColumnMove`, duplicate → insert + copy visual tiap layer.
- Capture Scene menyalin visual yang sedang playing ke scene baru.
- Selection disimpan sebagai **track UID + scene**; setelah layout berubah (track
  tambah/hapus/reorder) atau engine reconnect, `select` dihitung ulang dan dikirim lagi.
- Tab OpenVJ mendengarkan `LayoutChanged` dan me-reload layer/scene yang baru.
- Bukti uji: insert scene setelah scene 0 →
  `NOTA insert column 1`, layout 9 kolom, `select 0:0` ulang; unit test
  `scene_columns_keep_their_clips_when_inserted_removed_moved` lulus.
- Batasan: operasi scene dari Remote/MCP/undo yang tidak lewat `SessionView` belum
  menyesuaikan kolom; solusi penuh perlu scene UID.

## 7j. Reconnect & hapus clip visual (23 Nov)

- Saat engine visual restart, bridge memuat ulang `visuals/composition.json` project sebelum
  layout/timing dikirim (kalau project sudah disimpan), lalu `ResumePlayingVisuals()` mengirim
  `slot` untuk setiap track yang audio-nya masih playing.
- Hapus clip yang playing/queued mengirim `vizClear` dengan `atBeat` quantum global, jadi
  visual berhenti bersamaan dengan audio; clip diam langsung dibersihkan.
- Bukti: log `[resume] slot ...` dan `NOTA clear visual ... at=...` (diuji lewat jalur yang
  sama dengan stop/gate).
- Batasan: project yang belum disimpan tetap kehilangan komposisi visual saat engine restart.

## 7k. Performance pass (23 Nov)

- **Output-only fast path**: saat editor hidden tetapi output tampil, hanya frame output/mapping
  yang di-push; model UI, params, library, player, dan thumbnail pump dilewati.
- **UI sync 30 fps** saat editor terbuka; render output tetap 60 fps. `ui_images_pending`
  memastikan tekstur output/preview tetap dikirim saat editor dibuka kembali.
- **Watcher folder** efek/MilkDrop dari 1 dtk → 5 dtk (reload preset masih terasa instan untuk
  setup).
- Hasil ukur (M1, demo, `ps` CPU process):
  - hidden: **9,3% → 2,2–2,6%**
  - output-only: **32,6% → 24,3%**
  - editor+output: **48,5% → ~48%** (tidak turun; renderer window editor yang dominan,
    bukan sync)
  - `max_render` engine < 2 ms; memori ~133–186 MB tergantung cache.
- Optimasi lanjutan yang masih terbuka: render direct texture ke output window (hindari pass
  FemtoVG kedua), dan menurunkan render editor saat output tampil.

## 7l. Visual Performance profiles (23 Nov)

- Menu **View → Visual Performance**: Eco (30 fps / preview 15), Balanced (45/30), Quality
  (60/60). Pilihan disimpan di `settings.json` Nota dan dikirim ke engine lewat `vizPerf`.
- Engine memakai target FPS dinamis untuk timer window terlihat; saat hidden tetap 10 fps.
- Preview monitor hanya dirender jika editor terlihat; saat editor visible, preview mengikuti
  interval profile. Pass preview tidak lagi menyala saat output-only.
- Telemetry log per detik: `fps`, `max_dt`, `max_render`, `passes`, `drops`.
- Bukti uji yang akan diukur: Quality vs Eco pada output-only dan editor; profil harus
  bertahan setelah restart dan tetap selaras dengan phase/trim.
- Hasil ukur (M1): Quality output-only ~24,3%, editor+output ~48,5%; **Eco output-only 6,5%**,
  **Eco editor+output 9,3%**; fps 29–30, `max_render` ~1 ms, `passes=2`, `drops=0`. Profil Eco
  bertahan setelah restart (`VisualPerformanceMode` di settings.json).

## 7m. Klik clip openVJ men-trigger slot Nota (23 Nov)

- `cell_clicked` di openVJ, saat hosted dan bridge connected, tidak lagi memutar visual lokal.
  Ia mengirim `launchSlot {layer,col}` ke Nota.
- Nota memetakan layer → track (urutan kolom track terbalik), memilih slot, lalu menjalankan
  `LaunchSlotVelocity` (audio) dan `LaunchSlotVisual` (visual) pada quantum yang sama.
- Slot tanpa take audio (visual-only) tetap meluncurkan visualnya.
- Jika bridge tidak connect atau openVJ dijalankan standalone, klik tetap memutar visual lokal.
- Bukti uji: `OPENVJ_CLICK_TEST=0:0` pada project Sintel →
  `[viz<-] launchSlot L0 C0`, `openVJ: NOTA launch request 0:0`,
  `openVJ: NOTA clip 0:0 at=-0.000`.
- Batasan: klik header kolom, Piano press/release, dan peluncuran dari Remote/MCP belum
  men-trigger balik ke Nota.

## 7n. Audio ikut saat video dipasang ke slot visual (23 Nov)

- Penyebab "visual berganti, audio Nota tidak": visual bisa dipasang ke slot yang **kosong**
  (tidak ada clip audio). Klik sel itu hanya meluncurkan visual; tidak ada audio untuk
  dimulai. Video tanpa stream audio juga tetap senyap.
- Sekarang **media picker di tab OpenVJ** (dan drop di editor openVJ) memasangkan suara video
  ke slot Nota yang sama bila slot itu masih kosong: FFmpeg mengekstrak audio, clip diberi
  nama file, dan sync visual di-set ke panjang slot (beats). Slot yang sudah punya audio tidak
  ditimpa (status: audio lama dipertahankan).
- Track MIDI/instrument tidak punya slot audio; status menjelaskan bahwa suara butuh audio
  track.
- Drop di editor openVJ mengirim `vizMediaAudio {layer,col,path}`; Nota memetakan layer→track,
  mengimpor audio, lalu `VizPlayback(..., setSync:true)` tanpa mengganti clip visual yang
  sudah dipasang.
- Status saat klik clip: `Launched from openVJ` (ada audio) atau
  `Visual only — this slot has no audio clip`.
- Perbaikan manual untuk slot yang sudah berisi audio lain: hapus clip audio di slot itu, lalu
  pasang ulang videonya (atau Import video dari menu slot) agar pasangan audio+visual direset.

## 7o. Audio reactivity dari master Nota (23 Nov)

- **Tap native**: `Engine::render` menyalin blok master (setelah master chain + clamp) ke ring
  buffer file-backed. Audio thread hanya 1–2 `memcpy` + satu atomic store; tidak ada lock,
  alokasi, atau syscall di callback.
- **File**: `~/Library/Application Support/Syntetika Engine/audio-tap.shm`, 262.208 byte =
  header 64 B + 32.768 frame stereo f32. Header: magic `NTA1`, versi, channels, frames, sample
  rate (f32 bits), write index monotonic (u64), dropped (u64); data di offset 64.
- **Siklus**: `NotaEngine.Start()` memanggil `nota_engine_set_audio_tap(path)` sebelum device
  dibuka; engine membuka mapping di `start()` dan menutupnya di `stop()`.
  `VisualEngineProcess` mengirim `SYNTETIKA_AUDIO_TAP` ke proses visual.
- **openVJ**: bila env ada, `update_audio` memakai tap sebagai sumber (bukan input device):
  frame baru di-`push` ke `Analyzer` yang sama, lalu `ModInputs`, spectrum texture, dan PCM
  MilkDrop memakai hasilnya. Rate dibaca dari header (analyzer dibuat ulang bila berubah),
  file dicoba ulang tiap 1 s, dan restart engine terdeteksi dari index yang mundur.
- **Hasil uji** (video Sintel 52 s, output-only 60 fps): `tap level=0.76
  bands=[0.92, 0.79, 0.73, 0.59]`, feed 512–1024 sampel/frame,
  `fps=59.8 max_render=1.4 ms passes=4 drops=0`; hidden engine 3,2% CPU dan tap tetap
  menulis 44.544 frame/detik.
- **Debug**: `SYNTETIKA_TAP_DEBUG=1` mencetak `tap level/bands/rate/feed` sekali per detik.
- **Batasan**: tap di macOS/Linux (Windows stub); hanya parameter yang audio-reactive yang
  bergerak; tap selalu aktif saat engine hidup (biaya sangat kecil).

## 7p. Aplikasi makin berat: generator lambat & antrean bridge (23 Nov)

- **Temuan sesi pengguna**: fps 59 → 0,5–0,7 dalam satu detik setelah clip generator di layer 2
  scene 0 aktif. Log: `slow render 1863–2092 ms passes=4–5 active=["2:gen"]`. Sebelum clip itu
  aktif, 9 menit stabil 59 fps; jadi bukan kebocoran bertahap, melainkan satu visual yang
  memblokir UI thread ~1,9 detik per frame.
- **MilkDrop bukan penyebab umum**: batch 653 preset di 640×360 → median **1,39 ms**, p95
  **7,81 ms**, terburuk **41 ms**; generator bawaan **0,55 ms**. Jadi beban berasal dari shader
  generator/ISF tertentu (kemungkinan raymarching berat) atau ukuran komposisi, bukan mesin
  render secara umum. Preset yang gagal: 1 (syntax error di per-frame code).
- **Diagnostik**: `slow render` sekarang menyebut sumber persis (`isf:<id>`, `milk:<preset>`,
  `gen:<kind>`), efek aktif (`+[...]`), dan ukuran komposisi.
- **Adaptive render scale**: EMA render >200 ms → skala render dibagi dua (minimal 12,5%),
  jeda 2 detik antar perubahan; naik lagi saat <6 ms. Gambar lebih lembut, aplikasi tetap
  responsif. Label komposisi menampilkan `1280×720 · 25%`.
  Bukti: `OPENVJ_SCALE_TEST=0.25` → `render scale 25% (320×180)` → `50%` → `100%`,
  `fps=60.0 max_render=2,3 ms drops=0`.
- **Antrean bridge**: semua pesan engine (termasuk `vizSlotPhase` 4×/detik per track) dulu masuk
  `_inbox` yang hanya dikuras saat tab OpenVJ terlihat, sehingga tumbuh selama audio diputar.
  Sekarang hanya `vizState`/`vizCatalog` yang diantrekan (dengan batas 512 baris); tipe lain
  tetap dicatat/ditangani tanpa menumpuk.
- **Sisa**: clip lambat belum diidentifikasi namanya (komposisi tidak tersimpan). Log baru akan
  menyebutkannya saat diputar lagi; kandidat bisa dioptimalkan atau diberi peringatan.

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

### Hook uji (env-gated, tidak aktif tanpa env)

| Env | Fungsi |
|---|---|
| `NOTA_VIDEO_TEST=<file>` | Impor video ke slot scene 0 (audio + visual) 8 dtk setelah start |
| `NOTA_PAIR_TEST=layer:scene:<file>` | Simulasi drop video di openVJ → pasangkan audio ke slot |
| `OPENVJ_CLICK_TEST=layer:col` | Simulasi klik clip di editor openVJ (launch balik ke Nota) |
| `OPENVJ_PAIR_TEST=layer:col=<file>` | Simulasi drop video di grid openVJ (handshake `vizMediaAudio`) |
| `OPENVJ_SCALE_TEST=0.25` | Paksa render scale adaptif (uji resize/preview) |
| `SYNTETIKA_TAP_DEBUG=1` | Log `tap level/bands/rate/feed` sekali per detik |
| `NOTA_OPENVJ_UI_TEST=1`, `NOTA_VISUAL_OUTPUT_TEST=1`, `NOTA_MAPPING_TEST=1` | Buka jendela editor/output/mapping saat start |
| `NOTA_SLOT_SYNC_TEST`, `NOTA_COPY_TEST`, `NOTA_SCENE_TEST` | Uji trim/loop, copy visual, dan struktur scene |

## 10. Keputusan terbuka

- Apakah repo akan menyertakan binary rilis (saat ini source-only)?
- Nama device internal (“Nota Reverb”, dst.) dibiarkan — konfirmasi tetap begitu.
- CI penuh butuh Xcode 26 + .NET 10 di runner; mulai dari CI ringan.
- Apakah visual Arrangement masuk roadmap publik atau ditunda.
