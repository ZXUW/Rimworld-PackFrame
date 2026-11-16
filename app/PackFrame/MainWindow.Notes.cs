using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class MainWindow
{
    private bool _noteBatchMode;
    private string? _noteBatchCategory;
    private bool _noteBatchClear;
    private ListBox? _noteBatchList;
    private readonly HashSet<string> _noteBatchIds = new(StringComparer.OrdinalIgnoreCase);

    private void OnModNamePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is TextBlock { DataContext: ModInfo mod }) mod.ShowOriginalName = true;
    }

    private void OnModNamePointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is TextBlock { DataContext: ModInfo mod }) mod.ShowOriginalName = false;
    }

    private void OnNoteFieldLostFocus(object? sender, RoutedEventArgs e) => FlushNoteEdits();

    private void FlushNoteEdits()
    {
        if (_packRoot is null || _detailMod is null) return;
        var note = ModNoteStore.Get(_packRoot, _detailMod.PackageId);
        var alias = _noteAliasBox.Text;
        var category = _noteCategoryBox.Text;
        if (string.Equals(note?.Alias ?? string.Empty, alias ?? string.Empty,
                StringComparison.Ordinal) &&
            string.Equals(note?.Category ?? string.Empty, category ?? string.Empty,
                StringComparison.Ordinal))
        {
            return;
        }

        ModNoteStore.Set(_packRoot, _detailMod.PackageId, alias, category);
        ApplyNotesToLists();
    }

    private async Task ShowModNoteAsync(ModInfo mod)
    {
        if (_packRoot is null) return;
        var note = ModNoteStore.Get(_packRoot, mod.PackageId);
        var dialog = new ModNoteWindow(mod.Name, note?.Alias, note?.Category, _config.Language);
        var result = await dialog.ShowDialog<(string? Alias, string? Category)?>(this);
        if (result is null) return;
        ModNoteStore.Set(_packRoot, mod.PackageId, result.Value.Alias, result.Value.Category);
        ApplyNotesToLists();
        ShowDetails(mod);
    }

    private void ApplyNotesToLists()
    {
        var notes = ModNoteStore.Load(_packRoot);
        foreach (var mod in _allMods)
        {
            ApplyNote(mod, notes);
        }

        foreach (var list in GetModLists())
        {
            foreach (var mod in list.Items.OfType<ModInfo>())
            {
                ApplyNote(mod, notes);
            }
        }
    }

    private List<ModInfo> ApplyModNotes(List<ModInfo> mods)
    {
        var notes = ModNoteStore.Load(_packRoot);
        foreach (var mod in mods)
        {
            ApplyNote(mod, notes);
        }

        return mods;
    }

    private static void ApplyNote(ModInfo mod, ModNoteData notes)
    {
        notes.Notes.TryGetValue(mod.PackageId, out var note);
        mod.Alias = note?.Alias;
        mod.Category = note?.Category;
    }

    private async void OnBatchCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (_noteBatchMode)
        {
            SetNoteBatchMode(false);
            return;
        }

        if (_packRoot is null) return;
        var dialog = new BatchCategoryWindow(
            ModNoteStore.Categories(_packRoot),
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
        _batchCategoryButton.Content = _noteBatchMode
            ? (_noteBatchClear
                ? UpdateText("Clear category", "清除分类", "清除分類", "分類をクリア")
                : _noteBatchCategory ?? UpdateText("Batch category", "批量分类", "批次分類", "分類を一括"))
            : UpdateText("Batch category", "批量分类", "批次分類", "分類を一括");
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
            ModNoteStore.SetCategory(
                _packRoot,
                _noteBatchIds.ToList(),
                _noteBatchClear ? null : _noteBatchCategory);
        }

        _noteBatchIds.Clear();
        _noteBatchList = null;
        ApplyNotesToLists();
    }
}
