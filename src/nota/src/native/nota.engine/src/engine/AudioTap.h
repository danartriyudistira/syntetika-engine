// SPDX-License-Identifier: AGPL-3.0-only
//
// Syntetika Engine — the master output tap.
//
// The visual process analyses the sound Nota performs. The audio thread must never block,
// allocate or take a lock for that, so the master block is copied into a file-backed ring
// buffer and a frame counter is published with release semantics. openVJ maps the same file
// and drains it each frame (openvj-audio/src/tap.rs); the header offsets below are part of
// that interface, keep both sides in step.
//
// Layout (little endian):
//   0   u32 magic "NTA1"
//   4   u32 version
//   8   u32 channels
//   12  u32 ring frames (power of two)
//   16  u32 sample rate (f32 bits, atomic)
//   20  u32 reserved
//   24  u64 write index (monotonic frames, atomic, release)
//   32  u64 dropped frames (reader diagnostics, atomic)
//   40  .. reserved
//   64  interleaved f32 samples, channels × frames
#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <string>

namespace nota {

class AudioTap {
  public:
    static constexpr uint32_t kMagic = 0x3141544E; // "NTA1" little endian
    static constexpr uint32_t kVersion = 1;
    static constexpr uint32_t kChannels = 2;
    static constexpr uint32_t kFrames = 32768; // ~0.7 s at 48 kHz
    static constexpr size_t kRateOffset = 16;
    static constexpr size_t kIndexOffset = 24;
    static constexpr size_t kDroppedOffset = 32;
    static constexpr size_t kDataOffset = 64;

    AudioTap() = default;
    ~AudioTap();
    AudioTap(const AudioTap&) = delete;
    AudioTap& operator=(const AudioTap&) = delete;

    // Creates (or truncates) the file and maps it. Call with the audio thread stopped: the
    // engine opens it from start() before the backend starts and closes it after stop().
    bool open(const std::string& path, double sampleRate);
    void close();
    void setSampleRate(double sampleRate);
    bool isOpen() const { return map_ != nullptr; }

    // Audio thread: copies `frames` interleaved stereo frames of the master output.
    void write(const float* interleaved, int32_t frames) noexcept;

  private:
    int fd_ = -1;
    void* map_ = nullptr;
    size_t mapSize_ = 0;
    std::string path_;
    float* ring_ = nullptr;
    uint64_t written_ = 0;
    std::atomic<bool> enabled_{false};
};

} // namespace nota
