using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace PackFrame;

public partial class MainWindow
{
    private async void OnPaintModeClick(object? sender, RoutedEventArgs e)
    {
        if (_paintMode) { SetPaintMode(false); return; }
        var choice = await ChoosePaintColorAsync();
        if (choice is null) return;
        _paintEraser = choice.Clear;
        _paintColorHex = choice.Clear ? null : choice.Hex;
        SetPaintMode(true);
    }

    private async Task<ModColorChoice?> ChoosePaintColorAsync()
    {
        if (_packRoot is null) return null;
        var data = ModColorStore.Load(_packRoot);
        var picker = new ColorPaletteWindow(_paintColorHex, data.ColorLabels, _config.Language);
        var choice = await picker.ShowDialog<ModColorChoice?>(this);
        if (choice is null) return null;
        ModColorStore.SetLabels(_packRoot, choice.Labels);
        return choice.Cancelled ? null : choice;
    }

    private void SetPaintMode(bool active)
    {
        if (!active) FinishPaint();
        _paintMode = active;
        UpdatePaintButtonVisual();
    }

    private void UpdatePaintButtonVisual()
    {
        _paintModeButton.Content = _paintMode && _paintEraser
            ? Localization.EraserTool(_config.Language) : Localization.PaintTool(_config.Language);
        _paintModeButton.Background = _paintMode ? TextBrush : SecondaryBorderBrush;
        _paintModeButton.Foreground = _paintMode ? ThemeService.BackgroundBrush : Brushes.White;
        _paintModeButton.FontWeight = _paintMode ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private void BeginPaint(ListBox list, ModInfo mod, PointerPressedEventArgs e)
    {
        if (_packRoot is null || (!_paintEraser && string.IsNullOrWhiteSpace(_paintColorHex))) return;
        _painting = true;
        _paintList = list;
        _paintColumn = GetListColumn(list);
        _paintBeforeColors.Clear();
        _paintedIds.Clear();
        _paintData = ModColorStore.Load(_packRoot);
        PaintMod(mod);
        e.Pointer.Capture(list);
        list.Focus();
    }

    private void PaintMod(ModInfo mod)
    {
        if (_paintData is null || (!_paintEraser && string.IsNullOrWhiteSpace(_paintColorHex)) ||
            string.IsNullOrWhiteSpace(mod.PackageId) || !_paintedIds.Add(mod.PackageId)) return;
        if (!_paintBeforeColors.ContainsKey(mod.PackageId))
            _paintBeforeColors[mod.PackageId] = _paintData.ModColors.TryGetValue(mod.PackageId, out var current) ? current : null;
        ApplyColorToInfo(mod, _paintEraser ? null : _paintColorHex, _paintData);
    }

    private void PaintAtPointer(PointerEventArgs e)
    {
        if (_paintList is null) return;
        var item = FindListItem(_paintList, e.Source) ?? FindListItemAtPosition(_paintList, e.GetPosition(_paintList));
        if (item?.DataContext is ModInfo mod) PaintMod(mod);
    }

    private void FinishPaint()
    {
        if (!_painting) return;
        var ids = _paintedIds.ToList();
        var before = new Dictionary<string, string?>(_paintBeforeColors, StringComparer.OrdinalIgnoreCase);
        var color = _paintColorHex;
        var eraser = _paintEraser;
        var column = _paintColumn;
        var data = _paintData;
        _painting = false;
        _paintList = null;
        _paintData = null;
        _paintedIds.Clear();
        _paintBeforeColors.Clear();
        if (_packRoot is null || (!eraser && string.IsNullOrWhiteSpace(color)) || ids.Count == 0 || data is null) return;
        var resultColor = eraser ? null : color;
        var after = ids.ToDictionary(id => id, _ => resultColor, StringComparer.OrdinalIgnoreCase);
        ModColorStore.SetModColors(_packRoot, after);
        PushColorUndo(before, after, ids, column);
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var list in GetModLists())
            foreach (var mod in list.Items.OfType<ModInfo>())
                if (idSet.Contains(mod.PackageId)) ApplyColorToInfo(mod, resultColor, data);
    }
}
