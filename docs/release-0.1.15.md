# Syntetika Engine 0.1.15 — I/O Session ala Ableton dengan MIDI out

Perubahan pada mixer Session: bagian **I/O** kini punya baris **MIDI out** tersendiri, dan
susunan default mengikuti cara kerja Ableton Live.

## Baru

- **Baris MIDI out di I/O Session** untuk track instrumen:
  - pilih **port** (hardware/virtual, termasuk yang output-only),
  - pilih **channel 1–16** di sebelahnya,
  - track audio memakai baris kosong agar semua kolom tetap sejajar.
- **Baris Monitor untuk track instrumen** kini berisi **In / Auto / Off** (MIDI monitor),
  bukan kosong. Track audio tetap **In / Auto** seperti sebelumnya.
- Urutan I/O Session sekarang: **Input → Monitor → MIDI out → Output**, sama seperti
  blok I/O pada Mixer.
- **Default section Session**: **I/O aktif**, **Sends mati**, **Mixer aktif**. Pada
  Mixer docked/floating, **I/O** juga aktif dan **Sends** mati secara default.
- Input track instrumen kembali selebar kolom (MIDI out tidak lagi menempel di baris yang
  sama), jadi nama port tidak terpotong.

## Catatan

- MIDI out di sini memakai route yang sama dengan **MIDI To** di Mixer/Inspector: port +
  channel yang dipilih langsung dipakai clip Arrangement maupun Session.
- Sends tetap bisa dinyalakan lewat chip **Sends** di toolbar Session atau footer Mixer;
  pilihannya belum disimpan antar sesi (default kembali seperti di atas saat app dibuka).

## Build Windows

Tetap membawa: Auto visual profile (iGPU → Eco, dGPU → Quality), buffer WASAPI 512 default,
render worker physical-core, gamepad XInput, Spout output, DX12 + fallback Vulkan/OpenGL,
audio tap, installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, MIDI out per track,
dan MIDI out dari clip Session.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.15-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.15-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## macOS

Build macOS terbaru tetap **0.1.7**; sumber `main` sudah memuat perubahan ini. Build macOS
berikutnya mengikuti `docs/07-macos-prioritas.md`.

## Checksums

```
bcda08aea9d62b16dca59e8819b97a62c63114b38344fd81e85fcec48842a54f  Syntetika-Engine-Setup-0.1.15-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
