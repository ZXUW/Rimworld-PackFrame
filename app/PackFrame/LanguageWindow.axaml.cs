using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class LanguageWindow : Window
{
    public event EventHandler<string>? LanguageSelected;

    public LanguageWindow()
    {
        InitializeComponent();
    }

    private void OnLanguageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string language })
        {
            LanguageSelected?.Invoke(this, language);
        }
    }
}
