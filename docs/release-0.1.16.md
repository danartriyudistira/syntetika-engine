# Syntetika Engine 0.1.16 — perbaikan MIDI out dan mixer

Rilis patch: empat bug MIDI out yang bisa membuat synth eksternal menggantung atau berhenti
mengirim, plus pengurangan beban UI saat mixer dibuka.

## Diperbaiki

- **Ganti channel MIDI To tidak lagi meninggalkan note menggantung.** All-notes-off dulu
  dikirim ke channel **baru** padahal note yang berbunyi masih di channel **lama**; sekarang
  off dikirim ke route/channel lama sebelum pindah.
- **Edit atau hapus clip yang sedang berbunyi kini mengirim note-off ke port eksternal.**
  Sebelumnya note-off hanya sampai ke instrumen internal; hardware bisa terus berbunyi.
  Menghapus track yang punya route MIDI out juga mengirim all-notes-off.
- **MIDI out tetap hidup setelah audio restart.** Mengubah buffer/sample rate/device
  menghentikan pump MIDI, dan pump tidak dinyalakan lagi — hardware diam sampai route dipilih
  ulang. Sekarang pump dinyalakan kembali bila masih ada route, dan saat berhenti semua route
  di-flush dulu supaya tidak ada note tertinggal.
- **Enumerasi port MIDI di-cache sebentar.** Membuka mixer dengan banyak track dulu
  membuat scanner RtMidi/CoreMIDI baru per strip; sekarang satu enumerasi dipakai untuk
  seluruh build, dan diperbarui saat backend melaporkan perubahan port.

## Catatan

- Perbaikan ini tidak mengubah routing yang tersimpan; project lama tetap memakai port +
  channel yang sama.
- Performance gate sintetis (16 track realtime, VST3 overload, 64-frame buffer) masih bisa
  gagal di laptop kelas minimum; itu batas CPU/thermal, bukan regresi fungsional. Suite
  fungsional (MIDI out, Session, project, undo) lulus.

## Build Windows

Tetap membawa: Auto visual profile (iGPU → Eco, dGPU → Quality), buffer WASAPI 512 default,
render worker physical-core, I/O Session dengan MIDI out, MIDI monitor In/Auto/Off, gamepad
XInput, Spout output, DX12 + fallback Vulkan/OpenGL, audio tap, installer self-contained.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.16-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.16-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## Checksums

```
11dfb66c12fcf3b31efeaab37991a9491188243334e9d5f9365191a1dde001ad  Syntetika-Engine-Setup-0.1.16-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
