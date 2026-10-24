using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

internal sealed record UpdateChoices(HashSet<string> KeepLocal, bool KeepOrder);

internal sealed class UpdateChoicesWindow : Window
{
    public UpdateChoicesWindow(PackUpdatePlan plan, string? language)
    {
        Title = "PackFrame — Update";
        Width = 650; Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = OperationText.Choose(language,
            "Checked = keep my content. Unchecked = use the new pack version.",
            "勾选：保留我的内容；不勾选：使用合集新版。", "勾選：保留我的內容；不勾選：使用合集新版。",
            "チェック: 自分の内容を保持。未チェック: 新版に置換。"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"+{plan.Added.Count} / -{plan.Removed.Count}\n" +
            string.Join("\n", plan.Added.ConvertAll(id => "+ " + id)) + "\n" + string.Join("\n", plan.Removed.ConvertAll(id => "- " + id)), TextWrapping = TextWrapping.Wrap });
        var checks = new Dictionary<string, CheckBox>();
        foreach (var path in plan.Conflicts)
        {
            var check = new CheckBox { Content = path, IsChecked = true };
            checks[path] = check;
            panel.Children.Add(check);
        }
        var order = new CheckBox { Content = OperationText.Choose(language,
            "Keep my order (new mods may not be enabled)", "保留当前排序（新增模组可能未启用）",
            "保留目前排序（新增模組可能未啟用）", "現在の並び順を保持（新 MOD は未有効の場合あり）"), IsVisible = plan.OrderChanged };
        panel.Children.Add(order);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var cancel = new Button { Content = Localization.CancelButton(language) };
        cancel.Click += (_, _) => Close();
        var apply = new Button { Content = OperationText.Choose(language, "Apply update", "应用更新", "套用更新", "更新を適用") };
        apply.Click += (_, _) =>
        {
            var keep = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (path, check) in checks) if (check.IsChecked == true) keep.Add(path);
            Close(new UpdateChoices(keep, order.IsChecked == true));
        };
        buttons.Children.Add(cancel); buttons.Children.Add(apply); panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel };
    }
}
