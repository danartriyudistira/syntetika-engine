# Syntetika Engine 0.1.8 — MIDI out ke synth eksternal

Track instrument di Syntetika Engine kini bisa memutar **synth hardware atau virtual** lewat
MIDI, bukan hanya instrument internal. Ini yang diminta: “Session MIDI untuk mengontrol
external synthesizer”.

## MIDI out per track

- Menu track (atau inspector) → **MIDI out** → pilih port output (RtMidi; loopMIDI, USB-MIDI,
DIN lewat interface) lalu **Channel** 1–16.
- Yang dikirim: **note on/off**, **CC** (mod wheel, sustain, ekspresi, dll.), **pitch bend**,
dan **channel pressure** dari clip track itu — setelah MIDI effects track.
- **Stop atau lompat posisi** mengirim all-notes-off + reset controller (CC 123/120/64), jadi
tidak ada nada menggantung di synth.
- Pengiriman lewat **thread pump** sendiri (antrean lock-free dari audio thread), karena RtMidi
tidak realtime-safe. Audio thread tidak pernah menunggu.
- Route disimpan di project (`MIDI out` + channel) dan dipulihkan saat project dibuka.
- Rilis ini juga membawa **Random MIDI parts** (bassline, pad, melody) dan perbaikan tempo realtime untuk audio warped dari upstream 0.1.8.

Cara pakai singkat: buat track instrument, isi clip MIDI (atau mainkan dari keyboard), buka
menu track → **MIDI out** → pilih port dan channel synth Anda → tekan Play.

## Windows

Build Windows 0.1.8 berisi semua yang sudah ada: DX12 + fallback Vulkan/OpenGL, Spout output,
audio tap, installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, menu klik-kanan
folder `.nota`, Random pattern per genre dan 20 preset genre Drum Rack.

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-0.1.8-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.8-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway** (installer belum ditandatangani).

Persyaratan: Windows 10/11 x64. Tidak perlu .NET/VC++/FFmpeg terpasang.

## macOS

Build macOS terbaru tetap **0.1.6**; rilis ini berisi aset Windows. MIDI out tersedia di kedua
platform (RtMidi/CoreMIDI).

## Checksums

```
1f2f2af043c85c0c6b3fc19cf83b4768d7b9da7a01742242721788fb59b4fe1c  Syntetika-Engine-Setup-0.1.8-x64.exe
bc16054b56441ba80edd52855ee4b985f535014dcbb7cb17b872a5c375b567c8  Syntetika-Engine-0.1.6-arm64.dmg
```
