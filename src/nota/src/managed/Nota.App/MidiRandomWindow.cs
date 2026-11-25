// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Random MIDI part — choose the part (bassline, pad or melody), one genre or a blend of
// two, the key and mode, then let MidiPartGenerator write a take. The dialog is a decision
// surface only: Generate previews in the piano roll through GenerateRequested, Apply keeps
// the preview as one undo step, and Close drops it. Each roll is a new seed, so the same
// settings can be auditioned until one fits.

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

/// <summary>What the Random MIDI dialog returns; the clip editor turns it into notes.</summary>
public sealed record MidiRandomOptions(
    MidiPart Part,
    string GenreA,
    string? GenreB,
    double Mix,
    int Root,
    int ScaleIndex,
    int LengthBars,
    double Density,
    double Variation,
    int Octave,
    bool Replace);

/// <summary>The outcome of one Generate: how many notes the preview holds, and the line
/// under the dialog's controls.</summary>
public readonly record struct MidiRandomPreview(int Notes, string Status);

public sealed class MidiRandomWindow : NotaWindow
{
    private static MidiRandomOptions? s_last;

    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    // The modes the dialog offers: every named scale except Chromatic, whose twelve degrees
    // would make the generator stack semitones instead of chords.
    private static readonly string[] Modes = MidiScales.Names.Take(9).ToArray();
    private static readonly int[] LengthBars = { 1, 2, 4, 8 };

    private readonly IReadOnlyList<MidiPartGenre> _genres;
    private readonly ComboBox _genreA, _genreB, _key, _mode, _length;
    private readonly Slider _mix, _density, _variation;
    private readonly TextBlock _mixValue, _densityValue, _variationValue;
    private readonly TextBlock _seedText, _status;
    private readonly Button _apply;
    private Action<int> _setPart = _ => { };
    private Action<int> _setOctave = _ => { };
    private Action<int> _setOutput = _ => { };
    private MidiPart _part;
    private bool _replace;
    private int _octave;
    private int _seed = Random.Shared.Next(1, 9999);
    private int _previewNotes;
    private bool _ready, _suspend;

    /// <summary>Raised for every Generate; the roll previews the returned take and reports
    /// back how many notes it holds.</summary>
    public Func<MidiRandomOptions, int, MidiRandomPreview>? GenerateRequested { get; set; }

    /// <summary>Keeps the preview as one undo step. Receives the note count of the preview.</summary>
    public Func<int, string>? ApplyRequested { get; set; }

    /// <summary>Drops the preview without touching the clip.</summary>
    public Action? RevertRequested { get; set; }

    public MidiRandomWindow(IReadOnlyList<MidiPartGenre> genres, int defaultRoot, int defaultScaleIndex)
    {
        _genres = genres;
        var last = s_last ?? new MidiRandomOptions(
            MidiPart.Bassline, genres[0].Id, null, .5,
            Math.Clamp(defaultRoot, 0, 11), Math.Clamp(defaultScaleIndex, 0, Modes.Length - 1),
            0, .5, .25, 0, true);
        _part = last.Part;

        Title = Loc.T("midi_random.title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.SurfaceCard;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Close(); } };

        var body = new StackPanel { Margin = new Thickness(18, 16), Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = Loc.T("midi_random.hint"), FontSize = 9, Foreground = TextTertiary,
            TextWrapping = TextWrapping.Wrap,
        });

        // Part: the whole point of the dialog, so it gets the full width.
        var partNames = new[] { Loc.T("midi_random.bassline"), Loc.T("midi_random.pad"), Loc.T("midi_random.melody") };
        var parts = ClipEditorKit.Segments(partNames, (int)_part,
            i => { _part = (MidiPart)i; OptionsChanged(); }, out _setPart, fill: true);
        body.Children.Add(ClipEditorKit.Section(Loc.T("midi_random.part"), parts));

        // Genre and its optional mix partner.
        var names = _genres.Select(g => g.Name).ToArray();
        _genreA = Combo(names, IndexOf(last.GenreA));
        var bNames = new List<string> { Loc.T("common.none") };
        bNames.AddRange(names);
        _genreB = Combo(bNames.ToArray(), last.GenreB is { Length: > 0 } b ? IndexOf(b) + 1 : 0);
        _genreA.SelectionChanged += (_, _) =>
        {
            _suspend = true;
            if (_genreB.SelectedIndex == _genreA.SelectedIndex + 1) _genreB.SelectedIndex = 0;
            _suspend = false;
            UpdateMixEnabled();
            OptionsChanged();
        };
        _genreB.SelectionChanged += (_, _) =>
        {
            _suspend = true;
            if (_genreB.SelectedIndex > 0 && _genreB.SelectedIndex == _genreA.SelectedIndex + 1)
                _genreB.SelectedIndex = 0;
            _suspend = false;
            UpdateMixEnabled();
            OptionsChanged();
        };
        var genreRow = TwoUp(
            ClipEditorKit.Section(Loc.T("midi_random.genre"), _genreA),
            ClipEditorKit.Section(Loc.T("midi_random.mix_with"), _genreB));
        body.Children.Add(genreRow);

        // Key, mode, length, octave.
        _key = Combo(MidiScales.KeyNames, Math.Clamp(last.Root, 0, 11));
        _key.SelectionChanged += (_, _) => OptionsChanged();
        _mode = Combo(Modes, Math.Clamp(last.ScaleIndex, 0, Modes.Length - 1));
        _mode.SelectionChanged += (_, _) => OptionsChanged();
        _length = Combo(LengthNames(), LengthIndexOf(last.LengthBars));
        _length.SelectionChanged += (_, _) => OptionsChanged();

        var scaleRow = TwoUp(
            ClipEditorKit.Section(Loc.T("midi_random.key"), _key),
            ClipEditorKit.Section(Loc.T("midi_random.mode"), _mode));
        body.Children.Add(scaleRow);

        _octave = Math.Clamp(last.Octave, -1, 1);
        var octaves = ClipEditorKit.Segments(new[] { "−1", "0", "+1" }, _octave + 1,
            i => { _octave = i - 1; OptionsChanged(); }, out _setOctave);
        var lengthRow = TwoUp(
            ClipEditorKit.Section(Loc.T("midi_random.length"), _length),
            ClipEditorKit.Section(Loc.T("midi_random.octave"), octaves));
        body.Children.Add(lengthRow);

        // Mix, density, variation.
        _mix = Slider(last.Mix);
        _mixValue = ValueText(_mix);
        _mix.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") OptionsChanged(); };
        var mixRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        mixRow.Children.Add(_mix);
        Grid.SetColumn(_mixValue, 1); mixRow.Children.Add(_mixValue);
        var mixBody = new StackPanel
        {
            Spacing = 6,
            Children = { mixRow, ClipEditorKit.Mono(Loc.T("midi_random.mix_hint"), 9, TextTertiary) },
        };
        body.Children.Add(ClipEditorKit.Section(Loc.T("midi_random.mix"), mixBody));

        _density = Slider(last.Density);
        _densityValue = ValueText(_density);
        _density.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") OptionsChanged(); };
        body.Children.Add(ClipEditorKit.Section(Loc.T("midi_random.density"), SliderRow(_density, _densityValue)));

        _variation = Slider(last.Variation);
        _variationValue = ValueText(_variation);
        _variation.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") OptionsChanged(); };
        body.Children.Add(ClipEditorKit.Section(Loc.T("midi_random.variation"), SliderRow(_variation, _variationValue)));

        // Seed: the same seed + settings always make the same take.
        _seedText = new TextBlock
        {
            Text = _seed.ToString(NotaNum.Culture), FontSize = NotaType.Body,
            Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center,
        };
        var reroll = ClipEditorKit.Button(Loc.T("midi_random.reroll"), () =>
        {
            _seed = Random.Shared.Next(1, 9999);
            _seedText.Text = _seed.ToString(NotaNum.Culture);
            Generate();
        });
        reroll.HorizontalAlignment = HorizontalAlignment.Right;
        var seedRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        seedRow.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Caps(Loc.T("midi_random.seed")), _seedText },
        });
        Grid.SetColumn(reroll, 1); seedRow.Children.Add(reroll);
        body.Children.Add(seedRow);

        // Output: replace the clip, or layer the take over what is there.
        _replace = last.Replace;
        var output = ClipEditorKit.Segments(
            new[] { Loc.T("midi_random.replace"), Loc.T("midi_random.add") },
            _replace ? 0 : 1, i => { _replace = i == 0; OptionsChanged(); }, out _setOutput, fill: true);
        body.Children.Add(ClipEditorKit.Section(Loc.T("midi_random.output"), output));
        body.Children.Add(new TextBlock
        {
            Text = Loc.T("midi_random.replace_hint"), FontSize = 9, Foreground = TextTertiary,
            TextWrapping = TextWrapping.Wrap,
        });

        _status = new TextBlock
        {
            Text = "", FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(_status);

        // Footer: roll, audition, commit, leave.
        var surprise = ClipEditorKit.Button(Loc.T("midi_random.surprise"), () => { Surprise(); Generate(); });
        var revert = ClipEditorKit.Button(Loc.T("midi_random.revert"), Revert);
        var close = ClipEditorKit.Button(Loc.T("common.close"), Close);
        var generate = ClipEditorKit.Button(Loc.T("midi_random.generate"), Generate);
        generate.Classes.Add("primary");
        _apply = ClipEditorKit.Button(Loc.T("midi_random.apply"), Apply);
        _apply.IsEnabled = false;

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), ColumnSpacing = 8 };
        footer.Children.Add(surprise);
        Grid.SetColumn(revert, 2); footer.Children.Add(revert);
        Grid.SetColumn(close, 3); footer.Children.Add(close);
        Grid.SetColumn(generate, 4); footer.Children.Add(generate);
        Grid.SetColumn(_apply, 5); footer.Children.Add(_apply);
        body.Children.Add(footer);

        UpdateMixEnabled();
        SetBody(body);
        _ready = true;
    }

    /// <summary>Rolls a take with the current settings and previews it in the roll.</summary>
    public void Generate()
    {
        var options = Current();
        s_last = options;
        var result = GenerateRequested?.Invoke(options, _seed) ?? new MidiRandomPreview(0, "");
        _previewNotes = Math.Max(0, result.Notes);
        _status.Text = result.Status;
        _apply.IsEnabled = _previewNotes > 0;
    }

    /// <summary>Every option change reruns the take, so the roll always shows the settings
    /// on screen and Apply can never commit an older preview.</summary>
    private void OptionsChanged()
    {
        if (!_ready || _suspend) return;
        Generate();
    }

    /// <summary>Keeps the previewed take as one undo step and leaves the dialog open for
    /// another roll. Does nothing when there is no preview.</summary>
    public void Apply()
    {
        if (_previewNotes <= 0) return;
        _status.Text = ApplyRequested?.Invoke(_previewNotes) ?? "";
        _previewNotes = 0;
        _apply.IsEnabled = false;
    }

    private void Revert()
    {
        RevertRequested?.Invoke();
        _previewNotes = 0;
        _apply.IsEnabled = false;
        _status.Text = Loc.T("midi_random.reverted");
    }

    private MidiRandomOptions Current()
    {
        int ai = Math.Clamp(_genreA.SelectedIndex, 0, _genres.Count - 1);
        int bi = _genreB.SelectedIndex - 1;
        string? bId = bi >= 0 && bi < _genres.Count && bi != ai ? _genres[bi].Id : null;
        int bars = _length.SelectedIndex switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4, _ => 8 };
        return new MidiRandomOptions(
            _part, _genres[ai].Id, bId, _mix.Value,
            Math.Clamp(_key.SelectedIndex, 0, 11),
            Math.Clamp(_mode.SelectedIndex, 0, Modes.Length - 1),
            bars, _density.Value, _variation.Value, Math.Clamp(_octave, -1, 1), _replace);
    }

    private void UpdateMixEnabled()
    {
        bool hasB = _genreB.SelectedIndex > 0;
        _mix.IsEnabled = hasB;
        _mixValue.Foreground = hasB ? TextSecondary : TextTertiary;
    }

    private void Surprise()
    {
        _suspend = true;
        var rng = Random.Shared;
        _part = (MidiPart)rng.Next(3);
        _setPart((int)_part);
        int a = rng.Next(_genres.Count);
        int b = -1;
        if (_genres.Count > 1 && rng.NextDouble() < .55)
        {
            b = rng.Next(_genres.Count - 1);
            if (b >= a) b++;
        }
        _genreA.SelectedIndex = a;
        _genreB.SelectedIndex = b < 0 ? 0 : b + 1;
        _mix.Value = .35 + rng.NextDouble() * .3;
        _key.SelectedIndex = rng.Next(12);
        _mode.SelectedIndex = rng.Next(Modes.Length);
        _length.SelectedIndex = rng.Next(5);
        _octave = rng.Next(-1, 2);
        _setOctave(_octave + 1);
        _density.Value = .35 + rng.NextDouble() * .4;
        _variation.Value = .15 + rng.NextDouble() * .35;
        _replace = true;
        _setOutput(0);
        _seed = rng.Next(1, 9999);
        _seedText.Text = _seed.ToString(NotaNum.Culture);
        UpdateMixEnabled();
        _suspend = false;
        Generate();
    }

    // ---- small bits ----------------------------------------------------------------------

    private int IndexOf(string id)
    {
        for (int i = 0; i < _genres.Count; i++)
            if (_genres[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private static string[] LengthNames()
    {
        var names = new List<string> { Loc.T("midi_random.whole_clip") };
        foreach (int bars in LengthBars)
            names.Add(Loc.Plural("midi_random.bars", bars));
        return names.ToArray();
    }

    private static int LengthIndexOf(int bars) => bars switch { 1 => 1, 2 => 2, 4 => 3, 8 => 4, _ => 0 };

    private static ComboBox Combo(string[] names, int selected)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string n in names) c.Items.Add(new ComboBoxItem { Content = n });
        c.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, names.Length - 1));
        return c;
    }

    private static Grid TwoUp(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1); grid.Children.Add(right);
        return grid;
    }

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

    private static Grid SliderRow(Slider slider, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(slider);
        Grid.SetColumn(value, 1); grid.Children.Add(value);
        return grid;
    }

    private static TextBlock Caps(string text) => new()
    {
        Text = text, FontSize = 8.5, FontWeight = FontWeight.Medium, Foreground = TextTertiary,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
