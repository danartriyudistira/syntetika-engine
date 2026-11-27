// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// macOS device enumeration for MidiConfig.h (CoreMIDI). Persistence
// (midi.json) lives in the shared ConfigJson.cpp.

#include "MidiConfig.h"

#include <algorithm>
#include <future>
#include <mutex>
#include <string>
#include <thread>

#if defined(__APPLE__)

#import <CoreMIDI/CoreMIDI.h>
#import <CoreFoundation/CoreFoundation.h>

namespace nota {
namespace {

std::string cfToString(CFStringRef s) {
    if (!s) return {};
    if (const char* fast = CFStringGetCStringPtr(s, kCFStringEncodingUTF8)) return std::string(fast);
    CFIndex len = CFStringGetLength(s);
    CFIndex max = CFStringGetMaximumSizeForEncoding(len, kCFStringEncodingUTF8) + 1;
    std::string out(static_cast<size_t>(max), '\0');
    if (CFStringGetCString(s, out.data(), max, kCFStringEncodingUTF8))
        out.resize(std::char_traits<char>::length(out.c_str()));
    else
        out.clear();
    return out;
}

std::string endpointDisplayName(MIDIEndpointRef ep) {
    CFStringRef name = nullptr;
    if (MIDIObjectGetStringProperty(ep, kMIDIPropertyDisplayName, &name) != noErr || !name) return {};
    std::string out = cfToString(name);
    CFRelease(name);
    return out;
}

} // namespace

std::string midiUidForEndpoint(uint32_t endpoint) {
    SInt32 uid = 0;
    if (MIDIObjectGetIntegerProperty(static_cast<MIDIObjectRef>(endpoint), kMIDIPropertyUniqueID, &uid) != noErr)
        return {};
    return std::to_string(uid);
}

std::string midiEndpointName(uint32_t endpoint) {
    return endpointDisplayName(static_cast<MIDIEndpointRef>(endpoint));
}

void watchMidiPorts() {
    static std::once_flag once;
    std::call_once(once, [] {
        // The block does nothing: receiving the add/remove message is what refreshes
        // CoreMIDI's per-process enumeration. The client must own the run loop that
        // delivers them, so it is created on the watcher thread itself. Wait for the
        // client before returning, so the caller's first enumeration sees a live list.
        std::promise<void> ready;
        std::thread([&ready] {
            MIDIClientRef client = 0;
            const OSStatus status = MIDIClientCreateWithBlock(CFSTR("Nota Port Watch"), &client,
                ^(const MIDINotification*) {});
            ready.set_value();
            if (status != noErr) return;
            CFRunLoopRun(); // never returns; the thread lives as long as the app
        }).detach();
        ready.get_future().wait();
    });
}

std::vector<MidiDeviceInfo> enumerateMidiInputs() {
    watchMidiPorts();
    std::vector<MidiDeviceInfo> out;
    const ItemCount n = MIDIGetNumberOfSources();
    for (ItemCount i = 0; i < n; ++i) {
        MIDIEndpointRef src = MIDIGetSource(i);
        if (!src) continue;
        MidiDeviceInfo info;
        info.uid = midiUidForEndpoint(src);
        info.name = endpointDisplayName(src);
        if (info.uid.empty()) continue; // no stable id -> can't persist a choice
        if (info.name.empty()) info.name = "MIDI input " + info.uid;
        out.push_back(std::move(info));
    }
    return out;
}

} // namespace nota

#endif // defined(__APPLE__)
