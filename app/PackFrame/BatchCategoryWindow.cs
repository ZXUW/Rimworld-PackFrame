using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace PackFrame;

public sealed record BatchCategoryChoice(bool Start, string? Category, bool Clear);

internal sealed class BatchCategoryWindow : Window
{
    internal sealed record ModRow(string Name, string Category, bool Dim);

    private readonly string? _language;
    private readonly bool _viewer;
    private readonly TextBox _input;
    private readonly CategoryMapView _map;
    private readonly TextBlock _status;
    private readonly TextBlock _modHeader;
    private readonly ListBox _modList;
    private readonly Border _sideFrame;
    private readonly CategoryMapNode _root;
    private bool _syncing;
    private bool _panning;
    private Point _panPoint;

    internal BatchCategoryWindow(
        IReadOnlyList<string> categories,
        IReadOnlyList<(string Name, string? Category)> mods,
        string? language,
        string? viewerTitle = null)
    {
        _language = language;
        // Same map and mod column, opened read-only to browse a category table.
        _viewer = viewerTitle is not null;
        Title = viewerTitle ?? T("Batch category", "批量分类", "批次分類", "分類を一括");
        Width = 1160;
        Height = 660;
        MinWidth = 720;
        MinHeight = 480;
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;

        _map = new CategoryMapView { RootLabel = T("All categories", "全部分类", "全部分類", "すべての分類") };
        _root = CategoryMap.Build(categories, mods);
        _map.SetRoot(_root);
        _map.SelectionChanged += (_, _) => SyncFromMap();

        _status = new TextBlock
        {
            Foreground = ThemeService.MutedTextBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _input = new TextBox
        {
            PlaceholderText = _viewer
                ? T("Search a category", "搜索分类", "搜尋分類", "分類を検索")
                : T(
                    "Search or type a category (use / for levels, e.g. Race/XXX), then click Batch category brush",
                    "搜索选择或输入分类（用 / 分层级，如 种族/XXX），然后点击批量分类刷子",
                    "搜尋選擇或輸入分類（用 / 分層級，如 種族/XXX），然後點擊批次分類刷子",
                    "分類を検索・選択または入力（/ で階層、例: 種族/XXX）、その後「分類ブラシ」をクリック")
        };
        _input.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            // Selecting a node fills this box with its path; that is not a search.
            if (string.Equals(_input.Text, _map.SelectedPath, StringComparison.Ordinal)) return;
            _map.SetQuery(_input.Text);
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || _viewer) return;
            Apply(_input.Text);
            e.Handled = true;
        };

        var fit = SmallButton(T("Fit", "适应窗口", "適應視窗", "ウィンドウに合わせる"));
        fit.Click += (_, _) => _map.FitToWindow();
        var collapse = SmallButton(CollapseLabel());
        collapse.Click += (_, _) =>
        {
            _map.ToggleCollapse();
            collapse.Content = CollapseLabel();
        };

        var start = new Button
        {
            Content = T("Batch category brush", "批量分类刷子", "批次分類刷子", "分類ブラシ"),
            IsDefault = true,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        var clear = new Button
        {
            Content = T("Batch clear (brush)", "批量清除分类刷子", "批次清除分類刷子", "分類クリア（ブラシ）"),
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        var cancel = new Button
        {
            Content = T("Cancel", "取消", "取消", "キャンセル"),
            IsCancel = true,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        start.Click += (_, _) => Apply(_input.Text);
        clear.Click += (_, _) => Close(new BatchCategoryChoice(false, null, true));
        cancel.Click += (_, _) => Close();
        if (_viewer)
        {
            // Nothing to pick here; keep the search box and the map only.
            start.IsVisible = false;
            start.IsDefault = false;
            clear.IsVisible = false;
            cancel.Content = T("Close", "关闭", "關閉", "閉じる");
        }

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Spacing = 6,
            // Sits just left of the side guide line.
            Margin = new Thickness(0, 6, 8, 0),
            Children = { fit, collapse }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { clear, start, cancel }
        };
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        actions.Children.Add(_status);
        Grid.SetColumn(buttons, 1);
        actions.Children.Add(buttons);

        // Search / direct input sits under the map, above the action row.
        var locate = new Button
        {
            Content = CreateLocateIcon(ThemeService.PrimaryTextBrush),
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
            Background = Brushes.Transparent,
            Width = 34,
            MinWidth = 34,
            MinHeight = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        ToolTip.SetTip(locate, T(
            "Locate selection",
            "定位到选中分类",
            "定位到選取分類",
            "選択した分類へ移動"));
        locate.Click += (_, _) => _map.Locate();

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        searchRow.Children.Add(_input);
        Grid.SetColumn(locate, 1);
        searchRow.Children.Add(locate);

        var bottom = new StackPanel
        {
            Spacing = 8,
            // A little breathing room before the guide line on the right.
            Margin = new Thickness(0, 10, 12, 0),
            Children = { searchRow, actions }
        };

        // Right-hand flat list: mods of the picked category, then its subfolders dimmed.
        _modHeader = new TextBlock
        {
            FontSize = 12,
            Foreground = ThemeService.SecondaryTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _modList = new ListBox
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<ModRow>((row, _) => CreateModRow(row))
        };
        ToolTip.SetServiceEnabled(_modList, false);
        _modList.PointerReleased += (_, _) =>
        {
            if (_modList.SelectedItem is not ModRow row) return;
            _map.SelectedPath = row.Category;
            SyncFromMap();
        };

        var side = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Width = 228,
            Margin = new Thickness(0, 0, 0, 0)
        };
        side.Children.Add(_modHeader);
        Grid.SetRow(_modList, 1);
        side.Children.Add(_modList);

        // Guide line at the left edge of the mod column, top to bottom of the window.
        var sideFrame = new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(12, 4, 0, 4),
            Margin = new Thickness(0),
            Child = side
        };
        _sideFrame = sideFrame;

        var mapHost = new Grid();
        mapHost.Children.Add(_map);
        mapHost.Children.Add(tools);

        // Map and its action bar share the left column; the mod column spans both rows.
        var stage = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        stage.Children.Add(mapHost);
        Grid.SetRow(bottom, 1);
        stage.Children.Add(bottom);
        Grid.SetColumn(sideFrame, 1);
        Grid.SetRowSpan(sideFrame, 2);
        stage.Children.Add(sideFrame);

        var root = new Grid { Margin = new Thickness(18) };
        root.Children.Add(stage);
        Content = root;
        UpdateModList();

        Opened += (_, _) =>
        {
            _input.Focus();
            _map.FitToWindow();
        };
        AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnWindowWheel,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerPressedEvent,
            OnWindowPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerMovedEvent,
            OnWindowMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerReleasedEvent,
            OnWindowReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        UpdateStatus();
    }

    // Wheel zooms and drag pans from anywhere in the window; over a drum the wheel
    // scrolls that level, and over the mod column it scrolls that list.
    private void OnWindowWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || IsWithin(e.Source, _sideFrame)) return;
        var local = e.GetPosition(_map);
        var inside = local.X >= 0 && local.Y >= 0 &&
                     local.X <= _map.Bounds.Width && local.Y <= _map.Bounds.Height;
        // Outside the canvas we still zoom, aiming at the nearest edge point.
        var point = inside
            ? local
            : new Point(
                Math.Clamp(local.X, 16, Math.Max(16, _map.Bounds.Width - 16)),
                Math.Clamp(local.Y, 16, Math.Max(16, _map.Bounds.Height - 16)));
        if (!_map.TryRotateAt(point, e.Delta.Y, e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            _map.ZoomAt(point, e.Delta.Y);
        }
        e.Handled = true;
    }

    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsWithin(e.Source, _map) || IsInteractive(e.Source)) return;
        _panning = true;
        _panPoint = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    private void OnWindowMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning) return;
        var point = e.GetPosition(this);
        _map.PanBy(point.X - _panPoint.X, point.Y - _panPoint.Y);
        _panPoint = point;
    }

    private void OnWindowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
    }

    // Buttons, text fields and scrollbars keep their own pointer handling.
    private static bool IsInteractive(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button or TextBox or ComboBox or ScrollBar or ListBox or CheckBox or Slider)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsWithin(object? source, Control target)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, target)) return true;
        }
        return false;
    }

    private string CollapseLabel() => _map.AllCollapsed
        ? T("Expand all", "全部展开", "全部展開", "すべて展開")
        : T("Collapse all", "全部折叠", "全部摺疊", "すべて折りたたむ");

    private Button SmallButton(string text) => new()
    {
        Content = text,
        Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
        MinHeight = 28,
        Padding = new Thickness(10, 4),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };

    // Classic locate glyph: crosshair ticks around a large ring with a small ring inside.
    // Container recycling can ask for a null row; keep the template null-safe.
    private static TextBlock CreateModRow(ModRow? row) => new()
    {
        Text = row?.Name ?? string.Empty,
        FontSize = 12,
        Opacity = row?.Dim == true ? 0.5 : 1,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = row?.Dim == true ? ThemeService.SecondaryTextBrush : ThemeService.PrimaryTextBrush,
        Margin = new Thickness(2, 1)
    };

    private static Control CreateLocateIcon(IBrush brush)
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        var outer = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = brush,
            StrokeThickness = 1.4
        };
        Canvas.SetLeft(outer, 2);
        Canvas.SetTop(outer, 2);
        canvas.Children.Add(outer);

        var inner = new Ellipse
        {
            Width = 4,
            Height = 4,
            Stroke = brush,
            StrokeThickness = 1.4
        };
        Canvas.SetLeft(inner, 6);
        Canvas.SetTop(inner, 6);
        canvas.Children.Add(inner);

        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 8,0 L 8,3 M 8,13 L 8,16 M 0,8 L 3,8 M 13,8 L 16,8"),
            Stroke = brush,
            StrokeThickness = 1.4
        });
        return canvas;
    }

    private void SyncFromMap()
    {
        var path = _map.SelectedPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        // Picking a node leaves search mode instead of flying the camera around.
        _map.SetQuery(null);
        _syncing = true;
        _input.Text = path;
        _input.CaretIndex = path.Length;
        _syncing = false;
        UpdateStatus();
        UpdateModList();
    }

    private void UpdateModList()
    {
        var rows = new List<ModRow>();
        var path = _map.SelectedPath;
        CategoryMapNode? node = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            node = CategoryMap.Flatten(_root).FirstOrDefault(candidate =>
                string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        if (node is null || node.TotalMods == 0)
        {
            _modHeader.Text = T(
                "Pick a category to list its mods",
                "点选分类查看其中的模组",
                "點選分類查看其中的模組",
                "分類を選ぶと MOD 一覧を表示します");
            _modList.ItemsSource = rows;
            return;
        }

        foreach (var entry in CategoryMap.CollectMods(node))
        {
            foreach (var name in entry.Mods)
            {
                rows.Add(new ModRow(name, entry.Path, !entry.Direct));
            }
        }

        _modHeader.Text =
            $"{node.Path}\n" +
            $"{T("direct", "直接", "直接", "直接")} {node.Mods.Count} / " +
            $"{T("with subfolders", "含子分类", "含子分類", "サブ含む")} {node.TotalMods}";
        _modList.ItemsSource = rows;
    }

    private void UpdateStatus()
    {
        var path = _map.SelectedPath;
        _status.Text = string.IsNullOrWhiteSpace(path)
            ? T("No category selected", "未选择分类", "未選擇分類", "分類が未選択")
            : T("Selected: ", "已选：", "已選：", "選択中: ") + path;
    }

    private void Apply(string? typed)
    {
        if (_viewer) return;
        var category = CategoryTree.NormalizePath(typed);
        if (string.IsNullOrWhiteSpace(category)) category = _map.SelectedPath;
        if (string.IsNullOrWhiteSpace(category)) return;
        Close(new BatchCategoryChoice(true, category, false));
    }

    private string T(string en, string zh, string tw, string ja) =>
        OperationText.Choose(_language, en, zh, tw, ja);
}
