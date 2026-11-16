using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PackFrame;

public enum CompletionChoice
{
    Close = 0,
    MissingMods = 1,
    OpenFolder = 2
}

public partial class CompletionWindow : Window
{
    public CompletionWindow()
    {
        InitializeComponent();
    }

    public CompletionWindow(
        string title,
        string message,
        string? missingText,
        string openFolderText,
        string closeText,
        string? skippedHeader = null,
        IReadOnlyList<string>? skippedStages = null
    )
        : this()
    {
        Title = "Rimworld PackFrame - " + title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        if (skippedStages is { Count: > 0 })
        {
            this.FindControl<Border>("SkippedPanel")!.IsVisible = true;
            this.FindControl<TextBlock>("SkippedHeaderText")!.Text = skippedHeader ?? string.Empty;
            this.FindControl<SelectableTextBlock>("SkippedListText")!.Text =
                string.Join(Environment.NewLine, skippedStages.Select(item => "• " + item));
        }
        var missing = this.FindControl<Button>("MissingButton")!;
        missing.Content = missingText;
        missing.IsVisible = !string.IsNullOrWhiteSpace(missingText);
        this.FindControl<Button>("OpenFolderButton")!.Content = openFolderText;
        this.FindControl<Button>("CloseButton")!.Content = closeText;
        KeyDown += OnWindowKeyDown;
    }

    private void OnMissingClick(object? sender, RoutedEventArgs e)
    {
        Close(CompletionChoice.MissingMods);
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        Close(CompletionChoice.OpenFolder);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close(CompletionChoice.Close);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(CompletionChoice.Close);
            e.Handled = true;
        }
    }
}
