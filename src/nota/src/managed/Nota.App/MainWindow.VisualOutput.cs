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
            _vm.StatusText = Loc.T(_visualOutputShown ? "main.visual_output_shown" : "main.visual_output_hidden");
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
            _vm.StatusText = Loc.T(_openVjShown ? "main.openvj_window_shown" : "main.openvj_window_hidden");
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
            _vm.StatusText = Loc.T(_mappingShown ? "main.mapping_shown" : "main.mapping_hidden");
        if (_mappingShown && !bridge.Connected) VisualEngineProcess.Current?.Start();
    }

    private void SyncMappingMenu()
    {
        if (MenuItemFor("view.mapping") is { } mi) mi.IsChecked = _mappingShown;
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
        }
        SyncVisualOutputMenu();
        SyncOpenVjMenu();
        SyncMappingMenu();

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
    }
}
