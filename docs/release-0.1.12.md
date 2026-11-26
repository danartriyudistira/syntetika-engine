# Syntetika Engine 0.1.12 — daftar port MIDI out hardware

Menu **MIDI To** di strip mixer sekarang menampilkan semua port MIDI output yang terpasang,
termasuk perangkat yang hanya punya output (synth hardware, loopMIDI, interface USB-MIDI).

## Perbaikan

- **MIDI To menemukan hardware.** Sebelumnya daftar output diambil dari port input yang
  kebetulan punya nama sama, jadi synth/loopMIDI yang output-nya beda nama (atau tidak punya
  input sama sekali) tidak pernah muncul. Engine sekarang membaca daftar tujuan MIDI langsung
  dari backend (WinMM/ALSA di Windows/Linux, CoreMIDI di macOS) lewat
  `nota_midi_output_count/uid/name`, terpisah dari daftar input.
- Daftar yang sama dipakai di semua tempat: blok **MIDI To** pada strip Mixer, menu track,
  picker di Session I/O, dan Inspector.
- Port yang sedang dipilih tapi sudah dicabut tetap tampil sebagai “not connected”, jadi
  routing di project tidak hilang.
- Test `--midi-out` menambah pemeriksaan bahwa port output benar-benar terdaftar di
  enumerasi output.

## Catatan

- Urutan port mengikuti backend; di Windows nama port itulah uid yang dipakai untuk membuka
  dan mengirim MIDI.
- Kalau perangkat baru dicolok, buka ulang tampilan Mixer (atau tunggu hot-plug MIDI
  berikutnya) supaya daftarnya diperbarui.

## Build Windows

Tetap membawa: gamepad XInput, Spout output, DX12 + fallback Vulkan/OpenGL, audio tap,
installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, mixer I/O ala Ableton,
monitor In/Auto/Off, dan MIDI out per track.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.12-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.12-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## macOS

Build macOS terbaru tetap **0.1.7**; rilis ini berisi aset Windows (enumerasi output CoreMIDI
ikut di sumber, jadi build macOS berikutnya memakai jalur yang sama).

## Checksums

```
3cfbde3b50737923979599b86f73ab6ac9d805a87aef834a3dd1190587b6c4e1  Syntetika-Engine-Setup-0.1.12-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
