# Syntetika Engine 0.1.11 — mixer I/O ala Ableton

Strip mixer sekarang punya blok I/O berlabel seperti Ableton Live, jadi MIDI ke synth
eksternal terlihat dan bisa diatur dari strip, bukan dari menu tersembunyi.

## Baru

- **Instrument track**: blok I/O di strip berisi
  - **MIDI From** — perangkat (atau “From” track lain) + **channel**,
  - **Monitor** — **In / Auto / Off** (In: input live selalu bunyi, Auto: hanya saat track
    di-arm, Off: tidak pernah; clip tetap diputar),
  - **MIDI To** — **port output** (loopMIDI/USB-MIDI) + **channel 1–16**,
  - **Audio To** — Master atau group.
- **Audio track**: **Audio From** (input perangkat), **Monitor In/Auto/Off**, **Audio To**
  (Master/group).
- Semua strip memakai tinggi I/O yang sama supaya fader tetap sejajar; group/return/master
  menampilkan tujuan audionya.
- Monitor mode disimpan di project (`monitorMode`, `midiMonitor`) dan dipulihkan saat dibuka.
- Chip **MIDI** di header Arrangement, bagian **MIDI input/MIDI out** di Inspector, picker di
  Session I/O, dan perintah palette tetap ada dari 0.1.10.

## Catatan

- “In” pada MIDI berarti input live selalu dimainkan tanpa perlu arm (seperti Ableton);
  rekaman tetap mengikuti arm.
- Audio “Auto” memonitor hanya saat track di-arm; “Off” tetap merekam bila di-arm, tanpa
  mendengar input.

## Build Windows

Tetap membawa: gamepad XInput, Spout output, DX12 + fallback Vulkan/OpenGL, audio tap,
installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, Random pattern/preset genre,
Random MIDI parts, dan MIDI out per track.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.11-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.11-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## macOS

Build macOS terbaru tetap **0.1.7**; rilis ini berisi aset Windows.

## Checksums

```
0fece0abcf2935561853b55cd5f0b227d21915078235b32d35db68d23552dec5  Syntetika-Engine-Setup-0.1.11-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
