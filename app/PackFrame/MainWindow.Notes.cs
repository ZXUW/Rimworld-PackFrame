using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PackFrame;

public partial class MainWindow
{
    private bool _noteBatchMode;
    private string? _noteBatchCategory;
    private bool _noteBatchClear;
    private ListBox? _noteBatchList;
    private readonly HashSet<string> _noteBatchIds = new(StringComparer.OrdinalIgnoreCase);

    private void OnNoteFieldLostFocus(object? sender, RoutedEventArgs e) => FlushNoteEdits();

    private void FlushNoteEdits()
    {
        if (_packRoot is null || _detailMod is null) return;
        var note = ModNoteStore.Get(_packRoot, _detailMod.PackageId);
        var currentCategory = CategoryTree.NormalizePath(note?.Category);
        var alias = _noteAliasBox.Text;
        var category = CategoryTree.NormalizePath(_noteCategoryBox.Text);
        if (string.Equals(note?.Alias ?? string.Empty, alias?.Trim() ?? string.Empty,
                StringComparison.Ordinal) &&
            string.Equals(currentCategory ?? string.Empty, category ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var categoryChanged = !string.Equals(currentCategory ?? string.Empty, category ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        ModNoteStore.Set(_packRoot, _detailMod.PackageId, alias, category);
        ApplyNotesToLists(categoryChanged);
    }

    private async Task ShowModNoteAsync(ModInfo mod)
    {
        if (_packRoot is null) return;
        var note = ModNoteStore.Get(_packRoot, mod.PackageId);
        var dialog = new ModNoteWindow(mod.Name, note?.Alias, note?.Category, _config.Language);
        var result = await dialog.ShowDialog<(string? Alias, string? Category)?>(this);
        if (result is null) return;
        ModNoteStore.Set(_packRoot, mod.PackageId, result.Value.Alias, result.Value.Category);
        ApplyNotesToLists(true);
        ShowDetails(mod);
    }

    private async Task ShowSetCategoryAsync(IReadOnlyList<ModInfo> mods)
    {
        if (_packRoot is null || mods.Count == 0) return;
        var categories = ModNoteStore.Categories(_packRoot);
        var dialog = new BatchCategoryWindow(categories, _allMods, _config.Language);
        var choice = await dialog.ShowDialog<BatchCategoryChoice?>(this);
        if (choice is null) return;
        if (!choice.Start && !choice.Clear) return;
        var packageIds = mods.Select(m => m.PackageId).ToList();
        var before = CaptureCategoryStates(packageIds);
        var category = choice.Clear ? null : choice.Category;
        ModNoteStore.SetCategory(_packRoot, packageIds, category);
        PushCategoryUndo(
            before,
            packageIds.ToDictionary(id => id, _ => category, StringComparer.OrdinalIgnoreCase));
        ApplyNotesToLists(true);
        if (_detailMod is not null)
        {
            _noteCategoryBox.Text = ModNoteStore.Get(_packRoot, _detailMod.PackageId)?.Category ?? string.Empty;
        }
    }

    private Dictionary<string, string?> CaptureCategoryStates(IEnumerable<string> packageIds)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (_packRoot is null) return result;
        foreach (var id in packageIds)
        {
            if (string.IsNullOrWhiteSpace(id) || result.ContainsKey(id)) continue;
            result[id] = ModNoteStore.Get(_packRoot, id)?.Category;
        }
        return result;
    }

    private void PushCategoryUndo(
        Dictionary<string, string?> before,
        Dictionary<string, string?> after)
    {
        if (before.Count == 0 || CategoryMapsEqual(before, after)) return;
        _redoHistory.Clear();
        _undoHistory.Add(new UndoEntry(
            new List<InstanceSnapshot>(),
            after.Keys.ToList(),
            0,
            0,
            CategoryBefore: new Dictionary<string, string?>(before, StringComparer.OrdinalIgnoreCase),
            CategoryAfter: new Dictionary<string, string?>(after, StringComparer.OrdinalIgnoreCase)));
        while (_undoHistory.Count > 10) _undoHistory.RemoveAt(0);
        UpdateUndoButton();
    }

    private static bool CategoryMapsEqual(
        IReadOnlyDictionary<string, string?> first,
        IReadOnlyDictionary<string, string?> second)
    {
        if (first.Count != second.Count) return false;
        foreach (var pair in first)
        {
            if (!second.TryGetValue(pair.Key, out var other)) return false;
            var left = CategoryTree.NormalizePath(pair.Value);
            var right = CategoryTree.NormalizePath(other);
            if (!string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyNotesToLists(bool rebuildCategoryTree = false)
    {
                var notes = ModNoteStore.Load(_packRoot);
        foreach (var mod in _allMods)
        {
            ApplyNote(mod, notes);
        }

        if (rebuildCategoryTree && _config.InactiveCategoryMode)
        {
            RefreshInactivePreservingScroll();
            foreach (var list in new[] { _activeList1, _activeList2 })
            {
                foreach (var mod in list.Items.OfType<ModInfo>())
                {
                    ApplyNote(mod, notes);
                }
            }
            return;
        }

        foreach (var list in GetModLists())
        {
            foreach (var mod in list.Items.OfType<ModInfo>())
            {
                ApplyNote(mod, notes);
            }
        }
    }

    private List<ModInfo> ApplyModNotes(List<ModInfo> mods, bool showCategoryBadge = true)
    {
        var notes = ModNoteStore.Load(_packRoot);
        var result = new List<ModInfo>(mods.Count);
        foreach (var mod in mods)
        {
            var copy = mod.ShowCategoryBadge == showCategoryBadge
                ? mod
                : mod with { ShowCategoryBadge = showCategoryBadge };
            ApplyNote(copy, notes);
            result.Add(copy);
        }

        return result;
    }

    private static void ApplyNote(ModInfo mod, ModNoteData notes)
    {
        notes.Notes.TryGetValue(mod.PackageId, out var note);
        mod.Alias = note?.Alias;
        mod.Category = note?.Category;
        mod.IgnoredPrerequisites = note?.IgnoreHardPrerequisites == true
            ? new HashSet<string>(mod.ModDependencies, StringComparer.OrdinalIgnoreCase)
            : note?.IgnoredPrerequisites is { Count: > 0 } ids
                ? new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase) : null;
    }

    private void UpdatePrerequisitesDetails(ModInfo? mod)
    {
        _prerequisitesItems.Children.Clear();
        _prerequisitesSection.IsVisible = mod is { ModDependencies.Count: > 0 };
        _prerequisitesHint.IsVisible = _packRoot is not null;
        _prerequisitesHint.Text = UpdateText("Click to ignore prerequisites", "点击可屏蔽前置", "點擊可屏蔽前置", "クリックして必須MODを無視");
        if (mod is null) return;
        foreach (var id in mod.ModDependencies)
        {
            var label = new TextBlock { Text = RequiredModName(mod, id, _modsById), TextWrapping = TextWrapping.Wrap };
            if (_packRoot is null)
            {
                _prerequisitesItems.Children.Add(label);
                continue;
            }
            var button = new Button
            {
                Content = label,
                Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
                Background = Brushes.Transparent,
                Padding = new Thickness(0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            button.Classes.Add("prerequisite-row");
            AutomationProperties.SetName(button, label.Text);
            void UpdateRow()
            {
                var ignored = mod.IsPrerequisiteIgnored(id);
                label.Foreground = ignored ? ThemeService.MutedTextBrush : ThemeService.PrimaryTextBrush;
                label.TextDecorations = ignored ? TextDecorations.Strikethrough : null;
                AutomationProperties.SetHelpText(button, ignored
                    ? UpdateText("Ignored. Click to restore.", "已屏蔽，点击恢复", "已屏蔽，點擊恢復", "無視中。クリックで復元")
                    : UpdateText("Click to ignore.", "点击屏蔽", "點擊屏蔽", "クリックで無視"));
            }
            UpdateRow();
            button.Click += (_, _) =>
            {
                if (TogglePrerequisite(mod, id)) UpdateRow();
            };
            _prerequisitesItems.Children.Add(button);
        }
    }

    private bool TogglePrerequisite(ModInfo mod, string id)
    {
        if (_packRoot is null || !ReferenceEquals(_detailMod, mod)) return false;
        try
        {
            ModNoteStore.ToggleIgnoredPrerequisite(_packRoot, mod, id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Save prerequisite preference", exception);
            ShowToast(UpdateText("Could not save prerequisite preference", "无法保存前置设置", "無法儲存前置設定", "必須MODの設定を保存できませんでした"));
            return false;
        }

        ApplyNotesToLists();
        ApplyNote(mod, ModNoteStore.Load(_packRoot));
        CloseProblemTooltip();
        RefreshInactivePreservingScroll();
        return true;
    }

    private async void OnBatchCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (_noteBatchMode)
        {
            SetNoteBatchMode(false);
            return;
        }

        if (_paintMode) SetPaintMode(false);

        if (_packRoot is null) return;
        var dialog = new BatchCategoryWindow(
            ModNoteStore.Categories(_packRoot),
            _allMods,
            _config.Language);
        var choice = await dialog.ShowDialog<BatchCategoryChoice?>(this);
        if (choice is null) return;
        if (!choice.Start && !choice.Clear) return;
        _noteBatchCategory = choice.Category;
        _noteBatchClear = choice.Clear;
        SetNoteBatchMode(true);
    }

    private void SetNoteBatchMode(bool active)
    {
        if (!active)
        {
            FinishNoteBatch();
        }

        _noteBatchMode = active;
        UpdateBatchCategoryButtonVisual();
    }

    private void UpdateBatchCategoryButtonVisual()
    {
        _batchCategoryButton.Content = _noteBatchMode
            ? (_noteBatchClear
                ? UpdateText("Batch clear (brush)", "批量清除分类刷子", "批次清除分類刷子", "分類クリア（ブラシ）")
                : _noteBatchCategory ?? UpdateText("Batch category brush", "批量分类刷子", "批次分類刷子", "分類ブラシ"))
            : UpdateText("Batch category brush", "批量分类刷子", "批次分類刷子", "分類ブラシ");
        _batchCategoryButton.Background = _noteBatchMode ? TextBrush : SecondaryBorderBrush;
        _batchCategoryButton.Foreground = _noteBatchMode
            ? ThemeService.BackgroundBrush
            : Avalonia.Media.Brushes.White;
        _batchCategoryButton.FontWeight = _noteBatchMode
            ? Avalonia.Media.FontWeight.SemiBold
            : Avalonia.Media.FontWeight.Normal;
    }

    private void BeginNoteBatch(ListBox list, ModInfo mod, PointerPressedEventArgs e)
    {
        if (_packRoot is null) return;
        _noteBatchList = list;
        _noteBatchIds.Clear();
        ApplyBatchCategory(mod);
        e.Pointer.Capture(list);
        list.Focus();
    }

    private void ApplyBatchCategory(ModInfo mod)
    {
        if (string.IsNullOrWhiteSpace(mod.PackageId) || !_noteBatchIds.Add(mod.PackageId)) return;
        mod.Category = _noteBatchClear ? null : _noteBatchCategory;
    }

    private void NoteBatchAtPointer(PointerEventArgs e)
    {
        if (_noteBatchList is null) return;
        var item = FindListItem(_noteBatchList, e.Source) ??
            FindListItemAtPosition(_noteBatchList, e.GetPosition(_noteBatchList));
        if (item?.DataContext is ModInfo mod)
        {
            ApplyBatchCategory(mod);
        }
    }

    private void FinishNoteBatch()
    {
        if (_noteBatchIds.Count == 0)
        {
            _noteBatchList = null;
            return;
        }

        if (_packRoot is not null)
        {
            var ids = _noteBatchIds.ToList();
            var before = CaptureCategoryStates(ids);
            var category = _noteBatchClear ? null : _noteBatchCategory;
            ModNoteStore.SetCategory(_packRoot, ids, category);
            PushCategoryUndo(
                before,
                ids.ToDictionary(id => id, _ => category, StringComparer.OrdinalIgnoreCase));
        }

        _noteBatchIds.Clear();
        _noteBatchList = null;
        ApplyNotesToLists(true);
    }
}
