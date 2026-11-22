// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// View → Visual Output (Syntetika Engine): opens openVJ's output window on the second screen
// or projector. The window is always on top, so the visuals stay visible above the DAW and
// other apps. The state comes back over the visual bridge (`vizOut`), which keeps the menu
// checkmark right even when the window is closed from openVJ itself.

using System;
using Avalonia.Threading;
using Nota.Application.Localization;
using Nota.Infrastructure.Visuals;

namespace Nota.App;

public partial class MainWindow
{
    private bool _visualOutputShown;
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

    private void SyncVisualOutputMenu()
    {
        if (MenuItemFor("view.visualOutput") is { } mi) mi.IsChecked = _visualOutputShown;
    }

    /// <summary>Wires the menu checkmark to the engine's reports and applies the test hook.</summary>
    private void AttachVisualOutputState()
    {
        if (_visualOutputAttached) return;
        _visualOutputAttached = true;
        if (VisualBridge.Current is { } bridge)
        {
            _visualOutputShown = bridge.OutputOpen;
            bridge.OutputOpenChanged += open => Dispatcher.UIThread.Post(() =>
            {
                _visualOutputShown = open;
                SyncVisualOutputMenu();
            });
        }
        SyncVisualOutputMenu();

        if (Environment.GetEnvironmentVariable("NOTA_VISUAL_OUTPUT_TEST") is { Length: > 0 } mode)
        {
            DispatcherTimer.RunOnce(ToggleVisualOutput, TimeSpan.FromSeconds(6));
            if (mode != "1")
                DispatcherTimer.RunOnce(ToggleVisualOutput, TimeSpan.FromSeconds(9));
        }
    }
}
