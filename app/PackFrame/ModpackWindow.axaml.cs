using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PackFrame;

public sealed class ModpackListItem
{
    public required ModpackEntry Entry { get; init; }

    public string Name => string.IsNullOrWhiteSpace(Entry.Name)
        ? Entry.Id ?? "Modpack"
        : Entry.Name;

    public string Summary => Entry.Summary ?? string.Empty;

    public string CountText { get; init; } = string.Empty;
}

public sealed class ModpackModRow
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string GameVersion { get; init; }
}

public partial class ModpackWindow : Window
{
    private static readonly HttpClient ImageHttp = CreateImageHttpClient();

    private readonly AppConfig? _config;
    private readonly RimWorldInstallation? _game;
    private readonly string? _packRoot;
    private readonly ManagedInstance? _currentInstance;
    private readonly string _language;

    private List<ModpackListItem> _items = new();
    private Uri? _indexUri;
    private ModpackDefinition? _selectedPack;
    private string? _workshopUrl;
    private ModpackListItem? _selectedItem;
    private Bitmap? _coverBitmap;
    private MemoryStream? _coverStream;
    private bool _busy;
    private bool _canInstall;
    private bool _canApplyConfig;
    private bool _closed;
    private CancellationTokenSource? _operationCancellation;
    private int _detailVersion;
    private CancellationTokenSource? _coverCancellation;
    private readonly Dictionary<string, byte[]> _coverCache = new();
    private long _coverCacheBytes;

    public ModpackWindow()
    {
        InitializeComponent();
        _language = Localization.English;
    }

    public bool ConfigApplied { get; private set; }

    public ModpackWindow(
        AppConfig config,
        RimWorldInstallation game,
        string packRoot,
        ManagedInstance? currentInstance
    )
        : this()
    {
        _config = config;
        _game = game;
        _packRoot = packRoot;
        _currentInstance = currentInstance;
        _language = config.Language ?? Localization.English;

        ApplyLocalization();
        var indexUrl = string.IsNullOrWhiteSpace(config.ModpackIndexUrl)
            ? ModpackService.GiteeIndexUrl
            : config.ModpackIndexUrl;
        RepoBox.Text = indexUrl;
        config.ModpackIndexUrl = indexUrl;
        config.Save();
        Opened += async (_, _) => await LoadIndexAsync();
        KeyDown += OnWindowKeyDown;
        Closing += (_, e) =>
        {
            if (_committingUpdate) { e.Cancel = true; return; }
            _closed = true;
            _operationCancellation?.Cancel();
            _coverCancellation?.Cancel();
            DisposeCover();
        };
    }

    private void ApplyLocalization()
    {
        var title = Localization.ModpacksTitle(_language);
        Title = $"Rimworld PackFrame - {title}";
        Find<TextBlock>("TitleText").Text = title;
        Find<Button>("RefreshButton").Content = Localization.Refresh(_language);
        Find<TextBlock>("RepoLabel").Text = Localization.ModpackRepoLabel(_language);
        Find<TextBox>("RepoBox").PlaceholderText =
            Localization.ModpackRepoWatermark(_language);
        Find<TextBlock>("ListHeader").Text = title;
        Find<TextBlock>("LinkLabel").Text = Localization.ModpackLinkLabel(_language);
        Find<TextBox>("LinkBox").PlaceholderText =
            Localization.ModpackLinkPlaceholder(_language);
        Find<Button>("OpenWorkshopButton").Content =
            Localization.ModpackOpenWorkshop(_language);

        SetButtonText(Find<Button>("InstallButton"), Localization.ModpackInstall(_language));
        SetButtonText(
            Find<Button>("SteamCmdButton"),
            Localization.ModpackSteamCmdInstall(_language)
        );
        SetWorkshopOnlyButtonText(
            Find<Button>("WorkshopOnlyButton"),
            Localization.ModpackWorkshopOnlyInstall(_language)
        );
        SetButtonText(
            Find<Button>("ApplyConfigButton"),
            Localization.ModpackApplyConfig(_language)
        );
        Find<Button>("CloseButton").Content = Localization.CloseButton(_language);
        Find<Button>("CancelOperationButton").Content =
            Localization.CancelButton(_language);
        Find<Button>("CancelOperationButton").IsVisible = false;

        Find<TextBlock>("StatusText").Text = Localization.ModpackNoUrl(_language);
        SetControls(true);
    }

    private static void SetWorkshopOnlyButtonText(Button button, string text)
    {
        var panel = new StackPanel
        {
            Spacing = 1
        };
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            panel.Children.Add(new TextBlock
            {
                Text = lines[i],
                FontSize = i == 0 ? 14 : 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
        }

        button.Content = panel;
    }

    private static void SetButtonText(Button button, string text, double fontSize = 14)
    {
        button.Content = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private T Find<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"{name} was not found.");
    }

    private async Task LoadIndexAsync()
    {
        if (_busy)
        {
            return;
        }

        var rawUrl = RepoBox.Text?.Trim() ?? string.Empty;
        if (rawUrl.Length == 0)
        {
            ClearDetail(Localization.ModpackNoUrl(_language));
            return;
        }

        if (_config is not null)
        {
            _config.ModpackIndexUrl = rawUrl;
            _config.Save();
        }

        _busy = true;
        _selectedItem = null;
        _selectedPack = null;
        _detailVersion++;
        SetControls(false);
        ClearDetail(Localization.ModpackStatusReading(_language));
        Find<TextBlock>("StatusText").Text =
            Localization.ModpackStatusReading(_language);

        try
        {
            var index = await ModpackService.LoadIndexAsync(rawUrl);
            _indexUri = new Uri(ModpackService.NormalizeIndexUrl(rawUrl));
            _items = index.Packs
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id) ||
                                !string.IsNullOrWhiteSpace(entry.File))
                .Select(entry => new ModpackListItem
                {
                    Entry = entry,
                    CountText = entry.ModCount > 0
                        ? Localization.ModpackModsCount(_language, entry.ModCount)
                        : string.Empty
                })
                .ToList();

            var list = Find<ListBox>("PackList");
            list.ItemsSource = _items;
            Find<TextBlock>("ListCount").Text = _items.Count.ToString();
            if (_items.Count == 0)
            {
                ClearDetail(Localization.ModpackEmpty(_language));
            }
            else
            {
                Find<TextBlock>("StatusText").Text =
                    Localization.ModpackSelectHint(_language);
            }
        }
        catch (Exception exception)
        {
            _items = new List<ModpackListItem>();
            Find<ListBox>("PackList").ItemsSource = _items;
            Find<TextBlock>("ListCount").Text = "0";
            ClearDetail(Localization.ModpackLoadFailed(
                _language,
                exception.Message
            ));
        }
        finally
        {
            _busy = false;
            SetControls(true);
        }

        if (_items.Count > 0)
        {
            var matching = _items.FindIndex(item => item.Entry.Id == _currentInstance?.Metadata?.PackId);
            Find<ListBox>("PackList").SelectedIndex = matching >= 0 ? matching : 0;
        }
    }

    private async void OnPackSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list ||
            list.SelectedItem is not ModpackListItem item ||
            _indexUri is null)
        {
            return;
        }

        await LoadPackAsync(item);
    }

    private async Task LoadPackAsync(ModpackListItem item)
    {
        if (_indexUri is null)
        {
            return;
        }

        var version = ++_detailVersion;
        _selectedItem = item;
        _selectedPack = null;
        _busy = true;
        SetControls(false);
        ClearDetail(Localization.ModpackStatusReading(_language));
        Find<TextBlock>("StatusText").Text =
            Localization.ModpackStatusReading(_language);

        try
        {
            var pack = await ModpackService.LoadPackAsync(item.Entry, _indexUri);
            if (version != _detailVersion)
            {
                return;
            }

            _selectedPack = pack;
            ShowPack(pack, item);
            Find<TextBlock>("StatusText").Text =
                Localization.ModpackSelectHint(_language);
            _ = LoadCoverAsync(pack.Image ?? item.Entry.Image, version);
        }
        catch (Exception exception)
        {
            if (version == _detailVersion)
            {
                ClearDetail(Localization.ModpackLoadFailed(
                    _language,
                    exception.Message
                ));
            }
        }
        finally
        {
            if (version == _detailVersion)
            {
                _busy = false;
                SetControls(true);
            }
        }
    }

    private void ShowPack(ModpackDefinition pack, ModpackListItem item)
    {
        var title = string.IsNullOrWhiteSpace(pack.Name) ? item.Name : pack.Name;
        Find<SelectableTextBlock>("PackTitle").Text = title;
        Find<SelectableTextBlock>("PackDescription").Text =
            string.IsNullOrWhiteSpace(pack.Description)
                ? Localization.ModpackNoDescription(_language)
                : pack.Description;

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(pack.Author))
        {
            meta.Add($"{Localization.FieldAuthor(_language)}: {pack.Author}");
        }
        if (!string.IsNullOrWhiteSpace(pack.GameVersion))
        {
            meta.Add($"{Localization.ModpackGameVersionLabel(_language)}: {pack.GameVersion}");
        }
        var collectionId = ModpackService.ParseWorkshopId(pack.WorkshopCollectionId) ??
            ModpackService.ParseWorkshopId(pack.WorkshopUrl);
        if (collectionId is not null)
        {
            meta.Add(Localization.ModpackWorkshopCollection(
                _language,
                collectionId.Value.ToString()
            ));
        }
        Find<SelectableTextBlock>("PackMeta").Text = string.Join("  ·  ", meta);

        _workshopUrl = !string.IsNullOrWhiteSpace(pack.WorkshopUrl)
            ? pack.WorkshopUrl.Trim()
            : collectionId is null
                ? null
                : $"https://steamcommunity.com/sharedfiles/filedetails/?id={collectionId}";
        if (!string.IsNullOrWhiteSpace(_workshopUrl) &&
            (!Uri.TryCreate(_workshopUrl, UriKind.Absolute, out var workshopUri) ||
             (workshopUri.Scheme != "http" && workshopUri.Scheme != "https")))
        {
            _workshopUrl = null;
        }
        Find<TextBox>("LinkBox").Text = _workshopUrl ?? string.Empty;

        var rows = BuildModRows(pack);
        Find<TextBlock>("ModsHeader").Text =
            Localization.ModpackModsHeader(_language) +
            (rows.Count > 0 ? $" ({rows.Count})" : string.Empty);
        Find<ItemsControl>("ModList").ItemsSource = rows;

        var empty = Find<TextBlock>("EmptyModsText");
        empty.IsVisible = rows.Count == 0;
        empty.Text = collectionId is not null
            ? Localization.ModpackCollectionOnly(
                _language,
                collectionId.Value.ToString()
            )
            : Localization.ModpackNoMods(_language);

        _canInstall = rows.Count > 0 ||
            (pack.PackageIds?.Count ?? 0) > 0 ||
            collectionId is not null;
        _canApplyConfig = _currentInstance is not null && !_currentInstance.IsDamaged &&
            !_currentInstance.IsScratch &&
            !_currentInstance.IsOfficial &&
            !string.IsNullOrWhiteSpace(_currentInstance.ConfigDirectoryPath) &&
            !string.IsNullOrWhiteSpace(pack.ConfigBaseUrl) &&
            (pack.ConfigFiles?.Count ?? 0) > 0;
        Find<Button>("InstallButton").IsEnabled = _canInstall && !_busy;
        Find<Button>("SteamCmdButton").IsEnabled = _canInstall && !_busy;
        Find<Button>("ApplyConfigButton").IsEnabled =
            _canApplyConfig && !_busy;
    }

    private List<ModpackModRow> BuildModRows(ModpackDefinition pack)
    {
        var rows = new List<ModpackModRow>();
        foreach (var mod in (pack.Mods ?? new List<ModpackMod>())
                     .Where(item => item is not null))
        {
            var name = mod.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = mod.PackageId;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = mod.WorkshopId;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = mod.Repo;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Mod";
            }

            var detail = mod.PackageId;
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = mod.WorkshopId ?? mod.WorkshopUrl;
            }
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = mod.Repo ?? mod.DownloadUrl;
            }
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = mod.Path;
            }
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = Localization.ModpackPackageUnknown(_language);
            }

            rows.Add(new ModpackModRow
            {
                Name = name,
                Detail = detail,
                GameVersion = GameVersionLabel(pack.GameVersion)
            });
        }

        return rows;
    }

    private static string GameVersionLabel(string? version)
    {
        var text = (version ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var first = text.IndexOf('.');
        if (first <= 0)
        {
            return text;
        }

        var second = text.IndexOf('.', first + 1);
        var minor = second > first + 1
            ? text[(first + 1)..second]
            : text[(first + 1)..];
        minor = new string(minor.TakeWhile(char.IsDigit).ToArray());
        return minor.Length == 0 ? text : $"{text[..first]}.{minor}";
    }

    private async Task LoadCoverAsync(string? image, int version)
    {
        _coverCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _coverCancellation = cancellation;
        if (string.IsNullOrWhiteSpace(image) || _indexUri is null)
        {
            _coverCancellation = null;
            SetCover(null);
            return;
        }

        try
        {
            var uri = new Uri(_indexUri, image);
            if (!_coverCache.TryGetValue(uri.AbsoluteUri, out var bytes))
            {
                bytes = await ImageHttp.GetByteArrayAsync(uri, cancellation.Token);
                if (bytes.Length <= 4 * 1024 * 1024)
                {
                    if (_coverCacheBytes + bytes.Length > 16 * 1024 * 1024)
                    { _coverCache.Clear(); _coverCacheBytes = 0; }
                    _coverCache[uri.AbsoluteUri] = bytes;
                    _coverCacheBytes += bytes.Length;
                }
            }
            if (_closed || version != _detailVersion || cancellation.IsCancellationRequested)
            {
                return;
            }

            var bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                return Bitmap.DecodeToHeight(stream, 480);
            }, cancellation.Token);
            if (_closed || version != _detailVersion || cancellation.IsCancellationRequested)
            { bitmap.Dispose(); return; }
            SetCover(bitmap);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("Load pack cover: " + image, exception);
            if (!_closed && version == _detailVersion)
            {
                SetCover(null);
            }
        }
        finally
        {
            if (ReferenceEquals(_coverCancellation, cancellation)) _coverCancellation = null;
        }
    }

    private void SetCover(Bitmap? bitmap, MemoryStream? stream = null)
    {
        var image = Find<Image>("CoverImage");
        image.Source = bitmap;
        _coverBitmap?.Dispose();
        _coverStream?.Dispose();
        _coverBitmap = bitmap;
        _coverStream = stream;
    }

    private void ClearDetail(string status)
    {
        _coverCancellation?.Cancel();
        _canInstall = false;
        _canApplyConfig = false;
        _workshopUrl = null;
        Find<SelectableTextBlock>("PackTitle").Text = string.Empty;
        Find<SelectableTextBlock>("PackMeta").Text = string.Empty;
        Find<SelectableTextBlock>("PackDescription").Text = string.Empty;
        Find<TextBox>("LinkBox").Text = string.Empty;
        Find<ItemsControl>("ModList").ItemsSource = null;
        Find<TextBlock>("ModsHeader").Text =
            Localization.ModpackModsHeader(_language);
        Find<TextBlock>("EmptyModsText").IsVisible = false;
        Find<Button>("InstallButton").IsEnabled = false;
        Find<Button>("SteamCmdButton").IsEnabled = false;
        Find<TextBlock>("StatusText").Text = status;
        SetCover(null);
    }

    private void SetControls(bool enabled)
    {
        RefreshUpdateControls();
        var ready = enabled && !_busy;
        Find<Button>("RefreshButton").IsEnabled = ready;
        Find<Button>("WorkshopOnlyButton").IsEnabled = ready;
        Find<TextBox>("RepoBox").IsEnabled = ready;
        Find<ListBox>("PackList").IsEnabled = ready;
        Find<Button>("CancelOperationButton").IsVisible =
            _operationCancellation is not null;
        Find<Button>("CloseButton").IsEnabled = true;
        Find<Button>("InstallButton").IsEnabled = ready && _canInstall;
        Find<Button>("SteamCmdButton").IsEnabled = ready && _canInstall;
        Find<Button>("ApplyConfigButton").IsEnabled =
            ready && _canApplyConfig;
    }

    private void OnRepoMenuClick(object? sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var defaultItem = new MenuItem
        {
            Header = Localization.ModpackRepoDefault(_language)
        };
        defaultItem.Click += async (_, _) =>
        {
            RepoBox.Text = ModpackService.GiteeIndexUrl;
            await LoadIndexAsync();
        };
        flyout.Items.Add(defaultItem);

        var customItem = new MenuItem
        {
            Header = Localization.ModpackRepoCustom(_language)
        };
        customItem.Click += (_, _) =>
        {
            RepoBox.Focus();
            RepoBox.SelectAll();
        };
        flyout.Items.Add(customItem);
        flyout.ShowAt((Control)sender!);
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        _coverCache.Clear();
        _coverCacheBytes = 0;
        await LoadIndexAsync();
    }

    private async void OnRepoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoadIndexAsync();
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        await InstallFromLinkAsync(false);
    }

    private async void OnSteamCmdInstallClick(object? sender, RoutedEventArgs e)
    {
        await InstallFromLinkAsync(true);
    }

    private async void OnWorkshopOnlyInstallClick(object? sender, RoutedEventArgs e)
    {
        await InstallFromLinkAsync(false, skipModsDownload: true);
    }

    private void OnOpenWorkshopClick(object? sender, RoutedEventArgs e)
    {
        var url = Find<TextBox>("LinkBox").Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            url = _workshopUrl;
        }

        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var workshopId = ModpackService.ParseWorkshopId(url);
        var steamUrl = workshopId is { } id
            ? $"steam://url/CommunityFilePage/{id}"
            : url;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
    }

    private async Task InstallFromLinkAsync(bool useSteamCmd, bool skipModsDownload = false)
    {
        var link = Find<TextBox>("LinkBox").Text?.Trim();
        if (ModpackService.ParseWorkshopId(link) is null)
        {
            Find<TextBlock>("StatusText").Text =
                Localization.ModpackLinkInvalid(_language);
            return;
        }

        await InstallAsync(link, useSteamCmd, skipModsDownload);
    }

    private async void OnApplyConfigClick(object? sender, RoutedEventArgs e)
    {
        if (_currentInstance?.IsDamaged == true)
        {
            Find<TextBlock>("StatusText").Text = _currentInstance.LoadError;
            return;
        }
        if (_busy || _selectedPack is null)
        {
            return;
        }

        if (_currentInstance is null ||
            _currentInstance.IsScratch ||
            _currentInstance.IsOfficial ||
            string.IsNullOrWhiteSpace(_currentInstance.ConfigDirectoryPath))
        {
            Find<TextBlock>("StatusText").Text =
                Localization.SyncConfigNoInstance(_language);
            return;
        }

        if (GameLaunchService.IsRunning())
        {
            Find<TextBlock>("StatusText").Text =
                Localization.ModpackGameRunning(_language);
            return;
        }

        if (InstanceStore.HasPendingMods)
        {
            Find<TextBlock>("StatusText").Text = Localization.UnsavedChanges(_language);
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedPack.ConfigBaseUrl) ||
            _selectedPack.ConfigFiles.Count == 0)
        {
            Find<TextBlock>("StatusText").Text =
                Localization.SyncConfigNoSource(_language);
            return;
        }

        _busy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        SetControls(false);
        Find<TextBlock>("StatusText").Text =
            Localization.SyncConfig(_language) + "...";
        try
        {
            await ModpackService.DownloadConfigToAsync(
                _selectedPack.ConfigBaseUrl,
                _selectedPack.ConfigFiles,
                _currentInstance.ConfigDirectoryPath,
                text => Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed) Find<TextBlock>("StatusText").Text = text;
                }), cancellation.Token, _language
            );
            ConfigApplied = true;
            InstanceStore.SetPackBinding(_currentInstance, _selectedPack.Id,
                _selectedPack.ConfigBaseUrl, _selectedPack.ConfigFiles, _currentInstance.IsSteamCmd);
            if (!_closed) Close();
        }
        catch (OperationCanceledException)
        {
            if (!_closed) Find<TextBlock>("StatusText").Text = Localization.OperationCancelled(_language);
        }
        catch (Exception exception)
        {
            Find<TextBlock>("StatusText").Text =
                Localization.SyncConfigFailed(_language, exception.Message);
            AppLog.Error("Apply modpack configuration", exception);
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation)) _operationCancellation = null;
            _busy = false;
            if (!_closed)
            {
                SetControls(true);
            }
        }
    }

    private async Task InstallAsync(string? workshopLink, bool useSteamCmd, bool skipModsDownload = false)
    {
        if (_busy || _selectedPack is null || _game is null || _packRoot is null)
        {
            return;
        }

        _busy = true;
        SetControls(false);
        Find<TextBlock>("StatusText").Text =
            Localization.ModpackStatusInstalling(_language);


        var stagingRoot = Path.Combine(
            _packRoot,
            "runtime",
            "install-" + Guid.NewGuid().ToString("N")
        );
        var modsRoot = Path.Combine(stagingRoot, "Mods");
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        SetControls(false);
        try
        {
            var result = await ModpackService.InstallAsync(
                _selectedPack,
                _game,
                _packRoot,
                modsRoot,
                workshopLink,
                useSteamCmd,
                skipModsDownload,
                text =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_closed)
                        {
                            Find<TextBlock>("StatusText").Text = text;
                        }
                    });
                },
                cancellation.Token, _language
            );
            if (!_closed)
            {
                Close(result);
            }
            else
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(stagingRoot);
            if (!_closed)
            {
                Find<TextBlock>("StatusText").Text =
                    Localization.OperationCancelled(_language);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Install modpack", exception);
            TryDeleteDirectory(stagingRoot);
            if (!_closed)
            {
                Find<TextBlock>("StatusText").Text =
                    Localization.ModpackInstallFailed(_language, exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            _busy = false;
            if (!_closed)
            {
                SetControls(true);
            }
        }
    }

    private void OnCancelOperationClick(object? sender, RoutedEventArgs e)
    {
        _operationCancellation?.Cancel();
        Find<TextBlock>("StatusText").Text =
            Localization.OperationCancelled(_language);
    }
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_busy)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DisposeCover()
    {
        _coverBitmap?.Dispose();
        _coverStream?.Dispose();
        _coverBitmap = null;
        _coverStream = null;
    }

    private static HttpClient CreateImageHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame/1.0");
        return client;
    }
}









