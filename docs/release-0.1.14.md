# Syntetika Engine 0.1.14 — performa stabil di mesin minimum

Rilis ini menyetel default Windows agar audio + visual tetap stabil di laptop kelas minimum
(iGPU, 4C/8T 15 W, RAM single-channel), tanpa menghilangkan pilihan kualitas untuk mesin kuat.

## Baru

- **Profil visual Auto** (View → Visual Performance → **Auto**, default pengaturan baru):
  - iGPU / CPU → **Eco** (30 fps, preview 15)
  - dGPU → **Quality** (60/60)
  - macOS tetap **Quality** (Apple Silicon tidak ikut heuristic iGPU Windows)
  - Pilihan Eco/Balanced/Quality tetap ada dan disimpan seperti sebelumnya.
- **Buffer WASAPI default 512 frame** (~10,7 ms @48 kHz) saat belum ada `audio.json`, supaya
  lonjakan DPC latency laptop tidak langsung menjadi dropout. Device default / 128 / 256 / 1024
  tetap bisa dipilih dari status bar atau Settings → Audio.
- **Render worker mengikuti physical core**: 4C/8T memakai 3 worker, bukan 7, sehingga audio
  callback, UI, dan proses visual punya thread cadangan. Mesin 2 core tetap render inline.

## Kenapa

Di laptop uji (i5-10310U, UHD 620, 16 GB **single-channel**), profil Quality 60/60 menyisakan
headroom sangat tipis: log visual menunjukkan `drops` dan `max_dt` hingga ~27 ms bahkan pada
komposisi ringan, dan dokumentasi mencatat 15–25 fps untuk dua generator 720p. Auto memilih Eco
di kelas GPU ini; buffer 512 dan worker fisik mengurangi risiko xrun saat UI/visual sibuk.

## Tuning yang disarankan

1. Biarkan **Auto**; jika ingin 60 fps, pilih Quality hanya untuk komposisi ringan.
2. Jika masih ada crackle: buffer **1024** + Preferences → Performance (cek DSP peak/dropout).
3. Jangan simpan project aktif di folder OneDrive selama sesi; matikan Spout jika tidak dipakai.
4. Gunakan power mode **Best Performance** saat di AC dan jaga ventilasi.

## Build Windows

Tetap membawa: gamepad XInput, Spout output, DX12 + fallback Vulkan/OpenGL, audio tap,
installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, mixer I/O ala Ableton,
monitor In/Auto/Off, MIDI out per track, dan MIDI out dari clip Session.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.14-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.14-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## macOS

Build macOS terbaru tetap **0.1.7**; sumber `main` sudah memuat perubahan ini (Auto tetap
memilih Quality di macOS). Build macOS berikutnya mengikuti `docs/07-macos-prioritas.md`.

## Checksums

```
fc294da150da5ee5174390c7d0707aca805da56e2ea15c6eee8cf001ff966cad  Syntetika-Engine-Setup-0.1.14-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
