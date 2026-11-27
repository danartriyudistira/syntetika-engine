# 07 — Prioritas build macOS berikutnya

Catatan ini dibuat 26 November 2026, setelah rilis Windows **0.1.17**. Tujuannya: saat build
macOS berikutnya dikerjakan, perbaikan-perbaikan terbaru ini **wajib masuk dan diuji**, bukan
hanya ikut ter-build diam-diam.

## Status singkat

| | |
|---|---|
| Rilis macOS terakhir | **0.1.7** (Apple Silicon, DMG) |
| Rilis Windows terakhir | **0.1.17** |
| Sumber di `main` | sudah memuat perubahan 0.1.8–0.1.13 (engine + managed dipakai bersama) |
| Binary macOS baru | **belum ada** — build macOS berikutnya adalah yang pertama membawa fitur ini |

Artinya: kode macOS untuk item P0 di bawah **sudah ada di `main`**, tetapi belum pernah
dirilis/diuji sebagai aplikasi macOS. Jangan menganggapnya selesai sampai build macOS
menjalankan checklist di bagian akhir.

## P0 — wajib ada dan diverifikasi di build macOS berikutnya

### 1. MIDI To menampilkan port hardware (bukan hanya input yang namanya sama)

- Dulu daftar output hanya berisi port input yang punya nama output identik (bug khas WinMM),
  sehingga synth/port output-only tidak muncul.
- Kode sekarang:
  - `MidiOutput::availablePorts()` di `src/nota/src/native/nota.engine/src/audio_io/macos/MidiOutput.mm`
    (enumerasi `MIDIGetNumberOfDestinations`, uid = `kMIDIPropertyUniqueID`, label =
    `midiEndpointName`).
  - ABI `nota_midi_output_count/uid/name` di `src/abi/nota_engine_audio.cpp` +
    `include/nota/nota_engine.h`.
  - `Engine::midiOutputs()` di `src/engine/Engine.cpp`.
  - `MidiOutputPick.Outputs()` di `src/managed/Nota.App/MidiInputPick.cs` memakai enumerasi
    output lalu menambahkan pasangan input→output sebagai cadangan.
- Yang perlu dicek di macOS:
  - uid CoreMIDI **bukan nama port**; pastikan `MidiOutput::open()` mencocokkan uid yang sama
    dengan yang ditampilkan picker.
  - Uji dengan IAC Driver (buat satu destination) dan/atau synth USB.
  - Port output-only harus muncul tanpa perlu punya input dengan nama sama.

### 2. Clip Session mengirim MIDI ke port eksternal

- Dulu tap MIDI out hanya ada di jalur Arrangement (`renderInstrumentRaw`); clip yang
  diluncurkan dari grid Session memakai `renderSessionSlotRaw` dan tidak mengirim apa pun.
- Kode sekarang: `Engine::sendExternalMidi(...)` dipanggil dari **kedua** jalur di
  `src/nota/src/native/nota.engine/src/engine/Engine_Render.cpp`.
- Yang perlu dicek di macOS:
  - Launch clip Session → note/CC/pitch bend sampai ke IAC/synth.
  - Note-off dan all-notes-off saat Stop/Seek.
  - Track dengan MIDI FX (arp) tetap mengirim note post-FX.

### 3. Route MIDI To dan mode monitor tidak hilang saat track diedit

- Dulu `Engine::cloneTrack` tidak menyalin route MIDI out; setiap edit clip/rekam membuat
  salinan track dan route bisa kosong tanpa terlihat. Mode monitor In/Auto/Off juga hilang.
- Kode sekarang: `cloneTrack` dan `deepCloneTrack` menyalin `midiOutPort`, `midiOutChannel`,
  `midiOutUid`, `monitorAuto`, dan `midiMonitor`
  (`src/engine/Engine.cpp`, `src/engine/Engine_Tracks.cpp`).
- Yang perlu dicek di macOS:
  - Set MIDI To → tambah/ubah clip → route masih terpasang.
  - Duplikat track → route dan mode monitor ikut.
  - Undo/redo setelah edit tidak mengembalikan route ke Off.

### 4. Monitor In / Auto / Off (MIDI dan audio) + persistensi

- Kode: `nota_track_set_midi_monitor`, `nota_track_set_monitor_mode` (ABI), `Track.h`,
  `Engine_Recording.cpp`; UI di `MixerView.cs`, `SessionView.Mixer.cs`, `TrackInspectorView.cs`.
- Persistensi: `MidiMonitor` dan `MonitorMode` di `ProjectModel.cs` /
  `ProjectService.cs`.
- Yang perlu dicek di macOS:
  - Mode In: input live selalu berbunyi tanpa arm.
  - Auto: hanya saat track di-arm (default Ableton).
  - Off: input live tidak berbunyi, clip tetap diputar.
  - Simpan → tutup → buka project: mode kembali sama.

### 5. Blok I/O mixer ala Ableton (MIDI From / Monitor / MIDI To / Audio To)

- Kode: `src/managed/Nota.App/MixerView.cs` (`MidiIoRow`, `AudioIoRow`,
  `DestinationCombo`, `IoBlockH`), `SessionView.Mixer.cs`, `TrackInspectorView.cs`.
- Yang perlu dicek di macOS:
  - Layout strip dengan tinggi I/O seragam; fader tetap sejajar.
  - Mixer dock dan mixer float (jendela terpisah) di layar Retina.
  - Combo panjang (nama port) ter-truncate dengan elipsis, bukan memotong container.
  - Tombol I/O di footer mixer menyembunyikan/menampilkan blok tanpa merusak layout.

## P1 — segera setelah P0

6. **Refresh hot-plug port MIDI.** Picker dibangun saat mixer dibuka; perangkat yang dicolok
   setelahnya belum muncul sampai mixer dibuka ulang. `nota_midi_ports_generation` sudah
   memberi notifikasi CoreMIDI — pertimbangkan auto-refresh combo saat generasi berubah.
7. **Jalankan `--midi-out` di macOS.** Suite yang sama sudah menguji enumerasi output, clip
   Arrangement, dan clip Session. Jalankan juga `--midi-part`. Catatan: gate performa
   (Consort/VST3/render-pool) bisa gagal di laptop lambat dan bukan regresi MIDI.
8. **Paritas output visual.** Windows memakai Spout; macOS memakai Syphon. Pastikan perilaku
   publikasi frame, penamaan output, dan audio tap (`AudioTap.cpp` jalur POSIX `mmap`) tetap
   setara.
9. **Gamepad.** Windows memakai XInput (0.1.9); macOS memakai `GamepadInput.mm` (IOKit).
   Cek pemetaan tombol/axis yang sama.
10. **Audio tap POSIX.** `AudioTap.cpp` sudah punya jalur non-Windows (`open`/`ftruncate`/`mmap`);
    verifikasi `syntetika-visual` membaca tap di macOS (layout header sama dengan Windows).

## Checklist build macOS

- [ ] `Branding.ProductVersion` sudah 0.1.13+ (jangan rilis macOS sebagai 0.1.7).
- [ ] Build native: `MidiOutput.mm` + `MidiInput.mm` ikut ter-compile (CMake `if(APPLE)`).
- [ ] Build managed + bundle: `scripts/bundle-mac.sh`, `scripts/package-dmg.sh`.
- [ ] Uji manual: MIDI To (IAC + hardware), clip Session, edit/duplikat track, monitor mode,
      simpan/buka project, mixer dock/float.
- [ ] Jalankan `--midi-out` dan `--midi-part`.
- [ ] Rilis DMG + `.sha256`, perbarui `docs/download.md`, dan samakan catatan rilis.
- [ ] Setelah rilis: hapus item yang sudah beres dari dokumen ini atau tandai selesai.

## Catatan teknis macOS vs Windows

- **uid output**: CoreMIDI memakai unique id endpoint (string), WinMM/ALSA memakai nama port.
  Jangan menyamakan label dengan uid.
- **Jangan pairing berdasarkan nama** untuk daftar output; enumerasi destination langsung.
- **Session punya clock sendiri**: tap MIDI out harus ada di `renderSessionSlotRaw`, bukan
  hanya di `renderInstrumentRaw`.
- **Clone track**: setiap field routing baru harus ikut disalin di `cloneTrack` **dan**
  `deepCloneTrack`, kalau tidak akan hilang saat clip diedit.
- **CoreMIDI notification** sudah dipakai `MidiInput::portsGeneration()`; gunakan itu untuk
  hot-plug, bukan polling input.

## Riwayat terkait

- **0.1.11** — blok I/O mixer ala Ableton, mode monitor In/Auto/Off.
- **0.1.12** — enumerasi port MIDI output langsung dari backend (MIDI To menemukan hardware).
- **0.1.13** — clip Session mengirim MIDI out; route MIDI To bertahan saat edit/duplikat.
