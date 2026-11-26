// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Windows gamepad input (XInput): the same small surface the macOS GameController build
// exposes. A poll thread reads up to four XInput pads, turns button edges into the shared
// event ring and keeps the latest axis positions; the UI maps edges to notes and axes to
// parameters (see GamepadInput.h).
//
// XInput rather than RawInput on purpose: it decodes Xbox-compatible pads (including the
// 8BitDo pads in XInput mode) without vendor-specific report parsing, and it is silent when
// a pad is idle.

#include "audio_io/GamepadInput.h"

#if defined(_WIN32)

#include <windows.h>

#include <xinput.h>

#include <array>
#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <thread>
#include <vector>

namespace nota {

namespace {

// XInput's button bits (xinput.h names them XINPUT_GAMEPAD_*).
constexpr WORD kA = 0x1000, kB = 0x2000, kX = 0x4000, kY = 0x8000;
constexpr WORD kLb = 0x0100, kRb = 0x0200;
constexpr WORD kUp = 0x0001, kDown = 0x0002, kLeft = 0x0004, kRight = 0x0008;
// A trigger counts as a button once it is pressed this far (of 255), so resting noise and
// the first millimetre of travel do not fire notes.
constexpr BYTE kTriggerDown = 40;
constexpr BYTE kTriggerUp = 20;

} // namespace

struct GamepadInput::Impl {
    static constexpr int kPads = XUSER_MAX_COUNT; // 4
    static constexpr int kAxes = 6;

    struct Pad {
        bool connected = false;
        char uid[32] = {};
        char name[64] = {};
        WORD buttons = 0;
        bool lt = false, rt = false;
        std::array<std::atomic<int>, kAxes> axes{};
    };

    std::array<Pad, kPads> pads;
    std::atomic<int32_t> count{0};
    std::atomic<bool> running{false};
    std::thread thread;

    std::mutex eventsMutex;
    std::vector<ButtonEvent> events;
    std::mutex padsMutex; // uid/name/connected while padInfo runs

    void push(int32_t pad, int32_t button, bool pressed) {
        std::lock_guard<std::mutex> lock(eventsMutex);
        if (events.size() >= 512) return; // a saturated UI tick loses the oldest edges
        events.push_back(ButtonEvent{pressed ? 1 : 0, button, pad});
    }

    void edge(int32_t pad, WORD before, WORD after, WORD bit, int32_t button) {
        const bool was = (before & bit) != 0, now = (after & bit) != 0;
        if (was != now) push(pad, button, now);
    }

    void setAxes(Pad& p, const XINPUT_GAMEPAD& g) {
        // Sticks are bipolar (rest 64), triggers unipolar (rest 0), all 0..127.
        p.axes[0].store(g.sThumbLX < 0 ? 64 + g.sThumbLX / 512 : 64 + g.sThumbLX / 512, std::memory_order_relaxed);
        p.axes[1].store(64 + g.sThumbLY / 512, std::memory_order_relaxed); // up = 127, down = 0
        p.axes[2].store(g.sThumbRX < 0 ? 64 + g.sThumbRX / 512 : 64 + g.sThumbRX / 512, std::memory_order_relaxed);
        p.axes[3].store(64 + g.sThumbRY / 512, std::memory_order_relaxed);
        p.axes[4].store(g.bLeftTrigger >> 1, std::memory_order_relaxed);
        p.axes[5].store(g.bRightTrigger >> 1, std::memory_order_relaxed);
    }

    void poll() {
        int32_t connected = 0;
        for (DWORD i = 0; i < XUSER_MAX_COUNT; ++i) {
            XINPUT_STATE state{};
            const DWORD result = XInputGetState(i, &state);
            Pad& p = pads[i];
            if (result != ERROR_SUCCESS) {
                if (p.connected) {
                    std::lock_guard<std::mutex> lock(padsMutex);
                    p.connected = false;
                    p.buttons = 0;
                    p.lt = p.rt = false;
                }
                continue;
            }
            std::lock_guard<std::mutex> lock(padsMutex);
            if (!p.connected) {
                p.connected = true;
                std::snprintf(p.uid, sizeof p.uid, "xinput:%lu", static_cast<unsigned long>(i));
                std::snprintf(p.name, sizeof p.name, "Xbox Controller %lu", static_cast<unsigned long>(i + 1));
                p.buttons = state.Gamepad.wButtons;
                p.lt = state.Gamepad.bLeftTrigger >= kTriggerDown;
                p.rt = state.Gamepad.bRightTrigger >= kTriggerDown;
                setAxes(p, state.Gamepad);
                ++connected;
                continue;
            }
            ++connected;
            const WORD before = p.buttons, after = state.Gamepad.wButtons;
            p.buttons = after;
            edge(static_cast<int32_t>(i), before, after, kA, 1);
            edge(static_cast<int32_t>(i), before, after, kB, 2);
            edge(static_cast<int32_t>(i), before, after, kX, 3);
            edge(static_cast<int32_t>(i), before, after, kY, 4);
            edge(static_cast<int32_t>(i), before, after, kLb, 5);
            edge(static_cast<int32_t>(i), before, after, kRb, 6);
            edge(static_cast<int32_t>(i), before, after, kUp, GamepadButton::DpadUp);
            edge(static_cast<int32_t>(i), before, after, kDown, GamepadButton::DpadDown);
            edge(static_cast<int32_t>(i), before, after, kLeft, GamepadButton::DpadLeft);
            edge(static_cast<int32_t>(i), before, after, kRight, GamepadButton::DpadRight);
            const bool lt = state.Gamepad.bLeftTrigger >= (p.lt ? kTriggerUp : kTriggerDown);
            const bool rt = state.Gamepad.bRightTrigger >= (p.rt ? kTriggerUp : kTriggerDown);
            if (lt != p.lt) { p.lt = lt; push(static_cast<int32_t>(i), 7, lt); }
            if (rt != p.rt) { p.rt = rt; push(static_cast<int32_t>(i), 8, rt); }
            setAxes(p, state.Gamepad);
        }
        count.store(connected, std::memory_order_relaxed);
    }

    void loop() {
        while (running.load(std::memory_order_relaxed)) {
            poll();
            std::this_thread::sleep_for(std::chrono::milliseconds(4));
        }
    }
};

GamepadInput::GamepadInput() : impl_(new Impl()) {}
GamepadInput::~GamepadInput() {
    stop();
    delete impl_;
}

void GamepadInput::start() {
    if (impl_->running.exchange(true)) return;
    impl_->thread = std::thread([this] { impl_->loop(); });
}

void GamepadInput::stop() {
    if (!impl_->running.exchange(false)) return;
    if (impl_->thread.joinable()) impl_->thread.join();
}

int32_t GamepadInput::padCount() const {
    return impl_->count.load(std::memory_order_relaxed);
}

void GamepadInput::padInfo(int32_t i, const char** outUid, const char** outName) const {
    std::lock_guard<std::mutex> lock(impl_->padsMutex);
    if (i < 0 || i >= Impl::kPads || !impl_->pads[static_cast<size_t>(i)].connected) {
        *outUid = "";
        *outName = "";
        return;
    }
    *outUid = impl_->pads[static_cast<size_t>(i)].uid;
    *outName = impl_->pads[static_cast<size_t>(i)].name;
}

int32_t GamepadInput::pollEvents(ButtonEvent* out, int32_t max) {
    if (!out || max <= 0) return 0;
    std::lock_guard<std::mutex> lock(impl_->eventsMutex);
    const int32_t n = static_cast<int32_t>(std::min<size_t>(impl_->events.size(), static_cast<size_t>(max)));
    std::memcpy(out, impl_->events.data(), static_cast<size_t>(n) * sizeof(ButtonEvent));
    impl_->events.erase(impl_->events.begin(), impl_->events.begin() + n);
    return n;
}

int32_t GamepadInput::axisValues(int32_t i, int32_t* out, int32_t max) const {
    if (!out || max <= 0 || i < 0 || i >= Impl::kPads) return 0;
    const auto& p = impl_->pads[static_cast<size_t>(i)];
    std::lock_guard<std::mutex> lock(impl_->padsMutex);
    if (!p.connected) return 0;
    const int32_t n = std::min<int32_t>(max, Impl::kAxes);
    for (int32_t a = 0; a < n; ++a) out[a] = p.axes[static_cast<size_t>(a)].load(std::memory_order_relaxed);
    return n;
}

} // namespace nota

#endif // _WIN32
