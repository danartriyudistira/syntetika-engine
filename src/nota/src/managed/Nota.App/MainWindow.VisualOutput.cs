// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// View → Visual Output (Syntetika Engine): opens openVJ's output window on the second screen
// or projector. The window is always on top, so the visuals stay visible above the DAW and
// other apps. View → OpenVJ shows or hides openVJ's editor window; the engine itself runs as a
// background backend by default. Both states come back over the visual bridge (`vizOut`,
// `vizUiState`), which keeps the menu checkmarks right even when a window is closed in openVJ.

using System;
using Avalonia.Threading;
using Nota.Application.Localization;
using Nota.Infrastructure.Visuals;

namespace Nota.App;

public partial class MainWindow
{
    private bool _visualOutputShown;
    private bool _openVjShown;
    private bool _mappingShown;
    private bool _visualOutputAttached;
    /// <summary>The A/V offset dialog, while it is open.</summary>
    private VisualOffsetWindow? _offsetWindow;
    /// <summary>The metronome state before calibration, restored when the dialog closes.</summary>
    private bool _offsetMetronomeWasOn;

    private void ToggleVisualOutput()
    {
        if (VisualBridge.Current is not { } bridge)
        {
            if (_vm is not null) _vm.StatusText = Loc.T("main.visual_output_offline");
            return;
        }
        // Worth opening only when the engine is connected, or when its binary can be started.
        if (!bridge.Connected && VisualEngineProcess.Current is not { IsAvailable: true })
        {
            if (_vm is not null) _vm.StatusText = Loc.T("main.visual_output_offline");
            return;
        }
        _visualOutputShown = !_visualOutputShown;
        bridge.VizOutput(_visualOutputShown);
        SyncVisualOutputMenu();
        if (_vm is not null)
            _vm.StatusText = _visualOutputShown
                ? Loc.T("main.visual_output_shown")
                : Loc.T("main.visual_output_hidden");
        // A click before the auto-started engine has connected must not be lost: the bridge
        // replays the last request once the connection is up.
        if (_visualOutputShown && !bridge.Connected) VisualEngineProcess.Current?.Start();
    }

    private void ToggleOpenVjWindow()
    {
        if (VisualBridge.Current is not { } bridge)
        {
            if (_vm is not null) _vm.StatusText = Loc.T("main.visual_output_offline");
            return;
        }
        if (!bridge.Connected && VisualEngineProcess.Current is not { IsAvailable: true })
        {
            if (_vm is not null) _vm.StatusText = Loc.T("main.visual_output_offline");
            return;
        }
        _openVjShown = !_openVjShown;
        bridge.VizUi(_openVjShown);
        SyncOpenVjMenu();
        if (_vm is not null)
            _vm.StatusText = _openVjShown
                ? Loc.T("main.openvj_window_shown")
                : Loc.T("main.openvj_window_hidden");
        if (_openVjShown && !bridge.Connected) VisualEngineProcess.Current?.Start();
    }

    private void SyncVisualOutputMenu()
    {
        if (MenuItemFor("view.visualOutput") is { } mi) mi.IsChecked = _visualOutputShown;
    }

    private void SyncOpenVjMenu()
    {
        if (MenuItemFor("view.openvj") is { } mi) mi.IsChecked = _openVjShown;
    }

    private void ToggleMapping()
    {
        if (VisualBridge.Current is not { } bridge)
        {
            if (_vm is not null) _vm.StatusText = Loc.T("main.visual_output_offline");
            return;
        }
        if (!bridge.Connected && VisualEngineProcess.Current is not { IsAvailable: true })
        {
            if (_vm is not null) _vm.StatusText = Loc.T("main.visual_output_offline");
            return;
        }
        _mappingShown = !_mappingShown;
        bridge.VizMapping(_mappingShown);
        SyncMappingMenu();
        if (_vm is not null)
            _vm.StatusText = _mappingShown
                ? Loc.T("main.mapping_shown")
                : Loc.T("main.mapping_hidden");
        if (_mappingShown && !bridge.Connected) VisualEngineProcess.Current?.Start();
    }

    private void SyncMappingMenu()
    {
        if (MenuItemFor("view.mapping") is { } mi) mi.IsChecked = _mappingShown;
    }

    /// <summary>
    /// View → A/V Offset: a slider for the picture/sound offset plus a calibration strobe.
    /// Opening it turns the metronome on and opens the output window (the strobe is drawn there);
    /// closing it puts everything back.
    /// </summary>
    private void ShowAvOffset()
    {
        if (_vm is null) return;
        if (VisualBridge.Current is not { } bridge)
        {
            _vm.StatusText = Loc.T("main.visual_output_offline");
            return;
        }
        if (_offsetWindow is { } open)
        {
            open.Activate();
            return;
        }

        int ms = Math.Clamp(_vm.Settings.Current.VisualAvOffsetMs, -200, 200);
        if (!_visualOutputShown) ToggleVisualOutput();
        _offsetMetronomeWasOn = _vm.Transport.MetronomeOn;
        _vm.Transport.MetronomeOn = true;
        bridge.VizOffset(ms);
        bridge.VizCalibrate(true);

        var window = new VisualOffsetWindow(ms);
        window.OffsetChanged += v =>
        {
            _vm.Settings.Current.VisualAvOffsetMs = v;
            _vm.Settings.Save();
            bridge.VizOffset(v);
            _vm.StatusText = Loc.F("visual.av_offset_status", v);
        };
        window.CalibrationToggled += on => bridge.VizCalibrate(on);
        window.Closed += (_, _) =>
        {
            bridge.VizCalibrate(false);
            _vm.Transport.MetronomeOn = _offsetMetronomeWasOn;
            _vm.Settings.Save();
            _offsetWindow = null;
        };
        _offsetWindow = window;
        window.Show(this);
    }

    private void SetVisualPerformance(int mode)
    {
        mode = Math.Clamp(mode, 0, 2);
        if (_vm is not null)
        {
            _vm.Settings.Current.VisualPerformanceMode = mode;
            _vm.Settings.Save();
            _vm.StatusText = mode switch
            {
                0 => Loc.T("main.visual_eco"),
                1 => Loc.T("main.visual_balanced"),
                _ => Loc.T("main.visual_quality"),
            };
        }
        VisualBridge.Current?.VizPerf(mode);
        SyncVisualPerformanceMenu();
    }

    private void SyncVisualPerformanceMenu()
    {
        int mode = _vm?.Settings.Current.VisualPerformanceMode ?? 2;
        if (MenuItemFor("view.visualEco") is { } eco) eco.IsChecked = mode == 0;
        if (MenuItemFor("view.visualBalanced") is { } balanced) balanced.IsChecked = mode == 1;
        if (MenuItemFor("view.visualQuality") is { } quality) quality.IsChecked = mode == 2;
    }

    /// <summary>After a track reorder/add/remove, point the OpenVJ tab at the same slot's new layer.</summary>
    private void RefreshVisualTab()
    {
        if (_visualView is null || _session is null) return;
        var slot = _session.SelectedSlot;
        if (slot.TrackId <= 0 || slot.Scene < 0) return;
        int layer = _session.VisualLayerFor(slot.TrackId);
        if (layer != _visualView.Layer || slot.Scene != _visualView.Col)
            _visualView.SetTarget(layer, slot.Scene);
        else
            _visualView.Reload();
    }

    /// <summary>Wires the menu checkmarks to the engine's reports and applies the test hooks.</summary>
    private void AttachVisualOutputState()
    {
        if (_visualOutputAttached) return;
        _visualOutputAttached = true;
        if (VisualBridge.Current is { } bridge)
        {
            _visualOutputShown = bridge.OutputOpen;
            _openVjShown = bridge.UiOpen;
            _mappingShown = bridge.MappingOpen;
            bridge.OutputOpenChanged += open => Dispatcher.UIThread.Post(() =>
            {
                _visualOutputShown = open;
                SyncVisualOutputMenu();
            });
            bridge.UiOpenChanged += open => Dispatcher.UIThread.Post(() =>
            {
                _openVjShown = open;
                SyncOpenVjMenu();
            });
            bridge.MappingOpenChanged += open => Dispatcher.UIThread.Post(() =>
            {
                _mappingShown = open;
                SyncMappingMenu();
            });
            bridge.SlotAudioRequested += (layer, scene, path) =>
                Dispatcher.UIThread.Post(() => _ = PairSlotAudioAsync(layer, scene, path));
            bridge.LayoutChanged += () => Dispatcher.UIThread.Post(RefreshVisualTab);
            bridge.SceneLaunchRequested += scene => Dispatcher.UIThread.Post(() =>
            {
                bool launched = _session?.LaunchSceneFromOpenVJ(scene) == true;
                if (_vm is not null)
                    _vm.StatusText = launched
                        ? Loc.T("session.scene_launched_from_openvj")
                        : Loc.T("session.the_scene_is_not_in_this_deck");
            });
            bridge.DeckSwitchRequested += index => Dispatcher.UIThread.Post(() =>
            {
                if (_session?.SwitchDeckFromOpenVJ(index) == true)
                    VisualBridge.Current?.DeckSwitchedFromOpenVJ();
            });
            bridge.SlotLaunchRequested += (layer, scene) => Dispatcher.UIThread.Post(() =>
            {
                bool audio = _session?.LaunchSlotFromVisual(layer, scene) == true;
                if (_vm is not null)
                    _vm.StatusText = audio
                        ? Loc.T("session.launched_from_openvj")
                        : Loc.T("session.visual_only_no_audio");
            });
        }
        SyncVisualOutputMenu();
        SyncOpenVjMenu();
        SyncMappingMenu();
        SyncVisualPerformanceMenu();
        VisualBridge.Current?.VizPerf(_vm?.Settings.Current.VisualPerformanceMode ?? 2);
        VisualBridge.Current?.VizOffset(_vm?.Settings.Current.VisualAvOffsetMs ?? 0);

        if (Environment.GetEnvironmentVariable("NOTA_VISUAL_OUTPUT_TEST") is { Length: > 0 } mode)
        {
            DispatcherTimer.RunOnce(ToggleVisualOutput, TimeSpan.FromSeconds(6));
            if (mode != "1")
                DispatcherTimer.RunOnce(ToggleVisualOutput, TimeSpan.FromSeconds(9));
        }
        if (Environment.GetEnvironmentVariable("NOTA_OPENVJ_UI_TEST") is { Length: > 0 } uiMode)
        {
            DispatcherTimer.RunOnce(ToggleOpenVjWindow, TimeSpan.FromSeconds(6));
            if (uiMode != "1")
                DispatcherTimer.RunOnce(ToggleOpenVjWindow, TimeSpan.FromSeconds(9));
        }
        if (Environment.GetEnvironmentVariable("NOTA_MAPPING_TEST") is { Length: > 0 } mappingMode)
        {
            DispatcherTimer.RunOnce(ToggleMapping, TimeSpan.FromSeconds(6));
            if (mappingMode != "1")
                DispatcherTimer.RunOnce(ToggleMapping, TimeSpan.FromSeconds(9));
        }
        if (Environment.GetEnvironmentVariable("NOTA_AV_CAL_TEST") is { Length: > 0 })
            DispatcherTimer.RunOnce(ShowAvOffset, TimeSpan.FromSeconds(16));
        if (Environment.GetEnvironmentVariable("NOTA_VISUAL_UNDO_TEST") is { Length: > 0 })
        {
            // The viz test saves around 15-18 s; its successful save must clear the history.
            DispatcherTimer.RunOnce(
                () => System.Console.Error.WriteLine(
                    $"[undo-test] after save canUndo={VisualBridge.Current?.CanUndo} canRedo={VisualBridge.Current?.CanRedo}"),
                TimeSpan.FromSeconds(21));
            // A new edit after the save must be undoable again.
            DispatcherTimer.RunOnce(
                () => VisualBridge.Current?.VizLayer(0, 0.6f, null, null), TimeSpan.FromSeconds(22));
            DispatcherTimer.RunOnce(() => VisualBridge.Current?.VisualUndo(), TimeSpan.FromSeconds(24));
            DispatcherTimer.RunOnce(
                () => System.Console.Error.WriteLine(
                    $"[undo-test] after undo canUndo={VisualBridge.Current?.CanUndo} canRedo={VisualBridge.Current?.CanRedo}"),
                TimeSpan.FromSeconds(25));
            DispatcherTimer.RunOnce(() => VisualBridge.Current?.VisualRedo(), TimeSpan.FromSeconds(27));
            DispatcherTimer.RunOnce(
                () => System.Console.Error.WriteLine(
                    $"[undo-test] after redo canUndo={VisualBridge.Current?.CanUndo} canRedo={VisualBridge.Current?.CanRedo}"),
                TimeSpan.FromSeconds(28));
        }
    }
}
