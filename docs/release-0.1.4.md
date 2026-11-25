# Syntetika Engine 0.1.4 — Windows x64

Syntetika Engine kini berjalan di Windows: DAW (Nota) dan mesin visual (openVJ) dalam satu
aplikasi, dengan visual yang diluncurkan pada beat yang sama.

## Yang baru

- **Build Windows x64** — engine native (`nota_engine.dll`, WASAPI/ASIO), aplikasi Avalonia,
  dan `syntetika-visual.exe` yang dinyalakan, diawasi, dan dimatikan oleh aplikasi.
- **DX12** sebagai backend GPU default di Windows (`WGPU_BACKEND` untuk override).
- **Audio tap Windows** — parameter audio-reactive mengikuti master Nota lewat
  memory-mapped file.
- **Installer self-contained** — tanpa .NET; runtime Visual C++ dan FFmpeg ikut dibundel;
  instal per user tanpa UAC; menu klik-kanan "Open with Syntetika Engine" untuk folder `.nota`.
- **Demo lengkap** — project `demo/` sekarang membawa visualnya (15 clip ink-kaleido).

## Perbaikan

- Mesin visual menyala lagi setelah crash (file log tidak lagi mengunci restart).
- Instans kedua menaikkan window yang sudah jalan, bukan keluar diam-diam.

## Instalasi

1. Unduh `Syntetika-Engine-Setup-0.1.4-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.4-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway** (installer belum ditandatangani).

Persyaratan: Windows 10/11 x64. Tidak perlu .NET/VC++/FFmpeg terpasang.
Panduan lengkap: `docs/download.md`. Batasan: Syphon belum ada di Windows (Spout belum
diintegrasikan), Ableton Link belum dikompilasi.

## Checksums

Lihat `Syntetika-Engine-Setup-0.1.4-x64.exe.sha256` pada rilis ini.
