// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Syntetika Engine — the OpenVJ tab in the detail panel.
//
// It shows the same controls as openVJ's Clip panel for the selected Session slot: the
// source's parameters, Speed / beat sync, the fixed Transform and every effect in the
// clip's chain (bypass, dry/wet, reorder, remove). Values are normalised/real exactly as
// openVJ reports them, edits go over the visual bridge, and edits made in openVJ itself
// come back through the same channel (two-way).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nota.Application.Localization;
using Nota.Infrastructure.Visuals;

namespace Nota.App;

public sealed class VisualParamsView : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 12, Margin = new Thickness(14, 10, 14, 18) };
    private readonly DispatcherTimer _timer;
    private int _layer = -1, _col = -1;
    private DateTime _lastEditAt;
    private bool _offlineShown;
    private bool _wasConnected;
    private string _stateKey = "";
    private readonly List<(string Kind, string Id, string Name)> _catalogAll = new();
    private readonly List<(string Kind, string Id, string Name)> _catalogShown = new();
    private TextBox? _catalogSearch;
    private string _catalogKind = "generators";
    private Window? _catalog;
    private Action<JsonElement>? _catalogPick;
    private ListBox? _catalogList;
    private readonly List<(string Kind, string Id, string Name)> _catalogItems = new();
    private readonly Button _undoBtn;
    private readonly Button _redoBtn;

    private static VisualBridge? Bridge => VisualBridge.Current;

    public VisualParamsView()
    {
        _undoBtn = new Button
        {
            Content = Loc.T("common.undo"),
            Padding = new Thickness(10, 4),
            FontSize = 12,
            IsEnabled = false,
        };
        _redoBtn = new Button
        {
            Content = Loc.T("common.redo"),
            Padding = new Thickness(10, 4),
            FontSize = 12,
            IsEnabled = false,
        };
        _undoBtn.Click += (_, _) => Bridge?.VisualUndo();
        _redoBtn.Click += (_, _) => Bridge?.VisualRedo();
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(14, 10, 14, 0),
        };
        tools.Children.Add(_undoBtn);
        tools.Children.Add(_redoBtn);
        var dock = new DockPanel();
        DockPanel.SetDock(tools, Dock.Top);
        dock.Children.Add(tools);
        dock.Children.Add(new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = dock;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Pump());
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
            _catalog?.Close();
        };
    }

    public int Layer => _layer;
    public int Col => _col;

    /// <summary>Hands a picked media file to the window, which pairs a video's sound with its
    /// visual in the same Session slot. Null keeps the visual-only behaviour.</summary>
    public Func<int, int, string, Task>? MediaPicked { get; set; }

    /// <summary>Shows a message instead of parameters (no Session clip, engine offline…).</summary>
    public void ShowHint(string text)
    {
        _layer = -1;
        _col = -1;
        _root.Children.Clear();
        _root.Children.Add(Text(text, NotaPalette.TextSecondary, 12));
    }

    /// <summary>Points the tab at a slot (track's layer + scene) and asks openVJ for its state.</summary>
    public void SetTarget(int layer, int col)
    {
        if (layer == _layer && col == _col) return;
        _layer = layer;
        _col = col;
        _offlineShown = false;
        _stateKey = "";
        _root.Children.Clear();
        _root.Children.Add(Text(Loc.T("visual.loading"), NotaPalette.TextSecondary, 12));
        Bridge?.VizGet(layer, col);
    }

    /// <summary>Re-reads the current slot after the layer/scene structure changed.</summary>
    public void Reload()
    {
        _stateKey = "";
        if (_layer >= 0) Bridge?.VizGet(_layer, _col);
    }

    private void Pump()
    {
        _undoBtn.IsEnabled = Bridge is { Connected: true, CanUndo: true };
        _redoBtn.IsEnabled = Bridge is { Connected: true, CanRedo: true };
        if (Bridge is not { Connected: true })
        {
            _wasConnected = false;
            if (_layer >= 0 && !_offlineShown)
            {
                _offlineShown = true;
                ShowOffline();
            }
            return;
        }
        _offlineShown = false;
        if (!_wasConnected)
        {
            _wasConnected = true;
            if (_layer >= 0) Bridge.VizGet(_layer, _col);   // fresh state after a reconnect
        }
        while (Bridge.TakeMessage() is { } line) Handle(line);
    }

    private void Handle(string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return; }
        using (doc)
        {
            var v = doc.RootElement;
            var t = v.TryGetProperty("t", out var te) ? te.GetString() : null;
            if (t == "vizState")
            {
                if (!v.TryGetProperty("layer", out var l) || !l.TryGetInt32(out int layer)) return;
                if (!v.TryGetProperty("col", out var c) || !c.TryGetInt32(out int col)) return;
                if (layer == _layer && col == _col) ApplyState(v);
            }
            else if (t == "vizCatalog")
            {
                FillCatalog(v);
            }
        }
    }

    // ---- state ---------------------------------------------------------------

    private void ShowOffline()
    {
        _root.Children.Clear();
        _root.Children.Add(Text(Loc.T("visual.offline"), NotaPalette.TextSecondary, 12));
        if (Nota.Infrastructure.Visuals.VisualEngineProcess.Current is { IsAvailable: true } engine)
        {
            var start = new Button { Content = Loc.T("visual.start_engine"), Margin = new Thickness(0, 6, 0, 0) };
            start.Click += (_, _) => engine.Start();
            _root.Children.Add(start);
        }
    }

    private void ApplyState(JsonElement v)
    {
        if ((DateTime.UtcNow - _lastEditAt).TotalMilliseconds < 350) return;   // keep a drag stable   // keep a drag stable
        try
        {
            string key = v.GetRawText();
            if (key == _stateKey) return;
            _stateKey = key;
            ApplyStateCore(v);
        }
        catch (Exception ex)
        {
            _root.Children.Clear();
            _root.Children.Add(Text(Loc.F("visual.panel_error", ex.Message), NotaPalette.TextSecondary, 11));
        }
    }

    private void ApplyStateCore(JsonElement v)
    {
        _root.Children.Clear();

        bool has = v.TryGetProperty("has_visual", out var h) && h.GetBoolean();
        if (!has)
        {
            _root.Children.Add(Text(Loc.T("visual.no_visual"), NotaPalette.TextSecondary, 12));
            var assign = new Button { Content = Loc.T("visual.assign"), Margin = new Thickness(0, 6, 0, 0) };
            assign.Click += (_, _) => OpenCatalog("generators", item => Bridge?.VizAssign(_layer, _col, item.GetProperty("kind").GetString() ?? "generator", item.GetProperty("id").GetString() ?? ""));
            _root.Children.Add(assign);
            return;
        }

        var source = v.TryGetProperty("source", out var s) ? s : default;
        string kind = source.ValueKind == JsonValueKind.Object && source.TryGetProperty("kind", out var sk) ? sk.GetString() ?? "" : "";
        string name = source.ValueKind == JsonValueKind.Object && source.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";
        string note = source.ValueKind == JsonValueKind.Object && source.TryGetProperty("note", out var nn) ? nn.GetString() ?? "" : "";

        // ---- header: what this visual is, and the two whole-clip actions
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(Text(name, NotaPalette.TextHeading, 13, FontWeight.SemiBold));
        header.Children.Add(Text(kind, NotaPalette.TextSecondary, 11));
        var replace = new Button { Content = Loc.T("visual.replace") };
        replace.Click += (_, _) => OpenCatalog("generators", item => Bridge?.VizAssign(_layer, _col, item.GetProperty("kind").GetString() ?? "generator", item.GetProperty("id").GetString() ?? ""));
        var clear = new Button { Content = Loc.T("visual.clear") };
        clear.Click += (_, _) => Bridge?.VizClear(_layer, _col);
        header.Children.Add(replace);
        header.Children.Add(clear);
        _root.Children.Add(header);

        // ---- three areas side by side: transport | source & playback | effects
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };

        var transport = new StackPanel { Spacing = 4 };
        transport.Children.Add(Section(Loc.T("visual.transport")));
        if (v.TryGetProperty("playback", out var pb) && pb.ValueKind == JsonValueKind.Object)
            BuildPlayback(pb, transport);
        columns.Children.Add(Area(transport));

        var left = new StackPanel { Spacing = 4 };
        left.Children.Add(Section(Loc.T("visual.source_playback")));
        if (v.TryGetProperty("groups", out var groups)
            && groups.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Array)
            foreach (var p in src.EnumerateArray()) left.Children.Add(ParamRow(p, "source"));

        if (v.TryGetProperty("layer_state", out var lyr) && lyr.ValueKind == JsonValueKind.Object)
        {
            left.Children.Add(Section(Loc.T("visual.layer")));
            double opacity = lyr.TryGetProperty("opacity", out var op) ? op.GetDouble() : 1.0;
            left.Children.Add(NotaSliderRow(Loc.T("visual.opacity"), 0, 100, opacity * 100, "%",
                value => Bridge?.VizLayer(_layer, value / 100.0, null, null)));

            string blend = lyr.TryGetProperty("blend", out var bl) ? bl.GetString() ?? "Normal" : "Normal";
            var blends = new[] { "Normal", "Add", "Screen", "Multiply", "Overlay", "Difference", "Lighten", "Darken", "Subtract" };
            var blendCombo = new ComboBox { ItemsSource = blends, SelectedIndex = Math.Max(0, Array.IndexOf(blends, blend)), Width = 150 };
            blendCombo.SelectionChanged += (_, _) =>
            {
                if (blendCombo.SelectedItem is string m) Bridge?.VizLayer(_layer, null, m, null);
            };
            left.Children.Add(ParamRowRaw(Text(Loc.T("visual.blend"), NotaPalette.TextSecondary, 11), blendCombo, null));

            bool bypass = lyr.TryGetProperty("bypass", out var by) && by.GetBoolean();
            var bypassBox = new CheckBox { IsChecked = bypass, Content = Loc.T("visual.bypass") };
            bypassBox.IsCheckedChanged += (_, _) => Bridge?.VizLayer(_layer, null, null, bypassBox.IsChecked == true);
            left.Children.Add(ParamRowRaw(Text("", NotaPalette.TextSecondary, 11), bypassBox, null));
        }
        var leftCard = Area(left);
        Grid.SetColumn(leftCard, 1);
        columns.Children.Add(leftCard);

        var right = new StackPanel { Spacing = 4 };
        var fxHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fxHeader.Children.Add(Section(Loc.T("visual.effects")));
        var add = new Button { Content = Loc.T("visual.add_effect"), Margin = new Thickness(8, 0, 0, 0) };
        add.Click += (_, _) => OpenCatalog("effects", item => Bridge?.VizFx(_layer, _col, "add", id: item.GetProperty("id").GetString()));
        fxHeader.Children.Add(add);
        right.Children.Add(fxHeader);

        if (v.TryGetProperty("groups", out var groups2)
            && groups2.TryGetProperty("transform", out var tf) && tf.ValueKind == JsonValueKind.Object)
            AddEffectCard(tf, isTransform: true, index: -1, right);

        if (v.TryGetProperty("groups", out var groups3)
            && groups3.TryGetProperty("effects", out var fx) && fx.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var card in fx.EnumerateArray())
            {
                AddEffectCard(card, isTransform: false, index: i, right);
                i++;
            }
        }
        var rightCard = Area(right);
        Grid.SetColumn(rightCard, 2);
        columns.Children.Add(rightCard);
        _root.Children.Add(columns);
    }

    /// <summary>One of the three framed areas.</summary>
    private static Control Area(Control content)
    {
        var card = ClipEditorKit.Card(content);
        card.Padding = new Thickness(10, 8);
        card.Margin = new Thickness(0, 2, 0, 2);
        return card;
    }

    private void AddEffectCard(JsonElement card, bool isTransform, int index, StackPanel target)
    {
        string title = card.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        bool enabled = !card.TryGetProperty("enabled", out var en) || en.GetBoolean();
        double mix = card.TryGetProperty("mix", out var m) ? m.GetDouble() : 1.0;

        var box = ClipEditorKit.Card(new StackPanel());
        box.Margin = new Thickness(0, 2, 0, 2);
        box.Padding = new Thickness(10, 6);
        var col = new StackPanel { Spacing = 6 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        if (!isTransform)
        {
            var on = new CheckBox { IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center };
            on.IsCheckedChanged += (_, _) => Bridge?.VizFx(_layer, _col, "enabled", index, value: on.IsChecked == true ? 1 : 0);
            head.Children.Add(on);
        }
        head.Children.Add(Text(title, NotaPalette.TextHeading, 12, FontWeight.SemiBold));

        if (!isTransform)
        {
            var mixRow = NotaSliderRow(Loc.T("visual.mix"), 0, 100, mix * 100, "%",
                v => Bridge?.VizFx(_layer, _col, "mix", index, value: v / 100.0), labelW: 34);
            mixRow.Width = 140;
            head.Children.Add(mixRow);
            var up = new Button { Content = "↑", Padding = new Thickness(4, 0), MinWidth = 22 };
            up.Click += (_, _) => Bridge?.VizFx(_layer, _col, "move", index, to: Math.Max(0, index - 1));
            var down = new Button { Content = "↓", Padding = new Thickness(4, 0), MinWidth = 22 };
            down.Click += (_, _) => Bridge?.VizFx(_layer, _col, "move", index, to: index + 1);
            var del = new Button { Content = "×", Padding = new Thickness(4, 0), MinWidth = 22 };
            del.Click += (_, _) => Bridge?.VizFx(_layer, _col, "remove", index);
            head.Children.Add(up);
            head.Children.Add(down);
            head.Children.Add(del);
        }
        col.Children.Add(head);

        if (card.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray())
                col.Children.Add(ParamRow(p, isTransform ? "transform" : $"fx:{index}"));

        box.Child = col;
        target.Children.Add(box);
    }

    private void BuildPlayback(JsonElement pb, StackPanel target)
    {
        double speed = pb.TryGetProperty("speed", out var sp) ? sp.GetDouble() : 1.0;
        bool sync = pb.TryGetProperty("sync_beats", out var sb) && sb.ValueKind == JsonValueKind.Number;
        double beats = sync ? sb.GetDouble() : 4.0;
        string mode = pb.TryGetProperty("play_mode", out var pm) ? pm.GetString() ?? "Loop" : "Loop";
        string trigger = pb.TryGetProperty("trigger", out var tr) ? tr.GetString() ?? "Normal" : "Normal";

        _root.Children.Add(NotaSliderRow(Loc.T("visual.speed"), 0.1, 4, speed, "×",
            v => Bridge?.VizPlayback(_layer, _col, v, null, null, null)));

        var syncRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cb = new CheckBox { Content = Loc.T("visual.beat_sync"), IsChecked = sync };
        var beatsCombo = new ComboBox
        {
            ItemsSource = new[] { "1", "2", "4", "8", "16", "32", "64", "128" },
            SelectedIndex = Math.Max(0, Array.IndexOf(new[] { 1, 2, 4, 8, 16, 32, 64, 128 }, (int)beats)),
            IsEnabled = sync,
            Width = 80,
        };
        cb.IsCheckedChanged += (_, _) =>
        {
            bool on = cb.IsChecked == true;
            beatsCombo.IsEnabled = on;
            double b = (int)(beatsCombo.SelectedIndex + 1) switch { 1 => 1, 2 => 2, 3 => 4, 4 => 8, 5 => 16, 6 => 32, 7 => 64, _ => 128 };
            Bridge?.VizPlayback(_layer, _col, null, on ? b : null, null, null, setSync: true);
        };
        beatsCombo.SelectionChanged += (_, _) =>
        {
            if (cb.IsChecked != true) return;
            double[] vals = { 1, 2, 4, 8, 16, 32, 64, 128 };
            double b = vals[Math.Clamp(beatsCombo.SelectedIndex, 0, vals.Length - 1)];
            Bridge?.VizPlayback(_layer, _col, null, b, null, null, setSync: true);
        };
        syncRow.Children.Add(cb);
        syncRow.Children.Add(beatsCombo);
        target.Children.Add(ParamRowRaw(Text(Loc.T("visual.sync"), NotaPalette.TextSecondary, 11), syncRow, null));

        var modes = new[] { "Loop", "Bounce", "Random", "Play once & clear", "Play once & hold" };
        var modeCombo = new ComboBox { ItemsSource = modes, SelectedIndex = Math.Max(0, Array.IndexOf(modes, mode)), Width = 170 };
        modeCombo.SelectionChanged += (_, _) =>
        {
            if (modeCombo.SelectedItem is string m) Bridge?.VizPlayback(_layer, _col, null, null, m, null);
        };
        target.Children.Add(ParamRowRaw(Text(Loc.T("visual.mode"), NotaPalette.TextSecondary, 11), modeCombo, null));

        var triggers = new[] { "Normal", "Toggle", "Piano" };
        var trigCombo = new ComboBox { ItemsSource = triggers, SelectedIndex = Math.Max(0, Array.IndexOf(triggers, trigger)), Width = 120 };
        trigCombo.SelectionChanged += (_, _) =>
        {
            if (trigCombo.SelectedItem is string m) Bridge?.VizPlayback(_layer, _col, null, null, null, m);
        };
        target.Children.Add(ParamRowRaw(Text(Loc.T("visual.trigger"), NotaPalette.TextSecondary, 11), trigCombo, null));
    }

    private Control ParamRow(JsonElement p, string group)
    {
        string key = p.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
        string type = p.TryGetProperty("type", out var ty) ? ty.GetString() ?? "float" : "float";
        switch (type)
        {
            case "bool":
            {
                bool on = p.TryGetProperty("value", out var bv) && bv.GetBoolean();
                var cb = new CheckBox { IsChecked = on };
                cb.IsCheckedChanged += (_, _) => Bridge?.VizSet(_layer, _col, group, key, cb.IsChecked == true ? 1 : 0);
                return ParamRowRaw(Label(p), cb, null);
            }
            case "long":
            {
                var labels = new List<string>();
                if (p.TryGetProperty("labels", out var ls)) foreach (var l in ls.EnumerateArray()) labels.Add(l.GetString() ?? "");
                var values = new List<double>();
                if (p.TryGetProperty("values", out var vs)) foreach (var v in vs.EnumerateArray()) values.Add(v.GetDouble());
                int index = 0;
                if (p.TryGetProperty("value", out var cv) && values.Count > 0)
                {
                    double cur = cv.GetDouble();
                    index = 0;
                    double best = double.MaxValue;
                    for (int i = 0; i < values.Count; i++)
                    {
                        double d = Math.Abs(values[i] - cur);
                        if (d < best) { best = d; index = i; }
                    }
                }
                var combo = new ComboBox { ItemsSource = labels, SelectedIndex = index, Width = 170 };
                combo.SelectionChanged += (_, _) =>
                {
                    int i = Math.Clamp(combo.SelectedIndex, 0, Math.Max(0, values.Count - 1));
                    if (i < values.Count) Bridge?.VizSet(_layer, _col, group, key, values[i]);
                };
                return ParamRowRaw(Label(p), combo, null);
            }
            case "event":
            {
                var btn = new Button { Content = Loc.T("visual.trigger") };
                btn.Click += (_, _) => Bridge?.VizSet(_layer, _col, group, key, 1);
                return ParamRowRaw(Label(p), btn, null);
            }
            default:
            {
                double min = p.TryGetProperty("min", out var mi) ? mi.GetDouble() : 0;
                double max = p.TryGetProperty("max", out var ma) ? ma.GetDouble() : 1;
                double value = p.TryGetProperty("value", out var va) ? va.GetDouble() : 0;
                string unit = p.TryGetProperty("unit", out var un) ? un.GetString() ?? "" : "";
                return NotaSliderRow(LabelText(p), min, max, value, unit,
                    v => Bridge?.VizSet(_layer, _col, group, key, v));
            }
        }
    }

    /// <summary>A Nota slider row (label + Shell SliderTrack with its value inside), normalised
    /// to the parameter's own range.</summary>
    private Control NotaSliderRow(string label, double min, double max, double value, string unit, Action<double> onValue, double labelW = 96)
    {
        double span = Math.Max(1e-9, max - min);
        double current = Math.Clamp((value - min) / span, 0.0, 1.0);
        var row = ClipEditorKit.SliderRow(
            label, labelW,
            () => current,
            norm =>
            {
                _lastEditAt = DateTime.UtcNow;
                current = norm;
                onValue(min + norm * span);
            },
            () => FormatValue(min + current * span, unit),
            out _);
        row.Margin = new Thickness(0, 1, 0, 1);
        return row;
    }

    private static string FormatValue(double v, string unit) => unit switch
    {
        "%" => $"{v:0}\u2009%",
        "°" => $"{v:0}°",
        "" => $"{v:0.00}",
        _ => $"{v:0.0}{unit}",
    };

    private Control ParamRowRaw(Control label, Control control, Action? _)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), Margin = new Thickness(0, 1, 0, 1) };
        var l = label;
        Grid.SetColumn(l, 0);
        Grid.SetColumn(control, 1);
        row.Children.Add(l);
        row.Children.Add(control);
        return row;
    }

    private static string LabelText(JsonElement p)
        => p.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";

    private static Control Label(JsonElement p) => Text(LabelText(p), NotaPalette.TextSecondary, 11);

    private static TextBlock Text(string text, IBrush brush, double size, FontWeight? weight = null) => new()
    {
        Text = text,
        Foreground = brush,
        FontSize = size,
        FontWeight = weight ?? FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static Control Section(string title) => new TextBlock
    {
        Text = title.ToUpperInvariant(),
        Foreground = NotaPalette.TextSecondary,
        FontSize = 10,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static Slider Slider(double min, double max, double value, Action<double> onValue, Action? press = null, Action? release = null)
    {
        var s = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            VerticalAlignment = VerticalAlignment.Center,
            Height = 20,
        };
        s.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) onValue(s.Value);
        };
        if (press is not null)
        {
            s.PointerPressed += (_, _) => press();
            s.PointerReleased += (_, _) => release?.Invoke();
            s.PointerCaptureLost += (_, _) => release?.Invoke();
        }
        return s;
    }

    // ---- catalog (assign / add effect) --------------------------------------

    private void OpenCatalog(string kind, Action<JsonElement> onPick)
    {
        if (Bridge is not { Connected: true })
        {
            var warn = new Window
            {
                Title = Loc.T("visual.assign"),
                Width = 320,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBlock { Text = Loc.T("visual.offline"), Margin = new Thickness(16) },
            };
            if (VisualRoot is Window ownerOffline) _ = warn.ShowDialog(ownerOffline);
            return;
        }
        _catalogPick = onPick;
        _catalogKind = kind;
        _catalogAll.Clear();
        _catalogShown.Clear();
        _catalogItems.Clear();
        _catalogList = new ListBox { Height = 320 };
        var search = new TextBox { Watermark = Loc.T("visual.search"), Margin = new Thickness(0, 0, 0, 8) };
        _catalogSearch = search;
        search.TextChanged += (_, _) => ApplyCatalogFilter(search.Text ?? "");
        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        if (kind == "generators")
        {
            Button KindButton(string text, string value)
            {
                var b = new Button { Content = text };
                b.Click += (_, _) =>
                {
                    _catalogKind = value;
                    if (value == "media") { PickMediaFile(); return; }
                    Bridge?.VizCatalog(value, "", DateTime.UtcNow.Ticks);
                };
                return b;
            }
            kindRow.Children.Add(KindButton(Loc.T("visual.generators"), "generators"));
            kindRow.Children.Add(KindButton(Loc.T("visual.media_file"), "media"));
            kindRow.Children.Add(KindButton(Loc.T("visual.camera"), "cameras"));
            kindRow.Children.Add(KindButton(Loc.T("visual.syphon"), "syphon"));
        }

        var ok = new Button { Content = Loc.T("visual.add"), IsDefault = true };
        var cancel = new Button { Content = Loc.T("common.cancel"), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        _catalog = new Window
        {
            Title = kind == "effects" ? Loc.T("visual.add_effect") : Loc.T("visual.assign"),
            Width = 460,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 6,
                Children = { kindRow, search, _catalogList, buttons },
            },
        };
        _catalog.Opened += (_, _) => Bridge?.VizCatalog(kind, "", DateTime.UtcNow.Ticks);
        ok.Click += (_, _) => PickCatalog();
        _catalogList.DoubleTapped += (_, _) => PickCatalog();
        cancel.Click += (_, _) => _catalog?.Close();
        _catalog.Closed += (_, _) =>
        {
            _catalog = null;
            _catalogPick = null;
            _catalogList = null;
            _catalogSearch = null;
        };
        if (VisualRoot is Window owner) _catalog.ShowDialog(owner);
        else _catalog.Show();
    }

    private void FillCatalog(JsonElement v)
    {
        if (_catalogList is null || !v.TryGetProperty("items", out var items)) return;
        _catalogAll.Clear();
        foreach (var item in items.EnumerateArray())
        {
            string kind = item.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
            string id = item.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
            _catalogAll.Add((kind, id, name));
        }
        ApplyCatalogFilter(_catalogSearch?.Text ?? "");
    }

    private void ApplyCatalogFilter(string query)
    {
        if (_catalogList is null) return;
        _catalogShown.Clear();
        var names = new List<string>();
        foreach (var item in _catalogAll)
        {
            if (query.Length == 0
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _catalogShown.Add(item);
                names.Add(item.Name);
            }
        }
        if (names.Count == 0) names.Add(_catalogKind switch
        {
            "cameras" => Loc.T("visual.no_cameras"),
            "syphon" => Loc.T("visual.no_syphon_servers"),
            _ => "(nothing matches)",
        });
        _catalogList.ItemsSource = names;
        _catalogItems.Clear();
        _catalogItems.AddRange(_catalogShown);
    }

    private void PickCatalog()
    {
        int i = _catalogList?.SelectedIndex ?? -1;
        if (i < 0 || i >= _catalogItems.Count) return;
        var (kind, id, _) = _catalogItems[i];
        if (_catalogKind == "cameras")
        {
            Bridge?.VizAssignLive(_layer, _col, "camera", id, "", "");
        }
        else if (_catalogKind == "syphon")
        {
            var parts = id.Split('|', 2);
            Bridge?.VizAssignLive(_layer, _col, "syphon", "", parts[0], parts.Length > 1 ? parts[1] : "");
        }
        else
        {
            var item = JsonDocument.Parse(JsonSerializer.Serialize(new { kind, id })).RootElement;
            _catalogPick?.Invoke(item);
        }
        _catalog?.Close();
    }

    /// <summary>Copies a picked video/image into the project (or staging) and assigns it.</summary>
    private async void PickMediaFile()
    {
        TopLevel? top = TopLevel.GetTopLevel(_catalog) ?? TopLevel.GetTopLevel(this);
        if (top is null || Bridge is null) return;
        try
        {
            var files = await top.PickFilesAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("visual.media_file"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(Loc.T("visual.media_files"))
                    {
                        Patterns = new[]
                        {
                            "*.mp4", "*.m4v", "*.mov", "*.mkv", "*.webm", "*.avi", "*.mxf", "*.mpg", "*.mpeg",
                            "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp",
                        },
                    },
                },
            });
            if (files.Count == 0) return;
            string? source = files[0].TryGetLocalPath();
            if (source is null || !MediaFiles.IsMedia(source)) return;
            if (MediaPicked is not null)
            {
                var picked = MediaPicked;
                _catalog?.Close();
                await picked(_layer, _col, source);
                return;
            }
            // Hashing a large video must not freeze the UI.
            string target = await Task.Run(() => MediaFiles.Prepare(source, Bridge.ProjectDir));
            Bridge.VizAssignMedia(_layer, _col, MediaFiles.KindOf(source), target);
            // A video assigned to a slot that already has audio takes that audio's timing.
            Bridge.ResendSlotTiming();
            Bridge.PublishSlotTiming(force: true);
            _catalog?.Close();
        }
        catch (Exception ex)
        {
            _catalogList?.ItemsSource = new[] { Loc.F("visual.could_not_add_media", ex.Message) };
        }
    }
}
