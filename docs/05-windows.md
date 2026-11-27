# M2 — Versi Windows (x64)

Status: **jalan** — engine native, aplikasi Nota dan mesin visual openVJ sudah dibangun dan
berjalan di Windows 11 x64. Dokumen ini mencatat apa yang berbeda dari build macOS.

## Hasil build

| Target | Status | Artefak |
|---|---|---|
| Engine Nota (WASAPI/ASIO) | **OK** | `nota_engine.dll`, `nota-scanworker.exe` |
| Aplikasi Nota (.NET 10 + Avalonia) | **OK** | `Nota.App.exe` |
| openVJ (`syntetika-visual`) | **OK** | `syntetika-visual.exe` (DX12) |

## Prasyarat

- Windows 10/11 x64.
- Visual Studio 2022 Build Tools — workload **Desktop C++** (MSVC v143 + Windows SDK).
- CMake 3.24+ dan Ninja (ikut di komponen "C++ CMake tools" VS, atau `winget install Ninja-build.Ninja`).
- .NET SDK 10 — pasang user-local tanpa admin:
  `powershell -File dotnet-install.ps1 -Channel 10.0 -InstallDir $env:USERPROFILE\.dotnet`
  lalu set `DOTNET_ROOT=%USERPROFILE%\.dotnet` dan tambahkan folder itu ke `PATH` (variabel
  user). Tanpa ini, `Nota.App.exe` berhenti dengan "You must install .NET to run this application".
- Rust (toolchain MSVC, `rustup default stable-x86_64-pc-windows-msvc`).
- Node 22 hanya kalau halaman Nota Remote ikut dibangun (skrip build memakai
  `-p:NotaSkipWebBuild=true`).
- Inno Setup 6.3+ hanya untuk membuat installer (`winget install JRSoftware.InnoSetup`).

## Build

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-win.ps1
```

Skrip itu menjalankan configure + build engine native (Ninja), `cargo build --release`
openVJ (fitur `camera,spout`, tanpa Syphon), `dotnet build Nota.sln`, lalu menyalin
`syntetika-visual.exe` ke folder output aplikasi supaya `VisualEngineProcess.FindBinary()`
menemukannya.

Manual, kalau perlu:

```powershell
# dari "Developer PowerShell for VS 2022"
cmake -G Ninja -S src/nota/src/native/nota.engine -B src/nota/src/native/nota.engine/build -DCMAKE_BUILD_TYPE=Release
cmake --build src/nota/src/native/nota.engine/build -j 8

cd src/openvj
cargo build --release -p openvj-app --no-default-features --features camera,spout

cd ../..
dotnet build src/nota/Nota.sln -c Release -p:NotaSkipWebBuild=true
copy src/openvj/target/release/syntetika-visual.exe src/nota/src/managed/Nota.App/bin/Release/net10.0/
```

Jalankan:

```powershell
& src/nota/src/managed/Nota.App/bin/Release/net10.0/Nota.App.exe
```

Nota menyalakan `syntetika-visual.exe` sendiri (auto-start, restart bila crash, dimatikan
saat app keluar) dengan `OPENVJ_NOTA`, `SYNTETIKA_UI_HIDDEN`, `SYNTETIKA_AUDIO_TAP` dan
`SYNTETIKA_EXIT_ON_DISCONNECT` di environment-nya.

## Packaging

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-win-syntetika.ps1 x64
```

Menghasilkan `dist/Syntetika-Engine-Setup-<versi>-x64.exe` (Inno Setup, memakai
`scripts/syntetika.iss`). Tanpa Inno Setup, skrip menulis ZIP portable
`dist/Syntetika-Engine-<versi>-portable-x64.zip`. Publish-nya **self-contained**, jadi PC
tanpa .NET tetap bisa menjalankan app. Paket membawa:

- runtime Visual C++ (app-local, PC bersih tanpa VC++ Redist tetap jalan),
- FFmpeg + FFprobe LGPL (video dan ekstraksi audio klip video),
- `licenses/` (AGPL/GPL, third-party notices, atribusi, tawaran source code).

Installer memasang **per user** tanpa UAC ke `%LOCALAPPDATA%\Programs\Syntetika Engine`
(atau untuk semua user bila dipilih di halaman hak akses). Karena project Nota adalah
*folder* berakhiran `.nota` — dan Windows selalu membuka folder di Explorer — installer
menambahkan menu klik-kanan **"Open with Syntetika Engine"** untuk folder `.nota`, bukan
asosiasi ekstensi. Skrip juga menulis `<installer>.sha256` di `dist/`.

### Code signing (opsional)

Installer tidak ditandatangani secara default. Untuk menandatangani app, engine, mesin
visual, FFmpeg, dan installer sekaligus, set sebelum menjalankan skrip:

```powershell
$env:SYNTETIKA_SIGN_CERT = 'C:\path\sertifikat.pfx'   # atau thumbprint dari cert store
$env:SYNTETIKA_SIGN_PASSWORD = '...'
powershell -ExecutionPolicy Bypass -File scripts/package-win-syntetika.ps1 x64
```

Skrip memakai `signtool.exe` dari Windows SDK dengan timestamp SHA-256. Tanpa sertifikat,
langkah itu dilewati dan SmartScreen tetap menampilkan "Unknown publisher".

## Yang khusus Windows

| Bagian | macOS | Windows |
|---|---|---|
| GPU | Metal | **DX12** (default), Vulkan bisa dipaksa `WGPU_BACKEND=vulkan` |
| Berbagi frame | Syphon | **Spout** (Settings → Output, default mati; nama pengirim "Syntetika Engine") |
| Gamepad | GameController (macOS) | **XInput** (Xbox-compatible; 4 pad) |
| MIDI out ke synth eksternal | MIDI out per track (RtMidi) | sama: track menu → **MIDI out** (port + channel) |
| Audio tap Nota → openVJ | `mmap` POSIX | `CreateFileMapping`/`MapViewOfFile` (`AudioTap.cpp`, `nota_tap.rs`) |
| FFmpeg | dibundel di `Helpers/` | dibundel di folder app (LGPL, `scripts/get-ffmpeg-win.ps1`) |
| Runtime VC++ | ikut sistem | app-local (`Microsoft.VC143.CRT` dari Visual Studio) |
| Project `.nota` | document bundle | folder + menu klik-kanan "Open with Syntetika Engine" |
| Prefs/cache openVJ | `~/Library/.../openVJ` | `%APPDATA%\Syntetika Engine\visual` (profil `openVJ` lama disalin sekali) |
| Instans kedua | `open -b` (fokus) | fokus + project yang diklik ikut dibuka |
| Data pengguna | `~/Library/Application Support/Syntetika Engine` | `%APPDATA%\Syntetika Engine` |
| Log | `.../logs` | `%APPDATA%\Syntetika Engine\logs` |

### MIDI out ke synth eksternal

Track instrument bisa memutar synth hardware/virtual: menu track (atau inspector) → **MIDI out** → pilih port
lalu **Channel** 1–16. Engine mengirim note, CC, pitch bend dan channel pressure dari clip
track itu ke port tersebut; stop/seek mengirim all-notes-off (CC 123/120/64). Pengiriman
berjalan di thread pump sendiri (RtMidi tidak realtime-safe), jadi audio tidak pernah menunggu.
Route disimpan di project (midiOutputUid / midiOutputChannel).

### Kenapa DX12

Driver Vulkan Intel (`igvk64.dll`, versi 31.0.101.2114 di mesin uji) crash `0xc0000005`
saat permukaan window pertama dibuat — proses mati ~4 detik setelah start, tanpa pesan.
Karena itu `init_gpu()` di `crates/openvj-app/src/main.rs` memilih DX12 di Windows bila
`WGPU_BACKEND` tidak diset. Di macOS tidak ada perubahan.

### Audio tap

Nota menulis master output ke `%APPDATA%\Syntetika Engine\audio-tap.shm` (ring 32768 frame
stereo, header 64 byte — layout sama dengan versi POSIX). openVJ memetakan file yang sama
read-only lewat mapping object sendiri, jadi tidak perlu nama shared memory yang dikirim
antar-proses. Dengan ini parameter audio-reactive mengikuti master Nota di Windows.

### Mesin visual yang crash

`VisualEngineProcess` menutup file log sebelum restart, lalu menyalakan ulang engine hingga
3 kali (1,5 detik jeda). Setelah engine hidup 15 detik, kuota restart diisi lagi. Bila
engine mati saat app jalan, log mencatat `visual engine exited; restarting (n/3)` dan
engine baru menyambung lagi ke bridge tanpa perlu restart app.

### Performa di mesin minimum (profil Auto)

Build **0.1.14** menyesuaikan profil visual ke kelas GPU lewat **View → Visual Performance → Auto**
(sekarang default untuk pengaturan baru):

- **iGPU / CPU** → **Eco** (30 fps, preview 15)
- **dGPU** → **Quality** (60/60)
- **macOS** tetap **Quality** (Apple Silicon tidak memakai heuristic iGPU Windows)

Selain profil, dua default dibuat lebih aman untuk laptop kelas minimum:

- **Buffer WASAPI 512 frame** (~10,7 ms pada 48 kHz) bila `audio.json` belum ada. Buffer 0 (device
  default), 128, 256 dan 1024 tetap bisa dipilih dari status bar atau **Settings → Audio**.
- **Render pool memakai physical core**, bukan logical: 4C/8T memakai 3 worker (bukan 7), sehingga
  audio callback, UI dan `syntetika-visual` punya sisa thread.

Panduan untuk laptop 15 W / iGPU / RAM single-channel (mis. i5-10310U + UHD 620):

1. Pakai **Auto** atau **Eco**; Quality 60 fps hanya untuk komposisi ringan.
2. Jika masih ada crackle, naikkan buffer ke **1024** dan jalankan **Preferences → Performance**
   (5 detik; lihat DSP peak dan jumlah dropout).
3. Jangan menyimpan project aktif di folder OneDrive saat sesi; sync dan backup menambah hitch I/O.
4. Matikan **Spout** bila tidak ada penerima (Resolume/OBS); jalur salinan frame GPU→CPU mahal di iGPU.
5. Gunakan **Best Performance** saat di AC, dan jaga ventilasi (i5-10310U cepat throttle).
6. Kecilkan window output visual ke 480p/720p bila output 1080p penuh membuat `drops` naik.

Telemetri: `%APPDATA%\Syntetika Engine\logs\visual-engine.log` menulis `fps`, `max_dt`,
`max_render`, `passes` dan `drops` tiap detik; baris `performance mode auto -> 0` menunjukkan
profil yang dipilih Auto.

## Batasan yang masih ada

- **Syphon** tetap macOS-only; di Windows dipakai **Spout**. Versi pertama memakai salinan GPU→CPU per frame (beberapa ms); jalur GPU-ke-GPU dan NDI menyusul.
- **Ableton Link** tidak dikompilasi di build Windows (fitur `link` mati, tombol LINK nonaktif).
- **Multi-monitor, HiDPI 150–200%, proyek simpan/buka, kamera, ASIO, fullscreen** belum
  melalui QA formal di Windows (uji dilakukan di 1 layar 1920×1080 @100%).
- FPS di mesin uji (Intel UHD, 2 generator 720p) ~15–25 fps; `View → Visual Performance` di
  Nota tetap berlaku.
- Installer belum ditandatangani (SmartScreen "Unknown publisher").
- Updater dimatikan (`Branding.UpdatesEnabled = false`); update dilakukan dengan installer baru.
