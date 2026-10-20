using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PackFrame;

public partial class ConfigFolderWindow : Window
{
    private readonly Border _pathField;
    private readonly TextBlock _autoDetectStatus;
    private string _autoDetectFailedText = string.Empty;

    public ConfigFolderWindow()
    {
        InitializeComponent();
        _pathField = this.FindControl<Border>("PathField")!;
        _autoDetectStatus = this.FindControl<TextBlock>("AutoDetectStatus")!;
        this.FindControl<TextBox>("PathBox")!.TextChanged += (_, _) =>
        {
            SetInvalid(false);
            _autoDetectStatus.Text = string.Empty;
        };
    }

    public ConfigFolderWindow(
        string? currentPath,
        string description,
        string browseText,
        string confirmText,
        string cancelText,
        string watermark,
        string autoDetectText,
        string autoDetectFailedText
    )
        : this()
    {
        var pathBox = this.FindControl<TextBox>("PathBox")!;
        pathBox.Text = currentPath ?? string.Empty;
        pathBox.PlaceholderText = watermark;
        this.FindControl<TextBlock>("DescriptionText")!.Text = description;
        this.FindControl<Button>("BrowseButton")!.Content = browseText;
        this.FindControl<Button>("ConfirmButton")!.Content = confirmText;
        this.FindControl<Button>("CancelButton")!.Content = cancelText;
        this.FindControl<Button>("AutoDetectButton")!.Content = autoDetectText;
        _autoDetectFailedText = autoDetectFailedText;

        Opened += (_, _) =>
        {
            pathBox.Focus();
            pathBox.SelectAll();
        };
        pathBox.KeyDown += OnPathKeyDown;
    }

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Confirm()
    {
        var text = (this.FindControl<TextBox>("PathBox")!.Text ?? string.Empty)
            .Trim()
            .Trim('"');
        if (text.Length == 0)
        {
            SetInvalid(true);
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(text));
        }
        catch
        {
            SetInvalid(true);
            return;
        }

        if (!Directory.Exists(fullPath))
        {
            SetInvalid(true);
            return;
        }

        Close(fullPath);
    }

    private void OnAutoDetectClick(object? sender, RoutedEventArgs e)
    {
        var path = RimWorldLocator.FindConfigDirectory();
        if (string.IsNullOrWhiteSpace(path))
        {
            _autoDetectStatus.Text = _autoDetectFailedText;
            SetInvalid(true);
            return;
        }

        this.FindControl<TextBox>("PathBox")!.Text = path;
        _autoDetectStatus.Text = string.Empty;
        SetInvalid(false);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null)
            {
                return;
            }

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false
            });
            if (folders.Count == 0)
            {
                return;
            }

            var path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            this.FindControl<TextBox>("PathBox")!.Text = path;
            SetInvalid(false);
        }
        catch
        {
        }
    }

    private void SetInvalid(bool invalid)
    {
        if (invalid)
        {
            if (!_pathField.Classes.Contains("invalid"))
            {
                _pathField.Classes.Add("invalid");
            }
        }
        else
        {
            _pathField.Classes.Remove("invalid");
        }
    }
}
