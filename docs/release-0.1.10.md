# Syntetika Engine 0.1.10 — MIDI out mudah ditemukan

Perbaikan UX untuk routing MIDI ke synth eksternal: sekarang terlihat di tempat yang biasa
dipakai, bukan hanya menu klik-kanan.

## Baru

- **Chip “MIDI” di header track** — klik chip MIDI di header Arrangement: menu berisi dua
  submenu, **MIDI in** dan **MIDI out** (port + channel). Saat track memutar synth eksternal,
  chip menampilkan `→ <port>` sehingga routing kelihatan sekilas.
- **Inspector** — bagian terpisah **MIDI input** dan **MIDI out** (Port + Channel), tampil
  begitu track instrument dipilih.
- **Mixer** — picker MIDI out kecil di samping MIDI in pada channel strip.
- **Session I/O** — dua picker sejajar: MIDI in dan MIDI out per track.
- **Command palette** — “MIDI out for the selected track” membuka pemilih port/channel
  langsung untuk track terpilih.

Fungsinya sama dengan 0.1.8: note, CC, pitch bend, channel pressure dikirim ke port output;
stop/seek mengirim all-notes-off; route tersimpan di project.

## Build Windows

Tetap membawa: gamepad XInput, Spout output, DX12 + fallback Vulkan/OpenGL, audio tap,
installer self-contained (VC++/FFmpeg), asosiasi `.syntetika`, Random pattern + preset genre,
Random MIDI parts, MIDI out per track.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.10-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.10-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway**.

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## macOS

Build macOS terbaru tetap **0.1.7**; rilis ini berisi aset Windows.

## Checksums

```
2b2f4da1c642a4f98ee7073e2b3aa1f44a11c9bfe13093e5dbb4d219ca211d85  Syntetika-Engine-Setup-0.1.10-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
