// SPDX-License-Identifier: AGPL-3.0-only
#include "engine/AudioTap.h"

#include <algorithm>
#include <cstring>

#if defined(_WIN32)
// Windows: a file-backed mapping created by Nota and opened read-only by the visual process
// (openvj-app/src/nota_tap.rs maps the same file through its own mapping object). The header
// layout is identical to the POSIX path.
// (the build defines WIN32_LEAN_AND_MEAN already)
#include <windows.h>

#include <atomic>

namespace nota {
namespace {

constexpr size_t kSamples = static_cast<size_t>(AudioTap::kChannels) * AudioTap::kFrames;

std::wstring widen(const std::string& utf8) {
    if (utf8.empty()) return {};
    int n = ::MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
    std::wstring out(static_cast<size_t>(n), L'\0');
    ::MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), out.data(), n);
    return out;
}

uint64_t* index_at(void* base) {
    return reinterpret_cast<uint64_t*>(static_cast<char*>(base) + AudioTap::kIndexOffset);
}
uint32_t* rate_at(void* base) {
    return reinterpret_cast<uint32_t*>(static_cast<char*>(base) + AudioTap::kRateOffset);
}
uint64_t* dropped_at(void* base) {
    return reinterpret_cast<uint64_t*>(static_cast<char*>(base) + AudioTap::kDroppedOffset);
}
void publish_index(void* base, uint64_t frames) {
    std::atomic_ref<uint64_t>(*index_at(base)).store(frames, std::memory_order_release);
}
void publish_rate(void* base, uint32_t bits) {
    std::atomic_ref<uint32_t>(*rate_at(base)).store(bits, std::memory_order_release);
}

} // namespace

AudioTap::~AudioTap() { close(); }

bool AudioTap::open(const std::string& path, double sampleRate) {
    close();
    if (path.empty()) return false;
    mapSize_ = kDataOffset + kSamples * sizeof(float);
    std::wstring wide = widen(path);
    if (wide.empty()) return false;
    HANDLE file = ::CreateFileW(wide.c_str(), GENERIC_READ | GENERIC_WRITE,
                                FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, CREATE_ALWAYS,
                                FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    HANDLE mapping =
        ::CreateFileMappingW(file, nullptr, PAGE_READWRITE, 0, static_cast<DWORD>(mapSize_), nullptr);
    if (mapping == nullptr) {
        ::CloseHandle(file);
        return false;
    }
    void* map = ::MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, mapSize_);
    ::CloseHandle(mapping); // the view keeps the section alive
    if (map == nullptr) {
        ::CloseHandle(file);
        return false;
    }
    file_ = file;
    map_ = map;
    ring_ = reinterpret_cast<float*>(static_cast<char*>(map_) + kDataOffset);
    std::memset(map_, 0, mapSize_);
    auto put32 = [this](size_t off, uint32_t v) { std::memcpy(static_cast<char*>(map_) + off, &v, sizeof v); };
    put32(0, kMagic);
    put32(4, kVersion);
    put32(8, kChannels);
    put32(12, kFrames);
    setSampleRate(sampleRate);
    written_ = 0;
    publish_index(map_, 0);
    std::atomic_ref<uint64_t>(*dropped_at(map_)).store(0, std::memory_order_release);
    path_ = path;
    enabled_.store(true, std::memory_order_release);
    return true;
}

void AudioTap::close() {
    enabled_.store(false, std::memory_order_release);
    if (map_ != nullptr) {
        ::UnmapViewOfFile(map_);
        map_ = nullptr;
        ring_ = nullptr;
    }
    if (file_ != nullptr) {
        ::CloseHandle(static_cast<HANDLE>(file_));
        file_ = nullptr;
    }
    mapSize_ = 0;
    written_ = 0;
}

void AudioTap::setSampleRate(double sampleRate) {
    if (map_ == nullptr) return;
    const float hz = static_cast<float>(sampleRate > 0.0 ? sampleRate : 48000.0);
    uint32_t bits = 0;
    std::memcpy(&bits, &hz, sizeof bits);
    publish_rate(map_, bits);
}

void AudioTap::write(const float* interleaved, int32_t frames) noexcept {
    if (!enabled_.load(std::memory_order_relaxed) || ring_ == nullptr) return;
    if (interleaved == nullptr || frames <= 0) return;
    const size_t n = static_cast<size_t>(frames) * kChannels;
    const size_t taken = n > kSamples ? kSamples : n;
    size_t pos = static_cast<size_t>(written_ % kFrames) * kChannels;
    const size_t first = std::min(taken, kSamples - pos);
    std::memcpy(ring_ + pos, interleaved, first * sizeof(float));
    if (taken > first) std::memcpy(ring_, interleaved + first, (taken - first) * sizeof(float));
    written_ += taken / kChannels;
    publish_index(map_, written_);
}

} // namespace nota
#else
#include <fcntl.h>
#include <sys/mman.h>
#include <unistd.h>

namespace nota {
namespace {

constexpr size_t kSamples = static_cast<size_t>(AudioTap::kChannels) * AudioTap::kFrames;

uint64_t* index_at(void* base) {
    return reinterpret_cast<uint64_t*>(static_cast<char*>(base) + AudioTap::kIndexOffset);
}
uint32_t* rate_at(void* base) {
    return reinterpret_cast<uint32_t*>(static_cast<char*>(base) + AudioTap::kRateOffset);
}
void publish_index(void* base, uint64_t frames) {
    __atomic_store_n(index_at(base), frames, __ATOMIC_RELEASE);
}
void publish_rate(void* base, uint32_t bits) {
    __atomic_store_n(rate_at(base), bits, __ATOMIC_RELEASE);
}

} // namespace

AudioTap::~AudioTap() { close(); }

bool AudioTap::open(const std::string& path, double sampleRate) {
    close();
    if (path.empty()) return false;
    mapSize_ = kDataOffset + kSamples * sizeof(float);
    fd_ = ::open(path.c_str(), O_RDWR | O_CREAT | O_TRUNC, 0644);
    if (fd_ < 0) return false;
    if (::ftruncate(fd_, static_cast<off_t>(mapSize_)) != 0) {
        ::close(fd_);
        fd_ = -1;
        return false;
    }
    void* map = ::mmap(nullptr, mapSize_, PROT_READ | PROT_WRITE, MAP_SHARED, fd_, 0);
    if (map == MAP_FAILED) {
        ::close(fd_);
        fd_ = -1;
        return false;
    }
    map_ = map;
    ring_ = reinterpret_cast<float*>(static_cast<char*>(map_) + kDataOffset);
    std::memset(map_, 0, mapSize_);
    // Everything a reader needs before the index moves is in place before enabled_.
    auto put32 = [this](size_t off, uint32_t v) { std::memcpy(static_cast<char*>(map_) + off, &v, sizeof v); };
    put32(0, kMagic);
    put32(4, kVersion);
    put32(8, kChannels);
    put32(12, kFrames);
    setSampleRate(sampleRate);
    written_ = 0;
    publish_index(map_, 0);
    __atomic_store_n(reinterpret_cast<uint64_t*>(static_cast<char*>(map_) + kDroppedOffset), 0, __ATOMIC_RELEASE);
    path_ = path;
    enabled_.store(true, std::memory_order_release);
    return true;
}

void AudioTap::close() {
    enabled_.store(false, std::memory_order_release);
    if (map_ != nullptr) {
        ::munmap(map_, mapSize_);
        map_ = nullptr;
        ring_ = nullptr;
    }
    if (fd_ >= 0) {
        ::close(fd_);
        fd_ = -1;
    }
    mapSize_ = 0;
    written_ = 0;
}

void AudioTap::setSampleRate(double sampleRate) {
    if (map_ == nullptr) return;
    const float hz = static_cast<float>(sampleRate > 0.0 ? sampleRate : 48000.0);
    uint32_t bits = 0;
    std::memcpy(&bits, &hz, sizeof bits);
    publish_rate(map_, bits);
}

void AudioTap::write(const float* interleaved, int32_t frames) noexcept {
    if (!enabled_.load(std::memory_order_relaxed) || ring_ == nullptr) return;
    if (interleaved == nullptr || frames <= 0) return;
    const size_t n = static_cast<size_t>(frames) * kChannels;
    const size_t taken = n > kSamples ? kSamples : n;
    size_t pos = static_cast<size_t>(written_ % kFrames) * kChannels;
    const size_t first = std::min(taken, kSamples - pos);
    std::memcpy(ring_ + pos, interleaved, first * sizeof(float));
    if (taken > first) std::memcpy(ring_, interleaved + first, (taken - first) * sizeof(float));
    written_ += taken / kChannels;
    publish_index(map_, written_);
}

} // namespace nota
#endif
