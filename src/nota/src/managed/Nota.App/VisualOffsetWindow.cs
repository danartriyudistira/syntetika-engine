// SPDX-License-Identifier: AGPL-3.0-only
//
// Syntetika Engine — A/V offset and calibration.
//
// The picture can lag the sound on a projector or a slow display. The slider shifts every
// visual launch and the video phase by ±200 ms; the calibration strobe on openVJ's output
// follows the same offset, so lining the white flash up with Nota's metronome click sets it.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

public sealed class VisualOffsetWindow : Window
{
    /// <summary>The A/V slider moved; the value is already clamped to ±200 ms.</summary>
    public event Action<int>? OffsetChanged;
    /// <summary>The MIDI out slider moved; the value is already clamped to ±200 ms.</summary>
    public event Action<int>? MidiOffsetChanged;
    /// <summary>The calibration strobe was switched on or off.</summary>
    public event Action<bool>? CalibrationToggled;

    private readonly Slider _slider;
    private readonly TextBlock _value;
    private readonly Slider _midiSlider;
    private readonly TextBlock _midiValue;

    public VisualOffsetWindow(int offsetMs, int midiOffsetMs)
    {
        Title = Loc.T("visual.av_offset_title");
        Width = 440;
        Height = 430;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.Panel;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 18), Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("visual.av_offset_hint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = NotaPalette.TextSecondary,
            FontSize = 12,
        });

        int start = Math.Clamp(offsetMs, -200, 200);
        _value = new TextBlock
        {
            Text = Loc.F("visual.av_offset_value", start),
            FontSize = 15,
            Foreground = NotaPalette.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _slider = new Slider
        {
            Minimum = -200,
            Maximum = 200,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Value = start,
            Margin = new Thickness(0, 2, 0, 0),
        };
        _slider.ValueChanged += (_, _) =>
        {
            int v = Math.Clamp((int)Math.Round(_slider.Value), -200, 200);
            _value.Text = Loc.F("visual.av_offset_value", v);
            OffsetChanged?.Invoke(v);
        };
        panel.Children.Add(_slider);
        panel.Children.Add(_value);

        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("visual.midi_offset_label"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary,
            Margin = new Thickness(0, 6, 0, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("visual.midi_offset_hint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = NotaPalette.TextSecondary,
            FontSize = 12,
        });

        int midiStart = Math.Clamp(midiOffsetMs, -200, 200);
        _midiValue = new TextBlock
        {
            Text = Loc.F("visual.av_offset_value", midiStart),
            FontSize = 15,
            Foreground = NotaPalette.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _midiSlider = new Slider
        {
            Minimum = -200,
            Maximum = 200,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Value = midiStart,
            Margin = new Thickness(0, 2, 0, 0),
        };
        _midiSlider.ValueChanged += (_, _) =>
        {
            int v = Math.Clamp((int)Math.Round(_midiSlider.Value), -200, 200);
            _midiValue.Text = Loc.F("visual.av_offset_value", v);
            MidiOffsetChanged?.Invoke(v);
        };
        panel.Children.Add(_midiSlider);
        panel.Children.Add(_midiValue);

        var calibrate = new CheckBox
        {
            Content = Loc.T("visual.calibrate"),
            IsChecked = true,
            Margin = new Thickness(0, 4, 0, 0),
        };
        calibrate.IsCheckedChanged += (_, _) => CalibrationToggled?.Invoke(calibrate.IsChecked == true);
        panel.Children.Add(calibrate);

        var close = new Button
        {
            Content = Loc.T("common.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);

        Content = panel;
    }
}
