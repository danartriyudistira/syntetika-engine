# Syntetika Engine 1.0.0 — rilis stabil pertama (Windows x64 + macOS arm64)

Versi **1.0.0** adalah rilis resmi pertama untuk **Windows x64 dan macOS arm64** — keduanya
dibangun dari sumber yang sama. Seluruh versi **0.1.x** (termasuk 0.1.20) sekarang ditandai
**beta / pre-release** dan tidak disarankan untuk penggunaan sehari-hari.

## Yang sudah ada di 1.0.0

- Session view Nota dengan clip audio/MIDI, scene, deck, follow action, dan rekaman.
- Mixer I/O ala Ableton: **MIDI From → Monitor In/Auto/Off → MIDI out + Channel → Audio To**;
  untuk audio **Audio From → Monitor → Audio To**.
- **MIDI out** ke synth hardware/virtual: note, CC, pitch bend, channel pressure; timestamped
  send; offset global di A/V Offset + trim per track; all-notes-off saat stop/seek/edit/quit.
- **Export Session Loops**: current deck → satu folder per scene, WAV per track dan MIDI per
  clip instrumen, loop-only, master effects di-bypass.
- Export master/stems, import audio, browser, piano roll, arrangement, automation, undo.
- Visual openVJ: Auto performance profile (iGPU → Eco), DX12 + fallback, Spout output,
  audio-reactive visuals.
- Gamepad XInput, audio tap, installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`.

## Catatan versi

- **Windows 1.0.0**: stabil.
- **macOS 1.0.0**: stabil, dari sumber yang sama; build ini juga membawa perbaikan hot-plug
  port MIDI khusus CoreMIDI.
- Semua rilis 0.1.x tetap tersedia untuk riwayat, tetapi ditandai **Pre-release** di GitHub.

## macOS

`Syntetika-Engine-1.0.0-arm64.dmg` — macOS 12+, Apple Silicon.

Build ini membawa fitur yang sama dengan Windows 1.0.0: MIDI out (timestamped + offset global
dan per track), I/O ala Ableton, Export Session Loops, Random MIDI parts, tempo realtime, dan
hot-plug port MIDI. Suite `--export-session` dan `--midi-out` lulus di macOS.

DMG ini **ad-hoc signed, belum notarized**. Saat pertama dibuka: klik kanan app → **Open** →
**Open**, atau System Settings → Privacy & Security → **Open Anyway**. Hanya Apple Silicon;
belum ada build Intel.

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-1.0.0-x64.exe`.
2. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
3. SmartScreen: **More info → Run anyway**.

Verifikasi SHA-256 **opsional** (hanya installer yang perlu diunduh):

```powershell
Get-FileHash .\Syntetika-Engine-Setup-1.0.0-x64.exe -Algorithm SHA256
```

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## Instalasi macOS

1. Unduh `Syntetika-Engine-1.0.0-arm64.dmg`.
2. (Opsional) Verifikasi:

   ```bash
   shasum -a 256 Syntetika-Engine-1.0.0-arm64.dmg
   ```

3. Buka image, seret **Syntetika Engine** ke **Applications**.
4. Jalankan; bila macOS menolak, pakai langkah Gatekeeper di atas.

## Checksums

```
ba1411d62ed793614592fa50df20cdd59014b32cedc72cb96883e567e5a33800  Syntetika-Engine-Setup-1.0.0-x64.exe
eb9650fd284b10f9626c2e7d658a2dfbfc1120146a46d766d3e606b3e787b0d5  Syntetika-Engine-1.0.0-arm64.dmg
```
