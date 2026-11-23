using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

internal sealed class ColorPickerWindow : Window
{
    private readonly Slider _red;
    private readonly Slider _green;
    private readonly Slider _blue;
    private readonly Border _preview;
    private readonly TextBlock _hexText;
    private readonly string? _language;

    internal ColorPickerWindow(string? language)
    {
        _language = language;
        Title = T("Pick a color", "选择颜色", "選擇顏色", "色を選択");
        Width = 380;
        Height = 340;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;

        _red = CreateSlider();
        _green = CreateSlider();
        _blue = CreateSlider();
        _red.Value = 90;
        _green.Value = 160;
        _blue.Value = 230;
        _preview = new Border
        {
            Height = 46,
            CornerRadius = new CornerRadius(4),
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(1)
        };
        _hexText = new TextBlock
        {
            FontSize = 12,
            Foreground = ThemeService.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var previewRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        previewRow.Children.Add(_preview);
        Grid.SetColumn(_hexText, 1);
        previewRow.Children.Add(_hexText);

        var sliders = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Margin = new Thickness(0, 12, 0, 0)
        };
        sliders.Children.Add(SliderRow("R", _red));
        var greenRow = SliderRow("G", _green);
        Grid.SetRow(greenRow, 1);
        sliders.Children.Add(greenRow);
        var blueRow = SliderRow("B", _blue);
        Grid.SetRow(blueRow, 2);
        sliders.Children.Add(blueRow);

        var save = new Button
        {
            Content = T("Save", "保存", "儲存", "保存"),
            IsDefault = true,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        var cancel = new Button
        {
            Content = T("Cancel", "取消", "取消", "キャンセル"),
            IsCancel = true,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        save.Click += (_, _) => Close(CurrentHex());
        cancel.Click += (_, _) => Close(null);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, save }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 0,
            Children = { previewRow, sliders, actions }
        };

        _red.PropertyChanged += (_, e) => OnValueChanged(e.Property);
        _green.PropertyChanged += (_, e) => OnValueChanged(e.Property);
        _blue.PropertyChanged += (_, e) => OnValueChanged(e.Property);
        UpdatePreview();
    }

    private static Slider CreateSlider() => new()
    {
        Minimum = 0,
        Maximum = 255,
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        SmallChange = 1,
        LargeChange = 16,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Control SliderRow(string label, Slider slider)
    {
        var value = new TextBlock
        {
            Width = 34,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeService.SecondaryTextBrush,
            FontSize = 12
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                value.Text = ((int)slider.Value).ToString(CultureInfo.InvariantCulture);
            }
        };
        value.Text = ((int)slider.Value).ToString(CultureInfo.InvariantCulture);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("20,*,40"),
            Margin = new Thickness(0, 2)
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeService.PrimaryTextBrush
        });
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        return row;
    }

    private void OnValueChanged(AvaloniaProperty property)
    {
        if (property == Slider.ValueProperty) UpdatePreview();
    }

    private Color CurrentColor() => Color.FromRgb(
        (byte)Math.Round(_red.Value),
        (byte)Math.Round(_green.Value),
        (byte)Math.Round(_blue.Value));

    private string CurrentHex()
    {
        var color = CurrentColor();
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void UpdatePreview()
    {
        var color = CurrentColor();
        _preview.Background = new SolidColorBrush(color);
        _hexText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private string T(string en, string zh, string tw, string ja) =>
        OperationText.Choose(_language, en, zh, tw, ja);
}
