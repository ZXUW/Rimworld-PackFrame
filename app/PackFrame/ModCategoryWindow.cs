using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

internal sealed class ModCategoryWindow : Window
{
    private readonly string? _language;
    private readonly Func<IReadOnlyList<CategoryTemplateEntry>> _loadTemplates;
    private readonly Func<CategoryTemplateEntry, Window?> _createViewer;
    private readonly Func<CategoryTemplateEntry, bool, Task<string?>> _applyTemplate;
    private readonly Func<Task<string?>> _saveCurrent;
    private readonly Func<Task<string?>> _clearCurrent;
    private readonly Func<Task<string?>> _importTemplate;
    private readonly Func<Task<string?>> _exportCurrent;
    private readonly Func<CategoryTemplateEntry, Task<string?>> _deleteTemplate;
    private readonly StackPanel _rows;
    private readonly StackPanel _bottom;
    private readonly TextBlock _status;
    private bool _busy;

    internal ModCategoryWindow(
        string? language,
        Func<IReadOnlyList<CategoryTemplateEntry>> loadTemplates,
        Func<CategoryTemplateEntry, Window?> createViewer,
        Func<CategoryTemplateEntry, bool, Task<string?>> applyTemplate,
        Func<Task<string?>> saveCurrent,
        Func<Task<string?>> clearCurrent,
        Func<Task<string?>> importTemplate,
        Func<Task<string?>> exportCurrent,
        Func<CategoryTemplateEntry, Task<string?>> deleteTemplate)
    {
        _language = language;
        _loadTemplates = loadTemplates;
        _createViewer = createViewer;
        _applyTemplate = applyTemplate;
        _saveCurrent = saveCurrent;
        _clearCurrent = clearCurrent;
        _importTemplate = importTemplate;
        _exportCurrent = exportCurrent;
        _deleteTemplate = deleteTemplate;

        Title = T("Mod categories", "模组分类", "模組分類", "MOD分類");
        Width = 780;
        SizeToContent = SizeToContent.Height;
        MinWidth = 680;
        MaxHeight = 560;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;

        _rows = new StackPanel { Spacing = 6 };
        _status = new TextBlock
        {
            Foreground = ThemeService.SecondaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        _bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var scroll = new ScrollViewer
        {
            Content = _rows,
            Height = 136,
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        _bottom.Children.Add(MakeButton(T("Save", "保存", "儲存", "保存"), () => RunAsync(_saveCurrent)));
        _bottom.Children.Add(MakeButton(T("Clear all", "清空目前分类", "清空目前分類", "現在の分類を消去"), () => RunAsync(_clearCurrent)));
        _bottom.Children.Add(MakeButton(T("Import", "导入", "匯入", "インポート"), () => RunAsync(_importTemplate)));
        _bottom.Children.Add(MakeButton(T("Export", "导出", "匯出", "エクスポート"), () => RunAsync(_exportCurrent)));
        _bottom.Children.Add(MakeButton(T("Cancel", "取消", "取消", "キャンセル"), () =>
        {
            Close();
            return Task.CompletedTask;
        }));

        var root = new Grid
        {
            Margin = new Thickness(6),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto")
        };
        Grid.SetRow(scroll, 0);
        Grid.SetRow(_status, 1);
        Grid.SetRow(_bottom, 2);
        _status.Margin = new Thickness(4, 2, 4, 2);
        root.Children.Add(scroll);
        root.Children.Add(_status);
        root.Children.Add(_bottom);
        Content = root;

        Opened += (_, _) => Reload();
    }

    private void Reload()
    {
        try
        {
            _rows.Children.Clear();
            foreach (var entry in _loadTemplates())
            {
                _rows.Children.Add(BuildRow(entry));
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Load category templates", exception);
            SetStatus(T("Could not load category templates.", "无法读取分类模板", "無法讀取分類模板", "分類テンプレートを読み込めません"));
        }
    }

    private Control BuildRow(CategoryTemplateEntry entry)
    {
        var border = new Border
        {
            Background = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(0)
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,140,110,*"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = entry.IsBuiltIn
                ? T("PackFrame repository", "PackFrame仓库", "PackFrame倉庫", "PackFrame リポジトリ")
                : entry.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = ThemeService.PrimaryTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(name, name.Text);
        var date = new TextBlock
        {
            Text = entry.GeneratedAt?.ToString("yyyy-MM-dd  HH:mm") ??
                T("Not downloaded", "未下载", "未下載", "未ダウンロード"),
            Foreground = ThemeService.SecondaryTextBrush,
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var count = new TextBlock
        {
            Text = T($"{entry.Count:N0} entries", $"{entry.Count:N0} 条", $"{entry.Count:N0} 條", $"{entry.Count:N0} 件"),
            Foreground = ThemeService.SecondaryTextBrush,
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8
        };
        actions.Children.Add(MakeRowButton(
            T("View", "查看", "查看", "表示"),
            entry.Count > 0,
            () => OpenViewer(entry)));
        actions.Children.Add(MakeRowButton(
            T("Supplement", "补充分类", "補充分類", "分類を補完"),
            entry.Count > 0,
            () => RunAsync(() => _applyTemplate(entry, false))));
        actions.Children.Add(MakeRowButton(
            T("Overwrite", "覆盖分类", "覆蓋分類", "分類を上書き"),
            entry.Count > 0,
            () => RunAsync(() => _applyTemplate(entry, true))));
        if (!entry.IsBuiltIn)
        {
            actions.Children.Add(MakeRowButton(
                T("Delete", "删除", "刪除", "削除"),
                true,
                () => RunAsync(() => _deleteTemplate(entry))));
        }

        grid.Children.Add(name);
        Grid.SetColumn(name, 0);
        grid.Children.Add(date);
        Grid.SetColumn(date, 1);
        grid.Children.Add(count);
        Grid.SetColumn(count, 2);
        grid.Children.Add(actions);
        Grid.SetColumn(actions, 3);

        border.Child = grid;
        return border;
    }

    private async Task OpenViewer(CategoryTemplateEntry entry)
    {
        var viewer = _createViewer(entry);
        if (viewer is not null) await viewer.ShowDialog(this);
    }

    private Button MakeRowButton(string text, bool enabled, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
            Background = ThemeService.BackgroundBrush,
            Foreground = ThemeService.PrimaryTextBrush,
            MinHeight = 26,
            Padding = new Thickness(8, 3),
            BorderThickness = new Thickness(0),
            IsEnabled = enabled
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private Button MakeButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
            MinHeight = 28,
            Padding = new Thickness(10, 3)
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private async Task RunAsync(Func<Task<string?>> action)
    {
        if (_busy) return;
        _busy = true;
        _rows.IsEnabled = false;
        _bottom.IsEnabled = false;
        try
        {
            var message = await action();
            Reload();
            SetStatus(message);
        }
        catch (Exception exception)
        {
            AppLog.Error("Category template action", exception);
            SetStatus(T("Action failed.", "操作失败", "操作失敗", "操作に失敗しました"));
        }
        finally
        {
            _busy = false;
            _rows.IsEnabled = true;
            _bottom.IsEnabled = true;
        }
    }

    private void SetStatus(string? text)
    {
        _status.Text = text ?? string.Empty;
        _status.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private string T(string en, string zh, string tw, string ja) =>
        OperationText.Choose(_language, en, zh, tw, ja);
}
