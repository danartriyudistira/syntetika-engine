// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Syntetika Engine fork branding. One place for the product identity so the fork can
// live beside a stock Nota install: its own data folder (projects, settings, plugin
// scans), its own MCP port, and no upstream self-update (which would replace the fork
// with a stock build).
//
// The upstream project stays credited: this file changes the *product* identity only.
// Every source file keeps its AGPL header and the About window keeps the licence text.

namespace Nota.Application;

public static class Branding
{
    /// <summary>Shown in the window title, welcome screen, splash, About and menus.</summary>
    public const string ProductName = "Syntetika Engine";

    /// <summary>Per-user data folder. Must differ from "Nota" so both apps can run at once.</summary>
    public const string DataFolder = "Syntetika Engine";

    /// <summary>Bundle identifier for the macOS app (and future Windows/Linux packaging).</summary>
    public const string BundleId = "studio.syntetika.engine";

    /// <summary>
    /// The fork ships from its own repository, not nota-daw/nota. Until that release feed
    /// exists, the in-app updater must stay inert: a stock Nota release would overwrite the
    /// fork's binary and remove the visual engine.
    /// </summary>
    public const bool UpdatesEnabled = false;

    /// <summary>Product version (the app), independent of the Nota engine version.</summary>
    public const string ProductVersion = "0.1.1";

    /// <summary>Loopback port for the MCP server; 3900 is taken by a stock Nota install.</summary>
    public const int DefaultMcpPort = 3910;
}
