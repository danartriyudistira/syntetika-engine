# QA Windows — daftar periksa sebelum rilis

Dipakai di mesin bersih (tanpa .NET, tanpa Visual C++ Redistributable, tanpa Visual Studio)
dan di mesin pengembang. Tandai tiap baris: **OK / GAGAL / TIDAK DIUJI**, catat versi
driver GPU.

## 1. Instalasi bersih

- [ ] Windows 10/11 x64, akun tanpa hak admin.
- [ ] Installer `Syntetika-Engine-Setup-<versi>-x64.exe` berjalan tanpa UAC.
- [ ] Terpasang di `%LOCALAPPDATA%\Programs\Syntetika Engine`; shortcut Start Menu + desktop ada.
- [ ] `%APPDATA%\Syntetika Engine\` dibuat saat pertama jalan; tidak menyentuh folder Nota asli.
- [ ] Uninstall menghapus folder aplikasi; project & settings pengguna tetap ada.
- [ ] Install ulang (upgrade) di atas versi lama berhasil, project tetap terbuka.
- [ ] SmartScreen: catat apakah muncul "Unknown publisher" (diharapkan selama belum ditandatangani).

## 2. Start & engine

- [ ] App terbuka < 10 detik; welcome screen muncul.
- [ ] `syntetika-visual.exe` muncul di Task Manager setelah app jalan.
- [ ] Kill `syntetika-visual.exe` → hidup lagi dalam ~2 detik; status tidak "offline".
- [ ] Tutup app → proses visual ikut berhenti (tidak ada proses menggantung).
- [ ] Log ada di `%APPDATA%\Syntetika Engine\logs` (`nota-*.log`, `visual-engine.log`).

## 3. Audio

- [ ] WASAPI: pilih device, mainkan clip, meter bergerak, tidak ada dropout.
- [ ] ASIO (bila ada driver): ganti audio system, sample rate benar.
- [ ] MIDI: controller terdeteksi; knob/fader menggerakkan parameter.
- [ ] Export audio (bila dipakai di rilis ini).

## 4. Visual

- [ ] Buka `demo/` (atau project berisi visual), launch scene → visual muncul di beat yang sama.
- [ ] **View → Visual Output**: window always-on-top, 854×480 windowed.
- [ ] Fullscreen (maximize / F) dan Esc kembali ke windowed.
- [ ] Ganti clip → visual berganti; pilih clip → tab OpenVJ menampilkan preview-nya.
- [ ] Audio-reactive: parameter yang di-map bergerak mengikuti master (bukan input device).
- [ ] Video: drop klip video → gambar tampil dan suaranya masuk slot (butuh FFmpeg bawaan).
- [ ] Kamera: pilih kamera di browser media → preview hidup.
- [ ] Ganti ukuran window output saat show → tidak crash, tidak freeze.

## 5. Dua monitor & proyektor

- [ ] Output window dipindah ke monitor kedua, always-on-top tetap berlaku.
- [ ] Projection Mapping: buat slice, warp, mask; hasil di output sesuai.
- [ ] Fullscreen di monitor kedua tidak menutupi UI utama.
- [ ] Cabut/sambung monitor saat app jalan → tidak crash (catat perilaku).

## 6. HiDPI

- [ ] Skala 125%, 150%, 200%: UI tidak blur, tidak terpotong, tombol title bar tidak menimpa kontrol.
- [ ] Output window tetap 1:1 piksel (tidak ikut di-scale) saat fullscreen.

## 7. Project & media

- [ ] Simpan project baru, tutup app, buka lagi → audio, visual, dan media kembali.
- [ ] Copy/paste clip membawa visual dan medianya.
- [ ] Tambah/hapus track & scene → layer/column visual ikut (layout mirroring).
- [ ] Buka project dari folder `.nota` lewat klik-kanan **"Open with Syntetika Engine"**.
- [ ] Project dengan path berisi spasi dan karakter non-ASCII.

## 8. Stabilitas

- [ ] Jalan 60 menit dengan visual output aktif: RAM stabil (catat awal/akhir), tidak ada crash.
- [ ] Ganti-ganti scene cepat (spam launch) 5 menit: tidak ada freeze, drift launch < 45 ms.
- [ ] Buka/tutup Preferences, Projection Mapping, OpenVJ editor berulang kali.
- [ ] Sleep/resume Windows saat app jalan.

## 9. Performa (catat angkanya)

| Skenario | FPS | GPU | Catatan |
|---|---|---|---|
| 1 layer generator 720p | | | |
| 3 layer generator 720p | | | |
| 3 layer + 2 efek 1080p | | | |
| Output 1080p fullscreen | | | |

Bandingkan dengan `View → Visual Performance` (Eco / Balanced / Quality).
