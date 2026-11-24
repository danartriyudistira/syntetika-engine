// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Audio → MIDI dialog (Convert ▸ Melody / Harmony). The model hears a stem well but
// returns more notes than a player would: weak neighbours, harmonics and tiny re-attacks.
// This dialog offers the two things a stem workflow needs — a grid so the result sits on the
// tempo, and thinning controls so it doesn't arrive as a chord cloud. Returns the chosen
// mode plus the note options MainWindow hands to Transcription.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>What the Audio → MIDI dialog returns.</summary>
public sealed record ConvertOptions(ClipConvertMode Mode, TranscriptionOptions Notes);

public sealed class ConvertWindow : NotaWindow
{
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush OnAccent = NotaPalette.TextOnAccent;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    // The grid ladder the dialog offers: 0 = no quantization, then a 1/32 … 1/4 note,
    // straight and triplet.
    private static readonly double[] GridBeats = { 0, 0.125, 1.0 / 6, 0.25, 1.0 / 3, 0.5, 1.0 };
    private static readonly string[] GridNames = { "", "1/32", "1/16T", "1/16", "1/8T", "1/8", "1/4" };

    // Shortest kept note: 0 = keep everything the model returned.
    private static readonly double[] ShortBeats = { 0, 1.0 / 16, 1.0 / 8, 0.25, 0.5 };
    private static readonly string[] ShortNames = { "", "1/64", "1/32", "1/16", "1/8" };

    private int _mode;                    // 0 = melody, 1 = harmony
    private int _grid, _shortest, _voices;
    private bool _quantizeEnds = true;
    private double _minStrength = 0.35;
    private Slider _strength = null!;
    private TextBlock _strengthValue = null!;

    public ConvertWindow(ClipConvertMode mode, GridStep quantize)
    {
        _mode = mode == ClipConvertMode.Harmony ? 1 : 0;
        _grid = NearestGrid(quantize);
        _shortest = 2;                    // 1/32: the model's own shortest note, so nothing real goes
        _voices = 4;                      // a chord keeps its four strongest voices

        Title = Loc.T("main.convert.audio_to_midi");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Panel;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Close(null); } };

        var body = new StackPanel { Margin = new Thickness(18, 16), Spacing = 14 };

        // Mode: which line to keep. The context menu pre-selects it; this lets one change it
        // without going back to the clip.
        var modes = Segmented(new[] { Loc.T("main.convert.melody"), Loc.T("main.convert.harmony") }, _mode,
            i => _mode = i);
        body.Children.Add(Section(Loc.T("main.convert.mode"), modes));

        // Timing: the model's onsets are close but not on the bar. Quantize starts (and,
        // optionally, ends) to the chosen grid; the piano roll's own Quantize step is the
        // default, so the dialog follows the project's working grid.
        var gridNames = (string[])GridNames.Clone();
        gridNames[0] = Loc.T("common.off");
        var grid = Combo(gridNames, _grid);
        grid.SelectionChanged += (_, _) => _grid = Math.Max(0, grid.SelectedIndex);
        var ends = new CheckBox
        {
            Content = Loc.T("main.convert.quantize_note_ends"),
            IsChecked = _quantizeEnds,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ends.IsCheckedChanged += (_, _) => _quantizeEnds = ends.IsChecked == true;
        var timing = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = Loc.T("main.convert.snap_to_grid"), FontSize = 10, Foreground = TextSecondary },
                        grid, ends,
                    },
                },
            },
        };
        body.Children.Add(Section(Loc.T("main.convert.timing"), timing));

        // Density: the strength floor, the shortest note and the most voices a chord may keep.
        _strength = new Slider
        {
            Minimum = 0, Maximum = 0.8, Value = _minStrength, SmallChange = 0.01, LargeChange = 0.1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _strengthValue = new TextBlock
        {
            Text = NotaNum.Pct(_minStrength), FontSize = 9, Foreground = TextSecondary,
            Width = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        _strength.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != "Value") return;
            _minStrength = Math.Round(_strength.Value, 2);
            _strengthValue.Text = NotaNum.Pct(_minStrength);
        };
        var strengthRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { _strength, _strengthValue },
        };

        var shortNames = (string[])ShortNames.Clone();
        shortNames[0] = Loc.T("common.off");
        var shortest = Combo(shortNames, _shortest);
        shortest.SelectionChanged += (_, _) => _shortest = Math.Max(0, shortest.SelectedIndex);
        var voices = Combo(VoiceNames(), _voices);
        voices.SelectionChanged += (_, _) => _voices = Math.Max(0, voices.SelectedIndex);

        var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        var c1 = Section(Loc.T("main.convert.shortest_note"), shortest);
        var c2 = Section(Loc.T("main.convert.max_voices"), voices);
        pair.Children.Add(c1);
        Grid.SetColumn(c2, 1); pair.Children.Add(c2);
        var density = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Section(Loc.T("main.convert.min_strength"), strengthRow),
                pair,
            },
        };
        body.Children.Add(Section(Loc.T("main.convert.density"), density));

        body.Children.Add(new TextBlock
        {
            Text = Loc.T("main.convert.notes_below_the_strength_or_length"),
            FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap,
        });

        // Footer: Cancel and the primary action.
        var convert = new Button { Content = Loc.T("main.convert.convert"), Classes = { "primary" } };
        convert.Click += (_, _) => Close(new ConvertOptions(
            _mode == 1 ? ClipConvertMode.Harmony : ClipConvertMode.Melody,
            new TranscriptionOptions(
                MinStrength: _minStrength,
                MinLengthBeats: ShortBeats[_shortest],
                MaxVoices: _voices,
                QuantizeGridBeats: GridBeats[_grid],
                QuantizeAmount: 1.0,
                QuantizeEnds: _quantizeEnds)));
        var cancel = new Button { Content = Loc.T("common.cancel"), Classes = { "ghost" } };
        cancel.Click += (_, _) => Close(null);
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, convert },
        });

        SetBody(body);
    }

    private static int NearestGrid(GridStep step)
    {
        double beats = step.Beats(Meter.BeatsPerBar);
        for (int i = 1; i < GridBeats.Length; i++)
            if (Math.Abs(GridBeats[i] - beats) < 1e-6) return i;
        return 3;   // the project grid is a bar step: 1/16 is the useful default
    }

    private static string[] VoiceNames()
    {
        var names = new string[9];
        names[0] = Loc.T("common.off");
        for (int i = 1; i < names.Length; i++) names[i] = i.ToString(NotaNum.Culture);
        return names;
    }

    private Control Section(string title, Control content) => new StackPanel
    {
        Spacing = 6,
        Children =
        {
            new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextTertiary },
            content,
        },
    };

    private static ComboBox Combo(string[] names, int selected)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int i = 0; i < names.Length; i++)
            c.Items.Add(new ComboBoxItem { Content = names[i] });
        c.SelectedIndex = selected;   // after Items are populated
        return c;
    }

    private Control Segmented(string[] labels, int active, Action<int> onSelect)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var cells = new Border[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var t = new TextBlock { Text = labels[i], FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { CornerRadius = NotaRadius.Cell, Padding = new Thickness(10, 2), Child = t };
            cell.Cursor = new Cursor(StandardCursorType.Hand);
            cell.PointerPressed += (_, _) => { onSelect(idx); Paint(idx); };
            cells[i] = cell;
            inner.Children.Add(cell);
        }
        void Paint(int a)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                bool on = i == a;
                cells[i].Background = on ? Brass : Brushes.Transparent;
                ((TextBlock)cells[i].Child!).Foreground = on ? OnAccent : TextSecondary;
                ((TextBlock)cells[i].Child!).FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        Paint(active);
        return new Border
        {
            Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Group, Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left, Child = inner,
        };
    }
}
