# Syntetika Engine 0.1.7 — Windows x64 (Spout) & macOS

Windows kini bisa mengirim gambar ke aplikasi lain lewat **Spout** — penerima seperti
Resolume, OBS (plugin Spout), TouchDesigner, vMix, dan Notch melihat **Syntetika Engine**
sebagai sumber.

## Windows 0.1.7

- **Spout output** — Settings → Output → **Spout output**. Pengirim bernama
  `Syntetika Engine`, ukuran mengikuti komposisi (mis. 1280×720), dan tetap mengirim saat
  window output disembunyikan. Default mati (setiap frame butuh satu salinan GPU→CPU);
  pilihan diingat antar sesi. `OPENVJ_SPOUT=1` menyalakannya untuk run headless.
- Semua yang sudah ada di build Windows tetap: DX12 (fallback Vulkan/OpenGL), audio tap
  master, instalasi self-contained (VC++ & FFmpeg ikut), asosiasi `.syntetika`, menu
  klik-kanan folder `.nota`, dan Nota 0.1.6 (Random pattern per genre, 20 preset genre Drum
  Rack, project satu file).

### Catatan performa

Versi pertama ini membaca balik frame ke CPU lalu mengunggahnya ke tekstur shared DirectX 11
milik Spout. Biayanya beberapa milidetik per frame; di mesin uji (Intel UHD, 1280×720)
pengirim berjalan 60 fps. Jalur GPU-ke-GPU (shared NT handle) menyusul.

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-0.1.7-x64.exe` dan `.sha256`-nya.
2. Verifikasi: `Get-FileHash .\Syntetika-Engine-Setup-0.1.7-x64.exe -Algorithm SHA256`.
3. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
4. SmartScreen: **More info → Run anyway** (installer belum ditandatangani).

Persyaratan: Windows 10/11 x64. Tidak perlu .NET/VC++/FFmpeg terpasang.

## macOS

- **Tempo realtime untuk audio warped** — mengganti BPM tidak lagi membangun ulang cache
  stretch di UI thread. Cache yang masih cocok tempo dipakai; setelah tempo berubah, clip
  warped diputar lewat `ClipWarpStream` live yang membaca tempo tiap block. Ini yang membuat
  BPM drag di project stem tidak freeze lagi. Detail dan batasan ada di `CHANGES.md`.
- Build macOS dibangun dari sumber 0.1.7 yang sama; Spout adalah fitur Windows-only, jadi
  macOS tetap memakai Syphon.
- Install: unduh `Syntetika-Engine-0.1.7-arm64.dmg`, buka, seret **Syntetika Engine** ke
  **Applications**. Build belum dinotarisasi: klik kanan app → **Open**, atau izinkan di
  System Settings → Privacy & Security → **Open Anyway** (sekali). Persyaratan: macOS 12+,
  Apple Silicon.

## Checksums

```
f915885a2ce21f4796f7ca5009282dc163bee37a19180db09159ba0fcb256435  Syntetika-Engine-Setup-0.1.7-x64.exe
b1c10c57a5801e7bc3c6f986221b84cfa1477ac980b02fc605be5c06f2b3c58a  Syntetika-Engine-0.1.7-arm64.dmg
```
