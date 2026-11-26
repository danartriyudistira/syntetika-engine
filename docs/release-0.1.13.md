# Syntetika Engine 0.1.13 — MIDI out dari clip Session

Perbaikan untuk **MIDI To** yang sudah menampilkan port hardware tetapi tidak membunyikan
synth saat clip diputar.

## Perbaikan

- **Clip Session sekarang mengirim MIDI ke port output.** Sebelumnya hanya clip Arrangement
  yang menyalurkan note/CC/pitch bend ke perangkat eksternal; clip yang diputar dari grid
  Session memakai jalur render sendiri dan tidak menyalurkan MIDI. Sekarang kedua jalur
  memakai tap output yang sama, jadi clip Session memainkan synth hardware juga.
- **Routing MIDI To tidak hilang lagi saat clip diedit.** Setiap edit clip (menambah clip,
  mengubah note, rekam) membuat salinan track di engine; salinan itu belum membawa route
  MIDI out sehingga pilihan port bisa kosong lagi tanpa terlihat. Route (port, channel,
  uid) sekarang ikut tersalin, begitu juga mode monitor In/Auto/Off.
- Duplikat track juga membawa route MIDI out dan mode monitor yang sama.

## Cara pakai

1. Pilih port di **MIDI To** pada strip Mixer (atau Session I/O / Inspector).
2. Atur **Channel** sesuai channel yang didengar synth (umumnya 1).
3. Putar clip — baik dari Arrangement maupun dengan meluncurkan clip di Session.
4. Untuk memainkan keyboard langsung ke synth eksternal, set **Monitor = In** atau arm-kan
   track (mode **Auto** hanya meneruskan input live saat track di-arm, seperti Ableton).

Catatan: clip yang diputar mengirim note-on/off, CC, pitch bend, serta all-notes-off saat
transport berhenti atau seek.

## Build Windows

Tetap membawa: gamepad XInput, Spout output, DX12 + fallback Vulkan/OpenGL, audio tap,
installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, mixer I/O ala Ableton,
monitor In/Auto/Off, dan MIDI out per track.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.13-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.13-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## macOS

Build macOS terbaru tetap **0.1.7**; rilis ini berisi aset Windows.

## Checksums

```
a8ab4b273cdf6db310d222d19577a77e4b28fa50be0c280a0dc190a9c2bd473e  Syntetika-Engine-Setup-0.1.13-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
