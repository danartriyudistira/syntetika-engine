# M1 — Bridge Nota ⇄ Visual Engine

Status: **slice pertama jalan** (clock + scene/slot launch + stop).

## Yang sudah bekerja

```
Nota (C#)                                    openVJ (Rust)
VisualBridge :47810  ── TCP JSON lines ──►  nota_bridge.rs
  clock 125 Hz (beat, bpm, playing)           BeatClock::sync()  → semua yang beat-synced ikut
  scene/slot launch + atBeat                  antrean → trigger tepat saat beat tercapai
  stopLayer / stopAll                         clear_layer / clear_all
```

Bukti dari uji otomatis (`NOTA_VISUAL_TEST=1`):

```
[nota]  test: scene 0 at beat 12
[nota]  test: scene 1 at beat 24
[openvj] NOTA scene col=0 at=12.000 applied=12.016 drift=8.11ms
[openvj] NOTA scene col=1 at=24.000 applied=24.026 drift=12.81ms
[openvj] NOTA stop all
```

Drift 8–13 ms = keterlambatan satu frame render (loop 60 Hz). Visual muncul di frame pertama
setelah beat target — di bawah ambang persepsi (±45 ms).

## Protokol v1

Satu objek JSON per baris, TCP loopback. Nota = server, visual engine = client (reconnect 1 s).

| Arah | Pesan |
|---|---|
| N→V | `{"t":"hello","v":1}` |
| N→V | `{"t":"clock","beat":128.375,"bpm":124,"playing":true}` (125 Hz) |
| N→V | `{"t":"scene","col":5,"atBeat":132}` |
| N→V | `{"t":"slot","layer":2,"col":5,"atBeat":132}` |
| N→V | `{"t":"stopLayer","layer":2}` / `{"t":"stopAll"}` |

Aturan:
- Launch selalu memakai **beat absolut**, bukan "sekarang".
- `atBeat` dihitung sama dengan `SessionPlayer`: `ceil(now/quant − 1e-9) × quant`.
- Visual engine menginterpolasi beat lokal dari sampel terakhir; koreksi halus, bukan lompatan.
- Tidak ada audio di kanal ini.

## Implementasi

**Nota** (`src/managed/Nota.Infrastructure/Visuals/VisualBridge.cs`)
- Listener loopback + thread clock 125 Hz (lock-free read: `PositionBeats`, `Bpm`, `IsPlaying`).
- Hook: `LaunchSceneRow`, `LaunchSelection`, klik sel grid, `StopColumn`, `StopAll` (toolbar + grid).
- Mapping layer: urutan kolom yang bisa diluncurkan (`Track`/`Group`).
- `NOTA_VISUAL_TEST=1` → self-test: play, launch scene 0 lalu 1, stopAll (untuk verifikasi tanpa klik).

**openVJ** (`crates/openvj-app/src/nota_bridge.rs` + `app.rs`)
- Koneksi background + reconnect; clock dipakai sebagai sumber ketiga di `update_tempo`.
- `nota_tick()`: tarik perintah, simpan `(atBeat, cmd)`, terapkan saat `beat >= atBeat`.
- Log drift per launch.

## Cara menjalankan

```bash
# 1. Visual engine lebih dulu (atau kapan saja; ia reconnect sendiri)
cd "$SYNTETIKA_ROOT/src/openvj"
OPENVJ_NOTA=127.0.0.1:47810 ./target/release/syntetika-visual

# 2. Nota
open "$SYNTETIKA_ROOT/src/nota/dist/Syntetika Engine.app"
```

Lalu buka **Session View** dan klik tombol launch pada sebuah scene/slot. openVJ akan
menjalankan kolom visual yang sama pada beat kuantisasi berikutnya.

## Batasan slice ini

- Belum ada **penugasan visual per clip**: semua scene memetakan ke kolom openVJ dengan nomor sama.
- Belum ada UI di Nota (badge/thumbnail, inspector visual).
- Belum ada auto-launch sidecar dari Nota (masih dijalankan manual).
- Belum ada `visuals/` di bundle project.
- Stop scene (per baris) belum diterjemahkan ke visual.
- Belum ada kompensasi offset A/V.

## Bug hunt (22 Nov, laporan "openVJ patah-patah")

### Bukti awal

| Gejala | Data |
|---|---|
| Drift launch kadang 100–423 ms | log `NOTA ... drift=...` |
| FPS 30–46 sesekali | `max_dt` 250 ms, `max_render` 160–540 ms |
| Jalan sendiri juga patah | openVJ tanpa bridge: `max_dt` 25–50 ms tiap detik |
| Clock tidak halus | implied BPM antar-paket **102–224** (nominal 120) |

### Akar masalah

1. **Clock jitter (bug bridge).** Nota `PositionBeats` dibaca 125 Hz tetapi nilainya
   bertahap; openVJ melakukan `beat_clock.sync()` keras setiap frame → semua yang
   beat-synced ikut goyang.
2. **Live thumbnail readback (bug openVJ).** `Engine::render` merender thumbnail semua clip
   tiap 1/15 s lalu membaca GPU→CPU (`read_target`), yang menunggu pipeline selesai →
   stall 160–540 ms.
3. **Kompilasi pipeline saat launch.** ISF di-compile di render thread pada frame pertama
   clip terlihat (naga + Metal), menambah hitch tepat di beat launch.

### Perbaikan

| Fix | File |
|---|---|
| PLL: beat lokal maju dengan tempo, dikoreksi 8%/frame; snap hanya bila error > 0.25 beat (seek/loop) | `app.rs` (`update_tempo`) |
| `Engine::set_live_thumbnails(false)` di performance mode; thumbnail grid dicat **sekali** (statis), preview tetap jalan | `openvj-render/lib.rs`, `app.rs` |
| Prewarm pipeline: antrean efek komposisi dikompilasi satu per frame setelah present | `openvj-render/lib.rs`, `app.rs` |
| Diagnostik: fps/max_dt/max_render/slew per detik + log `slow render` | `app.rs` |

### Hasil sesudah

| Metrik | Sebelum | Sesudah |
|---|---|---|
| `slow render` > 50 ms | 12 dalam 20 s (160–540 ms) | **0** |
| FPS | 30–46 | **59–60** |
| `max_render` | 260–540 ms | **< 2 ms** |
| Drift launch | 100–423 ms | **1.6–16 ms** |
| Koreksi PLL | — | 0.001 beat (~0.5 ms) |

Sisa `max_dt` 20–38 ms sesekali berasal dari pacing timer 60 Hz + WindowServer (muncul juga
saat openVJ jalan sendiri), bukan dari sinkronisasi.

### Tindak lanjut (permintaan pengguna)

Thumbnail grid dan Preview sempat hitam karena keduanya ikut dimatikan. Sekarang:

- **Thumbnail statis**: tiap clip dilukis sekali ke tekstur thumbnail (`thumb_painted`), tidak
  ada refresh/readback per 15 Hz → tetap terlihat, tanpa stall.
- **Preview hidup lagi**: render clip terpilih per frame (satu pass, tanpa readback), jadi
  panel Preview menampilkan clip yang dipilih seperti biasa.

Verifikasi snapshot (`OPENVJ_SNAPSHOT`): grid menampilkan Bars/Rings/Kaleido/Plasma/dll,
Preview menampilkan Plasma, Output tetap 1280×720, 60 fps, 0 slow render.

## Sinkronisasi struktur (track/scene)

NotA adalah sumber kebenaran; openVJ mencerminkan strukturnya.

- **Layer = track audio/MIDI** (group/return/master dilewati). **Column = scene.**
- Track paling atas NotA = layer paling atas openVJ (index openVJ dibalik).
- **Track UID permanen**: `TrackDto.Uid` baru di `project.json`, dipetakan ke id engine saat
  load (`TrackUids`). Reorder memindahkan layer beserta isinya; bukan menukar isi.
- **Snapshot penuh** tiap 250 ms bila signature berubah:
  `{"t":"layout","tracks":[{uid,name,kind}],"scenes":[{index,name}]}`.
- openVJ merekonsiliasi: `move_layer` untuk urutan, `remove_layer` untuk track yang hilang
  (permanen), `add_layer` untuk yang baru, `move_column` untuk scene.
- `visuals/layout.json` ditulis ke bundle saat project tersimpan (sidecar).

Bukti uji (`NOTA_LAYOUT_TEST=1`): tambah track 2→3→4 layer, tambah scene 8→9 kolom,
reorder mempertahankan uid (`…,399284` pindah ke atas), hapus 4→3 layer, `slow render` 0,
fps 58–60. Single-instance guard ditambahkan agar hanya satu NotA yang memegang bridge.

## Seleksi mengikuti NotA

Memilih slot di Session View NotA mengirim `{"t":"select","layer":L,"col":C}`; openVJ
memilih slot yang sama (`selected`, `selected_layer`, tab Clip, Preview). Tanpa launch,
tanpa mengubah apa pun yang sedang tampil di output.

Uji: `NOTA_SELECT_TEST=1` memanggil jalur seleksi asli sekali → log openVJ `NOTA select 1:0`.

## Bug hunt #2 (22 Nov, setelah uji restart)

| # | Bug | Dampak | Perbaikan |
|---|---|---|---|
| 1 | `apply_layout` **infinite loop** saat NotA mengirim layout 0 track | Render loop openVJ mati total (hanya thread clock hidup); jendela beku | Trim layer berhenti bila `remove_layer` menolak (minimal 1 layer) + guard `len` berubah |
| 2 | Perintah struktural diseleksi lewat beat | Layout/select/stop bisa tertahan di antrean beat | Layout/select/stop dieksekusi langsung saat diterima; hanya launch yang dijadwalkan |
| 3 | Layout hilang saat reconnect | Layer/kolom tidak tersinkron setelah openVJ restart | `client epoch`: setiap koneksi baru memaksa kirim snapshot |
| 4 | Single-instance pakai `Mutex` | Dev + bundle bisa jalan bersamaan; openVJ menempel ke instance salah | Ganti ke **file lock** (`app.lock`, `FileShare.None`); instance kedua memanggil `open -b` untuk memunculkan yang sudah jalan |
| 5 | PLL mengekstrapolasi saat transport berhenti | Beat “bergerak” lalu snap (slew 0.26) | Saat `playing=false`, beat dibekukan (slew 0.0000) |
| 6 | Layer sisa (stray) | Jumlah layer +1 dari jumlah track | Trim layer yatim setelah rekonsiliasi |

Bukti akhir pada artefak bundle: 45 detik terus-menerus, 9 layout, 2 scene, 4 select,
1 stop, **0 slow render**, fps ~59, drift launch **4.3–6.8 ms**.

## Tab OpenVJ di panel detail NotA

`Devices | Pattern | Clip | **OpenVJ**` — tab keempat menampilkan parameter visual slot
Session yang dipilih, dengan slider yang sama seperti panel Clip openVJ.

- Sumber: `vizGet` → `vizState` (grup `source`, `transform`, `effects`, `playback`).
- Edit: `vizSet` (nilai real; openVJ menormalkan lewat `InputDef::norm`), `vizPlayback`.
- Efek: `vizFx` add/remove/move/enabled/mix; parameter tiap efek ikut tampil.
- Assign/ganti/hapus visual: dialog katalog (`vizCatalog` → `vizAssign`/`vizClear`).
- Dua arah: openVJ mem-poll state slot terpilih tiap 250 ms dan mengirim balik saat berubah;
  NotA menerapkan hanya bila tidak sedang drag.
- Tombol tab hanya berarti untuk clip Session; clip Arrangement menampilkan pesan.

Terverifikasi lewat hook `NOTA_VIZ_TEST=1`: assign Plasma → state (Scale 40%, Hue 60%),
set Scale 80% → state 80, add Kaleido → efek + parameter, catalog "plasma" → daftar sumber.

### Persistence

- Save project / quit → `vizSave` menulis `visuals/composition.json` (komposisi + `layout_uids`).
- Buka project → `vizLoad` memulihkan komposisi; uid dipakai saat rekonsiliasi layout supaya
  clip visual menempel ke track yang benar.
- Terverifikasi: assign Plasma + Scale 80% + efek Kaleido → save → clear → load → state kembali
  (visual=True, efek ada), lalu `vizLayer` 50% + Add → state `opacity=0.50 blend=Add`.

### Kontrol layer

Tab punya bagian **Layer**: opacity, blend (9 mode), bypass.

### Bug yang ditemukan saat uji UI

`vizState` memakai key `"layer"` dua kali (index angka + objek opacity/blend); objek menimpa
index sehingga NotA gagal membaca layer dan panel berhenti di “Loading the visual…”. Diperbaiki
menjadi `"layer_state"` + guard parse dan error panel.

Tampilan panel memakai kontrol Nota: `ClipEditorKit.SliderRow` (label + `SliderTrack` Shell
dengan nilai di dalam bar) untuk source/transform/efek/speed/mix/opacity, dan
`ClipEditorKit.Card` sebagai frame tiap area/efek.

Layout tab mengikuti openVJ: header clip (nama + Replace/Remove), lalu **3 kolom sejajar**
(`*,*,*`):
1. **Transport** — Speed, Beat sync + beats, Mode, Trigger.
2. **Source & Playback** — parameter sumber + Layer (opacity/blend/bypass).
3. **Effects** — Transform + seluruh rantai efek + Add effect. Checkbox/ComboBox masih kontrol Avalonia
standar untuk bool/pilihan.

### Audit tab OpenVJ

| Bug | Dampak | Perbaikan |
|---|---|---|
| `VizPlayback` selalu mengirim `syncBeats:null` | **Beat sync mati** setiap Speed/Mode/Trigger diubah | Flag `setSync`; field hanya dikirim saat sync memang diubah |
| Kotak pencarian katalog tidak terhubung | Mengetik tidak memfilter | Filter lokal + daftar terpilih dipetakan benar |
| State identik tetap membangun ulang panel | Kedip/fokus hilang saat echo | Dedupe `vizState` |
| Reconnect tidak meminta state | Panel tetap “offline” setelah engine hidup | Kirim `vizGet` saat koneksi pulih |
| `long` param dicocokkan persis | Pilihan salah saat nilai tidak bulat | Pilih nilai terdekat |
| Assign saat offline | Dialog kosong | Dialog “Visual engine offline” |
| Assign/clear setelah layout berubah | Indeks di luar batas | Guard layer/kolom di openVJ |
| Prewarm queue basi setelah load | Hitch pada launch pertama | Queue efek dibangun ulang |
| Quit bisa kehilangan simpanan visual | Bundle tidak ter-update | Flush 150 ms sebelum socket ditutup |

Bukti: sesudah sync 4 beat lalu Speed 2×, state menunjukkan `speed=2.00 sync=4`; file
`visuals/composition.json` menyimpan `speed 2.0, sync_beats 4.0`, dan load mengembalikannya.

Belum termasuk: pemilih media/kamera, undo, mapping audio di tab.

## Lifecycle visual engine

- NotA men-start `syntetika-visual` sendiri: binary dari `Contents/Helpers` (bundle) atau
  `src/openvj/target/release` (dev), dengan `OPENVJ_NOTA=127.0.0.1:<port>` dan
  `SYNTETIKA_EXIT_ON_DISCONNECT=1`.
- Output engine ditulis ke `<data>/logs/visual-engine.log`.
- Crash → restart otomatis maksimal 3× (jeda 1,5 s).
- Quit NotA → child dihentikan; kalau NotA mati mendadak, child keluar sendiri setelah
  ±3 detik tanpa koneksi.
- Kalau sudah ada engine yang dijalankan manual (bridge connected), NotA tidak men-spawn
  dan tidak mematikannya.

## Berikutnya

1. Sidecar: Nota meluncurkan `openvj` sendiri (port ephemeral, argumen `--nota`).
2. Model data `visuals/mapping.json` + `composition.json` di bundle.
3. UI: badge visual di sel grid, inspector "Visual", pilih sumber.
4. Preview thumbnail 10–15 fps lewat kanal yang sama.
5. Master tap audio → band/hits.
