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
    private readonly AppConfig _appConfig;
    private int _page = 1;
    private bool _busy;
    private string? _collectionUrl;
    private string _packName = string.Empty;
    private string _packDescription = string.Empty;
    private Bitmap? _coverBitmap;
    private string? _githubUrl;
    private string? _giteeUrl;
    private string? _giteePackUrl;
    private string _lastGiteeRepoNameDefault = string.Empty;
    private string? _indexPrUrl;
    private string? _giteeIndexPrUrl;
    private bool _indexSubmitted;
    private bool _indexSubmitting;
    private CancellationTokenSource? _createCancellation;
    private readonly ManagedInstance? _sourceInstance;
    private readonly Func<Action<string>, Task>? _packLocalMods;
    private bool _localModsPacked;
    private bool _packing;
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
        _appConfig = new AppConfig();
    }

    public ExportCollectionWindow(
        ModpackRepoExportResult result,
        string? gamePath,
        string? language,
        string? indexUrl,
        ManagedInstance? sourceInstance = null,
        Func<Action<string>, Task>? packLocalMods = null,
        AppConfig? appConfig = null
    )
        : this()
    {
        _result = result;
        _sourceInstance = sourceInstance;
        _packLocalMods = packLocalMods;
        _appConfig = appConfig ?? new AppConfig();
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
        GithubTokenLabel.Text = Localization.ExportCollectionGithubToken(_language);
        GetTokenButton.Content = Localization.ExportCollectionGetToken(_language);
        GithubTokenHint.Text = Localization.ExportCollectionTokenHint(_language);
        GithubRepoDescriptionLabel.Text = OperationText.Choose(
            _language,
            "Gitee repository description",
            "Gitee 仓库描述",
            "Gitee 倉庫描述",
            "Gitee リポジトリの説明"
        );
        GithubCollectionUrlLabel.Text =
            Localization.ExportCollectionCollectionUrl(_language);
        UploadGithubButton.Content =
            Localization.ExportCollectionUploadGithub(_language);
        OpenGithubRepoButton.Content =
            Localization.ExportCollectionOpenGithubRepo(_language);
        GithubCollectionUrlBox.Text = _collectionUrl ?? string.Empty;
        GithubRepoDescriptionBox.Text = _packDescription;

        GiteeIntro.Text = OperationText.Choose(
            _language,
            "Upload the metadata directly. Pack local mods first only if you want the archive included.",
            "可以直接上传合集信息；需要包含本地模组时再打包。",
            "可以直接上傳合集資訊；需要包含本地模組時再打包。",
            "メタデータはそのままアップロードできます。ローカル MOD を含める場合だけ先にパッケージしてください。"
        );
        GiteePathLabel.Text = Localization.ExportCollectionRepo(_language);
        GiteePath.Text = _result.RepoPath;
        PackLocalModsButton.Content = OperationText.Choose(
            _language, "Pack local mods", "打包本地模组", "打包本地模組", "ローカル MOD をパッケージ"
        );
        PackLocalModsStatus.Text = OperationText.Choose(
            _language,
            "Upload directly, or pack local mods first to include them.",
            "可以直接上传；需要包含本地模组时再打包。",
            "可以直接上傳；需要包含本地模組時再打包。",
            "そのままアップロードできます。ローカル MOD を含める場合は先にパッケージしてください。"
        );
        OssSettingsButton.Content = OperationText.Choose(
            _language, "OSS settings", "OSS 配置", "OSS 設定", "OSS 設定"
        );
        UpdateOssStatus();
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
        _lastGiteeRepoNameDefault = _packName + "-Rimworld-PackFrame";
        GiteeRepoNameBox.Text = _lastGiteeRepoNameDefault;
        UpdateGiteeRepoNamePreview();
        UploadGiteeButton.IsEnabled = true;
        UploadGithubButton.IsEnabled = true;

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
        GiteePage.IsVisible = page == 3;
        GithubPage.IsVisible = page == 4;
        IndexPage.IsVisible = page == 5;

        TitleText.Text = page switch
        {
            2 => Localization.ExportCollectionPageSteam(_language),
            3 => OperationText.Choose(_language, "Upload to Gitee", "上传到 Gitee", "上傳到 Gitee", "Gitee へアップロード"),
            4 => Localization.ExportCollectionPageGithub(_language),
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
        if (page == 3)
        {
            var originalName = _packName + "-Rimworld-PackFrame";
            if (string.IsNullOrWhiteSpace(GiteeRepoNameBox.Text) ||
                string.Equals(GiteeRepoNameBox.Text, _lastGiteeRepoNameDefault, StringComparison.Ordinal))
            {
                GiteeRepoNameBox.Text = originalName;
            }
            _lastGiteeRepoNameDefault = originalName;
            UpdateGiteeRepoNamePreview();
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
        var hasGithub = !string.IsNullOrWhiteSpace(githubUrl);
        var hasGitee = !string.IsNullOrWhiteSpace(giteeUrl);
        if (!hasGithub && !hasGitee)
        {
            IndexStatus.Text = Localization.ExportCollectionIndexFailed(
                _language,
                OperationText.Choose(
                    _language,
                    "At least one repository link is required.",
                    "至少填写一个合集仓库链接。",
                    "至少填寫一個合集倉庫連結。",
                    "少なくとも 1 つのリポジトリリンクを入力してください。"
                )
            );
            return;
        }
        if (hasGithub && string.IsNullOrWhiteSpace(token))
        {
            IndexStatus.Text = Localization.ExportCollectionIndexFailed(
                _language,
                OperationText.Choose(
                    _language,
                    "GitHub Token is required when a GitHub repository link is filled.",
                    "填写 GitHub 仓库链接时必须填写 GitHub Token。",
                    "填寫 GitHub 倉庫連結時必須填寫 GitHub Token。",
                    "GitHub リポジトリリンクを入力する場合は GitHub Token が必要です。"
                )
            );
            return;
        }
        if (hasGitee && string.IsNullOrWhiteSpace(giteeToken) && !hasGithub)
        {
            IndexStatus.Text = Localization.ExportCollectionIndexFailed(
                _language,
                OperationText.Choose(
                    _language,
                    "Gitee Token is required when only a Gitee repository link is filled.",
                    "只填写 Gitee 仓库链接时必须填写 Gitee Token。",
                    "只填寫 Gitee 倉庫連結時必須填寫 Gitee Token。",
                    "Gitee リポジトリリンクのみ入力する場合は Gitee Token が必要です。"
                )
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
                    token!,
                    githubUrl
                );
                githubFile =
                    $"https://raw.githubusercontent.com/{info.Owner}/{info.Repository}/main/pack.json";
            }
            else
            {
                info = await GiteePublisher.ResolvePackAsync(giteeUrl!);
            }

            var summary = await ShowIndexSummaryAsync(info.Summary);
            if (summary is null)
            {
                IndexStatus.Text = Localization.OperationCancelled(_language);
                return;
            }

            var repoFullName = $"{info.Owner}/{info.Repository}";
            if (hasGithub)
            {
                try
                {
                    var result = await GitHubIndexPublisher.PublishAsync(
                        token!,
                        _indexUrl,
                        info.Owner,
                        info.Repository,
                        info.Id,
                        info.Name,
                        summary,
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
                            summary,
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
    private async Task<string?> ShowIndexSummaryAsync(string fallback)
    {
        var inherited = PackDescriptionBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(inherited))
        {
            inherited = _packDescription;
        }
        if (string.IsNullOrWhiteSpace(inherited))
        {
            inherited = fallback;
        }

        var dialog = new IndexSummaryWindow(inherited, _language);
        return await dialog.ShowDialog<string?>(this);
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
        if (_busy || _packing)
        {
            return;
        }

        var token = GithubTokenBox.Text?.Trim();
        var repoName = GiteeRepoNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(repoName))
        {
            GithubStatus.Text = Localization.ExportCollectionGithubFailed(
                _language,
                "GitHub Token / repository name is empty."
            );
            return;
        }

        if (!await EnsureOssArchiveUploadedAsync(GithubStatus))
        {
            return;
        }

        var fallbackArchive = !OssPublisher.IsEnabled(_appConfig)
            ? StageFallbackArchive()
            : null;
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
            FinishFallbackArchive(fallbackArchive);
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
            RestoreFallbackArchive(fallbackArchive);
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

    private async void OnOssSettingsClick(object? sender, RoutedEventArgs e)
    {
        var window = new OssSettingsWindow(_appConfig, _language);
        if (await window.ShowDialog<bool>(this))
        {
            UpdateOssStatus();
        }
    }

    private void UpdateOssStatus()
    {
        if (!_appConfig.OssEnabled)
        {
            OssStatus.Text = OperationText.Choose(
                _language,
                "OSS upload is disabled. Local mods will use the Gitee/GitHub Release fallback.",
                "OSS 上传已关闭。本地模组将使用 Gitee/GitHub Release 回退。",
                "OSS 上傳已關閉。本地模組將使用 Gitee/GitHub Release 回退。",
                "OSS アップロードは無効です。ローカル MOD は Gitee/GitHub Release のフォールバックを使用します。"
            );
            return;
        }

        OssStatus.Text = OssPublisher.IsConfigured(_appConfig)
            ? OperationText.Choose(
                _language,
                "OSS configured: ",
                "OSS 已配置：",
                "OSS 已設定：",
                "OSS 設定済み: "
            ) + _appConfig.OssBucket
            : OperationText.Choose(
                _language,
                "OSS is not configured. Local mods will not be uploaded.",
                "尚未配置 OSS，本地模组不会上传。",
                "尚未設定 OSS，本地模組不會上傳。",
                "OSS が未設定のため、ローカル MOD はアップロードされません。"
            );
    }

    private string? StageFallbackArchive()
    {
        var uploadPath = EnsureNamedUploadArchive();
        if (uploadPath is null)
        {
            return null;
        }

        var fallbackPath = Path.Combine(
            _result.RepoPath,
            Path.GetFileName(uploadPath)
        );
        File.Move(uploadPath, fallbackPath, true);
        ModpackRepoExporter.SetModsArchive(
            _result.RepoPath,
            Path.GetFileName(fallbackPath)
        );
        return fallbackPath;
    }

    private string? FindRootArchive()
    {
        return Directory
            .EnumerateFiles(_result.RepoPath, "*", SearchOption.TopDirectoryOnly)
            .Where(file =>
            {
                var extension = Path.GetExtension(file);
                return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private string? EnsureNamedUploadArchive()
    {
        var uploadPath = ModpackRepoExporter.GetUploadArchivePath(_result.RepoPath);
        if (File.Exists(uploadPath))
        {
            return uploadPath;
        }

        return null;
    }

    private void FinishFallbackArchive(string? fallbackPath)
    {
        if (fallbackPath is null || !File.Exists(fallbackPath))
        {
            return;
        }

        var finishedPath = ModpackRepoExporter.GetFinishedArchivePath(_result.RepoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(finishedPath)!);
        var manifestPath = Path.Combine(_result.RepoPath, "pack.json");
        var manifest = System.Text.Json.JsonSerializer.Deserialize(
            File.ReadAllText(manifestPath),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest?.ModsArchive is { } archive &&
            Uri.TryCreate(archive, UriKind.Absolute, out _))
        {
            File.Move(fallbackPath, finishedPath, true);
        }
        else
        {
            File.Copy(fallbackPath, finishedPath, true);
        }
    }

    private static void RestoreFallbackArchive(string? fallbackPath)
    {
        if (fallbackPath is null || !File.Exists(fallbackPath))
        {
            return;
        }

        var repoPath = Path.GetDirectoryName(fallbackPath);
        if (repoPath is null)
        {
            return;
        }

        var uploadPath = ModpackRepoExporter.GetUploadArchivePath(repoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(uploadPath)!);
        File.Move(fallbackPath, uploadPath, true);
    }

    private async Task<bool> EnsureOssArchiveUploadedAsync(TextBlock status)
    {
        var finishedPath = ModpackRepoExporter.GetFinishedArchivePath(_result.RepoPath);
        var uploadPath = EnsureNamedUploadArchive();
        var archiveForLimit = uploadPath ?? FindRootArchive();
        const long automaticUploadLimit = 100L * 1024 * 1024;
        if (!OssPublisher.IsEnabled(_appConfig) &&
            archiveForLimit is not null &&
            new FileInfo(archiveForLimit).Length > automaticUploadLimit)
        {
            status.Text = OperationText.Choose(
                _language,
                "Local mods archive exceeds 100 MB and OSS is disabled; automatic upload skipped. Upload it manually later.",
                "本地模组压缩包超过 100MB，OSS 未启用，已跳过自动上传；请后续手动上传。",
                "本機模組壓縮包超過 100MB，OSS 未啟用，已略過自動上傳；請後續手動上傳。",
                "ローカル MOD のアーカイブが 100MB を超え、OSS が無効なため自動アップロードを中止しました。後で手動でアップロードしてください。"
            );
            return false;
        }

        if (uploadPath is null)
        {
            return true;
        }

        if (!_appConfig.OssEnabled)
        {
            status.Text = OperationText.Choose(
                _language,
                "OSS upload is disabled; using the Gitee/GitHub Release fallback for the local mods archive.",
                "OSS 上传已关闭；本地模组压缩包将使用 Gitee/GitHub Release 回退。",
                "OSS 上傳已關閉；本地模組壓縮包將使用 Gitee/GitHub Release 回退。",
                "OSS アップロードは無効です。ローカル MOD のアーカイブは Gitee/GitHub Release のフォールバックを使用します。"
            );
            return true;
        }

        if (!OssPublisher.IsConfigured(_appConfig))
        {
            status.Text = OperationText.Choose(
                _language,
                "OSS is not configured; using the Gitee/GitHub Release fallback for the local mods archive.",
                "未配置 OSS，本地模组压缩包将回退到 Gitee/GitHub Release。",
                "未設定 OSS，本地模組壓縮包將回退到 Gitee/GitHub Release。",
                "OSS が未設定のため、ローカル MOD は Gitee/GitHub Release にフォールバックします。"
            );
            return true;
        }

        status.Text = OperationText.Choose(
            _language,
            "Uploading local mods archive to OSS...",
            "正在上传本地模组压缩包到 OSS...",
            "正在上傳本地模組壓縮包到 OSS...",
            "ローカル MOD のアーカイブを OSS にアップロードしています..."
        );
        try
        {
            var url = await OssPublisher.UploadAsync(
                _appConfig,
                uploadPath,
                Path.GetFileName(uploadPath)
            );
            ModpackRepoExporter.SetModsArchive(_result.RepoPath, url);
            Directory.CreateDirectory(Path.GetDirectoryName(finishedPath)!);
            File.Move(uploadPath, finishedPath, true);
            status.Text = OperationText.Choose(
                _language,
                "Local mods archive uploaded to OSS.",
                "本地模组压缩包已上传到 OSS。",
                "本地模組壓縮包已上傳到 OSS。",
                "ローカル MOD のアーカイブを OSS にアップロードしました。"
            );
            return true;
        }
        catch (Exception exception)
        {
            status.Text = OperationText.Choose(
                _language,
                "OSS upload failed: ",
                "OSS 上传失败：",
                "OSS 上傳失敗：",
                "OSS アップロード失敗: "
            ) + exception.Message;
            return false;
        }
    }

    private async void OnPackLocalModsClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _packing || _localModsPacked)
        {
            return;
        }

        if (_packLocalMods is null)
        {
            PackLocalModsStatus.Text = OperationText.Choose(
                _language,
                "No local mod source is available.",
                "没有可用的本地模组来源。",
                "沒有可用的本地模組來源。",
                "利用できるローカル MOD がありません。"
            );
            return;
        }

        _packing = true;
        PackLocalModsButton.IsEnabled = false;
        UploadGiteeButton.IsEnabled = false;
        UploadGithubButton.IsEnabled = false;
        PackLocalModsStatus.Text = OperationText.Choose(
            _language,
            "Preparing local mods...",
            "正在准备本地模组...",
            "正在準備本地模組...",
            "ローカル MOD を準備しています..."
        );
        try
        {
            await _packLocalMods(text =>
                Dispatcher.UIThread.Post(() => PackLocalModsStatus.Text = text)
            );
            _localModsPacked = true;
            PackLocalModsStatus.Text = OperationText.Choose(
                _language,
                "Local mods packed. Upload is enabled.",
                "本地模组已打包，可以上传。",
                "本地模組已打包，可以上傳。",
                "ローカル MOD のパッケージが完了しました。アップロードできます。"
            );
            PackLocalModsButton.Content = OperationText.Choose(
                _language,
                "Local mods packed",
                "本地模组已打包",
                "本地模組已打包",
                "ローカル MOD パッケージ済み"
            );
            UploadGiteeButton.IsEnabled = true;
            UploadGithubButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            PackLocalModsStatus.Text = OperationText.Choose(
                _language,
                "Packing local mods failed: ",
                "打包本地模组失败：",
                "打包本地模組失敗：",
                "ローカル MOD のパッケージに失敗しました: "
            ) + exception.Message;
        }
        finally
        {
            _packing = false;
            UploadGiteeButton.IsEnabled = true;
            UploadGithubButton.IsEnabled = true;
            if (!_localModsPacked)
            {
                PackLocalModsButton.IsEnabled = true;
            }
        }
    }

    private async void OnUploadGiteeClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _packing)
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

        if (!await EnsureOssArchiveUploadedAsync(GiteeStatus))
        {
            return;
        }

        var fallbackArchive = !OssPublisher.IsEnabled(_appConfig)
            ? StageFallbackArchive()
            : null;
        SavePackInfo();
        var description = GithubRepoDescriptionBox.Text?.Trim() ?? string.Empty;
        var collectionUrl = GithubCollectionUrlBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(collectionUrl))
        {
            ModpackRepoExporter.SetCollectionUrl(_result.RepoPath, collectionUrl);
        }
        ModpackRepoExporter.UpdateReadmeCover(_result.RepoPath);

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
                description,
                text => Dispatcher.UIThread.Post(() => GiteeStatus.Text = text),
                language: _language
            ));

            _giteeUrl = published.Url;
            _giteePackUrl = published.RawUrl;
            FinishFallbackArchive(fallbackArchive);
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
            RestoreFallbackArchive(fallbackArchive);
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
