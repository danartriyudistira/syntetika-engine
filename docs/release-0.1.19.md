# Syntetika Engine 0.1.19 — slider MIDI out di jendela A/V offset

Rilis ini menambahkan slider **MIDI out offset** seperti slider A/V offset, di atas kode
MIDI out 0.1.17. Tersedia untuk **Windows x64** dan, mulai build ini, **macOS arm64** — build
macOS 0.1.19 berasal dari sumber yang sama dan juga membawa perbaikan hot-plug port MIDI.

## Baru

- **Slider MIDI out offset (all tracks)** di **View → A/V Offset**, di bawah slider gambar.
  - Rentang **−200…+200 ms**, langkah 1 ms.
  - **Positif** menunda MIDI out; **negatif** memajukan sebanyak yang masih muat dalam blok.
  - Nilai disimpan di settings app dan langsung diterapkan ke engine.
  - Nilai ini **global**: ditambahkan ke trim per track dari Inspector, jadi satu slider bisa
    menggeser semua synth, lalu trim per perangkat untuk koreksi halus.
- Jendela A/V tetap membuka output visual + metronome untuk kalibrasi gambar; slider MIDI
  bisa digeser sambil clip perkusif diputar.

## Cara pakai

1. Buka **View → A/V Offset**.
2. Putar clip dengan suara perkusif yang sama di track internal dan synth eksternal.
3. Geser slider **MIDI out offset**:
   - synth **terlambat** → nilai **negatif**,
   - synth **terlalu awal** → nilai **positif**.
4. Kalau satu perangkat masih perlu koreksi sendiri, gunakan **Inspector → MIDI out → MIDI out
   offset** (trim per track).

Batas negatif tetap satu blok render: buffer 512 @48 kHz ≈ 10,7 ms. Untuk kompensasi lebih
besar, turunkan buffer atau tunggu lookahead.

## Ikut terbawa

- Timestamped MIDI out dan trim per track (0.1.17).
- Perbaikan note menggantung saat ganti channel, edit/hapus clip, ganti audio device, quit.
- Auto visual profile, buffer WASAPI 512 default, render worker physical-core, I/O Session
  dengan MIDI out, MIDI monitor In/Auto/Off, gamepad XInput, Spout, DX12 + fallback.

## Build Windows

Installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, tanpa .NET terpasang pun jalan.

## macOS

`Syntetika-Engine-0.1.19-arm64.dmg` — macOS 12+, Apple Silicon.

Build ini membawa seluruh rangkaian 0.1.8–0.1.19 (MIDI out, I/O ala Ableton, timing MIDI,
slider offset global), ditambah **hot-plug port MIDI**: port/controller yang dicolok setelah
app berjalan langsung muncul di MIDI To / MIDI From tanpa restart.

DMG ini **ad-hoc signed, belum notarized**. Saat pertama dibuka: klik kanan app → **Open** →
**Open**, atau System Settings → Privacy & Security → **Open Anyway**. Hanya tersedia untuk
Apple Silicon; belum ada build Intel.

## Instalasi Windows

Hanya installer/DMG yang perlu diunduh; hash SHA-256 di bagian Checksums opsional.

1. Unduh `Syntetika-Engine-Setup-0.1.19-x64.exe`.
2. (Opsional) Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.19-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64.

## Instalasi macOS

1. Unduh `Syntetika-Engine-0.1.19-arm64.dmg`.
2. (Opsional) Verifikasi:

   ```bash
   shasum -a 256 Syntetika-Engine-0.1.19-arm64.dmg
   ```

3. Buka image, seret **Syntetika Engine** ke **Applications**.
4. Jalankan; bila macOS menolak, pakai langkah Gatekeeper di atas.

## Checksums

```
a29c6c9b5a0ee7838a6bb87731c21a6f652fc9931ce1343f85be1a37760a7400  Syntetika-Engine-Setup-0.1.19-x64.exe
452333f1a1ceb25220531035c1fd2b110171b144b108569f9dd8ab16dc561d39  Syntetika-Engine-0.1.19-arm64.dmg
```
