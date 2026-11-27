# Syntetika Engine 1.0.0 — rilis stabil pertama (Windows x64)

Versi **1.0.0** adalah rilis resmi pertama untuk Windows x64. Seluruh versi **0.1.x**
(termasuk 0.1.20) sekarang ditandai **beta / pre-release** dan tidak disarankan untuk
penggunaan sehari-hari.

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
- **macOS 0.1.20**: masih **beta**; build macOS 1.0.0 menyusul dari sumber yang sama.
- Semua rilis 0.1.x tetap tersedia untuk riwayat, tetapi ditandai **Pre-release** di GitHub.

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-1.0.0-x64.exe`.
2. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
3. SmartScreen: **More info → Run anyway**.

Verifikasi SHA-256 **opsional** (hanya installer yang perlu diunduh):

```powershell
Get-FileHash .\Syntetika-Engine-Setup-1.0.0-x64.exe -Algorithm SHA256
```

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## Checksums

```
ba1411d62ed793614592fa50df20cdd59014b32cedc72cb96883e567e5a33800  Syntetika-Engine-Setup-1.0.0-x64.exe
2956150440a5bef6b0b6d0fc6e0a26fc51233d00c1e5f866a4ecd6dee6cfb3ad  Syntetika-Engine-0.1.20-arm64.dmg
```
