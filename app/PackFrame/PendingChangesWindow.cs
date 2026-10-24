using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

internal enum PendingChangesChoice { Cancel, Save, Discard }

internal sealed class PendingChangesWindow : Window
{
    public PendingChangesWindow(string? language)
    {
        Title = "Rimworld PackFrame";
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;
        Width = 480;
        Height = 180;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (text, choice) in new[]
        {
            (Localization.CancelButton(language), PendingChangesChoice.Cancel),
            (Localization.DiscardChanges(language), PendingChangesChoice.Discard),
            (Localization.Save(language), PendingChangesChoice.Save)
        })
        {
            var button = new Button { Content = text, MinHeight = 32,
                Theme = Application.Current?.FindResource("FlatButton") as ControlTheme };
            button.Click += (_, _) => Close(choice);
            buttons.Children.Add(button);
        }
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16), Spacing = 24,
            Children = { new TextBlock { Text = Localization.PendingChangesPrompt(language),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons }
        };
    }
}
