// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Graph.h"

#include <algorithm>

namespace nota {

void inheritGraphLevel(Graph& dst, const Graph& src) {
    dst.tempoMap = src.tempoMap;
    dst.sceneCount = src.sceneCount;
    dst.scenes = src.scenes;
    dst.decks = src.decks;
    dst.masterVolume = src.masterVolume;
    dst.masterTrack = src.masterTrack;
    dst.panLaw = src.panLaw;
    dst.sections = src.sections;
}

std::string defaultDeckName(int32_t index) {
    return "Deck " + std::to_string(index + 1);
}

int32_t graphDeckCount(const Graph& g) {
    return g.decks.empty() ? 1 : static_cast<int32_t>(g.decks.size());
}

int32_t graphDeckSceneCount(const Graph& g, int32_t deck) {
    const int32_t n = std::max(0, g.sceneCount);
    if (g.decks.empty()) return deck == 0 ? n : 0;
    if (deck < 0 || deck >= static_cast<int32_t>(g.decks.size())) return 0;
    return std::clamp(g.decks[deck].sceneCount, 0, n);
}

int32_t graphDeckSceneStart(const Graph& g, int32_t deck) {
    if (g.decks.empty()) return deck == 0 ? 0 : g.sceneCount;
    if (deck <= 0) return 0;
    int32_t start = 0;
    const int32_t last = std::min(deck, static_cast<int32_t>(g.decks.size()));
    for (int32_t i = 0; i < last; ++i) start += graphDeckSceneCount(g, i);
    return std::min(start, std::max(0, g.sceneCount));
}

std::string graphDeckName(const Graph& g, int32_t deck) {
    if (deck < 0 || deck >= graphDeckCount(g)) return {};
    if (g.decks.empty()) return defaultDeckName(0);
    const auto& name = g.decks[deck].name;
    return name.empty() ? defaultDeckName(deck) : name;
}

int32_t graphDeckOfScene(const Graph& g, int32_t scene) {
    if (g.decks.empty() || scene < 0) return 0;
    int32_t end = 0;
    for (size_t i = 0; i < g.decks.size(); ++i) {
        end += std::max(0, g.decks[i].sceneCount);
        if (scene < end) return static_cast<int32_t>(i);
    }
    return static_cast<int32_t>(g.decks.size()) - 1;
}

bool graphDeckRange(const Graph& g, int32_t scene, int32_t* start, int32_t* count) {
    if (scene < 0 || scene >= g.sceneCount) return false;
    const int32_t deck = graphDeckOfScene(g, scene);
    const int32_t n = graphDeckSceneCount(g, deck);
    if (n <= 0) return false;
    if (start) *start = graphDeckSceneStart(g, deck);
    if (count) *count = n;
    return true;
}

void ensureGraphDecks(Graph& g) {
    const int32_t scenes = std::max(0, g.sceneCount);
    if (g.decks.empty()) {
        DeckInfo deck;
        deck.name = defaultDeckName(0);
        deck.sceneCount = scenes;
        g.decks.push_back(std::move(deck));
        return;
    }
    int32_t sum = 0;
    for (auto& d : g.decks) {
        d.sceneCount = std::max(0, d.sceneCount);
        sum += d.sceneCount;
    }
    // A damaged or hand-edited file: keep the known decks and let the last one absorb the
    // difference (or shrink), so the ranges always tile sceneCount exactly.
    if (sum != scenes) {
        auto& last = g.decks.back();
        last.sceneCount = std::max(0, last.sceneCount + (scenes - sum));
    }
    if (g.decks.empty()) {
        DeckInfo deck;
        deck.name = defaultDeckName(0);
        deck.sceneCount = scenes;
        g.decks.push_back(std::move(deck));
    }
}

} // namespace nota
