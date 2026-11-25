// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Pattern ▸ Random — choose a genre or blend two, set how busy and how varied the result
// should be, then write the pattern into the open clip. The dialog only chooses options;
// DrumPatternGenerator makes the notes and DrumPatternView writes them in one undo step.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Application.Midi;

namespace Nota.App;

/// <summary>What the Random pattern dialog returns.</summary>
public sealed record PatternGeneratorOptions(
    string GenreA,
    string? GenreB,
    double Mix,
    bool AlternateBars,
    double Density,
    double Variation,
    bool ReplaceExisting);

public sealed class PatternGeneratorWindow : NotaWindow
{
    private static PatternGeneratorOptions? s_last;

    private static readonly IBrush Panel = NotaPalette.SurfaceCard;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    private readonly IReadOnlyList<DrumGenre> _genres;
    private readonly ComboBox _genreA, _genreB;
    private readonly Slider _mix, _density, _variation;
    private readonly TextBlock _mixValue, _densityValue, _variationValue;
    private readonly CheckBox _alternate, _replace;

    public PatternGeneratorWindow(IReadOnlyList<DrumGenre> genres, string roles)
    {
        _genres = genres;
        var last = s_last ?? new PatternGeneratorOptions("house", null, .5, false, .5, .25, true);

        Title = Loc.T("drum_pattern.random_title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Panel;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Close(null); } };

        var body = new StackPanel { Margin = new Thickness(18, 16), Spacing = 14 };
        body.Children.Add(new TextBlock
        {
            Text = Loc.T("drum_pattern.roles_from_pad_names"),
            FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap,
        });
        if (roles.Length > 0)
            body.Children.Add(Mono(Loc.F("drum_pattern.using_pads", roles), TextSecondary));

        // Genre and its mix partner.
        var names = _genres.Select(g => g.Name).ToArray();
        _genreA = Combo(names, IndexOf(last.GenreA));
        var bNames = new List<string> { Loc.T("common.none") };
        bNames.AddRange(names);
        _genreB = Combo(bNames.ToArray(), last.GenreB is { Length: > 0 } b ? IndexOf(b) + 1 : 0);
        _genreA.SelectionChanged += (_, _) =>
        {
            if (_genreB.SelectedIndex == _genreA.SelectedIndex + 1) _genreB.SelectedIndex = 0;
            UpdateMixEnabled();
        };
        _genreB.SelectionChanged += (_, _) =>
        {
            if (_genreB.SelectedIndex > 0 && _genreB.SelectedIndex == _genreA.SelectedIndex + 1)
                _genreB.SelectedIndex = 0;
            UpdateMixEnabled();
        };
        var genreGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        var genreACell = Section(Loc.T("drum_pattern.genre"), _genreA);
        var genreBCell = Section(Loc.T("drum_pattern.mix_with"), _genreB);
        genreGrid.Children.Add(genreACell);
        Grid.SetColumn(genreBCell, 1); genreGrid.Children.Add(genreBCell);
        body.Children.Add(genreGrid);

        // Mix amount and the alternate-bars switch.
        _mix = Slider(last.Mix);
        _mixValue = ValueText(_mix);
        _alternate = new CheckBox
        {
            Content = Loc.T("drum_pattern.alternate_bars"),
            IsChecked = last.AlternateBars,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var mixRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        mixRow.Children.Add(_mix);
        Grid.SetColumn(_mixValue, 1); mixRow.Children.Add(_mixValue);
        var mixBody = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                mixRow,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 12,
                    Children =
                    {
                        _alternate,
                        Mono(Loc.T("drum_pattern.mix_hint"), TextTertiary, 9),
                    },
                },
            },
        };
        body.Children.Add(Section(Loc.T("drum_pattern.mix"), mixBody));

        // Density and variation.
        _density = Slider(last.Density);
        _densityValue = ValueText(_density);
        body.Children.Add(Section(Loc.T("drum_pattern.density"), Row(_density, _densityValue)));

        _variation = Slider(last.Variation);
        _variationValue = ValueText(_variation);
        body.Children.Add(Section(Loc.T("drum_pattern.variation"), Row(_variation, _variationValue)));

        // Replace vs fill, and what the generator does under the hood.
        _replace = new CheckBox
        {
            Content = Loc.T("drum_pattern.replace_existing_pad_notes"),
            IsChecked = last.ReplaceExisting,
            VerticalAlignment = VerticalAlignment.Center,
        };
        body.Children.Add(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _replace,
                new TextBlock
                {
                    Text = Loc.T("drum_pattern.replace_hint"),
                    FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = Loc.T("drum_pattern.sixteenth_note_patterns"),
                    FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap,
                },
            },
        });

        // Footer: surprise, cancel, generate.
        var surprise = new Button { Content = Loc.T("drum_pattern.surprise_me"), Classes = { "ghost" } };
        surprise.Click += (_, _) => Surprise();
        var cancel = new Button { Content = Loc.T("common.cancel"), Classes = { "ghost" } };
        cancel.Click += (_, _) => Close(null);
        var generate = new Button { Content = Loc.T("drum_pattern.generate"), Classes = { "primary" } };
        generate.Click += (_, _) =>
        {
            int ai = Math.Clamp(_genreA.SelectedIndex, 0, _genres.Count - 1);
            int bi = _genreB.SelectedIndex - 1;
            string? bId = bi >= 0 && bi < _genres.Count && bi != ai ? _genres[bi].Id : null;
            var opts = new PatternGeneratorOptions(
                _genres[ai].Id, bId, _mix.Value, _alternate.IsChecked == true,
                _density.Value, _variation.Value, _replace.IsChecked == true);
            s_last = opts;
            Close(opts);
        };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        footer.Children.Add(surprise);
        Grid.SetColumn(cancel, 2); footer.Children.Add(cancel);
        Grid.SetColumn(generate, 3); footer.Children.Add(generate);
        body.Children.Add(footer);

        UpdateMixEnabled();
        SetBody(body);
    }

    private int IndexOf(string id)
    {
        for (int i = 0; i < _genres.Count; i++)
            if (_genres[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private void UpdateMixEnabled()
    {
        bool hasB = _genreB.SelectedIndex > 0;
        _mix.IsEnabled = hasB;
        _alternate.IsEnabled = hasB;
        _mixValue.Foreground = hasB ? TextSecondary : TextTertiary;
    }

    private void Surprise()
    {
        var rng = Random.Shared;
        int a = rng.Next(_genres.Count);
        int b = -1;
        if (_genres.Count > 1 && rng.NextDouble() < .65)
        {
            b = rng.Next(_genres.Count - 1);
            if (b >= a) b++;
        }
        _genreA.SelectedIndex = a;
        _genreB.SelectedIndex = b < 0 ? 0 : b + 1;
        _mix.Value = .35 + rng.NextDouble() * .3;
        _alternate.IsChecked = b >= 0 && rng.NextDouble() < .25;
        _density.Value = .35 + rng.NextDouble() * .4;
        _variation.Value = .15 + rng.NextDouble() * .35;
        _replace.IsChecked = true;
        UpdateMixEnabled();
    }

    // ---- small themed bits ----------------------------------------------------------------

    private static ComboBox Combo(string[] names, int selected)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var n in names) c.Items.Add(new ComboBoxItem { Content = n });
        c.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, names.Length - 1));
        return c;
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

    private static Slider Slider(double value) => new()
    {
        Minimum = 0, Maximum = 1, Value = Math.Clamp(value, 0, 1),
        SmallChange = .01, LargeChange = .1, VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock ValueText(Slider slider)
    {
        var text = new TextBlock
        {
            Text = NotaNum.Pct(slider.Value), FontSize = 9, Foreground = TextSecondary,
            Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value") text.Text = NotaNum.Pct(slider.Value);
        };
        return text;
    }

    private static Control Row(Slider slider, TextBlock value)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        g.Children.Add(slider);
        Grid.SetColumn(value, 1);
        g.Children.Add(value);
        return g;
    }

    private static TextBlock Mono(string text, IBrush colour, double size = 9) => new()
    {
        Text = text, FontSize = size, Foreground = colour, TextWrapping = TextWrapping.Wrap,
    };
}
