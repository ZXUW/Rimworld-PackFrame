using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

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
    private readonly string? _packRoot;
    private readonly string? _language;
    private readonly string? _currentColor;

    public ColorPaletteWindow()
    {
        InitializeComponent();
    }

    public ColorPaletteWindow(
        string? currentColor,
        IReadOnlyDictionary<string, string> labels,
        string? packRoot,
        string? language
    )
        : this()
    {
        _currentColor = currentColor;
        _packRoot = packRoot;
        _language = language;

        var title = Localization.ColorPaletteTitle(language);
        Title = $"Rimworld PackFrame - {title}";
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("HintText")!.Text =
            Localization.ColorPaletteHint(language);
        this.FindControl<Button>("ClearButton")!.Content =
            Localization.ClearColor(language);
        this.FindControl<Button>("CancelButton")!.Content =
            Localization.CancelButton(language);
        this.FindControl<TextBlock>("ShortcutHint")!.Text = OperationText.Choose(
            language,
            "Right-click cancels the current tool",
            "右键取消功能选择",
            "右鍵取消功能選擇",
            "右クリックで選択中の機能を解除");

        BuildPalette(labels);
        KeyDown += OnWindowKeyDown;
    }

    private void BuildPalette(IReadOnlyDictionary<string, string> labels)
    {
        var panel = this.FindControl<WrapPanel>("PalettePanel")!;
        panel.Children.Clear();
        _labelBoxes.Clear();

        var custom = new List<string>();
        if (!string.IsNullOrWhiteSpace(_packRoot))
        {
            foreach (var hex in ModColorStore.GetCustomColors(_packRoot))
            {
                if (!custom.Contains(hex)) custom.Add(hex);
            }
        }

        foreach (var hex in Palette)
        {
            AddTile(panel, hex, false, labels);
        }

        foreach (var hex in custom)
        {
            if (Palette.Contains(hex, StringComparer.OrdinalIgnoreCase)) continue;
            AddTile(panel, hex, true, labels);
        }

        panel.Children.Add(CreateAddTile());
    }

    private void AddTile(
        WrapPanel panel,
        string hex,
        bool isCustom,
        IReadOnlyDictionary<string, string> labels)
    {
        if (!Color.TryParse(hex, out var color)) return;
        var isCurrent = string.Equals(hex, _currentColor, StringComparison.OrdinalIgnoreCase);

        var item = new StackPanel
        {
            Width = 132,
            Margin = new Thickness(4, 2)
        };

        var swatch = new Button
        {
            Width = 132,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(color),
            BorderBrush = isCurrent
                ? new SolidColorBrush(Colors.White)
                : ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(isCurrent ? 2 : 1),
            CornerRadius = new CornerRadius(0)
        };
        ToolTip.SetTip(swatch, hex);
        var selectedHex = hex;
        swatch.Click += (_, _) => Choose(selectedHex);

        if (isCustom)
        {
            // Custom presets carry a small delete cross in their top-left corner.
            var holder = new Grid { Width = 132, Height = 32 };
            holder.Children.Add(swatch);
            var remove = new Border
            {
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"),
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.6,
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(remove, OperationText.Choose(
                _language,
                "Delete this preset",
                "删除该颜色预设",
                "刪除該顏色預設",
                "このプリセットを削除"));
            remove.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ModColorStore.RemoveCustomColor(_packRoot, selectedHex);
                BuildPalette(ReadLabels());
            };
            holder.Children.Add(remove);
            item.Children.Add(holder);
        }
        else
        {
            item.Children.Add(swatch);
        }

        var labelBox = new TextBox
        {
            Width = 132,
            Height = 26,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(6, 2),
            Background = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            FontSize = 13,
            PlaceholderText = Localization.ColorLabelPlaceholder(_language),
            Text = labels.TryGetValue(hex, out var label) ? label : string.Empty
        };
        item.Children.Add(labelBox);
        _labelBoxes[hex] = labelBox;
        panel.Children.Add(item);
    }

    private Button CreateAddTile()
    {
        var button = new Button
        {
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 7,0 L 7,14 M 0,7 L 14,7"),
                Stroke = ThemeService.PrimaryTextBrush,
                StrokeThickness = 1.6,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 132,
            Height = 32,
            Margin = new Thickness(4, 2),
            Padding = new Thickness(0),
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
            Background = Brushes.Transparent
        };
        ToolTip.SetTip(button, OperationText.Choose(
            _language,
            "Add a color",
            "添加颜色",
            "新增顏色",
            "色を追加"));
        button.Click += async (_, _) =>
        {
            var picker = new ColorPickerWindow(_language);
            var hex = await picker.ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(hex)) return;
            ModColorStore.AddCustomColor(_packRoot, hex);
            BuildPalette(ReadLabels());
        };
        return button;
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
