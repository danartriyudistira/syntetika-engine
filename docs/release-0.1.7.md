# Syntetika Engine 0.1.7 — Windows x64 (Spout)

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

Build macOS terbaru tetap **0.1.6** (`Syntetika-Engine-0.1.6-arm64.dmg`); rilis ini berisi
aset Windows. Syphon tetap menjadi jalur berbagi frame di macOS.

## Checksums

```
e88cca792ffbd0bd9e3be9898ca04437fd0e808a415062f78dabb8a43a160ee5  Syntetika-Engine-Setup-0.1.6-x64.exe (build sebelumnya)
f915885a2ce21f4796f7ca5009282dc163bee37a19180db09159ba0fcb256435  Syntetika-Engine-Setup-0.1.7-x64.exe
bc16054b56441ba80edd52855ee4b985f535014dcbb7cb17b872a5c375b567c8  Syntetika-Engine-0.1.6-arm64.dmg
```
