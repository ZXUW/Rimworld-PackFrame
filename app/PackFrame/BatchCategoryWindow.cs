using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

public sealed record BatchCategoryChoice(bool Start, string? Category, bool Clear);

internal sealed class BatchCategoryWindow : Window
{
    private readonly string? _language;

    internal BatchCategoryWindow(IReadOnlyList<string> categories, string? language)
    {
        _language = language;
        Title = T("Batch category", "批量分类", "批次分類", "分類を一括");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;

        var list = new ListBox
        {
            ItemsSource = categories,
            MaxHeight = 220,
            IsVisible = categories.Count > 0
        };
        var input = new TextBox
        {
            PlaceholderText = T("Category (use / for levels, e.g. Race/Milira)", "分类（用 / 分层级，如 种族/米莉拉）", "分類（用 / 分層級，如 種族/米莉拉）", "分類（/ で階層分け、例: 種族/ミリラ）")
        };
        var syncing = false;
        list.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            if (list.SelectedItem is string value)
            {
                syncing = true;
                input.Text = value;
                input.CaretIndex = value.Length;
                syncing = false;
            }
        };
        input.TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            list.SelectedItem = null;
            syncing = false;
        };

        var start = new Button
        {
            Content = T("OK", "确认", "確認", "確認"),
            IsDefault = true,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        var clear = new Button
        {
            Content = T("Clear category", "清除分类", "清除分類", "分類をクリア"),
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        var cancel = new Button
        {
            Content = T("Cancel", "取消", "取消", "キャンセル"),
            IsCancel = true,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        start.Click += (_, _) =>
        {
            var category = CategoryTree.NormalizePath(input.Text ?? list.SelectedItem as string);
            if (string.IsNullOrWhiteSpace(category)) return;
            Close(new BatchCategoryChoice(true, category, false));
        };
        clear.Click += (_, _) => Close(new BatchCategoryChoice(false, null, true));
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = T("Pick a category, then drag across mods.",
                        "选择分类后，在模组上拖动即可批量标记。",
                        "選擇分類後，在模組上拖動即可批次標記。",
                        "分類を選び、MOD 上をドラッグして一括設定します。"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeService.SecondaryTextBrush
                },
                list,
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { clear, cancel, start }
                }
            }
        };
        Opened += (_, _) => input.Focus();
    }

    private string T(string en, string zh, string tw, string ja) =>
        OperationText.Choose(_language, en, zh, tw, ja);
}
