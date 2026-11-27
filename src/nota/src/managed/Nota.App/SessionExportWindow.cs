// SPDX-License-Identifier: AGPL-3.0-only

// Export Session loops: the current deck's filled slots become one folder per scene,
// with a WAV per track and an optional MIDI file for instrument clips.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed record SessionExportOptions(int SampleRate, WavBitDepth Depth, bool IncludeMidi);

public sealed class SessionExportWindow : NotaWindow
{
    private static readonly int[] Rates = { 44100, 48000, 96000 };
    private static readonly string[] RateLabels = { "44 100", "48 000", "96 000" };

    private readonly ComboBox _rate;
    private readonly ComboBox _depth;
    private readonly CheckBox _midi;

    public SessionExportWindow(int scenes, int loops, int midiClips)
    {
        Title = Loc.T("session_export.title");
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Close(null); } };

        var body = new StackPanel { Margin = new Thickness(20, 18), Spacing = 14 };

        body.Children.Add(new TextBlock
        {
            Text = Loc.F("session_export.summary", scenes, loops, midiClips),
            FontSize = 12,
            Foreground = NotaPalette.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = Loc.T("session_export.note"),
            FontSize = 11,
            Foreground = NotaPalette.TextTertiary,
            TextWrapping = TextWrapping.Wrap,
        });

        _rate = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int i = 0; i < Rates.Length; i++) _rate.Items.Add(new ComboBoxItem { Content = RateLabels[i] });
        _rate.SelectedIndex = 1;

        _depth = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _depth.Items.Add(new ComboBoxItem { Content = Loc.T("session_export.pcm16") });
        _depth.Items.Add(new ComboBoxItem { Content = Loc.T("session_export.pcm24") });
        _depth.Items.Add(new ComboBoxItem { Content = Loc.T("session_export.float32") });
        _depth.SelectedIndex = 1;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        var rateBlock = Field(Loc.T("export.sample_rate"), _rate);
        var depthBlock = Field(Loc.T("export.bit_depth"), _depth);
        grid.Children.Add(rateBlock);
        Grid.SetColumn(depthBlock, 1);
        grid.Children.Add(depthBlock);
        body.Children.Add(grid);

        _midi = new CheckBox
        {
            Content = Loc.T("session_export.include_midi"),
            IsChecked = true,
        };
        body.Children.Add(_midi);

        var export = new Border
        {
            Background = NotaPalette.SurfaceHover,
            BorderBrush = NotaPalette.Seam,
            BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Field,
            Padding = new Thickness(18, 0),
            Height = 28,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = Loc.T("session_export.export"),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = NotaPalette.TextHeading,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        export.BindResource(Border.BoxShadowProperty, "Shadow.Raised");
        export.PointerPressed += (_, _) =>
        {
            int sr = Rates[Math.Clamp(_rate.SelectedIndex, 0, Rates.Length - 1)];
            var depth = _depth.SelectedIndex switch
            {
                0 => WavBitDepth.Pcm16,
                2 => WavBitDepth.Float32,
                _ => WavBitDepth.Pcm24,
            };
            Close(new SessionExportOptions(sr, depth, _midi.IsChecked == true));
        };

        var cancel = new TextBlock
        {
            Text = Loc.T("common.cancel"),
            FontSize = 10,
            Foreground = NotaPalette.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        cancel.PointerPressed += (_, _) => Close(null);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        footer.Children.Add(export);
        Grid.SetColumn(cancel, 2);
        footer.Children.Add(cancel);
        body.Children.Add(footer);

        SetBody(body);
    }

    private static Control Field(string title, Control control)
        => new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = title, FontSize = 9, Foreground = NotaPalette.TextTertiary },
                control,
            },
        };
}
