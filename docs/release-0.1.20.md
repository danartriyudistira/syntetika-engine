# Syntetika Engine 0.1.20 — Export Session Loops

Fitur baru: ekspor **current deck** dari Session menjadi loop audio (WAV) dan MIDI, satu
folder per scene.

## Cara pakai

1. Buka deck yang ingin diekspor (current deck).
2. **File → Export Session Loops…** (atau command palette).
3. Pilih sample rate / bit depth, centang MIDI jika perlu, lalu pilih folder tujuan.

Struktur hasil:

```
<folder pilihan>/
  01 Intro/
    01 Kobot - Eerie Riser.wav
    01 Kobot - Eerie Riser.mid
    02 Model D.wav
    02 Model D.mid
  02 Drop/
    01 Kobot.wav
    01 Kobot.mid
```

- **Satu folder per scene**; hanya scene yang punya slot terisi.
- **Satu WAV per track** dan **satu .mid per track instrumen** (bila diaktifkan).
- Slot kosong dilewati; group/return tidak dibuatkan file, tetapi efek group dan send tetap
  ikut ke loop track.
- **Loop hanya**: panjang file = panjang slot, tanpa tail. File langsung loop-ready.
- **Master effects di-bypass** saat render; fader/pan track, group, dan return tetap.
- Tempo scene dipakai saat render (lalu BPM project dikembalikan).
- Nama file aman untuk filesystem; track/scene/clip yang tidak bernama memakai nomor.

## Detail teknis

- Render offline per slot dengan engine diisolasi: track lain di-mute, solo dimatikan,
  launch quant 0, slot diluncurkan dari awal, lalu dirender sepanjang `SessionSlotLength`.
- Semua state dipulihkan setelah export: mute/solo, BPM, loop, metronome, launch quant,
  posisi transport, current deck, master device bypass, dan route MIDI out (dimatikan
  selama export supaya synth hardware tidak berbunyi).
- MIDI ditulis sebagai Standard MIDI File format 0 (480 PPQ) dengan tempo, time signature,
  nama track dan note on/off.
- Format: WAV 16-bit / 24-bit / 32-bit float; 44.1 / 48 / 96 kHz.
- Tanpa backend audio: export tetap jalan offline.

## Test

`--export-session` memeriksa:
- folder per scene dan nama file,
- jumlah frame loop pada 120 BPM dan tempo scene 140 BPM,
- loop mulai dari sample pertama (tidak ada blok hening saat launch),
- file MIDI valid,
- state mute, BPM, deck, dan master bypass pulih setelah export.

## Build Windows

Installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, Auto visual profile, MIDI out
timestamped + offset, mixer I/O, dan fitur sebelumnya tetap.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.20-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.20-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64.

## Checksums

```
fb0b996fc185ad413b4e05100d5ee211a0316be37290212c917c08a9a29bdfb2  Syntetika-Engine-Setup-0.1.20-x64.exe
4eed7055fb79b0648025ca159c4e1544a2cc33017371d8b69842a3fa8d37ecb7  Syntetika-Engine-0.1.18-arm64.dmg
```
