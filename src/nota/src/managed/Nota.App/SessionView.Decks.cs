// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view — the deck bar. A deck is Nota's own concept: a page of scene rows that
// shares the tracks, instruments, devices and mixer. openVJ mirrors it through the bridge
// (visual decks are storage, not a UI Nota shows). Switching keeps playing clips alive.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Localization;
using Nota.Infrastructure.Visuals;

namespace Nota.App;

public sealed partial class SessionView
{
    /// <summary>The row is in the open deck; cells outside it stay built (engine indices are
    /// global) but hidden, so every per-scene cache keeps its index.</summary>
    private bool InDeck(int scene) => scene >= _deckStart && scene < _deckEnd;

    private Control BuildDeckBar()
    {
        var tabs = new ScrollViewer
        {
            Content = _deckTabs,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _deckBar.Children.Add(tabs);

        var add = new Button
        {
            Content = Loc.T("session.deck.new"),
            Classes = { "ghost" },
            FontSize = 10,
            Padding = new Thickness(12, 4),
        };
        ToolTip.SetTip(add, Loc.T("session.deck.add_hint"));
        add.Click += (_, _) => AddNewDeck();
        Grid.SetColumn(add, 1);
        _deckBar.Children.Add(add);

        return new Border
        {
            Child = _deckBar,
            Padding = new Thickness(0, 6, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = NotaPalette.BorderDefault,
        };
    }

    private void RefreshDeckBar()
    {
        _deckTabs.Children.Clear();
        int current = Math.Clamp(_engine.CurrentDeck, 0, Math.Max(0, _engine.DeckCount - 1));
        for (int d = 0; d < _engine.DeckCount; d++)
        {
            int index = d;
            string name = _engine.GetDeckName(d);
            var tab = new Button
            {
                Content = name,
                Classes = { "ghost" },
                FontSize = 10,
                Padding = new Thickness(12, 4),
                MinWidth = 54,
                FontWeight = d == current ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = d == current ? NotaPalette.TextPrimary : NotaPalette.TextSecondary,
            };
            ToolTip.SetTip(tab, name);
            tab.Click += (_, _) => SwitchDeck(index);

            var menu = new ContextMenu();
            var rename = new MenuItem { Header = Loc.T("common.rename") };
            rename.Click += (_, _) => RenameDeck(index);
            var duplicate = new MenuItem { Header = Loc.T("common.duplicate") };
            duplicate.Click += (_, _) => DuplicateDeck(index);
            var remove = new MenuItem { Header = Loc.T("common.delete") };
            remove.Click += (_, _) => RemoveDeck(index);
            menu.Items.Add(rename);
            menu.Items.Add(duplicate);
            menu.Items.Add(remove);
            tab.ContextMenu = menu;

            _deckTabs.Children.Add(tab);
        }
    }

    /// <summary>openVJ's deck tab was clicked: switch the Session deck without echoing the
    /// command back (the visual engine already switched).</summary>
    internal bool SwitchDeckFromOpenVJ(int index)
    {
        if (index < 0 || index >= _engine.DeckCount || index == _engine.CurrentDeck) return false;
        _engine.SwitchDeck(index);
        Refresh();
        return true;
    }

    private void SwitchDeck(int index)
    {
        if (index == _engine.CurrentDeck) return;
        _engine.SwitchDeck(index);
        VisualBridge.Current?.SwitchDeck(index);
        Refresh();
    }

    private void AddNewDeck()
    {
        int index = _engine.AddDeck();
        if (index < 0) return;
        VisualBridge.Current?.AddDeck(index);
        VisualBridge.Current?.SyncDecks();
        Refresh();
        Say(Loc.T("session.deck.added"));
    }

    private void DuplicateDeck(int index)
    {
        int copy = _engine.DuplicateDeck(index);
        if (copy < 0) return;
        VisualBridge.Current?.DuplicateDeck(index);
        VisualBridge.Current?.SyncDecks();
        Refresh();
        Say(Loc.F("session.deck.duplicated", _engine.GetDeckName(copy)));
    }

    private void RemoveDeck(int index)
    {
        if (_engine.DeckCount <= 1) { Say(Loc.T("session.deck.last")); return; }
        int start = _engine.DeckSceneStart(index), count = _engine.DeckSceneCount(index);
        // The removed deck's picture goes with it; other decks keep playing. (Check before the
        // removal: the rows after it shift down.)
        var sounding = new List<int>();
        for (int i = 0; i < _engine.TrackCount; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
            for (int s = start; s < start + count; s++)
                if (_engine.SessionSlotState(ti.Id, s) is 2 or 3) { sounding.Add(ti.Id); break; }
        }
        if (!_engine.RemoveDeck(index)) { Say(Loc.T("session.deck.recording")); return; }
        foreach (int trackId in sounding) VisualBridge.Current?.StopTrack(trackId);
        VisualBridge.Current?.RemoveDeck(index);
        VisualBridge.Current?.SyncDecks();
        Refresh();
        Say(Loc.T("session.deck.removed"));
    }

    private async void RenameDeck(int index)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        string? name = await new TextPromptWindow(Loc.T("common.rename"), Loc.T("session.deck.name"),
                _engine.GetDeckName(index))
            .ShowDialog<string?>(owner);
        if (name is null || name.Trim().Length == 0) return;
        _engine.RenameDeck(index, name.Trim());
        VisualBridge.Current?.RenameDeck(index, name.Trim());
        Refresh();
    }
}
