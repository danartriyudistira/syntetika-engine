# Syntetika Engine 1.0.1 — visual engine on demand (Windows x64)

Rilis patch untuk **Windows x64**. macOS Apple Silicon tetap di **1.0.0**; perubahan yang sama
akan ikut build macOS berikutnya. Semua versi **0.1.x** tetap beta / pre-release.

## Yang berubah

- **Visual engine on demand** — `syntetika-visual` tidak lagi dijalankan saat aplikasi dibuka.
  Engine mulai hanya ketika kamu membuka **View → Visual Output**, **View → OpenVJ**,
  **View → Projection Mapping**, atau tab **OpenVJ**. Sesi audio-only tidak membayar memori,
  CPU, atau konteks GPU visual sama sekali. `SYNTETIKA_VISUAL_ALWAYS=1` mengembalikan perilaku
  selalu-aktif.
- **Prioritas visual** — engine berjalan **BelowNormal** saat tersembunyi dan **Normal** saat ada
  permukaan visual (output, editor, mapping, tab OpenVJ). Audio dan UI tetap **High**, jadi
  render visual tidak lagi berebut CPU dengan audio.
- **Memory guard** — bila engine tersembunyi melewati **1,2 GB** private, komposisi visual
  disimpan lebih dulu lalu proses dihentikan; permintaan visual berikutnya memulai engine baru
  yang segar. `SYNTETIKA_VISUAL_IDLE_LIMIT_MB` menimpa batas itu.
- **Save aman tanpa engine** — menyimpan proyek saat engine visual mati tidak lagi melaporkan
  "visuals were not saved": `visuals/composition.json` di disk adalah versi terakhir.

## Perbaikan

- Jalur on-demand tidak bisa memulai engine karena `VisualEngineProcess.Current` baru diisi
  setelah proses berjalan. Sekarang instance dibuat saat aplikasi start, sehingga Visual
  Output/OpenVJ/mapping/tab OpenVJ selalu bisa menyalakan engine.

## Baseline di laptop referensi

i5-10310U, UHD 620, RAM 16 GB single-channel, 1080p60:

| Kondisi | Private memory engine visual |
|---|---|
| Aplikasi dibuka, belum ada visual | **0 MB** (proses tidak ada) |
| Visual Output dibuka (Eco) | ~280–350 MB, prioritas Normal |
| Visual Output ditutup | ~345 MB, prioritas BelowNormal, render off |
| Sesi panjang yang membesar > 1,2 GB saat tersembunyi | disimpan lalu dihentikan, mulai segar saat diminta |

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-1.0.1-x64.exe`.
2. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
3. SmartScreen: **More info → Run anyway**.

Verifikasi SHA-256 **opsional** (hanya installer yang perlu diunduh):

```powershell
Get-FileHash .\Syntetika-Engine-Setup-1.0.1-x64.exe -Algorithm SHA256
```

```text
15654bcd6fc64ff1156fb2e6905b06da1c967cc99db2b02f0cc97e9042f4f97f  Syntetika-Engine-Setup-1.0.1-x64.exe
```

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.
