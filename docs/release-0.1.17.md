# Syntetika Engine 0.1.17 — MIDI out timing dan offset

Rilis ini memperbaiki sinkronisasi MIDI out ke synth hardware/virtual: event dikirim sesuai
sample time-nya, dan tiap track punya kompensasi latency sendiri.

## Baru

- **Timestamped MIDI out.** Pump tidak lagi mengirim semua note/CC satu blok sekaligus; tiap
  event membawa waktu kirim (`steady_clock`) dan dikirim mendekati sample time-nya. Buffer
  512 frame tidak lagi otomatis berarti jitter sampai ±10,7 ms.
- **MIDI out offset per track (-200…+200 ms).**
  - **positif** menunda pengiriman MIDI,
  - **negatif** memajukan pengiriman sebanyak yang masih muat dalam blok berjalan.
  - Hanya route eksternal yang bergeser; instrumen internal dan audio track tetap di tempat.
  - Lokasi: **Inspector → MIDI out → MIDI out offset**, atau menu MIDI out (nilai cepat
    −50…+50 ms).
  - Disimpan di project (`MidiOutOffsetMs`) dan ikut tersalin saat track diedit/diduplikat.
- **Stop/seek/quit dan ganti audio device** mengirim all-notes-off secara sinkron sebelum pump
  berhenti, jadi tidak ada note tertinggal di synth.
- **Ganti channel MIDI out** mengirim all-notes-off ke channel lama, bukan channel baru.

## Cara menyetel offset

1. Putar clip dengan suara perkusif yang sama di track internal dan synth eksternal.
2. Kalau synth **terlambat**, beri offset **negatif** (mis. −5, −10 ms) sampai sejajar.
3. Kalau synth **terlalu awal**, beri offset **positif**.
4. Batas negatif adalah satu blok render: buffer 512 @48 kHz ≈ 10,7 ms. Untuk kompensasi
   lebih besar, turunkan buffer (mis. 256) atau tunggu lookahead di versi berikutnya.

Offset tidak mengubah posisi note di clip; hanya waktu pengiriman MIDI keluar.

## Catatan

- Suite `--midi-out` menambah test untuk offset, clone track, note-off saat clip dihapus, dan
  pesan yang ditahan sampai due time.
- Performance gate sintetis di laptop minimum masih bisa gagal; itu batas CPU/thermal.

## Build Windows

Tetap membawa: Auto visual profile (iGPU → Eco, dGPU → Quality), buffer WASAPI 512 default,
render worker physical-core, I/O Session dengan MIDI out, MIDI monitor In/Auto/Off, gamepad
XInput, Spout output, DX12 + fallback Vulkan/OpenGL, audio tap, installer self-contained.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.17-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.17-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## Checksums

```
9215a2d511f409e66e8e536f5300a6d92eaf932f888271f8cf23b5c9d5a0099d  Syntetika-Engine-Setup-0.1.17-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
