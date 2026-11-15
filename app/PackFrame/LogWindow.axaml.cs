using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PackFrame;

public partial class LogWindow : Window
{
    private const int MaxLogChars = 400_000;
    private readonly string? _language;

    public LogWindow()
    {
        InitializeComponent();
    }

    public LogWindow(string? language)
        : this()
    {
        _language = language;
        Title = $"Rimworld PackFrame - {Localization.LogButton(language)}";
        this.FindControl<Button>("OpenLogFileButton")!.Content =
            Localization.OpenLogFile(language);
        this.FindControl<Button>("OpenLogFolderButton")!.Content =
            Localization.OpenLogFolder(language);
        KeyDown += OnWindowKeyDown;
        LoadLog();
        Opened += (_, _) => Dispatcher.UIThread.Post(
            () => this.FindControl<ScrollViewer>("LogScroll")!.ScrollToEnd(),
            DispatcherPriority.Background
        );
    }

    private void LoadLog()
    {
        var text = string.Empty;
        try
        {
            if (File.Exists(AppLog.LogPath))
            {
                text = File.ReadAllText(AppLog.LogPath);
                if (text.Length > MaxLogChars)
                {
                    text = text[^MaxLogChars..];
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        this.FindControl<SelectableTextBlock>("LogText")!.Text =
            string.IsNullOrWhiteSpace(text) ? Localization.LogEmpty(_language) : text;
    }

    private void OnOpenLogFileClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AppLog.LogPath))
            {
                File.WriteAllText(AppLog.LogPath, string.Empty);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLog.LogPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            this.FindControl<SelectableTextBlock>("LogText")!.Text = exception.Message;
        }
    }

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            InstanceStore.OpenFolder(Path.GetDirectoryName(AppLog.LogPath)!);
        }
        catch (Exception exception)
        {
            this.FindControl<SelectableTextBlock>("LogText")!.Text = exception.Message;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
