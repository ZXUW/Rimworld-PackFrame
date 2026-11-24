using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PackFrame;

public partial class MainWindow
{
    private async void OnModCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (_packRoot is null) return;
        var window = new ModCategoryWindow(
            _config.Language,
            () => CategoryTemplateStore.LoadAll(_packRoot),
            CreateCategoryTemplateViewer,
            ApplyCategoryTemplateAsync,
            SaveCategoryTemplateAsync,
            ClearCurrentCategoriesAsync,
            ImportCategoryTemplateAsync,
            ExportCurrentCategoriesAsync,
            DeleteCategoryTemplateAsync);
        await window.ShowDialog(this);
    }

    // Browses a whole category table in the same map the batch brush uses.
    private Window? CreateCategoryTemplateViewer(CategoryTemplateEntry entry)
    {
        var document = entry.Document;
        // The community table is keyed by workshop id; show the installed mod's
        // name where we have one, and the raw key otherwise.
        var installed = new Dictionary<string, (string Name, string? Link)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in _allMods)
        {
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            var known = (mod.Name, (string?)(string.IsNullOrWhiteSpace(mod.Link) ? null : mod.Link));
            var workshopId = ModpackService.ParseWorkshopId(mod.Link)?.ToString();
            if (workshopId is not null) installed.TryAdd(workshopId, known);
            if (!string.IsNullOrWhiteSpace(mod.PackageId)) installed.TryAdd(mod.PackageId, known);
        }

        var titles = document.Titles;
        var entries = new List<(string Name, string? Category)>();
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in CategoryTemplateTable(document))
        {
            string name;
            string? link;
            if (installed.TryGetValue(pair.Key, out var known))
            {
                name = known.Name;
                link = known.Link;
            }
            else if (titles.TryGetValue(pair.Key, out var title) && !string.IsNullOrWhiteSpace(title))
            {
                name = title;
                link = pair.Key.All(char.IsDigit) ? SteamLink(pair.Key) : null;
            }
            else if (pair.Key.All(char.IsDigit))
            {
                name = UpdateText("Workshop ", "创意工坊 ", "創意工坊 ", "ワークショップ ") + pair.Key;
                link = SteamLink(pair.Key);
            }
            else
            {
                name = pair.Key;
                link = null;
            }

            entries.Add((name, pair.Value));
            if (link is not null) links.TryAdd(name, link);
        }

        if (entries.Count == 0) return null;
        var categories = entries
            .Select(item => item.Item2)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new BatchCategoryWindow(
            categories,
            entries,
            _config.Language,
            entry.Name,
            links);
    }

    private static string SteamLink(string workshopId) =>
        "https://steamcommunity.com/sharedfiles/filedetails/?id=" + workshopId;

    // The section the "N entries" count in the row comes from.
    private static Dictionary<string, string> CategoryTemplateTable(ModCategoryDocument document) =>
        document.Workshop.Count > 0
            ? document.Workshop
            : document.PackageId.Count > 0
                ? document.PackageId
                : document.Name;

    private async Task<string?> ApplyCategoryTemplateAsync(CategoryTemplateEntry entry, bool overwrite)
    {
        if (_packRoot is null) return null;
        var normalizedNames = ModCategoryDatabase.BuildNormalizedNames(entry.Document.Name);
        var notes = ModNoteStore.Load(_packRoot);
        var before = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in _allMods)
        {
            if (string.IsNullOrWhiteSpace(mod.PackageId)) continue;
            var current = notes.Notes.TryGetValue(mod.PackageId, out var note)
                ? CategoryTree.NormalizePath(note.Category)
                : null;
            if (!overwrite && !string.IsNullOrWhiteSpace(current)) continue;
            var category = ModCategoryDatabase.Resolve(mod, entry.Document, normalizedNames);
            category = CategoryTree.NormalizePath(category);
            if (string.IsNullOrWhiteSpace(category)) continue;
            if (string.Equals(current, category, StringComparison.OrdinalIgnoreCase)) continue;
            before[mod.PackageId] = current;
            changes[mod.PackageId] = category;
        }

        if (changes.Count == 0)
        {
            return UpdateText("No categories to apply.", "没有可套用的分类", "沒有可套用的分類", "適用できる分類がありません");
        }

        var templateName = entry.IsBuiltIn
            ? UpdateText("PackFrame repository", "PackFrame仓库", "PackFrame倉庫", "PackFrame リポジトリ")
            : entry.Name;
        var message = overwrite
            ? UpdateText(
                $"Overwrite categories with \"{templateName}\"? {changes.Count} mods will change.",
                $"将用「{templateName}」覆盖已有分类，共 {changes.Count} 个模组会改变。是否继续？",
                $"將用「{templateName}」覆蓋已有分類，共 {changes.Count} 個模組會改變。是否繼續？",
                $"「{templateName}」で既存の分類を上書きします。{changes.Count} 件の MOD が変わります。続行しますか？")
            : UpdateText(
                $"Supplement categories from \"{templateName}\"? {changes.Count} uncategorized mods will be filled.",
                $"将用「{templateName}」补充分类，共 {changes.Count} 个未分类模组会被填充。是否继续？",
                $"將用「{templateName}」補充分類，共 {changes.Count} 個未分類模組會被填充。是否繼續？",
                $"「{templateName}」で分類を補完します。未分類の {changes.Count} 件が設定されます。続行しますか？");
        var confirm = new ConfirmWindow(
            message,
            UpdateText("Apply", "套用", "套用", "適用"),
            Localization.CancelButton(_config.Language));
        if (!await confirm.ShowDialog<bool>(this)) return null;

        var applied = ModNoteStore.SetCategories(_packRoot, changes);
        if (applied > 0)
        {
            PushCategoryUndo(before, changes, changes.Keys.ToList());
            ApplyNotesToLists(true);
        }

        var done = UpdateText(
            $"Applied {applied} categories.",
            $"已套用 {applied} 条分类",
            $"已套用 {applied} 條分類",
            $"{applied} 件の分類を適用しました");
        var undone = await ShowCategoryUndoPromptAsync(done);
        return undone
            ? UpdateText("Undone.", "已撤回", "已撤回", "元に戻しました")
            : done;
    }

    private async Task<string?> ClearCurrentCategoriesAsync()
    {
        if (_packRoot is null) return null;
        var before = CurrentCategoriesForInstalledMods().ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        if (before.Count == 0)
        {
            return UpdateText("No categories to clear.", "当前没有分类可清空", "目前沒有分類可清空", "消去する分類がありません");
        }

        var confirm = new ConfirmWindow(
            UpdateText(
                $"Clear all {before.Count} categorized mods? Aliases and prerequisites will be kept.",
                $"清空当前 {before.Count} 个模组的分类？别名和前置屏蔽会保留。",
                $"清空目前 {before.Count} 個模組的分類？別名和前置遮蔽會保留。",
                $"現在の {before.Count} 件の分類をすべて消去しますか？別名と前提の設定は残ります。"),
            UpdateText("Clear", "清空", "清空", "消去"),
            Localization.CancelButton(_config.Language));
        if (!await confirm.ShowDialog<bool>(this)) return null;

        var cleared = ModNoteStore.SetCategories(
            _packRoot,
            before.Keys.ToDictionary(key => key, _ => (string?)null, StringComparer.OrdinalIgnoreCase));
        if (cleared > 0)
        {
            var after = before.Keys.ToDictionary(
                key => key,
                _ => (string?)null,
                StringComparer.OrdinalIgnoreCase);
            PushCategoryUndo(before, after, before.Keys.ToList());
            ApplyNotesToLists(true);
        }

        var done = UpdateText(
            $"Cleared {cleared} categories.",
            $"已清空 {cleared} 条分类",
            $"已清空 {cleared} 條分類",
            $"{cleared} 件の分類を消去しました");
        var undone = await ShowCategoryUndoPromptAsync(done);
        return undone
            ? UpdateText("Undone.", "已撤回", "已撤回", "元に戻しました")
            : done;
    }

    private async Task<string?> SaveCategoryTemplateAsync()
    {
        if (_packRoot is null) return null;
        var categories = CurrentCategoriesForInstalledMods();
        if (categories.Count == 0)
        {
            return UpdateText("No categories to save.", "当前没有可保存的分类", "目前沒有可儲存的分類", "保存する分類がありません");
        }

        var nameDialog = new RenameWindow(
            string.Empty,
            UpdateText("Save", "保存", "儲存", "保存"),
            Localization.CancelButton(_config.Language));
        nameDialog.Title = UpdateText("Save categories", "保存分类", "儲存分類", "分類を保存");
        nameDialog.FindControl<TextBox>("NameBox")!.PlaceholderText =
            UpdateText("Template name", "模板名字", "模板名字", "テンプレート名");
        var name = await nameDialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var path = CategoryTemplateStore.PathForName(_packRoot, name);
        if (File.Exists(path))
        {
            var overwrite = new ConfirmWindow(
                UpdateText(
                    $"A template named \"{name.Trim()}\" already exists. Overwrite it?",
                    $"已存在名为「{name.Trim()}」的模板，是否覆盖？",
                    $"已存在名為「{name.Trim()}」的模板，是否覆蓋？",
                    $"「{name.Trim()}」というテンプレートが既にあります。上書きしますか？"),
                UpdateText("Overwrite", "覆盖", "覆蓋", "上書き"),
                Localization.CancelButton(_config.Language));
            if (!await overwrite.ShowDialog<bool>(this)) return null;
        }

        var document = new ModCategoryDocument
        {
            PackageId = new Dictionary<string, string>(categories, StringComparer.OrdinalIgnoreCase)
        };
        CategoryTemplateStore.SaveDocument(path, document, name.Trim());
        return UpdateText(
            $"Saved {categories.Count} categories.",
            $"已保存 {categories.Count} 条分类",
            $"已儲存 {categories.Count} 條分類",
            $"{categories.Count} 件の分類を保存しました");
    }

    private async Task<string?> ImportCategoryTemplateAsync()
    {
        if (_packRoot is null) return null;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PackFrame categories") { Patterns = new[] { "*.json" } }
            }
        });
        if (files.Count == 0) return null;

        var file = files[0];
        string text;
        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            text = await File.ReadAllTextAsync(localPath);
        }
        else
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            text = await reader.ReadToEndAsync();
        }

        var document = ModCategoryDatabase.Parse(text);
        var count = document is null ? 0 : ModCategoryDatabase.CountEntries(document);
        if (document is null || count == 0)
        {
            return UpdateText("Invalid category file.", "分类文件格式不正确", "分類檔案格式不正確", "分類ファイルの形式が正しくありません");
        }

        var name = string.IsNullOrWhiteSpace(document.Title)
            ? Path.GetFileNameWithoutExtension(localPath ?? file.Name)
            : document.Title!;
        var path = CategoryTemplateStore.PathForName(_packRoot, name);
        if (File.Exists(path))
        {
            var overwrite = new ConfirmWindow(
                UpdateText(
                    $"A template named \"{name}\" already exists. Overwrite it?",
                    $"已存在名为「{name}」的模板，是否覆盖？",
                    $"已存在名為「{name}」的模板，是否覆蓋？",
                    $"「{name}」というテンプレートが既にあります。上書きしますか？"),
                UpdateText("Overwrite", "覆盖", "覆蓋", "上書き"),
                Localization.CancelButton(_config.Language));
            if (!await overwrite.ShowDialog<bool>(this)) return null;
        }

        CategoryTemplateStore.SaveDocument(path, document, name);
        return UpdateText(
            $"Imported {count} categories.",
            $"已导入 {count} 条分类",
            $"已匯入 {count} 條分類",
            $"{count} 件の分類をインポートしました");
    }

    private async Task<string?> ExportCurrentCategoriesAsync()
    {
        if (_packRoot is null) return null;
        var categories = CurrentCategoriesForInstalledMods();
        if (categories.Count == 0)
        {
            return UpdateText("No categories to export.", "当前没有可导出的分类", "目前沒有可匯出的分類", "エクスポートする分類がありません");
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"modCategories-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PackFrame categories") { Patterns = new[] { "*.json" } }
            },
            ShowOverwritePrompt = true
        });
        if (file is null) return null;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return UpdateText("Could not write the selected file.", "无法写入所选文件", "無法寫入所選檔案", "選択したファイルに書き込めません");
        }

        var document = new ModCategoryDocument
        {
            PackageId = new Dictionary<string, string>(categories, StringComparer.OrdinalIgnoreCase)
        };
        CategoryTemplateStore.SaveDocument(path, document, Path.GetFileNameWithoutExtension(path));
        return UpdateText(
            $"Exported {categories.Count} categories.",
            $"已导出 {categories.Count} 条分类",
            $"已匯出 {categories.Count} 條分類",
            $"{categories.Count} 件の分類をエクスポートしました");
    }

    private async Task<string?> DeleteCategoryTemplateAsync(CategoryTemplateEntry entry)
    {
        if (entry.IsBuiltIn || string.IsNullOrWhiteSpace(entry.FilePath)) return null;
        var confirm = new ConfirmWindow(
            UpdateText(
                $"Delete the template \"{entry.Name}\"?",
                $"删除模板「{entry.Name}」？",
                $"刪除模板「{entry.Name}」？",
                $"テンプレート「{entry.Name}」を削除しますか？"),
            UpdateText("Delete", "删除", "刪除", "削除"),
            Localization.CancelButton(_config.Language));
        if (!await confirm.ShowDialog<bool>(this)) return null;

        CategoryTemplateStore.Delete(entry.FilePath!);
        return UpdateText("Deleted.", "已删除", "已刪除", "削除しました");
    }

    private async Task<bool> ShowCategoryUndoPromptAsync(string message)
    {
        var dialog = new ConfirmWindow(
            message + "\n\n" + UpdateText(
                "You can undo this action.",
                "可以撤回这次操作。",
                "可以撤回這次操作。",
                "この操作を元に戻せます。"),
            UpdateText("Undo", "撤回", "撤回", "元に戻す"),
            UpdateText("Close", "关闭", "關閉", "閉じる"));
        if (!await dialog.ShowDialog<bool>(this)) return false;
        UndoLastChange();
        return true;
    }

    private Dictionary<string, string> CurrentCategoriesForInstalledMods()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_packRoot is null) return result;
        var installed = _allMods
            .Select(mod => mod.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notes = ModNoteStore.Load(_packRoot);
        foreach (var (id, note) in notes.Notes)
        {
            if (!installed.Contains(id)) continue;
            var category = CategoryTree.NormalizePath(note.Category);
            if (string.IsNullOrWhiteSpace(category)) continue;
            result[id] = category;
        }
        return result;
    }

    private void PushCategoryUndo(
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after,
        IReadOnlyList<string> packageIds)
    {
        if (packageIds.Count == 0) return;
        _redoHistory.Clear();
        _undoHistory.Add(new UndoEntry(
            new List<InstanceSnapshot>(),
            packageIds.ToList(),
            0,
            0,
            CategoryBefore: new Dictionary<string, string?>(before, StringComparer.OrdinalIgnoreCase),
            CategoryAfter: new Dictionary<string, string?>(after, StringComparer.OrdinalIgnoreCase)));
        while (_undoHistory.Count > 10)
        {
            _undoHistory.RemoveAt(0);
        }

        UpdateUndoButton();
    }
}
