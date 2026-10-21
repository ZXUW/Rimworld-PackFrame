namespace PackFrame;

public static class Localization
{
    public static string IncompatibleMods(string? language, string names) => language switch
    {
        SimplifiedChinese => $"不兼容：{names}（调整顺序无法解决）",
        TraditionalChinese => $"不相容：{names}（調整順序無法解決）",
        Japanese => $"非互換: {names}（並べ替えでは解決しません）",
        _ => $"Incompatible: {names} (reordering will not resolve this)"
    };
    public static string DiscardChanges(string? language) => language switch
    {
        SimplifiedChinese => "丢弃修改", TraditionalChinese => "捨棄變更",
        Japanese => "変更を破棄", _ => "Discard changes"
    };

    public static string PendingChangesPrompt(string? language) => language switch
    {
        SimplifiedChinese => "模组列表有未保存的修改。是否保存？",
        TraditionalChinese => "模組清單有未儲存的變更。是否儲存？",
        Japanese => "MOD リストに未保存の変更があります。保存しますか？",
        _ => "The mod lists have unsaved changes. Save them?"
    };
    public const string English = "en";
    public const string SimplifiedChinese = "zh-Hans";
    public const string TraditionalChinese = "zh-Hant";
    public const string Japanese = "ja";

    public static bool IsSupported(string? language)
    {
        return language is English or SimplifiedChinese or TraditionalChinese or Japanese;
    }

    public static string GamePathLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "Rimworld 游戏路径",
            TraditionalChinese => "Rimworld 遊戲路徑",
            Japanese => "Rimworld ゲームパス",
            _ => "Rimworld Game Path"
        };
    }

    public static string AutoDetectButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "自动搜索",
            TraditionalChinese => "自動搜尋",
            Japanese => "自動検索",
            _ => "Auto Detect"
        };
    }

    public static string AutoDetected(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "已自动找到",
            TraditionalChinese => "已自動找到",
            Japanese => "自動検出しました",
            _ => "Auto-detected"
        };
    }

    public static string AutoDetectGameFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "未找到 RimWorld 游戏目录，请手动选择。",
            TraditionalChinese => "找不到 RimWorld 遊戲目錄，請手動選擇。",
            Japanese => "RimWorld のゲームフォルダーが見つかりません。手動で選択してください。",
            _ => "RimWorld game folder was not found. Select it manually."
        };
    }

    public static string AutoDetectConfigFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "未找到 Config 文件夹，请手动选择。",
            TraditionalChinese => "找不到 Config 資料夾，請手動選擇。",
            Japanese => "Config フォルダーが見つかりません。手動で選択してください。",
            _ => "Config folder was not found. Select it manually."
        };
    }

    public static string BrowseButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "选择文件夹",
            TraditionalChinese => "選擇資料夾",
            Japanese => "フォルダを選択",
            _ => "Browse"
        };
    }

    public static string Inactive(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "未启用",
            TraditionalChinese => "未啟用",
            Japanese => "未使用",
            _ => "Inactive"
        };
    }

    public static string Active(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "已启用",
            TraditionalChinese => "已啟用",
            Japanese => "使用中",
            _ => "Active"
        };
    }

    public static string InstanceName(string? language, int index)
    {
        return language switch
        {
            SimplifiedChinese => $"实例 {index}",
            TraditionalChinese => $"實例 {index}",
            Japanese => $"インスタンス {index}",
            _ => $"Instance {index}"
        };
    }

    public static string FieldName(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "名称",
            TraditionalChinese => "名稱",
            Japanese => "名前",
            _ => "Name"
        };
    }

    public static string FieldAuthor(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "作者",
            TraditionalChinese => "作者",
            Japanese => "作者",
            _ => "Author"
        };
    }

    public static string FieldPath(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "路径",
            TraditionalChinese => "路徑",
            Japanese => "パス",
            _ => "Path"
        };
    }

    public static string FieldPackageId(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "Package ID",
            TraditionalChinese => "Package ID",
            Japanese => "パッケージ ID",
            _ => "Package ID"
        };
    }

    public static string FieldDescription(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "简介",
            TraditionalChinese => "簡介",
            Japanese => "説明",
            _ => "Description"
        };
    }

    public static string Redo(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "重做",
            TraditionalChinese => "重做",
            Japanese => "やり直す",
            _ => "Redo"
        };
    }

    public static string RedoFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "重做失败：找不到模组",
            TraditionalChinese => "重做失敗：找不到模組",
            Japanese => "やり直せません：MODが見つかりません",
            _ => "Redo failed: mod not found"
        };
    }
    public static string Undo(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "撤销",
            TraditionalChinese => "復原",
            Japanese => "元に戻す",
            _ => "Undo"
        };
    }

    public static string UndoFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "撤回失败：找不到模组",
            TraditionalChinese => "復原失敗：找不到模組",
            Japanese => "元に戻せません：MODが見つかりません",
            _ => "Undo failed: mod not found"
        };
    }
    public static string NewInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "新建",
            TraditionalChinese => "新增",
            Japanese => "新規",
            _ => "New"
        };
    }

    public static string CopyInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "复制",
            TraditionalChinese => "複製",
            Japanese => "コピー",
            _ => "Copy"
        };
    }

    public static string RenameInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "重命名",
            TraditionalChinese => "重新命名",
            Japanese => "名前を変更",
            _ => "Rename"
        };
    }

    public static string DeleteInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "删除",
            TraditionalChinese => "刪除",
            Japanese => "削除",
            _ => "Delete"
        };
    }

    public static string ExportInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "导出",
            TraditionalChinese => "匯出",
            Japanese => "エクスポート",
            _ => "Export"
        };
    }

    public static string ImportInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "导入",
            TraditionalChinese => "匯入",
            Japanese => "インポート",
            _ => "Import"
        };
    }

    public static string OpenInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "打开",
            TraditionalChinese => "開啟",
            Japanese => "開く",
            _ => "Open"
        };
    }

    public static string DeleteConfirmMessage(string? language, string folderName)
    {
        return language switch
        {
            SimplifiedChinese => $"删除实例 {folderName}？其中的配置和存档会一起删除。",
            TraditionalChinese => $"刪除實例 {folderName}？其中的設定和存檔會一起刪除。",
            Japanese => $"インスタンス {folderName} を削除しますか？設定とセーブデータも削除されます。",
            _ => $"Delete instance {folderName}? Its config and saves will also be deleted."
        };
    }

    public static string Overwrite(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "覆盖",
            TraditionalChinese => "覆蓋",
            Japanese => "上書き",
            _ => "Overwrite"
        };
    }

    public static string OverwriteConfirmMessage(string? language, string displayName)
    {
        return language switch
        {
            SimplifiedChinese => $"用所选文件覆盖实例 {displayName} 的模组列表？当前 ModsConfig 会被替换。",
            TraditionalChinese => $"用所選檔案覆蓋實例 {displayName} 的模組清單？目前 ModsConfig 會被取代。",
            Japanese => $"選択したファイルでインスタンス {displayName} のMODリストを上書きしますか？現在の ModsConfig は置き換えられます。",
            _ => $"Overwrite the mod list for instance {displayName} with the selected file? Its ModsConfig will be replaced."
        };
    }

    public static string OverwriteArchiveConfirmMessage(string? language, string displayName)
    {
        return language switch
        {
            SimplifiedChinese => $"用所选压缩包覆盖实例 {displayName}？当前数据会被替换。",
            TraditionalChinese => $"用所選壓縮檔覆蓋實例 {displayName}？目前資料會被取代。",
            Japanese => $"選択したアーカイブでインスタンス {displayName} を上書きしますか？現在のデータは置き換えられます。",
            _ => $"Overwrite instance {displayName} with the selected archive? Its current data will be replaced."
        };
    }

    public static string DeleteButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "删除",
            TraditionalChinese => "刪除",
            Japanese => "削除",
            _ => "Delete"
        };
    }

    public static string CancelButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "取消",
            TraditionalChinese => "取消",
            Japanese => "キャンセル",
            _ => "Cancel"
        };
    }

    public static string ConfirmButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "确定",
            TraditionalChinese => "確定",
            Japanese => "OK",
            _ => "OK"
        };
    }

    public static string OfficialPath(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "官方路径",
            TraditionalChinese => "官方路徑",
            Japanese => "公式パス",
            _ => "Default"
        };
    }

    public static string ModifyConfigFolder(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "修改配置文件夹",
            TraditionalChinese => "修改設定資料夾",
            Japanese => "設定フォルダを変更",
            _ => "Modify Config Folder"
        };
    }

    public static string ConfigFolderTooltip(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "请选择 RimWorld 的 Config 文件夹，例如：%USERPROFILE%\\AppData\\LocalLow\\Ludeon Studios\\RimWorld by Ludeon Studios\\Config。官方路径会直接读写这个文件夹。",
            TraditionalChinese => "請選擇 RimWorld 的 Config 資料夾，例如：%USERPROFILE%\\AppData\\LocalLow\\Ludeon Studios\\RimWorld by Ludeon Studios\\Config。官方路徑會直接讀寫這個資料夾。",
            Japanese => "RimWorld の Config フォルダを選択してください（例：%USERPROFILE%\\AppData\\LocalLow\\Ludeon Studios\\RimWorld by Ludeon Studios\\Config）。公式パスはこのフォルダを直接読み書きします。",
            _ => "Select RimWorld's Config folder, for example: %USERPROFILE%\\AppData\\LocalLow\\Ludeon Studios\\RimWorld by Ludeon Studios\\Config. Default reads and writes this folder directly."
        };
    }

    public static string ConfigFolderWatermark(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "Config 文件夹路径",
            TraditionalChinese => "Config 資料夾路徑",
            Japanese => "Config フォルダのパス",
            _ => "Config folder path"
        };
    }

    public static string SearchByLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "搜索字段",
            TraditionalChinese => "搜尋欄位",
            Japanese => "検索対象",
            _ => "Search by"
        };
    }

    public static string SourceLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "来源",
            TraditionalChinese => "來源",
            Japanese => "ソース",
            _ => "Source"
        };
    }

    public static string SearchMods(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "搜索模组...",
            TraditionalChinese => "搜尋模組...",
            Japanese => "MODを検索...",
            _ => "Search mods..."
        };
    }

    public static string SearchByName(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "名称",
            TraditionalChinese => "名稱",
            Japanese => "名前",
            _ => "Name"
        };
    }

    public static string SearchByAuthor(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "作者",
            TraditionalChinese => "作者",
            Japanese => "作者",
            _ => "Author"
        };
    }

    public static string SearchByPath(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "路径",
            TraditionalChinese => "路徑",
            Japanese => "パス",
            _ => "Path"
        };
    }

    public static string SearchByPackageId(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "Package ID",
            TraditionalChinese => "Package ID",
            Japanese => "パッケージ ID",
            _ => "Package ID"
        };
    }

    public static string SearchByGameVersion(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "游戏版本",
            TraditionalChinese => "遊戲版本",
            Japanese => "ゲームバージョン",
            _ => "Game version"
        };
    }

    public static string SearchBySubscribedTime(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "订阅时间",
            TraditionalChinese => "訂閱時間",
            Japanese => "購読日時",
            _ => "Subscription time"
        };
    }

    public static string AllSources(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "全部",
            TraditionalChinese => "全部",
            Japanese => "すべて",
            _ => "All"
        };
    }

    public static string LocalMods(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "本地",
            TraditionalChinese => "本地",
            Japanese => "ローカル",
            _ => "Local"
        };
    }

    public static string WorkshopMods(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "工坊",
            TraditionalChinese => "工坊",
            Japanese => "ワークショップ",
            _ => "Workshop"
        };
    }

    public static string OfficialMods(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "官方",
            TraditionalChinese => "官方",
            Japanese => "公式",
            _ => "Official"
        };
    }
    public static string BadgeSortError(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "排序错误。",
            TraditionalChinese => "排序錯誤。",
            Japanese => "並び順エラー。",
            _ => "Load order error."
        };
    }

    public static string BadgeVersionMismatch(
        string? language,
        string supportedVersions,
        string? currentVersion
    )
    {
        return language switch
        {
            SimplifiedChinese => $"Mod版本({supportedVersions})； 不符合游戏版本({currentVersion})。",
            TraditionalChinese => $"Mod版本({supportedVersions})； 不符合遊戲版本({currentVersion})。",
            Japanese => $"Modバージョン({supportedVersions})； 現在のゲームバージョン({currentVersion})。",
            _ => $"Mod version({supportedVersions}); incompatible game version({currentVersion})."
        };
    }

    public static string BadgeDuplicate(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "文件夹里有两个模组是同一个 ID。",
            TraditionalChinese => "資料夾裡有兩個模組是同一個 ID。",
            Japanese => "フォルダ内に同じ ID の MOD が 2 つあります。",
            _ => "Two mods in the folders share the same ID."
        };
    }

    public static string ProblemSortTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "S — 排序错误",
            TraditionalChinese => "S — 排序錯誤",
            Japanese => "S — 並び順エラー",
            _ => "S — Sort order errors"
        };
    }

    public static string SortOrderErrorCategory(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "排序位置错误",
            TraditionalChinese => "排序位置錯誤",
            Japanese => "並び順エラー",
            _ => "Load order errors"
        };
    }

    public static string MissingDependencyCategory(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "缺少前置",
            TraditionalChinese => "缺少前置",
            Japanese => "不足している前提MOD",
            _ => "Missing prerequisites"
        };
    }

    public static string ProblemVersionTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "V — 游戏版本不匹配",
            TraditionalChinese => "V — 遊戲版本不匹配",
            Japanese => "V — ゲームバージョン不一致",
            _ => "V — Game version mismatch"
        };
    }

    public static string ProblemDuplicateTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "R — 重复模组",
            TraditionalChinese => "R — 重複模組",
            Japanese => "R — 重複MOD",
            _ => "R — Duplicate mods"
        };
    }

    public static string BadgeLocal(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "本地模组。",
            TraditionalChinese => "本地模組。",
            Japanese => "ローカル MOD。",
            _ => "Local mod."
        };
    }
    public static string SortAfterTooltip(string? language, string names)
    {
        return language switch
        {
            SimplifiedChinese => $"应排在：{names} 后面。",
            TraditionalChinese => $"應排在：{names} 後面。",
            Japanese => $"{names} の後に読み込む必要があります。",
            _ => $"Should load after: {names}."
        };
    }
    public static string MissingDependencies(string? language, string names)
    {
        return language switch
        {
            SimplifiedChinese => $"缺少前置：{names}。",
            TraditionalChinese => $"缺少前置：{names}。",
            Japanese => $"前提 MOD が見つかりません：{names}。",
            _ => $"Missing prerequisites: {names}."
        };
    }
    public static string SearchVisibilityHidden(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "未匹配模组：隐藏",
            TraditionalChinese => "未匹配模組：隱藏",
            Japanese => "未一致の MOD：非表示",
            _ => "Unmatched mods: hidden"
        };
    }

    public static string SearchVisibilityDimmed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "未匹配模组：半透明",
            TraditionalChinese => "未匹配模組：半透明",
            Japanese => "未一致の MOD：半透明",
            _ => "Unmatched mods: dimmed"
        };
    }
    public static string Ascending(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "升序",
            TraditionalChinese => "升冪",
            Japanese => "昇順",
            _ => "Ascending"
        };
    }

    public static string Descending(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "降序",
            TraditionalChinese => "降冪",
            Japanese => "降順",
            _ => "Descending"
        };
    }
    public static string Save(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "保存",
            TraditionalChinese => "儲存",
            Japanese => "保存",
            _ => "Save"
        };
    }

    public static string Refresh(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "刷新",
            TraditionalChinese => "重新整理",
            Japanese => "更新",
            _ => "Refresh"
        };
    }

    public static string UnsavedChanges(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "请先保存修改",
            TraditionalChinese => "請先儲存修改",
            Japanese => "先に変更を保存してください",
            _ => "Save your changes first"
        };
    }
    public static string Saved(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "已保存",
            TraditionalChinese => "已儲存",
            Japanese => "保存しました",
            _ => "Saved"
        };
    }

    public static string SaveFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "保存失败",
            TraditionalChinese => "儲存失敗",
            Japanese => "保存に失敗しました",
            _ => "Save failed"
        };
    }

    public static string LoadingMods(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "正在读取模组…",
            TraditionalChinese => "正在讀取模組…",
            Japanese => "MODを読み込み中…",
            _ => "Loading mods…"
        };
    }

    public static string Refreshed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "已刷新",
            TraditionalChinese => "已重新整理",
            Japanese => "更新しました",
            _ => "Refreshed"
        };
    }

    public static string RefreshDiscardConfirm(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "有未保存的修改。刷新会重新读取磁盘并丢弃这些修改，继续？",
            TraditionalChinese => "有未儲存的修改。重新整理會重新讀取磁碟並捨棄這些修改，繼續？",
            Japanese => "保存されていない変更があります。更新するとディスクから再読み込みし、変更は破棄されます。続行しますか？",
            _ => "There are unsaved changes. Refreshing will reload from disk and discard them. Continue?"
        };
    }
    public static string RefreshFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "刷新失败",
            TraditionalChinese => "重新整理失敗",
            Japanese => "更新に失敗しました",
            _ => "Refresh failed"
        };
    }
    public static string OpenInSteam(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "在 Steam 上打开",
            TraditionalChinese => "在 Steam 上開啟",
            Japanese => "Steam で開く",
            _ => "Open in Steam"
        };
    }

    public static string OpenInFolder(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "在文件夹中打开",
            TraditionalChinese => "在資料夾中開啟",
            Japanese => "フォルダーで開く",
            _ => "Open in Folder"
        };
    }

    public static string UnsubscribeMod(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "取消订阅",
            TraditionalChinese => "取消訂閱",
            Japanese => "購読を解除",
            _ => "Unsubscribe"
        };
    }

    public static string SteamUnavailable(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "Steam 未运行或无法初始化 Steamworks。",
            TraditionalChinese => "Steam 未執行或無法初始化 Steamworks。",
            Japanese => "Steam が実行されていないか、Steamworks を初期化できません。",
            _ => "Steam is not running or Steamworks could not initialize."
        };
    }

    public static string SteamUnsubscribed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "已通过 Steam 取消订阅",
            TraditionalChinese => "已透過 Steam 取消訂閱",
            Japanese => "Steam で購読を解除しました",
            _ => "Unsubscribed via Steam"
        };
    }

    public static string SteamUnsubscribeFailed(string? language, string error)
    {
        return language switch
        {
            SimplifiedChinese => $"取消订阅失败：{error}",
            TraditionalChinese => $"取消訂閱失敗：{error}",
            Japanese => $"購読解除に失敗しました：{error}",
            _ => $"Unsubscribe failed: {error}"
        };
    }

    public static string DeleteModFiles(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "删除模组文件",
            TraditionalChinese => "刪除模組檔案",
            Japanese => "MODファイルを削除",
            _ => "Delete Mod Files"
        };
    }

    public static string ColorMod(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "调色",
            TraditionalChinese => "調色",
            Japanese => "色を設定",
            _ => "Color"
        };
    }

    public static string ColorPaletteTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "选择模组颜色",
            TraditionalChinese => "選擇模組顏色",
            Japanese => "MODの色を選択",
            _ => "Choose Mod Color"
        };
    }

    public static string ColorPaletteHint(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "颜色和标签保存在 instance 根目录，所有实例共用。",
            TraditionalChinese => "顏色和標籤儲存在 instance 根目錄，所有實例共用。",
            Japanese => "色とラベルは instance ルートに保存され、全インスタンスで共有されます。",
            _ => "Colors and labels are saved in the instance root and shared by all instances."
        };
    }

    public static string ColorLabelPlaceholder(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "颜色标签",
            TraditionalChinese => "顏色標籤",
            Japanese => "色ラベル",
            _ => "Color label"
        };
    }

    public static string ClearColor(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "清除颜色",
            TraditionalChinese => "清除顏色",
            Japanese => "色をクリア",
            _ => "Clear Color"
        };
    }

    public static string PaintTool(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "画笔",
            TraditionalChinese => "畫筆",
            Japanese => "ブラシ",
            _ => "Brush"
        };
    }

    public static string EraserTool(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "橡皮",
            TraditionalChinese => "橡皮擦",
            Japanese => "消しゴム",
            _ => "Eraser"
        };
    }

    public static string DeleteModsConfirm(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"删除选中的 {count} 个本地模组文件夹？此操作不可恢复。",
            TraditionalChinese => $"刪除選取的 {count} 個本地模組資料夾？此操作無法復原。",
            Japanese => $"選択した {count} 個のローカルMODフォルダーを削除しますか？この操作は元に戻せません。",
            _ => $"Delete the {count} selected local mod folders? This cannot be undone."
        };
    }

    public static string SteamUnsubscribedCount(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"已通过 Steam 取消订阅 {count} 个模组",
            TraditionalChinese => $"已透過 Steam 取消訂閱 {count} 個模組",
            Japanese => $"Steam で {count} 件のMODの購読を解除しました",
            _ => $"Unsubscribed {count} mods via Steam"
        };
    }

    public static string DeleteModConfirm(string? language, string name)
    {
        return language switch
        {
            SimplifiedChinese => $"删除 {name} 的本地模组文件夹？此操作不可恢复。",
            TraditionalChinese => $"刪除 {name} 的本地模組資料夾？此操作無法復原。",
            Japanese => $"{name} のローカルMODフォルダーを削除しますか？この操作は元に戻せません。",
            _ => $"Delete the local mod folder for {name}? This cannot be undone."
        };
    }

    public static string ModFilesDeleted(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "模组文件已删除",
            TraditionalChinese => "模組檔案已刪除",
            Japanese => "MODファイルを削除しました",
            _ => "Mod files deleted"
        };
    }

    public static string ModDeleteFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "删除模组文件失败",
            TraditionalChinese => "刪除模組檔案失敗",
            Japanese => "MODファイルの削除に失敗しました",
            _ => "Failed to delete mod files"
        };
    }

    public static string MissingModsLabel(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"缺失 {count}",
            TraditionalChinese => $"缺失 {count}",
            Japanese => $"欠落 {count}",
            _ => $"Missing {count}"
        };
    }

    public static string MissingModsTooltip(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "查看缺失模组信息表",
            TraditionalChinese => "檢視缺失模組資訊表",
            Japanese => "欠落MODの情報を表示",
            _ => "View missing mod details"
        };
    }

    public static string MissingModsTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "缺失模组",
            TraditionalChinese => "缺失模組",
            Japanese => "欠落MOD",
            _ => "Missing Mods"
        };
    }

    public static string MissingModsSummary(string? language, string profile, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"{profile} 中有 {count} 个已启用模组在当前安装中缺失。",
            TraditionalChinese => $"{profile} 中有 {count} 個已啟用模組在目前安裝中缺失。",
            Japanese => $"{profile} で有効なMODのうち {count} 件が見つかりません。",
            _ => $"{profile} has {count} active mods missing from this installation."
        };
    }

    public static string MissingModsSourceNote(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "信息来自配置文件夹里的 ModFullNameList.json；未记录的模组仅显示 PackageId。",
            TraditionalChinese => "資訊來自設定資料夾裡的 ModFullNameList.json；未記錄的模組僅顯示 PackageId。",
            Japanese => "情報は設定フォルダーの ModFullNameList.json から取得します。未記録のMODは PackageId のみ表示します。",
            _ => "Details come from ModFullNameList.json in the profile folder; unrecorded mods show PackageId only."
        };
    }

    public static string MissingHistory(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "缺失历史",
            TraditionalChinese => "缺失歷史",
            Japanese => "欠落履歴",
            _ => "Missing History"
        };
    }

    public static string MissingHistoryLabel(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"历史 {count}",
            TraditionalChinese => $"歷史 {count}",
            Japanese => $"履歴 {count}",
            _ => $"History {count}"
        };
    }

    public static string MissingHistoryTooltip(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "查看缺失历史",
            TraditionalChinese => "檢視缺失歷史",
            Japanese => "欠落履歴を表示",
            _ => "View missing history"
        };
    }

    public static string MissingHistoryTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "缺失历史",
            TraditionalChinese => "缺失歷史",
            Japanese => "欠落履歴",
            _ => "Missing History"
        };
    }

    public static string MissingHistorySummary(string? language, string profile, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"{profile} 曾缺失 {count} 个模组。每次保存时，当前缺失模组会记录到这里。",
            TraditionalChinese => $"{profile} 曾缺失 {count} 個模組。每次儲存時，目前缺失模組會記錄到這裡。",
            Japanese => $"{profile} ではこれまでに {count} 件のMODが欠落しました。保存のたびに現在の欠落MODがここへ記録されます。",
            _ => $"{profile} has had {count} missing mods. Each save records currently missing mods here."
        };
    }

    public static string MissingHistoryEmpty(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "暂无缺失历史。",
            TraditionalChinese => "尚無缺失歷史。",
            Japanese => "欠落履歴はありません。",
            _ => "No missing history yet."
        };
    }

    public static string LastMissing(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "最后缺失",
            TraditionalChinese => "最後缺失",
            Japanese => "最終欠落",
            _ => "Last Missing"
        };
    }

    public static string ClearHistory(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "清空历史",
            TraditionalChinese => "清空歷史",
            Japanese => "履歴を消去",
            _ => "Clear History"
        };
    }

    public static string ClearHistoryConfirm(string? language, string profile)
    {
        return language switch
        {
            SimplifiedChinese => $"清空 {profile} 的缺失历史？此操作不可撤销。",
            TraditionalChinese => $"清空 {profile} 的缺失歷史？此操作無法復原。",
            Japanese => $"{profile} の欠落履歴を消去しますか？この操作は元に戻せません。",
            _ => $"Clear the missing history for {profile}? This cannot be undone."
        };
    }

    public static string MissingModsMappingPath(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "对照文件",
            TraditionalChinese => "對照檔案",
            Japanese => "照合ファイル",
            _ => "Mapping file"
        };
    }

    public static string Link(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "链接",
            TraditionalChinese => "連結",
            Japanese => "リンク",
            _ => "Link"
        };
    }

    public static string CloseButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "关闭",
            TraditionalChinese => "關閉",
            Japanese => "閉じる",
            _ => "Close"
        };
    }

    public static string LaunchGame(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "启动游戏",
            TraditionalChinese => "啟動遊戲",
            Japanese => "ゲームを起動",
            _ => "Launch Game"
        };
    }

    public static string LaunchFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "启动游戏失败",
            TraditionalChinese => "啟動遊戲失敗",
            Japanese => "ゲームを起動できません",
            _ => "Failed to launch game"
        };
    }

    public static string ModpacksButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "整合包",
            TraditionalChinese => "整合包",
            Japanese => "MODパック",
            _ => "Modpacks"
        };
    }

    public static string ModpacksTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "整合包",
            TraditionalChinese => "整合包",
            Japanese => "MODパック",
            _ => "Modpacks"
        };
    }

    public static string ModpackRepoDefault(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "默认仓库（ZXUW）",
            TraditionalChinese => "預設倉庫（ZXUW）",
            Japanese => "デフォルトリポジトリ（ZXUW）",
            _ => "Default repository (ZXUW)"
        };
    }

    public static string ModpackRepoCustom(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "自定义输入",
            TraditionalChinese => "自訂輸入",
            Japanese => "カスタム入力",
            _ => "Custom input"
        };
    }

    public static string ModpackRepoLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "仓库地址",
            TraditionalChinese => "倉庫位址",
            Japanese => "リポジトリURL",
            _ => "Repository"
        };
    }

    public static string ModpackRepoWatermark(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "https://raw.githubusercontent.com/用户/仓库/分支/index.json",
            TraditionalChinese => "https://raw.githubusercontent.com/使用者/倉庫/分支/index.json",
            Japanese => "https://raw.githubusercontent.com/ユーザー/リポジトリ/ブランチ/index.json",
            _ => "https://raw.githubusercontent.com/user/repo/branch/index.json"
        };
    }

    public static string ModpackNoUrl(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "请先填写整合包仓库地址",
            TraditionalChinese => "請先填寫整合包倉庫位址",
            Japanese => "MODパックのリポジトリURLを入力してください",
            _ => "Enter a modpack repository URL first"
        };
    }

    public static string ModpackLoadFailed(string? language, string details)
    {
        return language switch
        {
            SimplifiedChinese => $"加载整合包失败：{details}",
            TraditionalChinese => $"載入整合包失敗：{details}",
            Japanese => $"MODパックの読み込みに失敗しました：{details}",
            _ => $"Failed to load modpacks: {details}"
        };
    }

    public static string ModpackEmpty(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "仓库里没有整合包。",
            TraditionalChinese => "倉庫裡沒有整合包。",
            Japanese => "リポジトリにMODパックがありません。",
            _ => "The repository has no modpacks."
        };
    }

    public static string ModpackSelectHint(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "选择左侧整合包查看订阅方式。",
            TraditionalChinese => "選擇左側整合包查看訂閱方式。",
            Japanese => "左のMODパックを選択して導入方法を表示します。",
            _ => "Select a modpack on the left to see subscription options."
        };
    }

    public static string ModpackGameVersionLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "游戏版本",
            TraditionalChinese => "遊戲版本",
            Japanese => "ゲームバージョン",
            _ => "Game version"
        };
    }

    public static string ModpackModsHeader(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "模组",
            TraditionalChinese => "模組",
            Japanese => "MOD",
            _ => "Mods"
        };
    }

    public static string ModpackCollectionOnly(string? language, string id)
    {
        return language switch
        {
            SimplifiedChinese => $"安装时会从创意工坊合集 {id} 读取模组。",
            TraditionalChinese => $"安裝時會從創意工坊合集 {id} 讀取模組。",
            Japanese => $"導入時にワークショップコレクション {id} からMODを読み込みます。",
            _ => $"Mods will be read from Workshop collection {id} during installation."
        };
    }

    public static string ModpackModsCount(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"{count} 个模组",
            TraditionalChinese => $"{count} 個模組",
            Japanese => $"{count} 個のMOD",
            _ => $"{count} mods"
        };
    }

    public static string ModpackWorkshopCollection(string? language, string id)
    {
        return language switch
        {
            SimplifiedChinese => $"创意工坊合集 {id}",
            TraditionalChinese => $"創意工坊合集 {id}",
            Japanese => $"ワークショップコレクション {id}",
            _ => $"Workshop collection {id}"
        };
    }


    public static string ModpackNoDescription(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "暂无简介。",
            TraditionalChinese => "尚無簡介。",
            Japanese => "説明はありません。",
            _ => "No description."
        };
    }

    public static string ModpackInstall(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "安装并创建实例\n正版",
            TraditionalChinese => "安裝並建立實例\n正版",
            Japanese => "インスタンスを作成\n正規版",
            _ => "Install and create instance\nLicensed"
        };
    }

    public static string ModpackInstalling(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "正在安装...",
            TraditionalChinese => "正在安裝...",
            Japanese => "導入中...",
            _ => "Installing..."
        };
    }

    public static string ModpackLinkPlaceholder(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "粘贴创意工坊合集或模组链接",
            TraditionalChinese => "貼上創意工坊合集或模組連結",
            Japanese => "ワークショップのコレクションまたはMODリンクを貼り付け",
            _ => "Paste a Workshop collection or item link"
        };
    }

    public static string ModpackLinkLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创意工坊链接",
            TraditionalChinese => "創意工坊連結",
            Japanese => "ワークショップリンク",
            _ => "Workshop link"
        };
    }

    public static string ModpackSubscribeLink(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "订阅链接并创建实例",
            TraditionalChinese => "訂閱連結並建立實例",
            Japanese => "リンクを購読してインスタンスを作成",
            _ => "Subscribe link and create instance"
        };
    }

    public static string ModpackSteamCmd(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "SteamCMD 下载",
            TraditionalChinese => "SteamCMD 下載",
            Japanese => "SteamCMD ダウンロード",
            _ => "SteamCMD download"
        };
    }

    public static string ModpackApplyConfig(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "下载配置和排序到当前实例\n重置配置和排序",
            TraditionalChinese => "下載設定與排序到目前實例\n重置設定和排序",
            Japanese => "設定と並び順を現在のインスタンスへ\nリセット",
            _ => "Download config and load order to instance\nReset config and load order"
        };
    }

    public static string ModpackSteamCmdInstall(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "SteamCMD 下载并创建实例\n无正版",
            TraditionalChinese => "SteamCMD 下載並建立實例\n無正版",
            Japanese => "SteamCMD でダウンロードして作成\n非正規版",
            _ => "SteamCMD download and create\nNo license"
        };
    }

    public static string ModpackWorkshopOnlyInstall(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "只下载合集、配置和排序\n手动把本地模组放到\n该实例下的Mods目录",
            TraditionalChinese => "只下載合集、設定和排序\n手動把本地模組放到\n該實例下的Mods目錄",
            Japanese => "コレクション・設定・並び順のみ\nローカルMODは\n該当インスタンスのModsへ手動配置",
            _ => "Collection, config and order only\nPlace local mods in\nthis instance's Mods folder manually"
        };
    }

    public static string ModpackSteamCmdSoon(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "暂未支持",
            TraditionalChinese => "暫未支援",
            Japanese => "未対応",
            _ => "Coming later"
        };
    }

    public static string ModpackNeedsGame(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "请先设置有效的 RimWorld 游戏路径",
            TraditionalChinese => "請先設定有效的 RimWorld 遊戲路徑",
            Japanese => "有効な RimWorld ゲームパスを設定してください",
            _ => "Set a valid RimWorld game path first"
        };
    }

    public static string ModpackCreated(string? language, string name, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"已创建实例“{name}”，启用 {count} 个模组。",
            TraditionalChinese => $"已建立實例「{name}」，啟用 {count} 個模組。",
            Japanese => $"インスタンス「{name}」を作成し、{count} 個のMODを有効にしました。",
            _ => $"Created instance \"{name}\" with {count} active mods."
        };
    }

    public static string ModpackCreatedPending(
        string? language,
        string name,
        int count,
        int unresolved
    )
    {
        return language switch
        {
            SimplifiedChinese => $"已创建实例“{name}”，启用 {count} 个模组；{unresolved} 个工坊模组待下载后刷新启用。",
            TraditionalChinese => $"已建立實例「{name}」，啟用 {count} 個模組；{unresolved} 個工坊模組待下載後重新整理啟用。",
            Japanese => $"インスタンス「{name}」を作成し、{count} 個のMODを有効にしました。{unresolved} 個のワークショップMODはダウンロード後に更新して有効にしてください。",
            _ => $"Created instance \"{name}\" with {count} active mods; {unresolved} Workshop mods need to finish downloading before they can be activated."
        };
    }

    public static string ModpackCreateFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创建整合包实例失败",
            TraditionalChinese => "建立整合包實例失敗",
            Japanese => "MODパックのインスタンス作成に失敗しました",
            _ => "Failed to create the modpack instance"
        };
    }

    public static string ModpackInstallFailed(string? language, string details)
    {
        return language switch
        {
            SimplifiedChinese => $"整合包安装失败：{details}",
            TraditionalChinese => $"整合包安裝失敗：{details}",
            Japanese => $"MODパックの導入に失敗しました：{details}",
            _ => $"Modpack install failed: {details}"
        };
    }

    public static string SyncConfig(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "下载配置和排序",
            TraditionalChinese => "下載設定與排序",
            Japanese => "設定と並び順をダウンロード",
            _ => "Download config and load order"
        };
    }

    public static string SyncConfigNoInstance(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "请先选择一个普通实例",
            TraditionalChinese => "請先選擇一個一般實例",
            Japanese => "通常のインスタンスを選択してください",
            _ => "Select a normal instance first"
        };
    }

    public static string SyncConfigNoSource(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "当前实例没有绑定整合包配置",
            TraditionalChinese => "目前實例沒有綁定整合包設定",
            Japanese => "このインスタンスにはMODパック設定がありません",
            _ => "This instance has no modpack config source"
        };
    }

    public static string SyncConfigDone(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "配置和排序已同步",
            TraditionalChinese => "設定與排序已同步",
            Japanese => "設定と並び順を同期しました",
            _ => "Config and load order synced"
        };
    }

    public static string SyncConfigFailed(string? language, string details)
    {
        return language switch
        {
            SimplifiedChinese => $"同步配置失败：{details}",
            TraditionalChinese => $"同步設定失敗：{details}",
            Japanese => $"設定の同期に失敗しました：{details}",
            _ => $"Config sync failed: {details}"
        };
    }

    public static string ModpackGameRunning(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "请先退出正在运行的 RimWorld",
            TraditionalChinese => "請先關閉正在執行的 RimWorld",
            Japanese => "実行中の RimWorld を終了してください",
            _ => "Close RimWorld before launching another instance"
        };
    }

    public static string ModpackLinkPrepareFailed(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创建模组链接失败",
            TraditionalChinese => "建立模組連結失敗",
            Japanese => "MODリンクの作成に失敗しました",
            _ => "Failed to prepare mod links"
        };
    }

    public static string ModpackLinkInvalid(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创意工坊链接无效",
            TraditionalChinese => "創意工坊連結無效",
            Japanese => "ワークショップリンクが無効です",
            _ => "Workshop link is invalid"
        };
    }

    public static string ModpackSourceWorkshop(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创意工坊",
            TraditionalChinese => "創意工坊",
            Japanese => "ワークショップ",
            _ => "Workshop"
        };
    }

    public static string ModpackSourceGithub(string? language)
    {
        return "GitHub";
    }

    public static string ModpackSourceLocal(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "本地",
            TraditionalChinese => "本地",
            Japanese => "ローカル",
            _ => "Local"
        };
    }

    public static string ModpackSourceOfficial(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "官方",
            TraditionalChinese => "官方",
            Japanese => "公式",
            _ => "Official"
        };
    }

    public static string ModpackSourcePackage(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "已安装",
            TraditionalChinese => "已安裝",
            Japanese => "インストール済み",
            _ => "Installed"
        };
    }

    public static string ModpackStatusReading(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "正在读取整合包...",
            TraditionalChinese => "正在讀取整合包...",
            Japanese => "MODパックを読み込み中...",
            _ => "Reading modpacks..."
        };
    }

    public static string ModpackStatusInstalling(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "正在下载并订阅模组...",
            TraditionalChinese => "正在下載並訂閱模組...",
            Japanese => "MODをダウンロードして購読中...",
            _ => "Downloading and subscribing mods..."
        };
    }

    public static string ModpackNoMods(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "该整合包没有列出模组。",
            TraditionalChinese => "該整合包沒有列出模組。",
            Japanese => "このMODパックにはMODが登録されていません。",
            _ => "This modpack does not list any mods."
        };
    }

    public static string ModpackPackageUnknown(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "未提供 PackageId",
            TraditionalChinese => "未提供 PackageId",
            Japanese => "PackageId 未指定",
            _ => "No PackageId"
        };
    }

    public static string ExportCollectionTitle(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "输出合集",
            TraditionalChinese => "輸出合集",
            Japanese => "コレクション出力",
            _ => "Export collection"
        };
    }

    public static string ExportCollectionRepo(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "本地实例路径：",
            TraditionalChinese => "本機執行個體路徑：",
            Japanese => "ローカルインスタンスのパス：",
            _ => "Local instance path:"
        };
    }

    public static string ExportCollectionWorkshop(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"创意工坊模组：{count}",
            TraditionalChinese => $"創意工坊模組：{count}",
            Japanese => $"ワークショップ MOD：{count}",
            _ => $"Workshop mods: {count}"
        };
    }

    public static string ExportCollectionLocal(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"本地模组：{count}",
            TraditionalChinese => $"本機模組：{count}",
            Japanese => $"ローカル MOD：{count}",
            _ => $"Local mods: {count}"
        };
    }

    public static string ExportCollectionMissing(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"未找到来源：{count}",
            TraditionalChinese => $"找不到來源：{count}",
            Japanese => $"取得元が見つかりません：{count}",
            _ => $"Missing sources: {count}"
        };
    }

    public static string ExportCollectionTitleLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "合集标题",
            TraditionalChinese => "合集標題",
            Japanese => "コレクション名",
            _ => "Collection title"
        };
    }

    public static string ExportCollectionDescriptionLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "合集说明",
            TraditionalChinese => "合集說明",
            Japanese => "コレクション説明",
            _ => "Collection description"
        };
    }

    public static string ExportCollectionUpload(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "一键上传到创意工坊合集",
            TraditionalChinese => "一鍵上傳到創意工坊合集",
            Japanese => "ワークショップコレクションへアップロード",
            _ => "Upload to Workshop collection"
        };
    }

    public static string ExportCollectionUploading(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "正在创建创意工坊合集...",
            TraditionalChinese => "正在建立創意工坊合集...",
            Japanese => "ワークショップコレクションを作成中...",
            _ => "Creating Workshop collection..."
        };
    }

    public static string ExportCollectionPartial(string? language, int count)
    {
        return language switch
        {
            SimplifiedChinese => $"合集已创建，但 {count} 个模组添加失败（已跳过）：",
            TraditionalChinese => $"合集已建立，但 {count} 個模組加入失敗（已略過）：",
            Japanese => $"コレクションは作成されましたが、{count} 個の MOD を追加できませんでした（スキップ）：",
            _ => $"Collection created, but {count} mods could not be added (skipped):"
        };
    }

    public static string ExportCollectionSuccess(string? language, ulong id, string url)
    {
        return language switch
        {
            SimplifiedChinese => $"合集已创建：{id}\n{url}",
            TraditionalChinese => $"合集已建立：{id}\n{url}",
            Japanese => $"コレクションを作成しました：{id}\n{url}",
            _ => $"Collection created: {id}\n{url}"
        };
    }

    public static string ExportCollectionFailed(string? language, string error)
    {
        return language switch
        {
            SimplifiedChinese => $"上传失败：{error}",
            TraditionalChinese => $"上傳失敗：{error}",
            Japanese => $"アップロードに失敗しました：{error}",
            _ => $"Upload failed: {error}"
        };
    }

    public static string ExportCollectionNoWorkshop(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "没有可上传的创意工坊模组。",
            TraditionalChinese => "沒有可上傳的創意工坊模組。",
            Japanese => "アップロードできるワークショップ MOD がありません。",
            _ => "No Workshop mods to upload."
        };
    }

    public static string ExportCollectionHint(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "合集默认创建为私有；确认后在 Steam 页面公开。需要 Steam 已启动并登录。",
            TraditionalChinese => "合集預設建立為私人；確認後在 Steam 頁面公開。需要 Steam 已啟動並登入。",
            Japanese => "コレクションは既定で非公開です。確認後に Steam ページで公開してください。Steam へのログインが必要です。",
            _ => "The collection is created private. Publish it on the Steam page after checking it. Steam must be running and signed in."
        };
    }

    public static string ExportCollectionOpenRepo(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "打开实例目录",
            TraditionalChinese => "開啟執行個體目錄",
            Japanese => "インスタンスフォルダーを開く",
            _ => "Open instance folder"
        };
    }

    public static string ExportCollectionOpenUrl(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "打开合集页面",
            TraditionalChinese => "開啟合集頁面",
            Japanese => "コレクションページを開く",
            _ => "Open collection page"
        };
    }

    public static string ExportCollectionPrivateNote(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创意工坊 ID 会按加载顺序加入合集；本地模组不会加入。",
            TraditionalChinese => "創意工坊 ID 會依載入順序加入合集；本機模組不會加入。",
            Japanese => "ワークショップ ID は読み込み順に追加されます。ローカル MOD は追加されません。",
            _ => "Workshop IDs are added in load order; local mods are not included."
        };
    }
    public static string ExportCollectionPageRepo(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "导出整合包",
            TraditionalChinese => "匯出整合包",
            Japanese => "MOD パックを出力",
            _ => "Export modpack"
        };
    }

    public static string ExportCollectionPageSteam(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创建创意工坊合集",
            TraditionalChinese => "建立創意工坊合集",
            Japanese => "ワークショップコレクションを作成",
            _ => "Create Workshop collection"
        };
    }

    public static string ExportCollectionPageGithub(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "上传到 GitHub",
            TraditionalChinese => "上傳到 GitHub",
            Japanese => "GitHub へアップロード",
            _ => "Upload to GitHub"
        };
    }

    public static string ExportCollectionPackName(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "整合包名称",
            TraditionalChinese => "整合包名稱",
            Japanese => "MOD パック名",
            _ => "Modpack name"
        };
    }
    public static string ExportCollectionPackDescription(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "整合包简介",
            TraditionalChinese => "整合包簡介",
            Japanese => "MOD パックの説明",
            _ => "Modpack description"
        };
    }
    public static string ExportCollectionPreview(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "预览图",
            TraditionalChinese => "預覽圖",
            Japanese => "プレビュー画像",
            _ => "Preview image"
        };
    }

    public static string ExportCollectionUploadPreview(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "上传预览图",
            TraditionalChinese => "上傳預覽圖",
            Japanese => "プレビュー画像をアップロード",
            _ => "Upload preview image"
        };
    }

    public static string ExportCollectionNext(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "下一步",
            TraditionalChinese => "下一步",
            Japanese => "次へ",
            _ => "Next"
        };
    }

    public static string ExportCollectionPrevious(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "上一步",
            TraditionalChinese => "上一步",
            Japanese => "戻る",
            _ => "Back"
        };
    }

    public static string ExportCollectionFinish(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "完成",
            TraditionalChinese => "完成",
            Japanese => "完了",
            _ => "Finish"
        };
    }

    public static string ExportCollectionCreate(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创建合集",
            TraditionalChinese => "建立合集",
            Japanese => "コレクションを作成",
            _ => "Create collection"
        };
    }

    public static string ExportCollectionOpenGithub(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "打开 GitHub 新建仓库",
            TraditionalChinese => "開啟 GitHub 新增儲存庫",
            Japanese => "GitHub の新規リポジトリを開く",
            _ => "Open GitHub new repository"
        };
    }

    public static string ExportCollectionGithubIntro(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "仓库目录已经生成。下一步在 GitHub 新建仓库，上传整个导出目录即可。",
            TraditionalChinese => "儲存庫目錄已產生。下一步在 GitHub 新增儲存庫，上傳整個匯出目錄即可。",
            Japanese => "リポジトリのフォルダーを作成しました。GitHub で新しいリポジトリを作成し、出力フォルダー全体をアップロードしてください。",
            _ => "The repository folder is ready. Create a new GitHub repository and upload the whole export folder."
        };
    }

    public static string ExportCollectionGithubSteps(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "1. 新建公开仓库\n2. 上传 pack.json、config、mods、images\n3. 需要时填写 pack.json 的 configBaseUrl",
            TraditionalChinese => "1. 新增公開儲存庫\n2. 上傳 pack.json、config、mods、images\n3. 需要時填寫 pack.json 的 configBaseUrl",
            Japanese => "1. 公開リポジトリを作成\n2. pack.json、config、mods、images をアップロード\n3. 必要なら pack.json の configBaseUrl を設定",
            _ => "1. Create a public repository\n2. Upload pack.json, config, mods, and images\n3. Set configBaseUrl in pack.json if needed"
        };
    }

    public static string ExportCollectionCoverSaved(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "预览图已保存到 images/。",
            TraditionalChinese => "預覽圖已儲存到 images/。",
            Japanese => "プレビュー画像を images/ に保存しました。",
            _ => "Preview image saved to images/."
        };
    }
    public static string ExportCollectionOpenLocalRepo(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "打开本地仓库",
            TraditionalChinese => "開啟本機儲存庫",
            Japanese => "ローカルリポジトリを開く",
            _ => "Open local repository"
        };
    }

    public static string ExportCollectionGithubToken(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "GitHub Token",
            TraditionalChinese => "GitHub Token",
            Japanese => "GitHub トークン",
            _ => "GitHub token"
        };
    }

    public static string ExportCollectionGetToken(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "获取 Token",
            TraditionalChinese => "取得 Token",
            Japanese => "トークンを取得",
            _ => "Get token"
        };
    }

    public static string ExportCollectionTokenHint(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "1. 点“获取 Token”。\n2. 在弹出的网页页面里点\"Generate new token\"，点下拉菜单里的\"Generate new token (classic)\"。\n3. 新页面最上方“Note”输入框里填 PackFrame。\n4. 往下找到第一大项\"repo\"，勾上。\n5. 拉到页面最底部，点绿色按钮\"Generate token\"。\n6. 新页面顶部会出现 ghp_ 开头的 Token。点右边的复制按钮，回到这里，粘贴到 GitHub Token 输入框。",
            TraditionalChinese => "1. 開啟 classic token 頁面。\n2. Note 填 PackFrame，Expiration 依需求選擇。\n3. Select scopes 只勾 repo（Full control of private repositories）。\n4. 確保 admin:org 及其子項 write:org、read:org、manage_runners:org 全部取消。\n5. workflow、packages、user、delete:repo 等其他權限全部不勾。\n6. 點 Generate token，複製 ghp_... 貼到上方。",
            Japanese => "1. classic token ページを開きます。\n2. Note に PackFrame を入力し、Expiration を選択します。\n3. Select scopes では repo（Full control of private repositories）だけを選択します。\n4. admin:org とその子項目 write:org、read:org、manage_runners:org をすべて外します。\n5. workflow、packages、user、delete:repo などは選択しません。\n6. Generate token を押し、ghp_... をコピーして上に貼り付けます。",
            _ => "1. Open the classic token page.\n2. Enter PackFrame as Note and choose an Expiration.\n3. Under Select scopes, check only repo (Full control of private repositories).\n4. Uncheck admin:org and its children write:org, read:org, and manage_runners:org.\n5. Leave workflow, packages, user, delete:repo, and all other scopes unchecked.\n6. Click Generate token, copy ghp_..., and paste it above."
        };
    }
    public static string ExportCollectionRepoName(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "仓库名称",
            TraditionalChinese => "儲存庫名稱",
            Japanese => "リポジトリ名",
            _ => "Repository name"
        };
    }

    public static string ExportCollectionRepoNormalized(string? language, string name)
    {
        return language switch
        {
            SimplifiedChinese => $"自动规范后的仓库名：{name}",
            TraditionalChinese => $"自動規範後的儲存庫名稱：{name}",
            Japanese => $"自動正規化後のリポジトリ名：{name}",
            _ => $"Normalized repository name: {name}"
        };
    }
    public static string ExportCollectionRepoDescription(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "仓库简介",
            TraditionalChinese => "儲存庫簡介",
            Japanese => "リポジトリの説明",
            _ => "Repository description"
        };
    }

    public static string ExportCollectionCollectionUrl(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "创意工坊合集链接",
            TraditionalChinese => "創意工坊合集連結",
            Japanese => "ワークショップコレクションのリンク",
            _ => "Workshop collection link"
        };
    }

    public static string ExportCollectionUploadGithub(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "上传到 GitHub",
            TraditionalChinese => "上傳到 GitHub",
            Japanese => "GitHub へアップロード",
            _ => "Upload to GitHub"
        };
    }

    public static string ExportCollectionGithubUploading(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "正在上传到 GitHub...",
            TraditionalChinese => "正在上傳到 GitHub...",
            Japanese => "GitHub へアップロード中...",
            _ => "Uploading to GitHub..."
        };
    }

    public static string ExportCollectionGithubSuccess(string? language, string url)
    {
        return language switch
        {
            SimplifiedChinese => $"GitHub 仓库已上传：{url}",
            TraditionalChinese => $"GitHub 儲存庫已上傳：{url}",
            Japanese => $"GitHub リポジトリをアップロードしました：{url}",
            _ => $"GitHub repository uploaded: {url}"
        };
    }

    public static string ExportCollectionGithubFailed(string? language, string error)
    {
        return language switch
        {
            SimplifiedChinese => $"GitHub 上传失败：{error}",
            TraditionalChinese => $"GitHub 上傳失敗：{error}",
            Japanese => $"GitHub アップロードに失敗しました：{error}",
            _ => $"GitHub upload failed: {error}"
        };
    }

    public static string ExportCollectionOpenGithubRepo(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "打开 GitHub 仓库",
            TraditionalChinese => "開啟 GitHub 儲存庫",
            Japanese => "GitHub リポジトリを開く",
            _ => "Open GitHub repository"
        };
    }
    public static string OperationCancelled(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "已取消。",
            TraditionalChinese => "已取消。",
            Japanese => "キャンセルしました。",
            _ => "Cancelled."
        };
    }
    public static string ExportCollectionSubmitIndex(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "提交到整合包列表",
            TraditionalChinese => "提交到整合包列表",
            Japanese => "MOD パック一覧へ送信",
            _ => "Submit to modpack index"
        };
    }

    public static string ExportCollectionIndexSubmitting(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "正在提交到整合包列表审核...",
            TraditionalChinese => "正在提交到整合包列表審核...",
            Japanese => "MOD パック一覧のレビューへ送信中...",
            _ => "Submitting to the modpack index for review..."
        };
    }

    public static string ExportCollectionIndexSuccess(string? language, string url)
    {
        return language switch
        {
            SimplifiedChinese => $"审核 PR 已创建：{url}",
            TraditionalChinese => $"審核 PR 已建立：{url}",
            Japanese => $"レビュー PR を作成しました：{url}",
            _ => $"Review PR created: {url}"
        };
    }

    public static string ExportCollectionIndexFailed(string? language, string error)
    {
        return language switch
        {
            SimplifiedChinese => $"提交整合包列表失败：{error}",
            TraditionalChinese => $"提交整合包列表失敗：{error}",
            Japanese => $"MOD パック一覧への送信に失敗しました：{error}",
            _ => $"Failed to submit to the modpack index: {error}"
        };
    }

    public static string ExportCollectionOpenIndexPr(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "打开审核 PR",
            TraditionalChinese => "開啟審核 PR",
            Japanese => "レビュー PR を開く",
            _ => "Open review PR"
        };
    }

    public static string ExportCollectionNeedGithubUpload(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "请先完成 GitHub 上传。",
            TraditionalChinese => "請先完成 GitHub 上傳。",
            Japanese => "先に GitHub へのアップロードを完了してください。",
            _ => "Upload to GitHub first."
        };
    }
    public static string ExportCollectionPageIndex(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "提交到整合包列表",
            TraditionalChinese => "提交到整合包列表",
            Japanese => "MOD パック一覧へ送信",
            _ => "Submit to modpack index"
        };
    }

    public static string ExportCollectionIndexIntro(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "填写已创建的 GitHub 整合包仓库链接，提交后会向你的 Index 仓库创建审核 PR。审核合并后，整合包会出现在列表里。",
            TraditionalChinese => "填寫已建立的 GitHub 整合包儲存庫連結，提交後會向你的 Index 儲存庫建立審核 PR。審核合併後，整合包會出現在列表裡。",
            Japanese => "作成済みの GitHub MOD パックリポジトリのリンクを入力すると、Index リポジトリにレビュー PR を作成します。マージ後、一覧に表示されます。",
            _ => "Enter the GitHub modpack repository link. Submitting creates a review PR in your index repository; after it is merged, the pack appears in the list."
        };
    }

    public static string ExportCollectionIndexRepoLabel(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "整合包仓库链接（发布链接）",
            TraditionalChinese => "整合包儲存庫連結（發佈連結）",
            Japanese => "MOD パックのリポジトリリンク",
            _ => "Modpack repository link"
        };
    }

    public static string ExportCollectionIndexHint(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "可以直接填写自己建立的仓库链接；Token 用于创建分支和审核 PR。",
            TraditionalChinese => "可以直接填寫自己建立的儲存庫連結；Token 用於建立分支和審核 PR。",
            Japanese => "自分で作成したリポジトリのリンクを直接入力できます。トークンはブランチとレビュー PR の作成に使います。",
            _ => "You can paste a repository you created yourself. The token is used to create the branch and review PR."
        };
    }
    public static string CopyButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "复制",
            TraditionalChinese => "複製",
            Japanese => "コピー",
            _ => "Copy"
        };
    }

    public static string ApplyButton(string? language)
    {
        return language switch
        {
            SimplifiedChinese => "应用",
            TraditionalChinese => "套用",
            Japanese => "適用",
            _ => "Apply"
        };
    }}









