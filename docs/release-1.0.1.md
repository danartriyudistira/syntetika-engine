# Syntetika Engine 1.0.1 — visual engine on demand (Windows x64 + macOS arm64)

Rilis patch untuk **Windows x64** dan **macOS arm64** — keduanya dari sumber yang sama.
Semua versi **0.1.x** tetap beta / pre-release.

> Build ini **menggantikan installer 1.0.1 sebelumnya**: asset di-clobber dan hash di bawah
> adalah yang terbaru. Nomor versi tetap 1.0.1.

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
- **Folder bermerek Syntetika** — default baru `~/Music/Syntetika Samples` dan
  `~/Documents/Syntetika Projects`. Folder `Nota Samples`/`Nota Projects` yang sudah ada
  di-rename sekali saat start, dan daftar recent project ikut diperbarui; kalau rename gagal,
  path lama dipertahankan. Log harian sekarang `syntetika-YYYYMMDD.log`.

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

## macOS

`Syntetika-Engine-1.0.1-arm64.dmg` — macOS 12+, Apple Silicon.

Yang berlaku di macOS:

- **Visual engine on demand** — `syntetika-visual` tidak dijalankan saat app dibuka; baru
  menyala ketika Visual Output, OpenVJ, Projection Mapping, atau tab OpenVJ dibuka.
  Sesi audio-only tidak memakai proses/memori/konteks GPU visual.
  `SYNTETIKA_VISUAL_ALWAYS=1` mengembalikan perilaku selalu-aktif.
- **Memory guard** — engine tersembunyi di atas 1,2 GB private disimpan lalu dihentikan;
  permintaan berikutnya memulai engine baru. `SYNTETIKA_VISUAL_IDLE_LIMIT_MB` menimpa batas.
- **Save aman tanpa engine** — menyimpan project saat engine mati tetap menyimpan
  `visuals/composition.json` tanpa pesan “visuals were not saved”.
- Pengaturan prioritas **BelowNormal/Normal** khusus Windows; di macOS proses visual memakai
  prioritas normal dari OS.

Diverifikasi di macOS: tanpa helper saat launch, dan dengan `SYNTETIKA_VISUAL_ALWAYS=1`
helper menyala serta tersambung ke bridge.

DMG ini **ad-hoc signed, belum notarized**. Saat pertama dibuka: klik kanan app → **Open** →
**Open**, atau System Settings → Privacy & Security → **Open Anyway**. Hanya Apple Silicon;
belum ada build Intel.

## Instalasi Windows

1. Unduh `Syntetika-Engine-Setup-1.0.1-x64.exe`.
2. Jalankan installer (tanpa hak admin), lalu buka **Syntetika Engine** dari Start Menu.
3. SmartScreen: **More info → Run anyway**.

Verifikasi SHA-256 **opsional** (hanya installer yang perlu diunduh):

```powershell
Get-FileHash .\Syntetika-Engine-Setup-1.0.1-x64.exe -Algorithm SHA256
```

Persyaratan: Windows 10/11 x64. Tanpa .NET/VC++/FFmpeg terpasang pun jalan.

## Instalasi macOS

1. Unduh `Syntetika-Engine-1.0.1-arm64.dmg`.
2. (Opsional) Verifikasi:

   ```bash
   shasum -a 256 Syntetika-Engine-1.0.1-arm64.dmg
   ```

3. Buka image, seret **Syntetika Engine** ke **Applications**.
4. Jalankan; bila macOS menolak, pakai langkah Gatekeeper di atas.

## Checksums

```
193338753c29b9c5480d319313fda8b7e2dbf9271aa2cffdab815ffc556a859f  Syntetika-Engine-Setup-1.0.1-x64.exe
18cb1f66094bf4d3d0e255329a492ec7bef0f5a77b3aad11b1086a269a7971df  Syntetika-Engine-1.0.1-arm64.dmg
```
