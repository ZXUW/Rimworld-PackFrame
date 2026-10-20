using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class RenameWindow : Window
{
    public RenameWindow()
    {
        InitializeComponent();
    }

    public RenameWindow(string currentName, string confirmText, string cancelText)
        : this()
    {
        this.FindControl<TextBox>("NameBox")!.Text = currentName;
        this.FindControl<Button>("ConfirmButton")!.Content = confirmText;
        this.FindControl<Button>("CancelButton")!.Content = cancelText;
        Opened += (_, _) =>
        {
            var nameBox = this.FindControl<TextBox>("NameBox")!;
            nameBox.Focus();
            nameBox.SelectAll();
            nameBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Close(nameBox.Text ?? string.Empty);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Close(null);
                    e.Handled = true;
                }
            };
        };
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(this.FindControl<TextBox>("NameBox")!.Text ?? string.Empty);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
