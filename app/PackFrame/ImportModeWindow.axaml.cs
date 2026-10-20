using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PackFrame;

public enum ImportMode
{
    Overwrite,
    New
}

public partial class ImportModeWindow : Window
{
    public ImportModeWindow()
    {
        InitializeComponent();
    }

    public ImportModeWindow(string overwriteText, string newText, bool overwriteEnabled)
        : this()
    {
        var overwriteButton = this.FindControl<Button>("OverwriteButton")!;
        var newButton = this.FindControl<Button>("NewButton")!;
        overwriteButton.Content = overwriteText;
        overwriteButton.IsEnabled = overwriteEnabled;
        newButton.Content = newText;
    }

    private void OnOverwriteClick(object? sender, RoutedEventArgs e)
    {
        Close(ImportMode.Overwrite);
    }

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        Close(ImportMode.New);
    }
}
