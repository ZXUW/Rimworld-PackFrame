using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string message, string confirmText, string cancelText)
        : this()
    {
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<Button>("ConfirmButton")!.Content = confirmText;
        this.FindControl<Button>("CancelButton")!.Content = cancelText;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
