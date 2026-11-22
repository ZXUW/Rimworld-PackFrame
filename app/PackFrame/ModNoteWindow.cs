using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

internal sealed class ModNoteWindow : Window
{
    private readonly string? _language;

    internal ModNoteWindow(string modName, string? alias, string? category, string? language, bool categoryOnly = false)
    {
        _language = language;
        Title = categoryOnly
            ? T("Set category", "设置分类", "設定分類", "分類を設定")
            : T("Note", "备注", "備註", "メモ");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;

        var aliasBox = new TextBox
        {
            Text = alias ?? string.Empty,
            PlaceholderText = T("Alias", "别名", "別名", "別名"),
            IsVisible = !categoryOnly
        };
        var categoryBox = new TextBox
        {
            Text = category ?? string.Empty,
            PlaceholderText = T("Category (use / for levels, e.g. Race/Milira)", "分类（用 / 分层级，如 种族/米莉拉）", "分類（用 / 分層級，如 種族/米莉拉）", "分類（/ で階層分け、例: 種族/ミリラ）")
        };
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
        save.Click += (_, _) => Close((aliasBox.Text, categoryBox.Text));
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = modName,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                aliasBox,
                categoryBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, save }
                }
            }
        };
        Opened += (_, _) => (categoryOnly ? categoryBox : aliasBox).Focus();
    }

    private string T(string en, string zh, string tw, string ja) =>
        OperationText.Choose(_language, en, zh, tw, ja);
}
