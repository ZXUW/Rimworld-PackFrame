using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class MissingModsWindow : Window
{
    private readonly ManagedInstance? _instance;
    private readonly string? _language;

    public MissingModsWindow()
    {
        InitializeComponent();
    }

    public MissingModsWindow(
        ManagedInstance instance,
        IReadOnlyList<MissingModRow> rows,
        string? language
    )
        : this()
    {
        _instance = instance;
        _language = language;
        var title = Localization.MissingModsTitle(language);
        var mappingPath = ModFullNameList.GetPath(instance);
        Title = $"Rimworld PackFrame - {title}";
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("SummaryText")!.Text =
            Localization.MissingModsSummary(language, instance.DisplayName, rows.Count);
        this.FindControl<TextBlock>("SourceText")!.Text =
            Localization.MissingModsSourceNote(language);
        this.FindControl<TextBlock>("MappingLabel")!.Text =
            Localization.MissingModsMappingPath(language) + ":";
        this.FindControl<SelectableTextBlock>("MappingPathText")!.Text = mappingPath;
        this.FindControl<TextBlock>("LabelHeader")!.Text = Localization.FieldName(language);
        this.FindControl<TextBlock>("AuthorHeader")!.Text = Localization.FieldAuthor(language);
        this.FindControl<TextBlock>("PackageHeader")!.Text =
            Localization.FieldPackageId(language);
        this.FindControl<TextBlock>("LinkHeader")!.Text = Localization.Link(language);
        this.FindControl<Button>("HistoryButton")!.Content =
            Localization.MissingHistory(language);
        this.FindControl<Button>("FooterCloseButton")!.Content =
            Localization.CloseButton(language);
        this.FindControl<ItemsControl>("Rows")!.ItemsSource = rows;

        Opened += (_, _) =>
        {
            var pathText = this.FindControl<SelectableTextBlock>("MappingPathText")!;
            ToolTip.SetTip(pathText, mappingPath);
        };
        KeyDown += OnWindowKeyDown;
    }

    private async void OnHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (_instance is null)
        {
            return;
        }

        var window = new MissingHistoryWindow(_instance, _language);
        await window.ShowDialog(this);
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
