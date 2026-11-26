# Syntetika Engine 0.1.9 — gamepad di Windows

Paritas fitur macOS yang belum ada di Windows dilengkapi bertahap. 0.1.9 menambahkan
**gamepad** sebagai sumber nada live, plus semua yang sudah ada di 0.1.8.

## Baru di Windows

- **Gamepad (XInput)** — sampai 4 pad Xbox-compatible (termasuk 8BitDo dalam mode XInput).
  Tombol A/B/X/Y, LB/RB, LT/RT dan d-pad menjadi note/aksi di jalur live yang sama dengan
  keyboard; stick dan trigger menggerakkan parameter yang di-map (Preferences → Gamepads,
  atau MIDI-learn). Tidak perlu driver tambahan.
- Build Windows tetap membawa: DX12 + fallback Vulkan/OpenGL, **Spout output**, audio tap,
  **MIDI out per track**, installer self-contained, asosiasi `.syntetika`, Random pattern
  dan preset genre Drum Rack, Random MIDI parts.

## Catatan paritas macOS

- **Syphon** tetap macOS-only; di Windows output memakai **Spout**. Spout **input**
  (menerima frame dari Resolume/OBS) belum ada — rencana berikutnya.
- **Ableton Link** belum dikompilasi di build Windows: crate `rusty_link` membutuhkan
  `libclang` untuk bindgen. Build dapat diaktifkan dengan `scripts/build-win.ps1 -WithLink`
  setelah LLVM (libclang) terpasang dan `LIBCLANG_PATH` diset.
- Spout input dan Link adalah dua celah paritas yang tersisa; sisanya sudah setara.

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-0.1.9-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.9-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway** (installer belum ditandatangani).

Persyaratan: Windows 10/11 x64. Tidak perlu .NET/VC++/FFmpeg terpasang.

## macOS

Build macOS terbaru tetap **0.1.7**; rilis ini berisi aset Windows.

## Checksums

```
b3d0c0f18e806f01748b31f1c7c8b5fcd0b90ca7df4b220a4b381e423d22ea8f  Syntetika-Engine-Setup-0.1.9-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
