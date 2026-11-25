# Syntetika Engine 0.1.6 — Windows x64 dan macOS

Rilis ini membawa Syntetika Engine ke Windows, bersama fitur Nota 0.1.6: project satu file,
pola drum acak per genre, dan preset genre untuk Drum Rack.

## Windows (baru)

- **Build Windows x64** — engine native (`nota_engine.dll`, WASAPI/ASIO), aplikasi Avalonia,
  dan `syntetika-visual.exe` yang dinyalakan, diawasi, dan dimatikan oleh aplikasi.
- **DX12** sebagai backend GPU default (`WGPU_BACKEND` untuk override), dengan fallback
  Vulkan/OpenGL bila tidak ada adapter DX12.
- **Audio tap Windows** — parameter audio-reactive mengikuti master Nota lewat
  memory-mapped file.
- **Installer self-contained** — tanpa .NET; runtime Visual C++ dan FFmpeg ikut dibundel;
  instal per user tanpa UAC; asosiasi `.syntetika` untuk project satu file dan menu
  klik-kanan "Open with Syntetika Engine" untuk folder `.nota` lama.
- **Demo lengkap** — project `demo/` membawa visualnya (15 clip ink-kaleido).

## Nota 0.1.6

- **Project satu file** (`.syntetika`) — audio, visual, media, dan riwayat versi dalam satu
  file; folder `.nota` lama tetap terbuka dan dimigrasi saat Save As.
- **Random pattern per genre** — pola drum acak dengan genre dan campuran genre; jendela
  Random non-modal dan "Surprise me" langsung menghasilkan pola.
- **Preset genre Drum Rack** — preset pabrik per genre, siap dimuat di Drum Rack.

## Perbaikan (Windows)

- Mesin visual menyala lagi setelah crash (file log tidak lagi mengunci restart).
- Instans kedua menaikkan window yang sudah jalan dan membuka project yang diklik.
- Default GPU DX12 menghindari crash driver Vulkan Intel saat surface pertama dibuat.

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-0.1.6-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.6-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway** (installer belum ditandatangani).

Persyaratan: Windows 10/11 x64. Tidak perlu .NET/VC++/FFmpeg terpasang.
Panduan lengkap: `docs/download.md`. Batasan: Syphon belum ada di Windows (Spout belum
diintegrasikan), Ableton Link belum dikompilasi.

## macOS

DMG Apple Silicon tetap seperti rilis 0.1.6 sebelumnya:
`Syntetika-Engine-0.1.6-arm64.dmg`.

1. Unduh DMG, buka, dan seret **Syntetika Engine** ke **Applications**.
2. Build ini **belum dinotarisasi** (tanpa Developer ID): saat pertama dibuka macOS bisa
   memblokirnya — klik kanan app → **Open**, atau izinkan di
   System Settings → Privacy & Security → **Open Anyway**. Cukup sekali.
3. Persyaratan: macOS 12+ di Apple Silicon (arm64).

## Checksums

```
6f307c5e5f9007c950c0e4bede490d6c983577ff8ed76a44a81b397955b82143  Syntetika-Engine-0.1.6-arm64.dmg
e88cca792ffbd0bd9e3be9898ca04437fd0e808a415062f78dabb8a43a160ee5  Syntetika-Engine-Setup-0.1.6-x64.exe
```

