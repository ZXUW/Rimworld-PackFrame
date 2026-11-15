using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PackFrame;

public partial class MainWindow
{
    private readonly Border _sameListBadge1;
    private readonly Border _sameListBadge2;
    private readonly TextBlock _launchTargetText;
    private readonly TextBlock _dragGhostAction;
    private readonly Border _dropTargetOutline;
    private Grid _managerColumns = null!;
    private Button _splitViewButton = null!;
    private TextBlock _splitViewLabel = null!;
    private Button _closeSecondaryPaneButton = null!;

    private void InitializePaneNavigation()
    {
        _managerColumns = Find<Grid>("ManagerColumns");
        _splitViewButton = Find<Button>("SplitViewButton");
        _splitViewLabel = Find<TextBlock>("SplitViewLabel");
        _closeSecondaryPaneButton = Find<Button>("CloseSecondaryPaneButton");
        ApplyPaneLayout();
    }

    private void ApplyPaneLayout()
    {
        var show = _config.ShowSecondaryPane;
        _activeColumn2.IsVisible = show;
        _splitViewButton.IsVisible = !show;
        _managerColumns.ColumnDefinitions[3].Width = new GridLength(show ? 8 : 0);
        _managerColumns.ColumnDefinitions[4].Width = show
            ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        UpdateSplitViewButton();
    }

    private void UpdateSplitViewButton()
    {
        var open = Localization.OpenOnRight(_config.Language);
        _splitViewLabel.Text = open;
        _splitViewButton.IsEnabled = GetSelectedInstance(0) is not null;
        ToolTip.SetTip(_splitViewButton, Localization.OpenOnRightHint(_config.Language));
        AutomationProperties.SetName(_splitViewButton, open);
        var close = Localization.CloseRightPane(_config.Language);
        ToolTip.SetTip(_closeSecondaryPaneButton, close);
        AutomationProperties.SetName(_closeSecondaryPaneButton, close);
    }

    private void OnSplitViewClick(object? sender, RoutedEventArgs e)
    {
        if (_customDragActive || _painting) return;
        var source = GetSelectedInstance(0);
        var sourceIndex = _instanceCombo1.SelectedIndex;
        if (source is null || sourceIndex < 0) return;

        var changedInstance = !IsSameInstance(source, GetSelectedInstance(1));
        _config.ShowSecondaryPane = true;
        if (changedInstance)
        {
            // Opening this list is distinct from restoring the previous layout:
            // the requested list replaces the right pane's former instance.
            _updatingInstances = true;
            try { _instanceCombo2.SelectedIndex = sourceIndex; }
            finally { _updatingInstances = false; }
            SetLastInstanceIndex(1, sourceIndex);
            _activeSearch2.Query = string.Empty;
            _activeSearchBox2.Text = string.Empty;
            SaveProfileSelection();
        }

        ApplyPaneLayout();
        if (_allMods.Count > 0)
        {
            if (changedInstance)
            {
                RefreshManager(false);
                InvalidatePaneScrollRestore(_activeList2);
                QueuePaneScrollRestore(_activeList2, default);
            }
            else
            {
                // Apply a search that was still debounced when this pane closed.
                RefreshSearch(_activeList2);
            }
        }
        UpdatePaneHeaders();
        SetPaneInteractionContext(_activeList2);
        Dispatcher.UIThread.Post(() =>
        {
            if (_config.ShowSecondaryPane && _activeSearchBox2.IsEffectivelyVisible)
                _activeSearchBox2.Focus();
        }, DispatcherPriority.Loaded);
        _config.Save();
    }

    private void OnCloseSecondaryPaneClick(object? sender, RoutedEventArgs e)
    {
        if (_customDragActive || _painting || !_config.ShowSecondaryPane) return;
        var rightContext = _activeList2.Classes.Contains("pane-active") || _activeColumn2.IsKeyboardFocusWithin;
        _config.ShowSecondaryPane = false;
        ApplyPaneLayout();
        UpdatePaneHeaders();
        CloseProblemTooltip();
        if (_searchTimers.TryGetValue(_activeList2, out var timer)) timer.Stop();
        if (_lastEnableColumn == 2)
        {
            _lastEnableColumn = 1;
            UpdateEnableTargetBorders();
            if (_allMods.Count > 0) RefreshInactiveForTarget();
        }
        if (rightContext)
        {
            SetPaneInteractionContext(_activeList1);
            _activeList1.Focus();
        }
        _config.Save();
    }

    private static bool IsSameInstance(ManagedInstance? first, ManagedInstance? second) =>
        first is not null && second is not null &&
        string.Equals(first.DirectoryPath, second.DirectoryPath, StringComparison.OrdinalIgnoreCase);

    private void UpdatePaneHeaders()
    {
        var first = GetSelectedInstance(0);
        var second = GetSelectedInstance(1);
        var sameList = _config.ShowSecondaryPane && IsSameInstance(first, second);
        foreach (var badge in new[] { _sameListBadge1, _sameListBadge2 })
        {
            badge.IsVisible = sameList;
            ((TextBlock)badge.Child!).Text = Localization.SameList(_config.Language);
            ToolTip.SetTip(badge, Localization.SameListHint(_config.Language));
        }
        ToolTip.SetTip(_instanceCombo1, first?.LoadError);
        ToolTip.SetTip(_instanceCombo2, second?.LoadError);
        UpdateSplitViewButton();
    }

    private void UpdateLaunchTarget(ManagedInstance? instance)
    {
        var text = string.IsNullOrWhiteSpace(instance?.DisplayName)
            ? Localization.LaunchGame(_config.Language)
            : Localization.LaunchInstance(_config.Language, instance.DisplayName);
        _launchTargetText.Text = text;
        ToolTip.SetTip(_launchButton, text);
        AutomationProperties.SetName(_launchButton, text);
    }

    private bool UpdateModDragFeedback(ListBox? target, int targetIndex)
    {
        ClearModDragFeedback();
        if (target is null || _dragPayload is null)
        {
            return false;
        }

        var source = GetInstanceForColumn(_dragPayload.SourceColumn);
        var column = GetListColumn(target);
        var destination = GetInstanceForColumn(column);
        if (source?.IsDamaged == true || destination?.IsDamaged == true ||
            (column == 0 ? source is null : destination is null))
        {
            return false;
        }

        var top = target.TranslatePoint(default, _dragOverlay);
        if (top is null)
        {
            return false;
        }

        if (column == 0)
        {
            _dragGhostAction.Text = Localization.DragDisableFrom(_config.Language, source!.DisplayName);
        }
        else
        {
            var ids = _dragPayload.Mods.Select(mod => mod.PackageId).ToList();
            var unavailable = IsSameInstance(source, destination)
                ? new List<ModInfo>()
                : UnavailableForInstance(_dragPayload, destination!);
            if (unavailable.Count > 0)
            {
                _dragGhostAction.Text = BlockedText(unavailable);
                _dragGhostAction.IsVisible = true;
                _dropTargetOutline.Width = target.Bounds.Width;
                _dropTargetOutline.Height = target.Bounds.Height;
                Canvas.SetLeft(_dropTargetOutline, top.Value.X);
                Canvas.SetTop(_dropTargetOutline, top.Value.Y);
                _dropTargetOutline.IsVisible = true;
                return true;
            }

            var preview = ModListOperations.PreviewInsertion(destination!.ActivePackageIds, ids, targetIndex);
            _dragGhostAction.Text = !preview.HasChanges
                ? Localization.DragAlreadyHere(_config.Language)
                : IsSameInstance(source, destination)
                    ? preview.BeforeId is { } before
                        ? Localization.DragMoveBefore(_config.Language, DropAnchorName(target, before))
                        : preview.AfterId is { } after
                            ? Localization.DragMoveAfter(_config.Language, DropAnchorName(target, after))
                            : Localization.DragMoveHere(_config.Language)
                    : string.IsNullOrWhiteSpace(destination.DisplayName)
                        ? Localization.DragAddHere(_config.Language)
                        : preview.AddedCount == 0
                            ? Localization.DragReorderIn(_config.Language, destination.DisplayName)
                            : preview.ExistingCount > 0
                                ? Localization.DragAddAndReorderIn(_config.Language, destination.DisplayName)
                                : Localization.DragAddTo(_config.Language, destination.DisplayName);
        }
        _dragGhostAction.IsVisible = true;
        _dropTargetOutline.Width = target.Bounds.Width;
        _dropTargetOutline.Height = target.Bounds.Height;
        Canvas.SetLeft(_dropTargetOutline, top.Value.X);
        Canvas.SetTop(_dropTargetOutline, top.Value.Y);
        _dropTargetOutline.IsVisible = true;
        return true;
    }

    private string DropAnchorName(ListBox target, string id) =>
        target.Items.OfType<ModInfo>().FirstOrDefault(mod =>
            string.Equals(mod.PackageId, id, StringComparison.OrdinalIgnoreCase))?.Name
        ?? (_modsByPackage.TryGetValue(id, out var candidates) ? candidates.FirstOrDefault()?.Name : null)
        ?? id;

    private void ClearModDragFeedback()
    {
        _dropTargetOutline.IsVisible = false;
        _dragGhostAction.IsVisible = false;
    }

    private void EndModDrag()
    {
        StopAutoScroll();
        HideDragGhost();
        _insertionLine.IsVisible = false;
        _insertionLine.Opacity = 1;
        _customDragActive = false;
        _dragPayload = null;
        _dragSourceList = null;
    }
}
