using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

internal sealed class CollectionTargetWindow : Window
{
    public CollectionTargetWindow(IReadOnlyList<OwnedCollection> candidates, string language)
    {
        Title = "PackFrame — Steam Workshop"; Width = 650; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = OperationText.Choose(language,
            "Select your collection to update, or create a new one.", "选择要更新的本人合集，或另建新合集。",
            "選擇要更新的本人合集，或另建新合集。", "更新する自分のコレクションを選ぶか、新規作成してください。") });
        var list = new ListBox { Height = 190 };
        foreach (var item in candidates)
            list.Items.Add($"{item.Title}\nhttps://steamcommunity.com/sharedfiles/filedetails/?id={item.Id}");
        list.SelectedIndex = 0; panel.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var update = new Button { Content = OperationText.Choose(language, "Update selected", "更新所选合集", "更新所選合集", "選択を更新") };
        update.Click += (_, _) => { if (list.SelectedIndex >= 0) Close((ulong?)candidates[list.SelectedIndex].Id); };
        var create = new Button { Content = OperationText.Choose(language, "Create new", "另建新合集", "另建新合集", "新規作成") };
        create.Click += (_, _) => Close((ulong?)0UL);
        var cancel = new Button { Content = Localization.CancelButton(language) };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(update); buttons.Children.Add(create); buttons.Children.Add(cancel);
        panel.Children.Add(buttons); Content = panel;
    }
}
