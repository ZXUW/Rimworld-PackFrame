using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PackFrame;

// Captures selection by package identity so filtering and rebuilt row models do
// not turn a multi-selection into an arbitrary first result.
internal sealed record PaneSelectionSnapshot(string? Scope, string[] SelectedIds, string? AnchorId)
{
    internal static PaneSelectionSnapshot Capture(ListBox list, string? scope, string? anchorId) =>
        new(scope, list.SelectedItems!.OfType<ModInfo>().Select(mod => mod.PackageId).ToArray(), anchorId);

    // Details can belong to the last clicked row of a multi-selection, which is
    // not necessarily SelectedItem. Always return a current row, never a stale model.
    internal static ModInfo? ResolveDetails(ListBox? list, string? packageId)
    {
        var selected = list?.SelectedItems?.OfType<ModInfo>().ToList();
        return selected?.FirstOrDefault(mod =>
            string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            ?? selected?.LastOrDefault();
    }

    internal int Restore(ListBox list, IReadOnlyList<ModInfo> rows, string? scope, bool selectFirst)
    {
        var sameScope = string.Equals(Scope, scope, StringComparison.OrdinalIgnoreCase);
        var selected = sameScope ? new HashSet<string>(SelectedIds, StringComparer.OrdinalIgnoreCase) : new();
        var autoScroll = list.AutoScrollToSelectedItem;
        list.AutoScrollToSelectedItem = false;
        try
        {
            list.ItemsSource = rows;
            list.SelectedItems!.Clear();
            for (var index = 0; index < rows.Count; index++)
                if (selected.Contains(rows[index].PackageId)) list.SelectedItems.Add(rows[index]);
            if (selectFirst && list.SelectedItems.Count == 0 && (!sameScope || SelectedIds.Length == 0) && rows.Count > 0)
                list.SelectedItems.Add(rows[0]);
        }
        finally { list.AutoScrollToSelectedItem = autoScroll; }

        var anchor = sameScope && AnchorId is not null
            ? rows.ToList().FindIndex(mod => string.Equals(mod.PackageId, AnchorId, StringComparison.OrdinalIgnoreCase))
            : -1;
        return anchor >= 0 ? anchor : list.SelectedItem is ModInfo first ? rows.ToList().IndexOf(first) : -1;
    }
}

internal static class PaneSelectionRange
{
    internal static int Move(ListBox list, int direction, bool extend, int anchorIndex, int endpointIndex)
    {
        if (list.ItemCount == 0) return -1;
        var next = endpointIndex < 0
            ? direction > 0 ? 0 : list.ItemCount - 1
            : Math.Clamp(endpointIndex + direction, 0, list.ItemCount - 1);
        Select(list, extend && anchorIndex >= 0 ? anchorIndex : next, next);
        return next;
    }

    internal static void Select(ListBox list, int anchorIndex, int endpointIndex, bool additive = false)
    {
        var selection = list.Selection;
        var autoScroll = list.AutoScrollToSelectedItem;
        list.AutoScrollToSelectedItem = false;
        selection.BeginBatchUpdate();
        try
        {
            // Keep the batch within the selection model. Clearing SelectedItems
            // here queues collection synchronization that can discard the range.
            if (!additive) selection.Clear();
            selection.SelectRange(Math.Min(anchorIndex, endpointIndex), Math.Max(anchorIndex, endpointIndex));
        }
        finally
        {
            selection.EndBatchUpdate();
            list.AutoScrollToSelectedItem = autoScroll;
        }
    }
}

public partial class MainWindow
{
    private readonly Dictionary<ListBox, string?> _paneScopes = new();
    private readonly Dictionary<ListBox, string?> _paneAnchors = new();
    private readonly Dictionary<ListBox, string?> _paneRangeEnds = new();
    private readonly Dictionary<ListBox, int> _paneRestoreVersions = new();
    private ListBox? _activeSelectionList;

    private void InitializePaneSelection()
    {
        foreach (var list in GetModLists())
            list.GotFocus += (_, _) => SetPaneInteractionContext(list);
        foreach (var control in new Control[] { _inactiveSearchBox, _inactiveSearchField,
                     _inactiveSourceButton, _inactiveViewModeButton, _inactiveSortButton })
            control.GotFocus += (_, _) => SetPaneInteractionContext(_inactiveList);
        foreach (var control in new Control[] { _activeSearchBox1, _activeSearchField1,
                     _activeSourceButton1, _activeViewModeButton1, _instanceCombo1,
                     _instanceActionButton1, _modpackButton1 })
            control.GotFocus += (_, _) => SetPaneInteractionContext(_activeList1);
        foreach (var control in new Control[] { _activeSearchBox2, _activeSearchField2,
                     _activeSourceButton2, _activeViewModeButton2, _instanceCombo2,
                     _instanceActionButton2, _modpackButton2 })
            control.GotFocus += (_, _) => SetPaneInteractionContext(_activeList2);
    }

    private void SetPaneInteractionContext(ListBox list)
    {
        if (!list.IsEffectivelyVisible) return;
        var contextChanged = !ReferenceEquals(_activeSelectionList, list);
        _activeSelectionList = list;
        _selectionAnchorList = list;
        _selectionAnchorIndex = _paneAnchors.TryGetValue(list, out var id) && id is not null
            ? list.Items.OfType<ModInfo>().ToList().FindIndex(mod =>
                string.Equals(mod.PackageId, id, StringComparison.OrdinalIgnoreCase))
            : list.SelectedIndex;
        foreach (var pane in GetModLists())
        {
            if (ReferenceEquals(pane, list)) pane.Classes.Add("pane-active");
            else pane.Classes.Remove("pane-active");
        }
        var column = GetListColumn(list);
        if (column > 0 && _lastEnableColumn != column)
        {
            _lastEnableColumn = column;
            UpdateEnableTargetBorders();
            if (_allMods.Count > 0) RefreshInactiveForTarget();
        }
        if (contextChanged && !_refreshing)
            ShowDetails(list.SelectedItems!.OfType<ModInfo>().LastOrDefault());
    }

    private ListBox? GetCommandList() =>
        GetModLists().FirstOrDefault(list => list.IsEffectivelyVisible && list.IsKeyboardFocusWithin) ??
        (_activeSelectionList?.IsEffectivelyVisible == true ? _activeSelectionList : null);

    private void SetPaneAnchor(ListBox list, int index)
    {
        _selectionAnchorList = list;
        _selectionAnchorIndex = index;
        _paneAnchors[list] = index >= 0 && index < list.ItemCount && list.Items[index] is ModInfo mod
            ? mod.PackageId : null;
        _paneRangeEnds[list] = _paneAnchors[list];
    }

    private void SetPaneRangeEnd(ListBox list, int index) =>
        _paneRangeEnds[list] = index >= 0 && index < list.ItemCount && list.Items[index] is ModInfo mod
            ? mod.PackageId : null;

    private int GetPaneRangeEnd(ListBox list) =>
        _paneRangeEnds.TryGetValue(list, out var id) && id is not null
            ? list.Items.OfType<ModInfo>().ToList().FindIndex(mod =>
                string.Equals(mod.PackageId, id, StringComparison.OrdinalIgnoreCase))
            : list.SelectedIndex;

    private static string? PaneInstanceScope(ManagedInstance? instance) => instance is null
        ? null : instance.DirectoryPath + "\n" + instance.FolderName;

    private void InvalidatePaneScrollRestore(ListBox list) =>
        _paneRestoreVersions[list] = _paneRestoreVersions.GetValueOrDefault(list) + 1;

    private void ReplacePaneItems(ListBox list, List<ModInfo> rows, string? scope, bool selectFirst = false)
    {
        var oldScope = _paneScopes.GetValueOrDefault(list);
        var snapshot = PaneSelectionSnapshot.Capture(list, oldScope, _paneAnchors.GetValueOrDefault(list));
        var offset = GetListScrollOffset(list);
        var anchor = snapshot.Restore(list, rows, scope, selectFirst);
        _paneScopes[list] = scope;
        _paneAnchors[list] = anchor >= 0 ? rows[anchor].PackageId : null;
        if (!string.Equals(oldScope, scope, StringComparison.OrdinalIgnoreCase) || GetPaneRangeEnd(list) < 0)
            _paneRangeEnds[list] = _paneAnchors[list];
        if (ReferenceEquals(_selectionAnchorList, list))
            _selectionAnchorIndex = anchor;
        InvalidatePaneScrollRestore(list);
        if (!string.Equals(oldScope, scope, StringComparison.OrdinalIgnoreCase)) return;
        QueuePaneScrollRestore(list, offset);
    }

    private void QueuePaneScrollRestore(ListBox list, Vector offset)
    {
        var version = _paneRestoreVersions.GetValueOrDefault(list);
        var loadVersion = _loadVersion;
        var scope = _paneScopes.GetValueOrDefault(list);
        void RestoreOffset()
        {
            if (list.IsEffectivelyVisible && _loadVersion == loadVersion &&
                string.Equals(_paneScopes.GetValueOrDefault(list), scope, StringComparison.OrdinalIgnoreCase) &&
                _paneRestoreVersions.GetValueOrDefault(list) == version)
                SetListScrollOffset(list, offset);
        }
        Dispatcher.UIThread.Post(RestoreOffset, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(RestoreOffset, DispatcherPriority.Background);
    }
}
