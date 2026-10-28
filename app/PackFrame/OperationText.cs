namespace PackFrame;

// Fixed operation messages, with English fallback and language captured by the caller.
internal static class OperationText
{
    public static string Choose(string? language, string en, string hans, string hant, string ja) => language switch
    {
        Localization.SimplifiedChinese => hans,
        Localization.TraditionalChinese => hant,
        Localization.Japanese => ja,
        _ => en
    };
    public static string Download(string? language) => Choose(language, "Downloading local mods archive...", "正在下载本地模组压缩包...", "正在下載本機模組壓縮包...", "ローカル MOD アーカイブをダウンロード中...");
    public static string Extract(string? language) => Choose(language, "Extracting local mods...", "正在解压本地模组...", "正在解壓本機模組...", "ローカル MOD を展開中...");
    public static string Install(string? language) => Choose(language, "Installing mods...", "正在安装模组...", "正在安裝模組...", "MOD をインストール中...");
    public static string ResolveCollection(string? language) => Choose(language, "Resolving Steam collection...", "正在解析 Steam 合集...", "正在解析 Steam 合集...", "Steam コレクションを解析中...");
    public static string ExtractProgress(
        string? language,
        int completed,
        int total,
        long bytes,
        long totalBytes,
        string currentFile,
        System.TimeSpan elapsed
    ) => Choose(
        language,
        $"Extracting: {completed:N0} / {total:N0} files | {FormatBytes(bytes)} / {FormatBytes(totalBytes)} | Current: {currentFile} | Elapsed: {FormatElapsed(elapsed)}",
        $"正在解压：{completed:N0} / {total:N0} 文件 | 已写入：{FormatBytes(bytes)} / {FormatBytes(totalBytes)} | 当前：{currentFile} | 已用时：{FormatElapsed(elapsed)}",
        $"正在解壓：{completed:N0} / {total:N0} 檔案 | 已寫入：{FormatBytes(bytes)} / {FormatBytes(totalBytes)} | 目前：{currentFile} | 已用時：{FormatElapsed(elapsed)}",
        $"展開中: {completed:N0} / {total:N0} ファイル | {FormatBytes(bytes)} / {FormatBytes(totalBytes)} | 現在: {currentFile} | 経過: {FormatElapsed(elapsed)}"
    );
    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
        if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
        if (bytes >= 1L << 10) return (bytes / (double)(1L << 10)).ToString("0.0") + " KB";
        return bytes + " B";
    }
    private static string FormatElapsed(System.TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    public static string Config(string? language, string name) => Choose(language, "Downloading config: ", "正在下载配置：", "正在下載設定：", "設定をダウンロード中: ") + name;
    public static string SteamDownload(string? language) => Choose(language, "Downloading/updating SteamCMD...", "正在下载并更新 SteamCMD...", "正在下載並更新 SteamCMD...", "SteamCMD をダウンロード・更新中...");
    public static string SteamExtract(string? language) => Choose(language, "Extracting SteamCMD...", "正在解压 SteamCMD...", "正在解壓 SteamCMD...", "SteamCMD を展開中...");
    public static string SteamInit(string? language) => Choose(language, "Initializing SteamCMD...", "正在初始化 SteamCMD...", "正在初始化 SteamCMD...", "SteamCMD を初期化中...");
    public static string SteamProgress(string? language) => Choose(language, "SteamCMD downloading", "SteamCMD 下载中", "SteamCMD 下載中", "SteamCMD ダウンロード中");
    public static string GithubLogin(string? language) => Choose(language, "Verifying GitHub login...", "正在验证 GitHub 登录...", "正在驗證 GitHub 登入...", "GitHub ログインを確認中...");
    public static string GithubRepo(string? language) => Choose(language, "Preparing GitHub repository...", "正在准备 GitHub 仓库...", "正在準備 GitHub 儲存庫...", "GitHub リポジトリを準備中...");
    public static string GithubAsset(string? language) => Choose(language, "Uploading large mods archive as a Release asset...", "正在将大型模组压缩包上传到 Release...", "正在將大型模組壓縮包上傳至 Release...", "大容量 MOD アーカイブを Release にアップロード中...");
    public static string GithubFiles(string? language) => Choose(language, "Uploading files", "正在上传文件", "正在上傳檔案", "ファイルをアップロード中");
    public static string GithubDone(string? language) => Choose(language, "GitHub upload complete.", "GitHub 上传完成。", "GitHub 上傳完成。", "GitHub アップロード完了。");
    public static string GiteeLogin(string? language) => Choose(language, "Verifying Gitee login...", "正在验证 Gitee 登录...", "正在驗證 Gitee 登入...", "Gitee ログインを確認中...");
    public static string GiteeRepo(string? language) => Choose(language, "Preparing Gitee repository...", "正在准备 Gitee 仓库...", "正在準備 Gitee 儲存庫...", "Gitee リポジトリを準備中...");
    public static string GiteeFiles(string? language) => Choose(language, "Uploading files", "正在上传文件", "正在上傳檔案", "ファイルをアップロード中");
    public static string GiteeCommit(string? language) => Choose(language, "Creating Gitee commit...", "正在创建 Gitee 提交...", "正在建立 Gitee 提交...", "Gitee コミットを作成中...");
    public static string GiteeDone(string? language) => Choose(language, "Gitee upload complete.", "Gitee 上传完成。", "Gitee 上傳完成。", "Gitee アップロード完了。");
    public static string IndexPrepare(string? language) => Choose(language, "Preparing index submission...", "正在准备合集列表审核...", "正在準備合集列表審核...", "一覧への掲載申請を準備中...");
    public static string IndexFork(string? language) => Choose(language, "Forking index repository...", "正在创建合集索引仓库分支...", "正在建立合集索引儲存庫分支...", "インデックスリポジトリをフォーク中...");
    public static string IndexWrite(string? language) => Choose(language, "Writing modpack files...", "正在写入合集文件...", "正在寫入合集檔案...", "MOD パックのファイルを書き込み中...");
    public static string IndexPr(string? language) => Choose(language, "Creating review pull request...", "正在创建审核 PR...", "正在建立審核 PR...", "レビュー用 PR を作成中...");
    public static string IndexDone(string? language) => Choose(language, "Review pull request created.", "审核 PR 已创建。", "審核 PR 已建立。", "レビュー用 PR を作成しました。");
    public static string IndexGiteePrepare(string? language) => Choose(language, "Preparing Gitee index commit...", "正在准备 Gitee 索引提交...", "正在準備 Gitee 索引提交...", "Gitee 索引コミットを準備中...");
    public static string IndexGiteePr(string? language) => Choose(language, "Creating Gitee review pull request...", "正在创建 Gitee 审核 PR...", "正在建立 Gitee 審核 PR...", "Gitee レビュー PR を作成中...");
    public static string IndexGiteeDone(string? language) => Choose(language, "Gitee review pull request created.", "Gitee 审核 PR 已创建。", "Gitee 審核 PR 已建立。", "Gitee レビュー PR が作成されました。");
}
