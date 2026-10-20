using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PackFrame;

public partial class ExportCollectionWindow : Window
{
    private readonly ModpackRepoExportResult _result;
    private readonly string? _gamePath;
    private readonly string _language;
    private readonly string _indexUrl;
    private int _page = 1;
    private bool _busy;
    private string? _collectionUrl;
    private string _packName = string.Empty;
    private string _packDescription = string.Empty;
    private Bitmap? _coverBitmap;
    private string? _githubUrl;
    private string _lastRepoNameDefault = string.Empty;
    private string _lastOriginalRepoNameDefault = string.Empty;
    private string? _indexPrUrl;
    private bool _indexSubmitted;
    private bool _indexSubmitting;
    private CancellationTokenSource? _createCancellation;

    public ExportCollectionWindow()
    {
        InitializeComponent();
        _result = new ModpackRepoExportResult(
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<ulong>(),
            Array.Empty<ModpackRepoWorkshopMod>(),
            Array.Empty<string>(),
            Array.Empty<string>()
        );
        _language = "zh-Hans";
        _indexUrl = GitHubIndexPublisher.DefaultIndexUrl;
    }

    public ExportCollectionWindow(
        ModpackRepoExportResult result,
        string? gamePath,
        string? language,
        string? indexUrl
    )
        : this()
    {
        _result = result;
        _gamePath = gamePath;
        _language = string.IsNullOrWhiteSpace(language) ? "zh-Hans" : language;
        _indexUrl = string.IsNullOrWhiteSpace(indexUrl)
            ? GitHubIndexPublisher.DefaultIndexUrl
            : indexUrl;
        Configure();
        Closing += (_, _) =>
        {
            _createCancellation?.Cancel();
            _coverBitmap?.Dispose();
        };
    }

    private void Configure()
    {
        Title = Localization.ExportCollectionTitle(_language);
        RepoLabel.Text = Localization.ExportCollectionRepo(_language);
        RepoPath.Text = _result.RepoPath;
        OpenRepoButton.Content = Localization.ExportCollectionOpenRepo(_language);
        PackNameLabel.Text = Localization.ExportCollectionPackName(_language);
        PackDescriptionLabel.Text =
            Localization.ExportCollectionPackDescription(_language);
        _packName = _result.Name;
        _packDescription = _result.Description;
        PackNameBox.Text = _packName;
        PackDescriptionBox.Text = _packDescription;
        LocalModsHeader();
        BackButton.Content = Localization.CancelButton(_language);
        NextButton.Content = Localization.ExportCollectionNext(_language);

        SummaryText.Text =
            Localization.ExportCollectionWorkshop(
                _language,
                _result.WorkshopIds.Count
            ) + "    " +
            Localization.ExportCollectionLocal(
                _language,
                _result.LocalMods.Count
            ) + "    " +
            Localization.ExportCollectionMissing(
                _language,
                _result.MissingMods.Count
            );

        LocalList.ItemsSource = _result.LocalMods.ToList();
        MissingList.ItemsSource = _result.MissingMods.ToList();
        LocalHeader.IsVisible = _result.LocalMods.Count > 0;
        LocalList.IsVisible = _result.LocalMods.Count > 0;
        MissingHeader.IsVisible = _result.MissingMods.Count > 0;
        MissingList.IsVisible = _result.MissingMods.Count > 0;

        WorkshopHeader1.Text = Localization.ExportCollectionWorkshop(
            _language,
            _result.WorkshopIds.Count
        );
        WorkshopHeader2.Text = WorkshopHeader1.Text;
        var workshopNames = _result.WorkshopMods
            .Select(mod => mod.Name)
            .ToList();
        WorkshopList1.ItemsSource = workshopNames;
        WorkshopList2.ItemsSource = workshopNames;
        CreateCollectionButton.Content =
            Localization.ExportCollectionCreate(_language);
        OpenCollectionButton.Content =
            Localization.ExportCollectionOpenUrl(_language);
        CancelCreateButton.Content = Localization.CancelButton(_language);
        CancelCreateButton.IsVisible = false;
        CollectionNameLabel.Text =
            Localization.ExportCollectionTitleLabel(_language);
        CollectionDescriptionLabel.Text =
            Localization.ExportCollectionDescriptionLabel(_language);
        CollectionNameBox.Text = _packName;
        CollectionDescriptionBox.Text = _packDescription;
        PrivateNote.Text = Localization.ExportCollectionHint(_language);
        StatusText.Text = string.Empty;
        CreateCollectionButton.IsEnabled = _result.WorkshopIds.Count > 0;
        if (_result.WorkshopIds.Count == 0)
        {
            StatusText.Text = Localization.ExportCollectionNoWorkshop(_language);
        }

        IndexIntro.Text = Localization.ExportCollectionIndexIntro(_language);
        IndexRepoLabel.Text = Localization.ExportCollectionIndexRepoLabel(_language);
        IndexTokenLabel.Text = Localization.ExportCollectionGithubToken(_language);
        IndexHint.Text = Localization.ExportCollectionIndexHint(_language);
        SubmitIndexButton.Content =
            Localization.ExportCollectionSubmitIndex(_language);
        OpenIndexPrButton.Content =
            Localization.ExportCollectionOpenIndexPr(_language);
        OpenRepoIndexButton.Content =
            Localization.ExportCollectionOpenLocalRepo(_language);
        GithubPathLabel.Text = Localization.ExportCollectionRepo(_language);
        GithubPath.Text = _result.RepoPath;
        GithubTokenLabel.Text = Localization.ExportCollectionGithubToken(_language);
        GetTokenButton.Content = Localization.ExportCollectionGetToken(_language);
        GithubTokenHint.Text = Localization.ExportCollectionTokenHint(_language);
        GithubRepoNameLabel.Text = Localization.ExportCollectionRepoName(_language);
        GithubRepoDescriptionLabel.Text =
            Localization.ExportCollectionRepoDescription(_language);
        GithubCollectionUrlLabel.Text =
            Localization.ExportCollectionCollectionUrl(_language);
        UploadGithubButton.Content =
            Localization.ExportCollectionUploadGithub(_language);
        OpenGithubRepoButton.Content =
            Localization.ExportCollectionOpenGithubRepo(_language);
        OpenRepoGithubButton.Content =
            Localization.ExportCollectionOpenLocalRepo(_language);
        GithubCollectionUrlBox.Text = _collectionUrl ?? string.Empty;
        GithubRepoDescriptionBox.Text = _packDescription;
        _lastOriginalRepoNameDefault = _packName + "-Rimworld-PackFrame";
        _lastRepoNameDefault = GitHubPublisher.DefaultRepositoryName(_packName);
        GithubRepoNameBox.Text = _lastOriginalRepoNameDefault;
        UpdateRepoNamePreview();

        LoadExistingCover();
        ShowPage(1);
    }

    private void LocalModsHeader()
    {
        LocalHeader.Text = Localization.ExportCollectionLocal(
            _language,
            _result.LocalMods.Count
        );
        MissingHeader.Text = Localization.ExportCollectionMissing(
            _language,
            _result.MissingMods.Count
        );
    }

    private void ShowPage(int page)
    {
        _page = page;
        RepoPage.IsVisible = page == 1;
        SteamPage.IsVisible = page == 2;
        GithubPage.IsVisible = page == 3;
        IndexPage.IsVisible = page == 4;

        TitleText.Text = page switch
        {
            2 => Localization.ExportCollectionPageSteam(_language),
            3 => Localization.ExportCollectionPageGithub(_language),
            4 => Localization.ExportCollectionPageIndex(_language),
            _ => Localization.ExportCollectionPageRepo(_language)
        };
        BackButton.Content = page == 1
            ? Localization.CancelButton(_language)
            : Localization.ExportCollectionPrevious(_language);
        NextButton.Content = page >= 4
            ? Localization.ExportCollectionFinish(_language)
            : Localization.ExportCollectionNext(_language);
        if (page == 2)
        {
            CollectionNameBox.Text = _packName;
            CollectionDescriptionBox.Text = _packDescription;
        }
        if (page == 4)
        {
            if (!string.IsNullOrWhiteSpace(_githubUrl) &&
                string.IsNullOrWhiteSpace(IndexRepoUrlBox.Text))
            {
                IndexRepoUrlBox.Text = _githubUrl;
            }
            if (string.IsNullOrWhiteSpace(IndexTokenBox.Text))
            {
                IndexTokenBox.Text = GithubTokenBox.Text;
            }
        }
        if (page == 3)
        {
            var originalName = _packName + "-Rimworld-PackFrame";
            if (string.IsNullOrWhiteSpace(GithubRepoNameBox.Text) ||
                string.Equals(
                    GithubRepoNameBox.Text,
                    _lastRepoNameDefault,
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    GithubRepoNameBox.Text,
                    _lastOriginalRepoNameDefault,
                    StringComparison.Ordinal
                ))
            {
                GithubRepoNameBox.Text = originalName;
            }
            _lastOriginalRepoNameDefault = originalName;
            _lastRepoNameDefault = GitHubPublisher.DefaultRepositoryName(_packName);
            UpdateRepoNamePreview();
            if (!string.IsNullOrWhiteSpace(_collectionUrl))
            {
                GithubCollectionUrlBox.Text = _collectionUrl;
            }
        }
        RefreshGithubStatus();
    }

    private void LoadExistingCover()
    {
        var images = Path.Combine(_result.RepoPath, "images");
        if (!Directory.Exists(images))
        {
            return;
        }

        var cover = Directory.EnumerateFiles(images, "cover.*").FirstOrDefault();
        if (cover is not null)
        {
            LoadCover(cover);
        }
    }

    private void LoadCover(string path)
    {
        _coverBitmap?.Dispose();
        _coverBitmap = new Bitmap(path);
        CoverPreview.Source = _coverBitmap;
        CoverPlaceholder.IsVisible = false;
    }

    private void RefreshGithubStatus()
    {
        var images = Path.Combine(_result.RepoPath, "images");
        var hasCover = Directory.Exists(images) &&
            Directory.EnumerateFiles(images, "cover.*").Any();
        if (hasCover && CoverPreview.Source is null)
        {
            LoadExistingCover();
        }
    }

    private async void OnCoverPickerPressed(object? sender, PointerPressedEventArgs e)
    {
        await PickCoverAsync();
    }

    private async Task PickCoverAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0)
        {
            return;
        }

        var localPath = files[0].TryGetLocalPath();
        try
        {
            if (string.IsNullOrWhiteSpace(localPath))
            {
                var temporary = Path.Combine(
                    Path.GetTempPath(),
                    "packframe-cover-" + Guid.NewGuid().ToString("N")
                );
                await using (var source = await files[0].OpenReadAsync())
                await using (var target = File.Create(temporary))
                {
                    await source.CopyToAsync(target);
                }

                localPath = temporary;
            }

            var cover = await Task.Run(() =>
                ModpackRepoExporter.SetCoverImage(_result.RepoPath, localPath)
            );
            if (!string.IsNullOrWhiteSpace(cover))
            {
                LoadCover(cover);
            }
        }
        catch
        {
        }
    }

    private string? GetCoverPath()
    {
        var images = Path.Combine(_result.RepoPath, "images");
        return Directory.Exists(images)
            ? Directory.EnumerateFiles(images, "cover.*").FirstOrDefault()
            : null;
    }
    private void SavePackInfo()
    {
        var name = PackNameBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            _packName = name;
        }

        _packDescription = PackDescriptionBox.Text?.Trim() ?? string.Empty;
        ModpackRepoExporter.SetPackInfo(
            _result.RepoPath,
            _packName,
            _packDescription
        );
    }

    private async void OnCreateCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (_busy ||
            string.IsNullOrWhiteSpace(_gamePath) ||
            _result.WorkshopIds.Count == 0)
        {
            return;
        }

        var collectionName = CollectionNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            collectionName = _packName;
        }
        var collectionDescription =
            CollectionDescriptionBox.Text?.Trim() ?? _packDescription;
        using var cancellation = new CancellationTokenSource();
        _createCancellation = cancellation;
        _busy = true;
        CreateCollectionButton.IsEnabled = false;
        CancelCreateButton.IsVisible = true;
        StatusText.Text = Localization.ExportCollectionUploading(_language);
        try
        {
            var collectionId = await Task.Run(() =>
                SteamCollectionClient.CreatePrivateCollection(
                    _gamePath,
                    collectionName,
                    collectionDescription,
                    _result.WorkshopIds,
                    GetCoverPath(),
                    cancellation.Token
                )
            );

            _collectionUrl =
                $"https://steamcommunity.com/sharedfiles/filedetails/?id={collectionId}";
            ModpackRepoExporter.SetCollectionInfo(_result.RepoPath, collectionId);
            StatusText.Text = Localization.ExportCollectionSuccess(
                _language,
                collectionId,
                _collectionUrl
            );
            GithubCollectionUrlBox.Text = _collectionUrl;
            OpenCollectionButton.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Localization.OperationCancelled(_language);
        }
        catch (Exception exception)
        {
            StatusText.Text = Localization.ExportCollectionFailed(
                _language,
                exception.Message
            );
        }
        finally
        {
            if (ReferenceEquals(_createCancellation, cancellation))
            {
                _createCancellation = null;
            }
            _busy = false;
            CancelCreateButton.IsVisible = false;
            CreateCollectionButton.IsEnabled = _result.WorkshopIds.Count > 0;
        }
    }

    private void OnCancelCreateClick(object? sender, RoutedEventArgs e)
    {
        _createCancellation?.Cancel();
        StatusText.Text = Localization.OperationCancelled(_language);
    }
    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (_page == 1)
        {
            Close();
        }
        else
        {
            ShowPage(_page - 1);
        }
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (_page == 1)
        {
            SavePackInfo();
        }

        if (_page < 4)
        {
            ShowPage(_page + 1);
        }
        else
        {
            Close();
        }
    }

    private void OnOpenRepoClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_result.RepoPath))
        {
            InstanceStore.OpenFolder(_result.RepoPath);
        }
    }

    private void OnRepoNameChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateRepoNamePreview();
    }

    private void UpdateRepoNamePreview()
    {
        var input = GithubRepoNameBox.Text?.Trim() ?? string.Empty;
        GithubRepoNormalizedLabel.Text = Localization.ExportCollectionRepoNormalized(
            _language,
            GitHubPublisher.NormalizeRepositoryName(input)
        );
    }
    private async void OnSubmitIndexClick(object? sender, RoutedEventArgs e)
    {
        if (_indexSubmitting || _indexSubmitted)
        {
            return;
        }

        var token = IndexTokenBox.Text?.Trim();
        var repositoryUrl = IndexRepoUrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(repositoryUrl))
        {
            IndexStatus.Text = Localization.ExportCollectionIndexFailed(
                _language,
                "GitHub Token / repository link is empty."
            );
            return;
        }

        _indexSubmitting = true;
        SubmitIndexButton.IsEnabled = false;
        IndexStatus.Text = Localization.ExportCollectionIndexSubmitting(_language);
        try
        {
            var info = await GitHubIndexPublisher.ResolvePackAsync(
                token,
                repositoryUrl
            );
            var result = await GitHubIndexPublisher.PublishAsync(
                token,
                _indexUrl,
                info.Owner,
                info.Repository,
                info.Id,
                info.Name,
                info.Summary,
                info.PackageIds,
                GithubCollectionUrlBox.Text?.Trim() ?? string.Empty,
                text => Dispatcher.UIThread.Post(() => IndexStatus.Text = text)
            );
            _indexPrUrl = result.PullRequestUrl;
            _indexSubmitted = true;
            IndexStatus.Text = Localization.ExportCollectionIndexSuccess(
                _language,
                _indexPrUrl
            );
            OpenIndexPrButton.IsVisible = true;
            NextButton.Content = Localization.CloseButton(_language);
        }
        catch (Exception exception)
        {
            IndexStatus.Text = Localization.ExportCollectionIndexFailed(
                _language,
                exception.Message
            );
        }
        finally
        {
            _indexSubmitting = false;
            SubmitIndexButton.IsEnabled = true;
        }
    }

    private void OnOpenIndexPrClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_indexPrUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _indexPrUrl,
            UseShellExecute = true
        });
    }
    private void OnGetTokenClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/settings/tokens",
            UseShellExecute = true
        });
    }

    private async void OnUploadGithubClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var token = GithubTokenBox.Text?.Trim();
        var repoName = GithubRepoNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(repoName))
        {
            GithubStatus.Text = Localization.ExportCollectionGithubFailed(
                _language,
                "GitHub Token / repository name is empty."
            );
            return;
        }

        SavePackInfo();
        var description = GithubRepoDescriptionBox.Text?.Trim() ?? string.Empty;
        var collectionUrl = GithubCollectionUrlBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(collectionUrl))
        {
            ModpackRepoExporter.SetCollectionUrl(_result.RepoPath, collectionUrl);
        }
        ModpackRepoExporter.UpdateReadmeCover(_result.RepoPath);

        _busy = true;
        UploadGithubButton.IsEnabled = false;
        GithubStatus.Text = Localization.ExportCollectionGithubUploading(_language);
        try
        {
            var published = await Task.Run(() => GitHubPublisher.PublishAsync(
                _result.RepoPath,
                token,
                repoName,
                description,
                collectionUrl,
                text => Dispatcher.UIThread.Post(() =>
                {
                    GithubStatus.Text = text;
                })
            ));

            _githubUrl = published.Url;
            GithubStatus.Text = Localization.ExportCollectionGithubSuccess(
                _language,
                published.Url
            );
            OpenGithubRepoButton.IsVisible = true;
            OpenGithubRepoButton.Content =
                Localization.ExportCollectionOpenGithubRepo(_language);
        }
        catch (Exception exception)
        {
            GithubStatus.Text = Localization.ExportCollectionGithubFailed(
                _language,
                exception.Message
            );
        }
        finally
        {
            _busy = false;
            UploadGithubButton.IsEnabled = true;
        }
    }

    private void OnOpenGithubRepoClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_githubUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _githubUrl,
            UseShellExecute = true
        });
    }



    private void OnOpenCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_collectionUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _collectionUrl,
            UseShellExecute = true
        });
    }
}
