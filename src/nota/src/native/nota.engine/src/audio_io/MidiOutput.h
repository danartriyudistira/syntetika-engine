// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI output to one destination (specs/051): control-surface feedback (LEDs, motor faders,
// scribble strips), DAW-mode init SysEx and identity requests. Message thread only — never
// the audio thread. CoreMIDI on macOS, RtMidiOut (WinMM / ALSA) elsewhere. A destination is
// named by the uid MidiInput::PortInfo::outputUid reports.

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace nota {

class MidiOutput {
  public:
    // One destination port as the backend lists it: the uid open() matches
    // (WinMM/ALSA: the port name; CoreMIDI: the endpoint's unique id) and a display name.
    struct PortEntry {
        std::string uid;
        std::string name;
    };

    // Every destination the backend currently exposes — independent of the inputs, so an
    // output-only device (a hardware synth, loopMIDI) is listed even when no input shares its
    // name. Message thread.
    static std::vector<PortEntry> availablePorts();

    MidiOutput();
    ~MidiOutput();
    MidiOutput(const MidiOutput&) = delete;
    MidiOutput& operator=(const MidiOutput&) = delete;

    bool open(const std::string& uid);
    // One or more complete messages; SysEx allowed. False if not open or the send failed.
    bool send(const uint8_t* data, int32_t len);
    void close();

    struct Impl;

  private:
    Impl* impl_ = nullptr;
};

} // namespace nota
