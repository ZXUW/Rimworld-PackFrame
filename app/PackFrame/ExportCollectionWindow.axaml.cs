using System;
using System.Collections.Generic;
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
    private string? _giteeUrl;
    private string? _giteePackUrl;
    private string _lastRepoNameDefault = string.Empty;
    private string _lastOriginalRepoNameDefault = string.Empty;
    private string _lastGiteeRepoNameDefault = string.Empty;
    private string? _indexPrUrl;
    private string? _giteeIndexPrUrl;
    private bool _indexSubmitted;
    private bool _indexSubmitting;
    private CancellationTokenSource? _createCancellation;
    private readonly ManagedInstance? _sourceInstance;
    private ulong? _boundCollectionId;

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
        string? indexUrl,
        ManagedInstance? sourceInstance = null
    )
        : this()
    {
        _result = result;
        _sourceInstance = sourceInstance;
        _gamePath = gamePath;
        _language = string.IsNullOrWhiteSpace(language) ? "zh-Hans" : language;
        _indexUrl = string.IsNullOrWhiteSpace(indexUrl)
            ? GitHubIndexPublisher.DefaultIndexUrl
            : indexUrl;
        if (_indexUrl.Contains("gitee.com", StringComparison.OrdinalIgnoreCase))
        {
            _indexUrl = GitHubIndexPublisher.DefaultIndexUrl;
        }
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
        var manifest = System.Text.Json.JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(_result.RepoPath, "pack.json")),
            PackFrameJsonContext.Default.ModpackRepoManifest);
        PackVersionLabel.Text = OperationText.Choose(_language, "Version (shown to players)", "版本号（显示给玩家）", "版本號（顯示給玩家）", "バージョン（プレイヤー向け表示）");
        PackChangelogLabel.Text = OperationText.Choose(_language, "What's new", "更新说明", "更新說明", "更新内容");
        PackVersionBox.Text = manifest?.Version ?? string.Empty;
        _boundCollectionId = ModpackService.ParseWorkshopId(manifest?.WorkshopCollectionId);
        PackChangelogBox.Text = manifest?.Changelog ?? string.Empty;
        PackRevisionText.Text = $"ID: {manifest?.Id}  ·  revision: {manifest?.Revision}\n" +
            OperationText.Choose(_language, "Revision is generated automatically. Keep the pack ID when publishing updates.",
                "revision 自动生成。发布更新时保持合集 ID 不变。", "revision 自動產生。發布更新時保持合集 ID 不變。",
                "revision は自動生成されます。更新時はパック ID を維持してください。");
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
            OperationText.Choose(_language, "Create / update collection", "创建 / 更新合集", "建立 / 更新合集", "コレクションを作成 / 更新");
        OpenCollectionButton.Content =
            Localization.ExportCollectionOpenUrl(_language);
        CancelCreateButton.Content = Localization.CancelButton(_language);
        CancelCreateButton.IsVisible = false;
        CollectionNameLabel.Text =
            Localization.ExportCollectionTitleLabel(_language);
        CollectionDescriptionLabel.Text =
            Localization.ExportCollectionDescriptionLabel(_language);
        InsertFrameworkLinkButton.Content =
            Localization.ExportCollectionInsertFrameworkLink(_language);
        CollectionNameBox.Text = _packName;
        CollectionDescriptionBox.Text = _packDescription;
        PrivateNote.Text = Localization.ExportCollectionHint(_language);
        StatusText.Text = string.Empty;
        CreateCollectionButton.IsEnabled = _result.WorkshopIds.Count > 0;
        if (_result.WorkshopIds.Count == 0)
        {
            StatusText.Text = Localization.ExportCollectionNoWorkshop(_language);
        }

        IndexIntro.Text = OperationText.Choose(
            _language,
            "Enter at least one modpack repository link. Submitting creates a review PR in the index repository; after it is merged, the pack appears in the list.",
            "至少填写一个合集仓库链接，提交后会向 Index 仓库创建审核 PR。审核合并后，合集会出现在列表里。",
            "至少填寫一個合集倉庫連結，提交後會向 Index 儲存庫建立審核 PR。審核合併後，合集會出現在列表裡。",
            "少なくとも 1 つの MOD パックリポジトリリンクを入力してください。Index リポジトリにレビュー PR を作成し、マージ後に一覧へ表示されます。"
        );
        IndexRepoLabel.Text = OperationText.Choose(
            _language,
            "GitHub modpack repository link (optional)",
            "GitHub 合集仓库链接（可选）",
            "GitHub 合集倉庫連結（可選）",
            "GitHub MOD パックリポジトリリンク（任意）"
        );
        IndexGiteeRepoLabel.Text = OperationText.Choose(
            _language,
            "Gitee modpack repository link (preferred)",
            "Gitee 合集仓库链接（优先）",
            "Gitee 合集倉庫連結（優先）",
            "Gitee MOD パックリポジトリリンク（優先）"
        );
        IndexTokenLabel.Text = OperationText.Choose(
            _language,
            "GitHub token (used to submit the index PR)",
            "GitHub Token（用于提交索引 PR）",
            "GitHub Token（用於提交索引 PR）",
            "GitHub トークン（Index PR の作成用）"
        );
        IndexGiteeTokenLabel.Text = OperationText.Choose(
            _language,
            "Gitee token (used to submit the Gitee index PR)",
            "Gitee Token（用于提交 Gitee 索引 PR）",
            "Gitee Token（用於提交 Gitee 索引 PR）",
            "Gitee トークン（Gitee Index PR の作成用）"
        );
        OpenGiteeIndexPrButton.Content = OperationText.Choose(
            _language,
            "Open Gitee PR",
            "打开 Gitee PR",
            "開啟 Gitee PR",
            "Gitee PR を開く"
        );
        IndexHint.Text = string.Empty;
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

        GiteeIntro.Text = OperationText.Choose(
            _language,
            "Upload the same exported repository to Gitee. Chinese users read the Gitee copy first.",
            "将同一份合集仓库上传到 Gitee。国内用户会优先读取 Gitee 镜像。",
            "將同一份合集倉庫上傳到 Gitee。國內使用者會優先讀取 Gitee 鏡像。",
            "同じ出力リポジトリを Gitee にもアップロードします。中国国内では Gitee が優先されます。"
        );
        GiteePathLabel.Text = Localization.ExportCollectionRepo(_language);
        GiteePath.Text = _result.RepoPath;
        GiteeTokenLabel.Text = OperationText.Choose(
            _language, "Gitee token", "Gitee Token", "Gitee Token", "Gitee トークン"
        );
        GetGiteeTokenButton.Content = OperationText.Choose(
            _language, "Get token", "获取 Token", "取得 Token", "トークンを取得"
        );
        GiteeTokenHint.Text = OperationText.Choose(
            _language,
            "Create a Gitee personal access token with projects permission.",
            "在 Gitee 创建私人令牌，勾选 projects 权限。",
            "在 Gitee 建立私人令牌，勾選 projects 權限。",
            "Gitee の個人アクセストークンを作成し、projects 権限を付与してください。"
        );
        GiteeRepoNameLabel.Text = Localization.ExportCollectionRepoName(_language);
        UploadGiteeButton.Content = OperationText.Choose(
            _language, "Upload to Gitee", "上传到 Gitee", "上傳到 Gitee", "Gitee へアップロード"
        );
        OpenGiteeRepoButton.Content = OperationText.Choose(
            _language, "Open Gitee repository", "打开 Gitee 仓库", "開啟 Gitee 儲存庫", "Gitee リポジトリを開く"
        );
        OpenRepoGiteeButton.Content = Localization.ExportCollectionOpenLocalRepo(_language);
        GiteeStatus.Text = string.Empty;
        _lastGiteeRepoNameDefault = _lastOriginalRepoNameDefault;
        GiteeRepoNameBox.Text = _lastGiteeRepoNameDefault;
        UpdateGiteeRepoNamePreview();

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
        GiteePage.IsVisible = page == 4;
        IndexPage.IsVisible = page == 5;

        TitleText.Text = page switch
        {
            2 => Localization.ExportCollectionPageSteam(_language),
            3 => Localization.ExportCollectionPageGithub(_language),
            4 => OperationText.Choose(_language, "Upload to Gitee", "上传到 Gitee", "上傳到 Gitee", "Gitee へアップロード"),
            5 => Localization.ExportCollectionPageIndex(_language),
            _ => Localization.ExportCollectionPageRepo(_language)
        };
        BackButton.Content = page == 1
            ? Localization.CancelButton(_language)
            : Localization.ExportCollectionPrevious(_language);
        NextButton.Content = page >= 5
            ? Localization.ExportCollectionFinish(_language)
            : Localization.ExportCollectionNext(_language);
        if (page == 2)
        {
            CollectionNameBox.Text = _packName;
            CollectionDescriptionBox.Text = _packDescription;
        }
        if (page == 5)
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
            if (!string.IsNullOrWhiteSpace(_giteeUrl) &&
                string.IsNullOrWhiteSpace(IndexGiteeRepoUrlBox.Text))
            {
                IndexGiteeRepoUrlBox.Text = _giteeUrl;
            }
            if (string.IsNullOrWhiteSpace(IndexGiteeTokenBox.Text))
            {
                IndexGiteeTokenBox.Text = GiteeTokenBox.Text;
            }
        }
        if (page == 4)
        {
            var originalName = _packName + "-Rimworld-PackFrame";
            if (string.IsNullOrWhiteSpace(GiteeRepoNameBox.Text) ||
                string.Equals(GiteeRepoNameBox.Text, _lastGiteeRepoNameDefault, StringComparison.Ordinal))
            {
                GiteeRepoNameBox.Text = originalName;
            }
            _lastGiteeRepoNameDefault = originalName;
            UpdateGiteeRepoNamePreview();
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
            _packDescription,
            PackVersionBox.Text,
            PackChangelogBox.Text
        );
    }

    private void OnInsertFrameworkLinkClick(object? sender, RoutedEventArgs e)
    {
        var current = CollectionDescriptionBox.Text?.TrimEnd() ?? string.Empty;
        var link = Localization.ExportCollectionFrameworkLinkText(_language);
        CollectionDescriptionBox.Text = current.Length == 0
            ? link
            : current + Environment.NewLine + Environment.NewLine + link;
        CollectionDescriptionBox.CaretIndex = CollectionDescriptionBox.Text?.Length ?? 0;
        CollectionDescriptionBox.Focus();
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
            ulong? SelectTarget(System.Collections.Generic.IReadOnlyList<OwnedCollection> candidates)
            {
                var completion = new TaskCompletionSource<ulong?>(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.UIThread.Post(async () =>
                {
                    if (cancellation.IsCancellationRequested) { completion.TrySetCanceled(cancellation.Token); return; }
                    try
                    {
                        var dialog = new CollectionTargetWindow(candidates, _language);
                        using var registration = cancellation.Token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
                        completion.TrySetResult(await dialog.ShowDialog<ulong?>(this));
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                });
                return completion.Task.WaitAsync(cancellation.Token).GetAwaiter().GetResult();
            }
            var createResult = await Task.Run(() =>
                SteamCollectionClient.CreatePrivateCollection(
                    _gamePath,
                    collectionName,
                    collectionDescription,
                    _result.WorkshopIds,
                    GetCoverPath(),
                    _result.WorkshopMods.ToDictionary(mod => mod.Id, mod => mod.Name),
                    cancellation.Token,
                    _boundCollectionId,
                    SelectTarget
                )
            );
            var collectionId = createResult.CollectionId;

            _collectionUrl =
                $"https://steamcommunity.com/sharedfiles/filedetails/?id={collectionId}";
            ModpackRepoExporter.SetCollectionInfo(_result.RepoPath, collectionId);
            _boundCollectionId = collectionId;
            if (_sourceInstance is { IsOfficial: false, IsScratch: false })
            {
                var metadata = _sourceInstance.Metadata ?? new InstanceMetadata();
                metadata.WorkshopCollectionId = collectionId.ToString();
                InstanceStore.SaveMetadata(_sourceInstance, metadata);
            }
            StatusText.Text = OperationText.Choose(_language, "Collection saved: ", "合集已保存：", "合集已儲存：", "コレクションを保存しました: ") + collectionId + "\n" + _collectionUrl;
            if (createResult.Failed.Count > 0)
            {
                var details = string.Join(
                    "\n",
                    createResult.Failed.Select(failure =>
                        string.IsNullOrWhiteSpace(failure.Name)
                            ? $"{failure.Id}: EResult {failure.Result}"
                            : $"{failure.Id} ({failure.Name}): EResult {failure.Result}")
                );
                StatusText.Text = Localization.ExportCollectionPartial(
                    _language,
                    createResult.Failed.Count
                ) + "\n" + _collectionUrl + "\n" + details;
            }
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

        if (_page < 5)
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
        var giteeToken = IndexGiteeTokenBox.Text?.Trim();
        var githubUrl = IndexRepoUrlBox.Text?.Trim();
        var giteeUrl = IndexGiteeRepoUrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(token) ||
            (string.IsNullOrWhiteSpace(githubUrl) && string.IsNullOrWhiteSpace(giteeUrl)))
        {
            IndexStatus.Text = Localization.ExportCollectionIndexFailed(
                _language,
                "GitHub Token / at least one repository link is empty."
            );
            return;
        }

        _indexSubmitting = true;
        SubmitIndexButton.IsEnabled = false;
        IndexStatus.Text = Localization.ExportCollectionIndexSubmitting(_language);
        var successes = new List<string>();
        var failures = new List<string>();
        try
        {
            GitHubPackInfo info;
            string? githubFile = null;
            var giteeFile = !string.IsNullOrWhiteSpace(giteeUrl)
                ? GiteePublisher.NormalizePackRawUrl(giteeUrl)
                : _giteePackUrl;
            if (!string.IsNullOrWhiteSpace(githubUrl))
            {
                info = await GitHubIndexPublisher.ResolvePackAsync(
                    token,
                    githubUrl
                );
                githubFile =
                    $"https://raw.githubusercontent.com/{info.Owner}/{info.Repository}/main/pack.json";
            }
            else
            {
                info = await GiteePublisher.ResolvePackAsync(giteeUrl!);
            }

            var repoFullName = $"{info.Owner}/{info.Repository}";
            try
            {
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
                    text => Dispatcher.UIThread.Post(() => IndexStatus.Text = text),
                    language: _language,
                    giteeFile: giteeFile,
                    githubFile: githubFile
                );
                _indexPrUrl = result.PullRequestUrl;
                successes.Add("GitHub: " + _indexPrUrl);
                OpenIndexPrButton.IsVisible = true;
            }
            catch (Exception exception)
            {
                failures.Add("GitHub: " + exception.Message);
            }

            if (!string.IsNullOrWhiteSpace(giteeFile))
            {
                if (string.IsNullOrWhiteSpace(giteeToken))
                {
                    failures.Add(OperationText.Choose(
                        _language,
                        "Gitee: token is empty.",
                        "Gitee：Token 为空。",
                        "Gitee：Token 為空。",
                        "Gitee: トークンが空です。"
                    ));
                }
                else
                {
                    try
                    {
                        var result = await GiteeIndexPublisher.PublishAsync(
                            giteeToken,
                            ModpackService.GiteeIndexUrl,
                            info.Id,
                            info.Name,
                            info.Summary,
                            githubFile,
                            giteeFile,
                            repoFullName,
                            info.PackageIds.Count,
                            text => Dispatcher.UIThread.Post(() => IndexStatus.Text = text),
                            language: _language
                        );
                        _giteeIndexPrUrl = result.PullRequestUrl;
                        successes.Add("Gitee: " + _giteeIndexPrUrl);
                        OpenGiteeIndexPrButton.IsVisible = true;
                    }
                    catch (Exception exception)
                    {
                        failures.Add("Gitee: " + exception.Message);
                    }
                }
            }

            IndexStatus.Text = string.Join(
                Environment.NewLine,
                successes.Concat(failures.Select(message => "FAILED " + message))
            );
            _indexSubmitted = successes.Count > 0;
            if (_indexSubmitted)
            {
                NextButton.Content = Localization.CloseButton(_language);
            }
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
    private void OnOpenGiteeIndexPrClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_giteeIndexPrUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _giteeIndexPrUrl,
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
                }), language: _language
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



    private void OnGiteeRepoNameChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateGiteeRepoNamePreview();
    }

    private void UpdateGiteeRepoNamePreview()
    {
        var input = GiteeRepoNameBox.Text?.Trim() ?? string.Empty;
        GiteeRepoNormalizedLabel.Text = Localization.ExportCollectionRepoNormalized(
            _language,
            GiteePublisher.NormalizeRepositoryName(input)
        );
    }

    private void OnGetGiteeTokenClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://gitee.com/profile/personal_access_tokens",
            UseShellExecute = true
        });
    }

    private async void OnUploadGiteeClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var token = GiteeTokenBox.Text?.Trim();
        var repoName = GiteeRepoNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(repoName))
        {
            GiteeStatus.Text = OperationText.Choose(
                _language,
                "Gitee token / repository name is empty.",
                "Gitee Token 或仓库名称为空。",
                "Gitee Token 或倉庫名稱為空。",
                "Gitee トークンまたはリポジトリ名が空です。"
            );
            return;
        }

        _busy = true;
        UploadGiteeButton.IsEnabled = false;
        _giteePackUrl = null;
        GiteeStatus.Text = OperationText.GiteeLogin(_language);
        try
        {
            var published = await Task.Run(() => GiteePublisher.PublishAsync(
                _result.RepoPath,
                token,
                repoName,
                _packDescription,
                text => Dispatcher.UIThread.Post(() => GiteeStatus.Text = text),
                language: _language
            ));

            _giteeUrl = published.Url;
            _giteePackUrl = published.RawUrl;
            GiteeStatus.Text = OperationText.Choose(
                _language,
                "Gitee upload complete: ",
                "Gitee 上传完成：",
                "Gitee 上傳完成：",
                "Gitee アップロード完了: "
            ) + published.Url;
            OpenGiteeRepoButton.IsVisible = true;
        }
        catch (Exception exception)
        {
            GiteeStatus.Text = OperationText.Choose(
                _language,
                "Gitee upload failed: ",
                "Gitee 上传失败：",
                "Gitee 上傳失敗：",
                "Gitee アップロード失敗: "
            ) + exception.Message;
        }
        finally
        {
            _busy = false;
            UploadGiteeButton.IsEnabled = true;
        }
    }

    private void OnOpenGiteeRepoClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_giteeUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _giteeUrl,
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
