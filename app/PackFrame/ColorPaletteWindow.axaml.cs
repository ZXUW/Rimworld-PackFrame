using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace PackFrame;

public partial class ColorPaletteWindow : Window
{
    private static readonly string[] Palette =
    {
        "#E05A5A",
        "#E07A4F",
        "#E0A64F",
        "#D6C85A",
        "#8FCB6A",
        "#4FC18F",
        "#4FC1C1",
        "#5AA9E0",
        "#5A7FE0",
        "#8A6FE0",
        "#C16AD6",
        "#E06AB0",
        "#E06A8A",
        "#B8C4D4",
        "#EAF1F8",
        "#9AA7B8",
        "#7A8A9E",
        "#6E7F91",
        "#A88A6A",
        "#C2A36B",
        "#6FAF8F",
        "#4F8F8A",
        "#6F7FBF",
        "#9F6FBF",
        "#C25A6A",
        "#5A6A7A"
    };

    private readonly Dictionary<string, TextBox> _labelBoxes =
        new(StringComparer.OrdinalIgnoreCase);

    public ColorPaletteWindow()
    {
        InitializeComponent();
    }

    public ColorPaletteWindow(
        string? currentColor,
        IReadOnlyDictionary<string, string> labels,
        string? language
    )
        : this()
    {
        var title = Localization.ColorPaletteTitle(language);
        Title = $"Rimworld PackFrame - {title}";
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("HintText")!.Text =
            Localization.ColorPaletteHint(language);
        this.FindControl<Button>("ClearButton")!.Content =
            Localization.ClearColor(language);
        this.FindControl<Button>("CancelButton")!.Content =
            Localization.CancelButton(language);

        var panel = this.FindControl<WrapPanel>("PalettePanel")!;
        foreach (var hex in Palette)
        {
            var color = Color.Parse(hex);
            var isCurrent = string.Equals(
                hex,
                currentColor,
                StringComparison.OrdinalIgnoreCase
            );
            var item = new StackPanel
            {
                Width = 132,
                Margin = new Thickness(4, 2)
            };
            var button = new Button
            {
                Width = 132,
                Height = 32,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = isCurrent
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Color.Parse("#455364")),
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
                CornerRadius = new CornerRadius(0)
            };
            ToolTip.SetTip(button, hex);
            var selectedHex = hex;
            button.Click += (_, _) => Choose(selectedHex);
            item.Children.Add(button);

            var labelBox = new TextBox
            {
                Width = 132,
                Height = 26,
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(6, 2),
                Background = new SolidColorBrush(Color.Parse("#455364")),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                FontSize = 13,
                PlaceholderText = Localization.ColorLabelPlaceholder(language),
                Text = labels.TryGetValue(hex, out var label) ? label : string.Empty
            };
            item.Children.Add(labelBox);
            _labelBoxes[hex] = labelBox;
            panel.Children.Add(item);
        }

        KeyDown += OnWindowKeyDown;
    }

    private Dictionary<string, string> ReadLabels()
    {
        var labels = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var pair in _labelBoxes)
        {
            labels[pair.Key] = pair.Value.Text?.Trim() ?? string.Empty;
        }

        return labels;
    }

    private void Choose(string hex)
    {
        Close(new ModColorChoice(false, false, hex, ReadLabels()));
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(new ModColorChoice(true, false, null, ReadLabels()));
            e.Handled = true;
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        Close(new ModColorChoice(false, true, null, ReadLabels()));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new ModColorChoice(true, false, null, ReadLabels()));
    }
}
