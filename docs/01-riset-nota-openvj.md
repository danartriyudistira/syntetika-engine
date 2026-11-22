# Riset: Syntetika Engine — Nota (host) + openVJ (visual engine)

Status: riset awal, **belum ada kode**. Keputusan arsitektur sudah diambil — lihat §14.
Tanggal: 22 November 2026.
Repo yang dibaca (clone dangkal, `~/research/syntetika/`):

| Repo | Commit | Tanggal commit | Ukuran kode |
|---|---|---|---|
| `nota-daw/nota` (branch `develop`, v0.60.4) | `d8b0be3` | 2026-11-21 | ±104k baris C++20 + ±201k baris C# |
| `openwave-studio/openvj` (branch `main`, v0.1.0) | `d383b9b` | 2026-11-17 | ±33.3k baris Rust |

---

## 0. TL;DR — rekomendasi

1. **Nota tetap jadi host**: UI, Session View, transport, project file, dan audio tetap milik Nota.
2. **openVJ dipakai sebagai "Visual Engine" yang berjalan sebagai proses terpisah** (sidecar) di dalam satu bundel aplikasi, dikendalikan lewat IPC loopback. Bukan di-embed sebagai library di iterasi pertama.
3. **Sinkronisasi memakai beat absolut, bukan timestamp**: Nota mengirim `(beat, bpm, playing)` ~120 Hz + perintah `launch {atBeat}`; openVJ menaruh launch itu di antrean dan mengeksekusinya tepat saat beat lokalnya melewati `atBeat`. openVJ sudah punya seam yang pas untuk ini (`BeatClock::sync`, dipakai Ableton Link & MIDI clock).
4. **Pemetaan domain**: track Nota → layer openVJ, scene Nota → column openVJ, slot (track × scene) → satu visual clip. Ini pemetaan yang paling murah dan paling mudah dipahami user.
5. **Preview di dalam jendela Nota** (opsional, fase 2) dilakukan dengan menampilkan output openVJ lewat Syphon/IOSurface di `NativeControlHost` Avalonia — bukan dengan memindahkan renderer ke dalam proses Nota.
6. **Estimasi** (keputusan: macOS Apple Silicon saja): MVP end-to-end yang bisa dipakai live = **9–13 minggu** (1 developer, full-time, dibantu AI). Produk siap rilis (packaging, signing, recovery, docs) = **±4 bulan**. Fase 2 (preview tertanam + kontrol parameter lanjutan) +3–5 minggu.
7. **Keputusan sudah dikunci** (§14): open source AGPL, macOS arm64, sidecar IPC, kontrol cepat + jendela editor, audio reactivity via master tap.
8. **Satu blocker tersisa sebelum coding**: **environment** — mesin ini belum punya Xcode penuh, .NET 10, Rust, CMake, Node, Homebrew. Setup toolchain 2–4 jam + unduhan besar (lihat §9).

---

## 1. Sepuluh temuan kunci

1. Nota punya pemisahan arsitektur yang sangat bersih: engine audio C++20 di balik **C ABI murni** (`src/native/nota.engine/include/nota/nota_engine.h`), UI .NET 10/Avalonia. Ini memudahkan penambahan fitur visual tanpa menyentuh audio thread.
2. Nota **belum punya kemampuan video/GPU sama sekali** — tidak ada Syphon, Spout, NDI, projector window, atau output OpenGL/Metal. Semua gambar UI adalah Avalonia/Skia 2D.
3. Nota **belum punya Ableton Link, OSC, atau MIDI clock out**. Jadi clock master harus dibuat sendiri lewat IPC (atau menambah Link, yang punya konsekuensi lisensi & build).
4. Nota Session View sudah lengkap secara model: slot MIDI & audio, launch quantization per clip/global, follow actions, scene tempo/signature, legato, stop button, rekam ke slot (`nota_session_*`, ±70 fungsi ABI).
5. **Launch di Nota dijalankan tepat di batas kuantisasi** di audio thread (`SessionPlayer.h`, `maybeApply`), dengan `appliedBeat` tersimpan. Artinya ada titik yang bisa dipakai untuk sinkronisasi sample-accurate ke visual.
6. Persistensi `.nota` adalah **bundle direktori + `project.json` format v35**, dan **serialisasi milik C#**. Setiap DTO punya `[JsonExtensionData] Extra`, tapi `Capture()` membangun ulang DTO dari state engine — jadi data visual lebih aman disimpan sebagai **sidecar** di dalam bundle daripada menambah field ke format.
7. Nota sudah punya preseden IPC yang bisa ditiru: **Nota Remote** (Kestrel + WebSocket `/ws`, pairing token, protokol berversi) dan **MCP server** (HTTP loopback). Tidak perlu menemukan pola baru.
8. openVJ adalah aplikasi VJ lengkap yang muda tapi serius: 9 generator GPU bawaan, **345 ISF generator** (Ghost Arcade), 12 efek ISF, MilkDrop (lactea), video via FFmpeg, DXV/HAP di GPU, kamera, Syphon input, output mapping (slice/warp/mask), MIDI learn, Ableton Link, MIDI clock, import komposisi Resolume `.avc`.
9. Renderer openVJ (`openvj-render`) **tidak bergantung pada UI**: `Engine::new(device, queue, w, h)`, `render(&Composition, now, &ModInputs, preview)`, tekstur output bisa dibaca/di-share. Ini yang membuat integrasi "satu aplikasi" secara teknis mungkin di fase 2.
10. openVJ **belum punya launch quantization** (ada di roadmap 0.5) dan klip video tidak memutar audio — justru cocok: audio tetap 100% milik Nota.

---

## 2. Metode riset

- Clone dangkal kedua repo, membaca dokumen arsitektur (`ARCHITECTURE.md`, `BUILD.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, `openspec/`), lalu membaca kode di titik-titik sambungan: model Session, ABI, persistensi, clock/beat, renderer, media, output.
- Verifikasi kapabilitas dengan pencarian menyeluruh (video/GPU/Syphon/NDI/OSC/Link/MIDI clock) di kedua repo.
- Verifikasi kelayakan embed di sisi Avalonia (`NativeControlHost` → NSView) dan wgpu (`SurfaceTargetUnsafe::RawHandle`).
- Verifikasi lisensi (AGPL-3.0-only, GPL-3.0-or-later, Ableton Link GPLv2+/proprietary).
- Cek toolchain mesin saat ini (`dotnet`, `rustc`, `cmake`, `xcodebuild`, `brew`, `node`) → **tidak ada yang terpasang**.

Yang **tidak** dilakukan: build, benchmark nyata, uji sync di mesin ini. Semua angka latency/effort di dokumen ini adalah estimasi berbasis kode, bukan hasil pengukuran.

---

## 3. Anatomi Nota

### 3.1 Dua lapis, satu C ABI

```
Avalonia (C#)  ── P/Invoke ──►  libnota_engine (C++20)
Domain/Application/Presentation        Engine, Transport,
Infrastructure/Interop                 Graph, Track, SessionPlayer,
App (422 file, ±136k baris)            devices, DSP, pluginhost (JUCE)
```

Aturan arsitektur yang mengikat (dari `ARCHITECTURE.md`): audio thread real-time safe (AR-6), graph immutable snapshot (AR-5), MIDI sample-accurate (AR-8), dan **`Transport` adalah satu-satunya sumber waktu musikal (AR-9)**. Ini penting: kalau kita menambah jam visual, jam itu harus **budak** dari `Transport`, bukan sumber kedua.

Implikasi praktis: fitur visual **tidak boleh** masuk ke engine audio. Titik sentuh yang benar ada di dua tempat: (a) C# (UI + IPC + persistensi), (b) opsional C++ hanya untuk *tap* audio (lihat §7.7).

### 3.2 Session View — model dan ABI

- Grid: **track × scene**. Slot bisa berisi MIDI clip atau audio take.
- Properti per clip: `LaunchMode` (trigger/gate/toggle/repeat), `Quant`, `Legato`, `Loop`, `VelocityAmount`, follow actions (`FollowA/B`, `ChanceA/B`, `FollowBeats`, `JumpScene`), nama, warna.
- Properti per scene: nama, warna, tempo, time signature, follow, follow beats.
- ABI: `nota_session_add_midi_clip`, `add_audio_file`, `launch_slot`, `launch_scene`, `stop_slot`, `slot_state`, `slot_position`, `get/set_clip_props`, `copy_slot`, `move_scene`, `capture_scene`, dll.
- Serialisasi: `SessionDto` → `SceneDto[]` + `SessionSlotDto[]` di `ProjectModel.cs` (baris ±588–640).

### 3.3 Kuantisasi launch — titik sinkronisasi terbaik

`src/native/nota.engine/src/engine/SessionPlayer.h`:

- Permintaan launch dari message thread masuk sebagai satu kata atomic (`request`), berisi slot + generasi + serial.
- Audio thread (`maybeApply`) menghitung batas kuantisasi dari beat transport:
  ```
  boundary = ceil(blockStartBeats / quant) * quant   (dijepit ke blok)
  ```
- Saat terjadi, `appliedBeat` mencatat beat persisnya.

Artinya ada dua cara sinkronisasi visual:

| Cara | Presisi | Perubahan yang perlu |
|---|---|---|
| **A. Hitung target beat di C#** saat user klik (`ceil(beat/quant)*quant`), kirim `launch {atBeat}` | ±0 ms terhadap batas kuantisasi (visual engine menghitung sendiri kapan beat itu tiba) | tidak perlu ubah engine |
| **B. Baca `appliedBeat` setelah launch** dan koreksi | presisi penuh, tapi ada delay 1 frame UI | tambah 1 fungsi ABI kecil, mis. `nota_session_slot_applied_beat()` |

Rekomendasi: pakai A sebagai default, B sebagai verifikasi/telemetry di fase testing.

Catatan: tempo per scene dan time signature juga mempengaruhi beat; stream clock harus menyertakan `bpm`, `sigNum/sigDen`, `loopStart/loopEnd`, dan event `seek`.

### 3.4 Persistensi `.nota`

- Bundle direktori: `project.json` + `samples/` + `plugin-states/` + `analysis/` + `.history/`.
- `ProjectService.CurrentFormatVersion = 35`. Migrasi berantai di `ProjectMigrations.cs`.
- Semua DTO punya `[JsonExtensionData] Extra`, **tetapi** saat save, `ProjectService.Capture()` membangun `SessionSlotDto` baru dari engine — field asing bisa hilang.
- **Konsekuensi**: data visual jangan dimasukkan ke `project.json` di iterasi pertama. Simpan di sidecar:
  ```
  <nama>.nota/
    project.json
    samples/
    visuals/
      mapping.json        # trackId + scene → visual clip
      composition.json    # komposisi ala openVJ (layer/column/clip/effect)
      media/              # video/gambar yang dipakai (disalin, path relatif)
      presets/            # preset visual milik user
  ```
  Ini menjaga kompatibilitas dua arah: Nota upstream tetap bisa membuka project (mengabaikan `visuals/`), dan formatVersion tidak perlu berubah.

### 3.5 Infrastruktur IPC yang sudah ada

| Komponen | Teknologi | Berguna untuk |
|---|---|---|
| `Nota.Remote` | Kestrel, WebSocket `/ws`, pairing token, protokol berversi (`ProtocolVersion`), inbox yang di-drain di UI thread | Pola persis untuk bridge visual |
| `Nota.Mcp` | MCP over HTTP, loopback-only, 27 file tool (termasuk `SessionTools`) | Referensi penamaan/operasi; bukan untuk realtime |
| `Nota.App/SessionView.*` | Custom-drawn grid (DrawingContext) | Tempat menaruh badge/thumbnail visual per sel |

### 3.6 Yang **tidak** ada di Nota (dan harus ditambah)

- Output video / window projector.
- GPU renderer untuk konten non-UI.
- Syphon/Spout/NDI (input maupun output).
- Ableton Link / OSC / MIDI clock out.
- API plugin/ekstensi untuk pihak ketiga.
- Tap audio master untuk dianalisis proses lain.

---

## 4. Anatomi openVJ

### 4.1 Crates

| Crate | Baris | Isi |
|---|---|---|
| `openvj-core` | 10.6k | Model komposisi (layer × column, clip, deck), playback, effect chain, ISF parser, modulasi, MIDI map, playlist, output mapping, JSON save/load. Tanpa GPU & UI. |
| `openvj-render` | 4.6k | Engine wgpu: render per frame jadi daftar pass (clip → transform/effect → crossfade → layer → blend → comp effects → master → output), 16-bit float, ISF compile via naga, thumbnail, mapping. |
| `openvj-media` | 4.2k | Gambar, video FFmpeg (child process), DXV/HAP di GPU, kamera, Syphon input, thumbnail, frame pacing. |
| `openvj-audio` | 0.5k | Input audio (cpal) + FFT: 4 band, hits, presence, spectrum. |
| `openvj-app` | 10.7k | Slint UI + state aplikasi + Ableton Link + MIDI + mapping UI + playlist player. |
| `openvj-resolume` | 2.8k | Import `.avc` dan preset Advanced Output/MIDI. |

### 4.2 Model komposisi (inti pemetaan)

```rust
Composition { width, height, master, bpm, columns: Vec<Column>,
              layers: Vec<Layer>, effects, decks, groups, crossfader, reactivity, clock }
Layer { name, opacity, blend, bypass, solo, clips: Vec<Option<Clip>>, group, ... }
Clip  { id, name, source, params[4], speed, mods[4], play_mode,
        sync_beats: Option<f32>, duration, transform, effects, macros, autopilot, trigger }
ClipSource = Generator | Isf | Media(path) | Camera | Syphon | Router | MilkDrop
```

`Composition::trigger(layer, col, now)` dan `trigger_column(col, now)` — analog langsung dari `LaunchSlot` dan `LaunchScene` di Nota.

### 4.3 Clock — seam yang sudah tersedia

`openvj-app/src/app.rs`:

```rust
let clock = if self.clock_on { self.midi_clock.bpm().zip(self.midi_clock.beat_at(now)) } else { None };
if let Some((bpm, beat)) = clock { ...; self.beat_clock.sync(now, beat, bpm); }
else if self.link.is_enabled() { let (beat, bpm) = self.link.capture(); ...; self.beat_clock.sync(now, beat, bpm); }
else { self.beat_clock.set_bpm(now, self.comp.bpm); }
self.beat_clock.beat_at(now)
```

`BeatClock::sync(now, beat, bpm)` (`openvj-core/src/modulation.rs:302`) menerima posisi beat eksternal. Menambah sumber ketiga — **"NOTA"** — adalah perubahan kecil dan mengikuti pola yang sudah ada.

Semua yang beat-synced otomatis ikut: `sync_beats` per clip, generator Bars/Rings/Strobe, tempo-shape modulation (Sine/Saw/Pulse/…), phase, `from launch`.

### 4.4 Render & output

- `Engine::render(&Composition, now, &ModInputs, preview)` — tidak butuh UI.
- Output, preview, thumbnail, dan layar mapping adalah `Target` (tekstur) yang diimpor Slint tanpa salinan.
- `shared_output()` + `syphon_out.rs` mempublikasikan output sebagai server Syphon "openVJ" (BGRA, blit di GPU, hanya saat ada klien).
- Output window terpisah bisa fullscreen ke proyektor; mapping mendukung banyak screen/slice/warp/mask.

### 4.5 Batasan v0.1 yang relevan

- Tidak ada launch quantization (roadmap 0.5) → harus ditambah di bridge (mudah, karena kita mengirim `atBeat`).
- Klip video tidak memutar audio → tidak masalah.
- Belum ada image sequence, belum ada teks/NDI.
- H.264/HEVC/ProRes didekode CPU via FFmpeg; banyak layer HD sebaiknya DXV/HAP.
- Windows "should work but untested"; Linux Vulkan jalan.
- Tidak ada undo untuk edit struktural (roadmap).
- Format file bisa berubah sebelum 1.0.

---

## 5. Pemetaan domain + gap

| Nota | openVJ | Catatan |
|---|---|---|
| Project (`.nota` bundle) | Composition | 1 project = 1 komposisi visual; disimpan di `visuals/composition.json` |
| Track (audio/MIDI) | Layer | Hanya track yang "playable" yang dipetakan; group/return/master dilewati (fase 2: layer group) |
| Scene (baris) | Column | Nama, warna, tempo scene dipakai untuk sinkron |
| Slot (track × scene) | Clip (layer × column) | Satu slot boleh punya 0 atau 1 visual clip |
| Clip length (beats) | `sync_beats` | Visual clip di-set `sync_beats = slot length` → loop visual = loop audio |
| Launch quant | (belum ada) | Diimplementasi di bridge: tahan perintah sampai beat target |
| Follow actions | Autopilot | Konflik: matikan autopilot saat mode "follow Nota" |
| Stop button slot | `clear_layer` | — |
| Scene tempo/signature | `comp.bpm` | Dikirim lewat stream clock |
| Track color | Layer color? | openVJ belum punya warna layer → tambah di UI Nota saja |
| Mixer/meter | — | Tidak relevan |
| Arrangement | — | Fase 3: timeline visual (opsional) |

**Gap konseptual yang harus diputuskan:**

1. **1 track = 1 layer?** Kalau user punya 16 track audio, apakah muncul 16 layer visual? Alternatif: layer visual independen (mis. 4 layer: Background, Mid, Overlay, Text) dan setiap clip memilih layer target. Rekomendasi: default 1:1, tapi properti visual clip punya `layer` yang bisa di-override (dan UI menyembunyikan track tanpa visual).
2. **Visual berdiri sendiri?** Beberapa user ingin memicu visual tanpa memicu audio (VJ manual). Rekomendasi: mode per clip: `Follow` (default) atau `Independent`.
3. **Transport stop** → visual freeze (frame terakhir) atau clear? Rekomendasi: setting global, default freeze.
4. **Scene launch** → launch seluruh column visual; layer yang tidak punya visual di scene itu di-clear (sama seperti perilaku openVJ).

---

## 6. Opsi arsitektur

### Opsi A — Sidecar process + IPC (rekomendasi v1)

```
┌──────────────────────────── Syntetika Engine.app ────────────────────────────┐
│  Nota (Avalonia/.NET)                          Visual Engine (Rust/wgpu)     │
│  ├─ Session View + inspector visual            ├─ openVJ core+render+media   │
│  ├─ Transport (master clock, AR-9)             ├─ clock "NOTA" (BeatClock)   │
│  ├─ Project bundle + visuals/                  ├─ output window (proyektor)  │
│  └─ VisualBridge (Kestrel/WS + shm audio) ───► ├─ bridge: launch-at-beat     │
│                                                └─ Syphon out (opsional)      │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Kelebihan**: semua fitur VJ langsung tersedia; isolasi crash (GPU/FFmpeg tidak mematikan audio); dua bahasa tetap terpisah; bisa dikembangkan paralel; upgrade openVJ upstream mudah; preview bisa ditambahkan kemudian lewat Syphon.
- **Kekurangan**: dua proses (startup, packaging, signing), perlu protokol, thumbnail harus dikirim lewat IPC, dan "satu jendela" baru tercapai di fase 2.
- **Effort**: MVP 9–13 minggu. Risiko: rendah–sedang.

### Opsi B — In-process library (Rust cdylib di dalam Nota)

```
Nota (Avalonia) ── P/Invoke ──► libsyntetika_visual (Rust: openvj-core+render+media)
      └─ NativeControlHost → NSView → wgpu Surface (CAMetalLayer)
```

Secara teknis **valid**: Avalonia `NativeControlHost` membuat NSView asli di macOS ( `NativeControlHostImpl` mengembalikan handle `"NSView"` ), dan wgpu 30 bisa membuat surface dari `RawWindowHandle` (AppKit NSView).

- **Kelebihan**: satu jendela, preview zero-copy, tanpa IPC, thumbnail instan, packaging satu binary.
- **Kekurangan**: `App` openVJ (10.7k baris) bercampur UI Slint (205 callback, 134 `ui.set_*`); harus direfaktor jadi core headless + frontend. Seluruh UI VJ (library, effects, mapping, playlist, MIDI learn) harus ditulis ulang di Avalonia — atau meng-embed Slint di dalam NSView (risiko input/focus/DPI). Crash Rust = crash Nota. Build dua toolchain dalam satu proses (GC .NET + Rust) menambah kompleksitas debugging.
- **Effort**: 6–9 bulan. Risiko: tinggi.

### Opsi C — Tulis ulang fitur VJ di dalam Nota (C#/GPU)

Butuh renderer GPU sendiri (Silk.NET/OpenGL atau wgpu via interop), shader pipeline, ISF, video decode, mapping. Praktis membuang seluruh nilai openVJ.

- **Effort**: 9–12+ bulan. **Tidak direkomendasikan.**

### Perbandingan

| Kriteria | A (sidecar) | B (in-process) | C (rewrite) |
|---|---|---|---|
| Time to first live demo | ★★★★★ (3–4 minggu) | ★★ | ★ |
| Fitur VJ yang didapat | 100% | 100% (UI harus dibangun ulang) | 0% (harus ditulis) |
| "Terasa satu aplikasi" | ★★★ (fase 2 jadi ★★★★★) | ★★★★★ | ★★★★★ |
| Risiko teknis | Rendah | Tinggi | Sangat tinggi |
| Isolasi crash | Ya | Tidak | — |
| Biaya maintenance upstream | Rendah | Tinggi | — |
| Effort MVP | 9–13 minggu | 6–9 bulan | 9–12 bulan |

**Keputusan: A untuk v1. Gerbang ke B dibuka hanya jika fase 2 (preview Syphon tertanam) terbukti tidak cukup.**

---

## 7. Desain target (Opsi A) — detail

### 7.1 Komponen

| Komponen baru | Lokasi | Tanggung jawab |
|---|---|---|
| `Nota.Visuals` (C#) | `src/managed/Nota.Visuals` | Klien IPC, model mapping, thumbnail cache, lifecycle proses |
| `VisualBridgeServer` | di dalam `Nota.Visuals` | Kestrel/WebSocket loopback (pola `RemoteServer`), token acak per sesi |
| `SessionView` patch | `Nota.App/SessionView.*` | Badge/thumbnail per sel, tab "Visual" di inspector, menu |
| `visuals/` bundle | `Nota.Infrastructure/Persistence` | Simpan/muat sidecar, salin media, relink path |
| `openvj-nota` (crate baru) | fork openVJ `crates/openvj-nota` | Service mode, clock NOTA, launch scheduler, protokol, thumbnail encoder |
| Patch `openvj-app` | fork | Mode `--nota` (tanpa Slint), `update_tempo` menerima clock NOTA, hook launch |

### 7.2 Protokol IPC

Transport: **WebSocket JSON di `127.0.0.1`** (port acak, ditulis ke handshake file / argumen proses), ditambah **shared memory ring** untuk audio (opsional) dan **tidak** untuk thumbnail (cukup JPEG di WS, 10–15 fps, hanya sel yang terlihat).

Clock: 120 Hz. Pesan kecil (~120 byte) → ~15 KB/s. Jitter loopback < 1 ms; interpolasi lokal menghilangkan sisanya.

```jsonc
// Nota → Visual Engine
{"t":"hello","v":1,"project":"...","comp":{"w":1920,"h":1080},"sampleRate":48000}
{"t":"clock","beat":128.375,"bpm":124.0,"sig":[4,4],"playing":true,"loop":[0,256],"ts":91234567}
{"t":"launch","layer":2,"col":5,"atBeat":132.0,"quant":4.0,"velocity":1.0}
{"t":"scene","col":5,"atBeat":132.0}
{"t":"stop","layer":2}
{"t":"stopAll"}
{"t":"assign","track":7,"scene":5,"visual":{ /* Clip JSON openVJ */ }}
{"t":"param","target":"clip:7:5:param0","value":0.42}
{"t":"transport","playing":false}          // freeze
{"t":"seek","beat":64.0}

// Visual Engine → Nota
{"t":"state","ready":true,"fps":59.8,"gpu":"Apple M…","clips":{"2:5":"playing"}}
{"t":"thumb","key":"7:5","jpeg":"<base64>","w":160,"h":90}
{"t":"error","code":"gpu_lost","msg":"..."}
```

Aturan:
- **Semua launch memakai beat absolut** (`atBeat`), bukan "sekarang". Ini membuat jaringan lambat pun tidak menggeser momen visual.
- Engine visual menyimpan `(anchorLocalTime, anchorBeat, bpm)` dan tiap frame menghitung `beat = anchorBeat + (now - anchorLocalTime) * bpm / 60`. Koreksi dari paket baru dilakukan dengan slew (maks ±2 ms/frame) agar tidak ada lompatan.
- `seek`/loop wrap dari Nota mengirim beat diskontinu → engine visual melakukan re-anchor keras.
- Kalau `playing=false`: clock berhenti; visual freeze (opsi: clear).

### 7.3 Perubahan di Nota (sisi C#)

1. **Hook launch** — di `SessionView.LaunchSceneRow` / `LaunchSlot` / `SessionView.Grid.cs` (cell click, menu), hitung `atBeat` dan kirim ke bridge. Satu helper:
   ```csharp
   double AtBeat(double quant) => quant <= 0 ? Now : Math.Ceiling(Now / quant) * quant;
   ```
   Sumber `Now` = `nota_transport_position_beats` + `LaunchQuant`.
2. **Hook stop** — `StopSlot`, `StopScene`, `StopAll`, `BackToArrangement`, `nota_session_*` yang relevan.
3. **Hook edit** — tambah/hapus/pindah scene, rename track, hapus track → sinkronkan mapping.
4. **Inspector** — `BuildClipInspector` mendapat section "Visual" (pilih sumber, on/off, opacity layer, speed/beat sync, buka editor).
5. **Grid** — `DrawPreview`/cell renderer mendapat thumbnail di pojok kanan atas (rasio 16:9, ±44×25 px), border menyala saat visual playing.
6. **Toolbar Session** — tombol Output (buka/tutup jendela proyektor), Blackout, Preview visual, indikator sync (offset ms).
7. **Preferences** — aktif/nonaktif visual engine, resolusi komposisi, offset A/V, device audio untuk analisis.
8. **Lifecycle** — start sidecar saat project dibuka (jika ada `visuals/` atau fitur diaktifkan), stop saat app keluar, restart otomatis saat crash (maks 3x), tampilkan status di status bar.

### 7.4 Perubahan di openVJ (fork)

1. **Crate baru `openvj-nota`**: protokol, scheduler, encoder thumbnail, service loop.
2. **Mode service**: jalankan `Composition` + `Engine` + media + audio/clock, **tanpa Slint**. Perlu ekstraksi `Session` dari `App`: state inti (comp, engine, media map, clock, selection tidak perlu), lalu `App` (Slint) menjadi frontend opsional di atasnya. Ini refaktor terbesar di sisi Rust; estimasi 2–3 minggu.
3. **Sumber clock ketiga** di `update_tempo` (pola Link/MIDI clock).
4. **Launch scheduler**: antrean `(beat, kind, target)`; dieksekusi saat `beat >= atBeat` di frame loop → panggil `comp.trigger` / `trigger_column` / `clear_layer`.
5. **Mode output**: window proyektor (bisa tetap memakai Slint `OutputWindow`), atau headless + Syphon.
6. **Thumbnail**: `Engine::thumb(clip_id)` sudah ada; encode JPEG/PNG kecil lalu kirim.
7. **Telemetry**: fps, frame drop, GPU, error ISF.

### 7.5 Model data & penyimpanan

`visuals/mapping.json`:

```jsonc
{
  "version": 1,
  "composition": "composition.json",
  "slots": {
    "track:7|scene:5": {
      "follow": true,            // ikut launch/stop clip audio
      "layer": 2,                // override layer (default: index track)
      "clip": { /* Clip openVJ */ }
    }
  },
  "layers": [{ "track": 7, "name": "Bass", "opacity": 0.9, "blend": 0 }],
  "settings": { "freezeOnStop": true, "defaultVisual": null }
}
```

- Media disalin ke `visuals/media/<hash>.<ext>` (mengikuti pola content-addressed `BundleContent` di Nota) → project tetap portabel.
- Path di `composition.json` ditulis relatif terhadap bundle, di-relink saat load.
- Saat scene dipindah/dihapus, mapping ikut dipindah/dihapus (hook di C#).
- Saat clip di-copy (`copy_slot`), visual ikut disalin.

### 7.6 UI/UX Session View

| Area | Isi |
|---|---|
| Sel grid | Thumbnail 16:9 kecil + indikator "V" (ada visual / tidak) + border menyala saat visual playing + progress pass (opsional) |
| Inspector clip | Section **Visual**: sumber (generator/ISF/video/gambar/kamera), tombol Assign/Clear, opacity layer, Beat Sync (ikut panjang clip), tombol **Edit…** |
| Inspector scene | Section **Scene Visual**: launch seluruh column, clear, "ikuti tempo scene" |
| Toolbar | Output window, Fullscreen, Blackout, Master opacity, A/V offset, status sync |
| Jendela Visual | Editor penuh (openVJ Slint atau Avalonia): library, effects, layer, transform, mapping, MIDI learn, playlist |
| Menu klik kanan sel | "Add visual…", "Open visual editor", "Follow clip / Independent", "Clear visual" |

Strategi UI: **kontrol cepat di Nota, authoring mendalam di jendela Visual**. Membangun ulang seluruh UI openVJ di Avalonia tidak sepadan untuk v1.

### 7.7 Audio reactivity (opsional tapi pembeda)

openVJ butuh audio untuk band/hits/spectrum. Tiga opsi:

| Opsi | Cara | Kualitas | Effort |
|---|---|---|---|
| **1. Tap master Nota** (rekomendasi produk) | Tambah ring buffer non-RT di engine (pola `CaptureRing.h`) + ABI `nota_engine_master_tap_read()`; C# menulis ke shared-memory ring; openVJ membaca sebagai input | Terbaik; bisa per-track di masa depan | 1–2 minggu |
| **2. Process tap macOS 14.2+** | openVJ menangkap audio proses Nota via CoreAudio process tap | Bagus, tanpa ubah Nota; butuh izin Screen/Audio Recording | 1 minggu |
| **3. Loopback driver** | BlackHole/Soundflower | Buruk untuk user | 0 (tapi UX jelek) |

Catatan: audio harus **sebelum** latency output; kalibrasi offset diperlukan.

### 7.8 Sinkronisasi & kalibrasi

- Target: offset audio–visual **< 20 ms** (satu frame @60 fps). Riset persepsi: audio boleh mendahului video ±45 ms, video mendahului audio ±125 ms sebelum terasa (ITU-R BT.1359).
- Default: visual dijadwalkan pada beat yang sama; kompensasi `offset = latency_output_audio − latency_display` diisi otomatis dari `inputLatencyFrames`/`outputLatencyFrames` Nota (ABI sudah melaporkan latency backend) dan refresh display.
- UI: slider A/V offset ±200 ms + tombol "kalibrasi" (flash + klik, user geser sampai pas).
- Telemetri: `appliedBeat` (dari engine Nota) vs beat visual saat frame pertama tampil → log drift.

### 7.9 Failure handling

| Kejadian | Perilaku |
|---|---|
| Sidecar mati | Nota tetap jalan; banner "Visual engine stopped"; restart otomatis 3x dengan backoff |
| GPU device lost | Sidecar restart, komposisi dimuat ulang dari `visuals/` |
| Media hilang | Placeholder + daftar file hilang (pola "Find media" openVJ) |
| Project tanpa `visuals/` | Fitur visual nonaktif, tidak ada error |
| Clock putus > 500 ms | Visual masuk free-run (pakai bpm terakhir) + indikator kuning |

---

## 8. Lisensi — baca sebelum menulis kode

| Komponen | Lisensi | Konsekuensi |
|---|---|---|
| Nota | **AGPL-3.0-only** (JUCE di `pluginhost` memaksa AGPL; lihat `LICENSES/README.md`) | Modifikasi + distribusi harus menyediakan source lengkap; klausa network-use §13 berlaku jika dijadikan layanan |
| openVJ | **GPL-3.0-or-later** (Slint GPLv3) | Kombinasi dengan AGPL diperbolehkan lewat GPLv3 §13 (bagian GPL tetap GPL, bagian AGPL tetap AGPL) |
| Shader Ghost Arcade | AGPL-3.0, dimuat sebagai data | Aman dibundel |
| FFmpeg (bundled openVJ) | LGPL-2.1+, dijalankan sebagai program terpisah | Aman |
| Ableton Link (jika ditambah ke Nota) | GPLv2+ / proprietary | GPLv2+ kompatibel dengan GPLv3/AGPLv3; tapi menambah dependensi Asio & build |

**Kesimpulan praktis:**

- Menjual **binary tertutup** dari produk gabungan **tidak mungkin** tanpa lisensi komersial dari pemilik hak cipta Nota (Egor Khindikaynen) dan openVJ.
- Model bisnis yang tetap sah: open source (AGPL) + jual jasa, preset/konten, dukungan, hardware bundle, atau hosting.
- Kalau masa depan harus proprietary: (a) negosiasi lisensi komersial (Nota pada dasarnya single-author; JUCE juga perlu lisensi komersial), atau (b) engine visual ditulis ulang clean-room, atau (c) pertahankan openVJ sebagai aplikasi terpisah yang dijalankan user sendiri (arm's-length) dan jual hanya "Nota + integrasi" — tetap AGPL untuk Nota.
- Ganti nama & logo untuk fork (AGPL tidak memberi hak merek).

Ini bukan nasihat hukum; sebelum komersialisasi, konsultasi lisensi.

---

## 9. Build & environment (kondisi nyata mesin ini)

Tidak ada: `dotnet`, `rustc`/`cargo`, `cmake`, `brew`, `node`, `ffmpeg`, dan `xcodebuild` hanya Command Line Tools (bukan Xcode penuh).

Kebutuhan:

| Tool | Versi | Untuk | Perkiraan unduhan |
|---|---|---|---|
| Xcode 26 + CLT | 26+ | Nota (macOS 26+), CoreAudio, clang | 15–20 GB |
| Homebrew | — | paket pendukung | kecil |
| CMake + Ninja | 3.24+ | Nota native | ~100 MB |
| .NET SDK | 10 | Nota managed | ~1 GB |
| Node.js | 22 | build halaman Nota Remote (bisa dilewati `-p:NotaSkipWebBuild=true`) | ~200 MB |
| Rust stable | 1.8x | openVJ | ~1.5 GB |
| FFmpeg | — | openVJ dev (bundled build script ±10 menit) | ~100 MB |

Perkiraan waktu setup: **2–4 jam** (unduhan + build pertama). Ruang disk: build Nota (universal + JUCE + CPM deps) ±5–10 GB, openVJ `target/` ±2–4 GB. Volume tujuan punya 134 GB kosong — cukup.

Catatan operasional:
- Nota **mematikan update otomatis** di fork (atau arahkan `NOTA_UPDATE_API` ke release sendiri), kalau tidak, updater upstream akan menimpa build Syntetika.
- openVJ butuh `cargo run --release`; macOS butuh `scripts/build-ffmpeg-macos.sh` sekali untuk video.
- CI: dua workflow (CMake+dotnet, cargo) + job packaging yang menyatukan `.app` + helper.

---

## 10. Roadmap & estimasi

Asumsi: 1 developer, familiar C# + Rust dasar, dibantu AI, full-time.

| Milestone | Isi | Estimasi |
|---|---|---|
| **M0. Validasi** | Setup toolchain, build kedua app, jalankan, uji sync manual (metronome + flash), benchmark GPU 4 layer 1080p | 1–2 minggu |
| **M1. Fondasi** | Fork + branch, sidecar launch, handshake, clock 120 Hz, mode service openVJ (tanpa Slint), logging | 2–3 minggu |
| **M2. Session sync** | Mapping track/scene, launch-at-beat, stop/scene, badge di grid, inspector minimal, `visuals/` save/load | 3–4 minggu |
| **M3. Authoring** | Pemilih sumber (generator/ISF/video/gambar/kamera), parameter, effects, layer control, output window, copy media/relink, **master tap + audio reactivity** | 5–6 minggu |
| **M4. Live polish** | Offset A/V + kalibrasi, freeze/clear, crash recovery, preset, undo assignment, keyboard/MIDI | 3–4 minggu |
| **M5. Packaging** | Satu `.app` + helper, signing/notarization, updater sendiri, first-run, docs | 2 minggu |
| **Total (macOS arm64 saja)** | | **14–20 minggu (≈3,5–4,5 bulan)** |
| **MVP (M0–M2)** | Bisa live: audio + visual per clip, sinkron | **6–9 minggu** |

Fase 2 (opsional): preview tertanam via Syphon/IOSurface + `NativeControlHost` (2–3 minggu), embed penuh (Opsi B) tidak disarankan.

---

## 11. Risiko & mitigasi

| Risiko | Dampak | Mitigasi |
|---|---|---|
| Nota upstream rilis mingguan, format v35, refactor cepat | Rebase sakit | Patch surface kecil (C# + 1 hook ABI); data di sidecar; rebase tiap 2 minggu; jangan ubah `project.json` |
| openVJ v0.1, format bisa berubah | Migrasi data visual | Simpan `visuals/version`; adaptor load; kontribusikan launch-quantize ke upstream |
| Lisensi AGPL/GPL memblokir model bisnis | Produk tidak bisa closed-source | Putuskan sekarang (§8, §13) |
| GPU/CPU tidak cukup untuk banyak layer H.264 | Drop frame saat live | Rekomendasi DXV/HAP; batas layer per scene; indikator fps; resolusi komposisi lebih rendah |
| Latency/offset audio–visual | Visual terasa telat | Beat-based scheduling + kalibrasi offset + telemetri |
| Crash sidecar saat show | Visual mati | Auto-restart, watchdog, "safe mode" (blackout + audio tetap) |
| Dua proses = dua window membingungkan | UX | Fase 2 preview tertanam; output window jelas; shortcut global |
| Epilepsi/photosensitivity (strobe) | Risiko kesehatan & hukum | Warning first-run, limiter opsional, dokumentasi |
| Fork tidak bisa upstream (di luar scope Nota) | Maintenance selamanya | Terima sebagai fork; jaga diff kecil |
| Toolchain/versi baru (Avalonia 12, .NET 10, Xcode 26) | Build rapuh | Pin versi, dokumentasikan, CI matrix |
| Nama/merek "Syntetika" vs Nota/openVJ | Masalah hukum | Rebrand penuh, atribusi AGPL/GPL |

---

## 12. Spike yang harus dilakukan sebelum coding penuh

Urutannya sengaja: yang paling berisiko duluan.

1. **Sync spike (2–3 hari)** — metronome Nota + flash putih di openVJ; rekam dengan kamera 240 fps atau loopback; ukur offset & jitter. *Go/no-go: jitter < 10 ms.*
2. **Service mode spike (1 minggu)** — jalankan openVJ tanpa Slint, render 4 layer 1080p, ukur fps/CPU/GPU. *Go/no-go: 60 fps stabil.*
3. **Embedded preview spike (2–3 hari)** — Syphon server openVJ → klien kecil di `NativeControlHost` (CALayer + IOSurface). *Kalau gagal, preview pakai thumbnail IPC saja.*
4. **Launch-at-beat spike (2 hari)** — patch `BeatClock` + scheduler; pastikan cut tepat di beat dengan `sync_beats`.
5. **Persistence spike (2 hari)** — simpan/muat `visuals/` + media relink; buka project di Nota upstream (tanpa visual) → tidak error.
6. **Audio tap spike (3–4 hari)** — master tap Nota → band/hits di openVJ; ukur latency analisis.
7. **Crash/restart spike (1 hari)** — kill -9 sidecar saat render; pastikan Nota selamat dan restart bersih.

Total spike ±3 minggu; sebagian besar masuk ke M1–M2.

---

## 13. Keputusan yang perlu diambil

1. **Model distribusi**: open source (AGPL, sesuai lisensi) atau mau mengejar lisensi komersial? Ini menentukan boleh/tidaknya fondasi Nota dipakai.
2. **Target platform v1**: macOS Apple Silicon saja (paling realistis) atau langsung cross-platform (Windows/Linux menambah risiko besar: openVJ Windows untested).
3. **Bentuk produk**: satu `.app` dengan sidecar + output window (Opsi A), atau menunggu embed penuh (Opsi B)?
4. **Kedalaman UI visual di dalam Nota**: kontrol cepat saja + jendela editor terpisah, atau seluruh UI VJ dibangun ulang di Avalonia?
5. **Audio reactivity**: perlu di v1 atau fase 2?
6. **Pemetaan layer**: 1 track = 1 layer, atau layer visual independen dengan routing per clip?
7. **Arrangement view**: visual hanya di Session (sesuai permintaan) atau juga timeline di Arrangement (fase 3)?

---

## 14. Keputusan yang sudah diambil (22 Nov 2026)

| Topik | Keputusan | Konsekuensi |
|---|---|---|
| Distribusi | **Open source (AGPL-3.0)**, monetisasi lewat jasa/preset/support/hardware | Source lengkap dipublikasikan; jangan gunakan aset merek Nota/openVJ; siapkan `LICENSES/` + atribusi |
| Platform v1 | **macOS Apple Silicon saja** | Metal + Syphon + FFmpeg bundled; QA Windows/Linux ditunda |
| Arsitektur | **Opsi A: sidecar process + IPC** | Satu bundel `.app` berisi app utama + helper `Visual Engine`; protokol WS loopback §7.2 |
| UI visual | **Kontrol cepat di Session View + jendela editor penuh** | Nota: assign, on/off, opacity, beat sync, thumbnail. Editor (library/effects/mapping) memakai UI openVJ |
| Audio reactivity | **Ya, tap master Nota di v1** | Perlu ring buffer non-RT + ABI baru + shared memory; masuk M3 |

Konsekuensi pada roadmap: audio tap masuk **M3** (bukan M4), dan QA/packaging hanya macOS (M5 lebih ringan ±1 minggu).

**Langkah berikutnya (belum dikerjakan):** M0 — setup toolchain (§9), build kedua app, lalu spike #1 (sync) dan #2 (service mode) di §12. Jangan menulis fitur sebelum spike sync lulus (< 10 ms jitter).

---

## Lampiran A — Referensi file penting

**Nota**
- `ARCHITECTURE.md` — batas C ABI, aturan AR-4..AR-9, pluginhost, format project.
- `src/native/nota.engine/include/nota/nota_engine.h:1414+` — seluruh ABI Session.
- `src/native/nota.engine/src/engine/SessionPlayer.h` — kuantisasi launch, `appliedBeat`, follow actions.
- `src/native/nota.engine/src/abi/nota_engine_session.cpp` — implementasi ABI Session.
- `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs:588` — `SessionDto`, `SceneDto`, `SessionSlotDto`.
- `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs:48` — `CurrentFormatVersion = 35`.
- `src/managed/Nota.App/SessionView.Grid.cs` — render sel, `DrawPreview`, menu.
- `src/managed/Nota.App/SessionView.Inspector.cs:223` — `BuildClipInspector` (tempat tab Visual).
- `src/managed/Nota.Remote/RemoteServer.cs` — pola Kestrel/WebSocket.
- `src/managed/Nota.Mcp/Tools/SessionTools.cs` — daftar operasi Session yang sudah terstruktur.

**openVJ**
- `crates/openvj-core/src/composition.rs:762` — struct `Composition`; `:1187` `beat_at`; `:1193` `trigger`.
- `crates/openvj-core/src/modulation.rs:274-307` — `BeatClock`, `sync`.
- `crates/openvj-app/src/app.rs:967` — `frame()`; `:1144-1163` — pemilihan sumber clock.
- `crates/openvj-render/src/lib.rs:1332` — `render()`; `:616` `set_shared_output`; `:1626` `read_target`.
- `crates/openvj-app/src/syphon_out.rs` — publikasi output sebagai IOSurface/Syphon.
- `README.md` bagian *Known limits* & *Roadmap* — batasan v0.1.

**Avalonia / wgpu**
- `Avalonia.Controls/NativeControlHost.cs` — `CreateNativeControlCore(IPlatformHandle)`.
- `Avalonia.Native/NativeControlHostImpl.cs` — handle `"NSView"`.
- `wgpu::SurfaceTargetUnsafe::RawHandle` — surface dari `RawWindowHandle` (AppKit).

---

## Lampiran B — Fitur openVJ yang langsung didapat

Generator (9 + 345 ISF), video (FFmpeg/DXV/HAP), gambar, kamera, Syphon input, 12 efek + ISF efek kustom, transform 3D per clip/layer, 9 blend mode, layer group, crossfader, clip play mode + beat sync + autopilot, crossfade per layer, output mapping (multi-screen, slice, corner-pin, warp grid, mask, soft edge), MIDI learn, tempo (BPM/tap/Link/MIDI clock), audio reactivity, library + Discover, playlist player, import Resolume `.avc`.

## Lampiran C — Yang ditunda (fase 2/3)

Preview visual di dalam grid pada 60 fps (v1 cukup 10–15 fps), palette warna layer, timeline visual di Arrangement, automation Nota → parameter visual, per-track audio reactivity, teks/NDI, image sequence, undo penuh untuk edit visual, kontrol visual di Nota Remote (HP), OSC.
