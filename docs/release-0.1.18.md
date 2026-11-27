# Syntetika Engine 0.1.18 — build macOS dengan MIDI out, timing, dan hot-plug

Build macOS pertama sejak 0.1.7. DMG ini membawa seluruh rangkaian MIDI out dan I/O dari
0.1.8–0.1.17, ditambah perbaikan **hot-plug port MIDI** yang belum ada di build Windows
mana pun.

Windows **0.1.17** sudah tersedia terpisah; isinya sama dengan build ini kecuali perbaikan
hot-plug.

## macOS

`Syntetika-Engine-0.1.18-arm64.dmg` — macOS 12+, Apple Silicon.

### MIDI out ke synth eksternal

- Setiap track instrumen bisa mengirim ke port MIDI (hardware atau virtual) dan **channel
  1–16**: note on/off, CC, pitch bend, dan channel pressure dari clip Arrangement **dan**
  clip Session.
- **Timestamped send (0.1.17)**: event dikirim mendekati sample time-nya, bukan seluruh blok
  sekaligus. Buffer 512 frame tidak lagi otomatis berarti jitter sampai ±10,7 ms.
- **MIDI out offset per track (−200…+200 ms)** di Inspector → MIDI out, untuk menyamakan
  synth hardware yang terlambat/terlalu awal. Positif menunda, negatif memajukan dalam blok
  berjalan.
- Stop, seek, ganti channel, hapus clip, ganti audio device, dan quit mengirim
  **all-notes-off + reset controller**; tidak ada nada menggantung.
- Route + channel + offset disimpan di project dan ikut tersalin saat track diduplikat.
- Port output dienumerasi langsung dari CoreMIDI dengan unique ID, jadi port output-only
  (synth, IAC) ikut muncul.

### I/O ala Ableton

- **Mixer**: **MIDI From → Monitor In/Auto/Off → MIDI out + Channel → Audio To** untuk track
  instrumen; **Audio From → Monitor → Audio To** untuk track audio.
- **Session**: baris **Input → Monitor → MIDI out → Output**; chip **I/O / Sends / Mixer**
  dengan default I/O aktif, Sends mati, Mixer aktif.
- MIDI out juga terlihat di header track, inspector, mixer, dan command palette.

### Hot-plug port MIDI

- Port atau controller yang dicolok **setelah app berjalan** langsung muncul di **MIDI To**
  dan **MIDI From** — tanpa restart dan tanpa membuka ulang Mixer.
- CoreMIDI menyimpan snapshot daftar port per proses sampai ada client dengan notification
  run loop. Engine sekarang menjalankan satu client seperti itu di thread sendiri
  (`watchMidiPorts()`), jadi enumerasi output, pembukaan route tersimpan, dan enumerasi input
  selalu segar. Cache enumerasi 500 ms dari 0.1.16 tetap dipakai dan ikut tersegarkan.
- Diverifikasi dengan destination CoreMIDI virtual: port muncul ~250 ms setelah dibuat dan
  hilang seketika saat proses pemiliknya berhenti.

### Ikut terbawa dari 0.1.8–0.1.17

- **Random MIDI parts** (bassline/pad/melody) di editor clip MIDI.
- Perubahan tempo **realtime** untuk audio warped.
- Render worker memakai **physical core**; profil visual **Auto** (di macOS tetap Quality).
- Perbaikan mixer, Session, dan MIDI dari rilis Windows 0.1.9–0.1.17.

### Catatan macOS

- DMG ini **ad-hoc signed, belum notarized**. Saat pertama dibuka: klik kanan app →
  **Open** → **Open**, atau System Settings → Privacy & Security → **Open Anyway**.
- Hanya **Apple Silicon (arm64)**; belum ada build Intel.
- uid port CoreMIDI berbeda dari nama port Windows, jadi route yang dibuat di Windows perlu
  dipilih ulang di Mac.
- Paritas mapping gamepad macOS (GameController) vs XInput Windows belum diuji dengan
  controller fisik.

## Checksums

```
4eed7055fb79b0648025ca159c4e1544a2cc33017371d8b69842a3fa8d37ecb7  Syntetika-Engine-0.1.18-arm64.dmg
```

Windows 0.1.17: `9215a2d511f409e66e8e536f5300a6d92eaf932f888271f8cf23b5c9d5a0099d`
(`Syntetika-Engine-Setup-0.1.17-x64.exe`).
