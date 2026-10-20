using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class MissingHistoryWindow : Window
{
    private ManagedInstance? _instance;
    private string? _language;

    public MissingHistoryWindow()
    {
        InitializeComponent();
    }

    public MissingHistoryWindow(ManagedInstance instance, string? language)
        : this()
    {
        _instance = instance;
        _language = language;
        var title = Localization.MissingHistoryTitle(language);
        Title = $"Rimworld PackFrame - {title}";
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<Button>("ClearButton")!.Content = Localization.ClearHistory(language);
        this.FindControl<TextBlock>("LabelHeader")!.Text = Localization.FieldName(language);
        this.FindControl<TextBlock>("AuthorHeader")!.Text = Localization.FieldAuthor(language);
        this.FindControl<TextBlock>("PackageHeader")!.Text =
            Localization.FieldPackageId(language);
        this.FindControl<TextBlock>("LinkHeader")!.Text = Localization.Link(language);
        this.FindControl<TextBlock>("LastMissingHeader")!.Text =
            Localization.LastMissing(language);
        this.FindControl<Button>("FooterCloseButton")!.Content =
            Localization.CloseButton(language);
        KeyDown += OnWindowKeyDown;
        LoadRows();
    }

    private void LoadRows()
    {
        if (_instance is null)
        {
            return;
        }

        var rows = ModFullNameList.LoadHistory(_instance)
            .Select(entry => new MissingHistoryEntry
            {
                Label = entry.Label,
                Author = string.IsNullOrWhiteSpace(entry.Author) ? "—" : entry.Author,
                PackageId = entry.PackageId,
                Link = string.IsNullOrWhiteSpace(entry.Link) ? "—" : entry.Link,
                LastMissingAt = string.IsNullOrWhiteSpace(entry.LastMissingAt)
                    ? "—"
                    : entry.LastMissingAt
            })
            .ToList();

        this.FindControl<ItemsControl>("Rows")!.ItemsSource = rows;
        this.FindControl<TextBlock>("SummaryText")!.Text =
            Localization.MissingHistorySummary(
                _language,
                _instance.DisplayName,
                rows.Count
            );
        this.FindControl<TextBlock>("EmptyText")!.Text =
            Localization.MissingHistoryEmpty(_language);
        this.FindControl<TextBlock>("EmptyText")!.IsVisible = rows.Count == 0;
        this.FindControl<Button>("ClearButton")!.IsEnabled = rows.Count > 0;
    }

    private async void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (_instance is null)
        {
            return;
        }

        var confirm = new ConfirmWindow(
            Localization.ClearHistoryConfirm(_language, _instance.DisplayName),
            Localization.ClearHistory(_language),
            Localization.CancelButton(_language)
        );
        if (!await confirm.ShowDialog<bool>(this))
        {
            return;
        }

        try
        {
            ModFullNameList.ClearHistory(_instance);
            LoadRows();
        }
        catch
        {
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

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
