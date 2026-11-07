using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace PackFrame;

public partial class MainWindow
{
    private WorkshopDownloadTracking _workshopDownloads = new();
    private readonly CancellationTokenSource _workshopMonitorCancellation = new();
    private readonly Dictionary<MissingModsWindow, ManagedInstance> _modStatusWindows = new();
    private DispatcherTimer? _workshopMonitorTimer;
    private bool _pollingWorkshop;
    private string? _workshopMonitorGamePath;

    private void InitializeWorkshopMonitoring()
    {
        _workshopMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _workshopMonitorTimer.Tick += async (_, _) => await PollWorkshopDownloadsAsync();
        Opened += (_, _) => { _workshopMonitorTimer.Start(); _ = PollWorkshopDownloadsAsync(); };
        Closed += (_, _) => { _workshopMonitorTimer.Stop(); _workshopMonitorCancellation.Cancel(); };
    }

    private void WakeWorkshopMonitoring()
    {
        if (_windowClosed || _workshopMonitorTimer is null) return;
        _workshopMonitorTimer.Interval = TimeSpan.FromSeconds(4);
        _ = PollWorkshopDownloadsAsync();
    }

    private List<WorkshopModReference> WorkshopReferences(ManagedInstance instance)
    {
        var references = WorkshopModReferences.ForInstance(instance);
        var active = new HashSet<string>(instance.ActivePackageIds, StringComparer.OrdinalIgnoreCase);
        foreach (var mod in _allMods.Where(mod => active.Contains(mod.PackageId)))
        {
            var id = GetWorkshopId(mod);
            if (id is not null) references.Add(new WorkshopModReference(id.Value, mod.PackageId, mod.Name));
        }
        return WorkshopModReferences.Normalize(references);
    }

    private WorkshopModAvailability WorkshopAvailability(ulong id, bool foundOnDisk)
    {
        if (!string.Equals(_workshopMonitorGamePath, _gameInstallation?.GamePath, StringComparison.OrdinalIgnoreCase))
            return WorkshopModAvailability.CheckingSteam;
        return _workshopDownloads.Availability(id, foundOnDisk);
    }

    private List<MissingModRow> BuildAvailabilityRows(ManagedInstance instance)
    {
        var result = new List<MissingModRow>();
        var references = instance.IsSteamCmd ? new List<WorkshopModReference>() : WorkshopReferences(instance);
        var byPackage = references.Where(reference => !string.IsNullOrWhiteSpace(reference.PackageId))
            .GroupBy(reference => reference.PackageId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var mapping = ModFullNameList.Load(instance);
        var history = ModFullNameList.LoadHistory(instance).ToDictionary(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase);
        var installedWorkshop = _allMods.Select(GetWorkshopId).Where(id => id is not null)
            .Select(id => id!.Value).ToHashSet();
        var unresolvedTargets = references.Where(reference => string.IsNullOrWhiteSpace(reference.PackageId) &&
            !installedWorkshop.Contains(reference.WorkshopId)).ToList();
        var waitingForUnmappedDownloads = unresolvedTargets.Any(reference =>
            WorkshopAvailability(reference.WorkshopId, false) != WorkshopModAvailability.Missing);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var activeId in instance.ActivePackageIds)
        {
            var id = activeId.Trim();
            if (id.Length == 0 || !seen.Add(id)) continue;
            var found = _modsById.ContainsKey(id);
            mapping.TryGetValue(id, out var entry);
            history.TryGetValue(id, out var old);
            var knownOtherSource = WorkshopModReferences.HasNonWorkshopSource(FirstText(entry?.Link, old?.Link, "—"));
            byPackage.TryGetValue(id, out var candidates);
            var reference = candidates?.FirstOrDefault(candidate => _workshopDownloads.Item(candidate.WorkshopId)?.IsDownloading == true)
                ?? candidates?.FirstOrDefault();
            var availability = reference is null
                ? !found && waitingForUnmappedDownloads && !knownOtherSource ? WorkshopModAvailability.Unknown : WorkshopModAvailability.Missing
                : WorkshopAvailability(reference.WorkshopId, found);
            if (found && availability is not (WorkshopModAvailability.Downloading or WorkshopModAvailability.Queued)) continue;
            var item = reference is null ? null : _workshopDownloads.Item(reference.WorkshopId);
            var status = Localization.WorkshopStatus(_config.Language, availability,
                item?.BytesDownloaded ?? 0, item?.BytesTotal ?? 0);
            result.Add(new MissingModRow
            {
                PackageId = id, Label = FirstText(entry?.Label, old?.Label, reference?.Name, id),
                Author = FirstText(entry?.Author, old?.Author, "—"),
                Link = FirstText(entry?.Link, old?.Link, reference is null ? null : WorkshopLink(reference.WorkshopId), "—"),
                WorkshopId = reference?.WorkshopId, Availability = availability, Status = status
            });
        }
        foreach (var reference in unresolvedTargets)
        {
            var availability = WorkshopAvailability(reference.WorkshopId, false);
            var item = _workshopDownloads.Item(reference.WorkshopId);
            result.Add(new MissingModRow
            {
                PackageId = string.Empty, Label = FirstText(reference.Name, "Workshop " + reference.WorkshopId),
                Author = "—", Link = WorkshopLink(reference.WorkshopId), WorkshopId = reference.WorkshopId,
                Availability = availability,
                Status = Localization.WorkshopStatus(_config.Language, availability, item?.BytesDownloaded ?? 0, item?.BytesTotal ?? 0)
            });
        }
        return result;
    }

    private static string FirstText(params string?[] texts) => texts.First(text => !string.IsNullOrWhiteSpace(text))!;
    private static string WorkshopLink(ulong id) => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;

    private void RefreshWorkshopAvailabilityUI()
    {
        var first = _instanceCombo1.SelectedIndex;
        var second = _instanceCombo2.SelectedIndex;
        _missingMods1 = BuildMissingMods(first);
        _missingMods2 = BuildMissingMods(second);
        UpdateMissingButton(_activeMissing1, _missingMods1, _missingMods1.Count > 0 ? 0 : GetHistoryCount(first));
        UpdateMissingButton(_activeMissing2, _missingMods2, _missingMods2.Count > 0 ? 0 : GetHistoryCount(second));
        UpdateOpenModStatusWindows();
    }

    private void UpdateOpenModStatusWindows()
    {
        foreach (var pair in _modStatusWindows.ToArray())
            pair.Key.UpdateRows(BuildAvailabilityRows(pair.Value));
    }

    // Keep the popup's row anchor and selectable content intact until it closes.
    private bool CanApplyWorkshopRefresh => !_windowClosed && !_loadingMods && !_customDragActive &&
        !_painting && !_instanceDragActive && !_refreshing && !_modBadgePopup.IsOpen;

    private async Task PollWorkshopDownloadsAsync()
    {
        if (_pollingWorkshop || _windowClosed || _loadingMods || _gameInstallation?.IsValid != true) return;
        var game = _gameInstallation;
        var gamePath = game.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath)) return;
        if (!string.Equals(_workshopMonitorGamePath, gamePath, StringComparison.OrdinalIgnoreCase))
        {
            _workshopMonitorGamePath = gamePath;
            _workshopDownloads = new WorkshopDownloadTracking();
        }
        var ids = _instances.Where(instance => !instance.IsSteamCmd && !instance.IsDamaged)
            .SelectMany(WorkshopReferences).Select(reference => reference.WorkshopId).ToHashSet();
        // Also notice newly completed folders for older instances without saved ID mappings.
        if (game.WorkshopPath is { } path && Directory.Exists(path))
        {
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(path))
                    if (ulong.TryParse(Path.GetFileName(folder), out var id) && id != 0) ids.Add(id);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (ids.Count == 0) return;
        _pollingWorkshop = true;
        var version = _loadVersion;
        var root = _packRoot;
        try
        {
            var snapshot = await SteamWorkshopClient.QueryDownloadStateAsync(gamePath, ids.ToList(), _workshopMonitorCancellation.Token);
            if (!WorkshopContextCurrent(gamePath, version)) return;
            _workshopDownloads.Observe(snapshot);
            if (CanApplyWorkshopRefresh) RefreshWorkshopAvailabilityUI();
            var ready = _workshopDownloads.ReadyToRefresh;
            if (ready.Count > 0 && CanApplyWorkshopRefresh)
            {
                var mods = await ModCatalog.LoadAsync(game, root);
                if (!WorkshopContextCurrent(gamePath, version) || !CanApplyWorkshopRefresh || mods.Count == 0) return;
                _allMods = mods;
                _modsByPackage = mods.GroupBy(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
                _modsById = _modsByPackage.ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);
                foreach (var instance in _instances.Where(instance => !instance.IsSteamCmd && !instance.IsDamaged))
                {
                    try { WorkshopModReferences.ResolveInstalled(instance, mods); }
                    catch (Exception exception) { AppLog.Error("Save resolved Workshop item IDs", exception); }
                }
                _workshopDownloads.Refreshed(ready, mods.Select(GetWorkshopId)
                    .Where(id => id is not null).Select(id => id!.Value));
                RefreshManager(false);
                if (GetCommandList()?.SelectedItem is ModInfo retained) ShowDetails(retained);
            }
            _workshopMonitorTimer!.Interval = TimeSpan.FromSeconds(snapshot is null ? 15 :
                snapshot.Items.Values.Any(item => item.IsDownloading || item.NeedsUpdate) ||
                _workshopDownloads.ReadyToRefresh.Count > 0 || _missingMods1.Count > 0 || _missingMods2.Count > 0 ? 4 : 30);
        }
        catch (OperationCanceledException) when (_workshopMonitorCancellation.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Monitor Steam Workshop downloads", exception); }
        finally { _pollingWorkshop = false; }
    }

    private bool WorkshopContextCurrent(string path, int version) => !_windowClosed &&
        version == _loadVersion && string.Equals(_gameInstallation?.GamePath, path, StringComparison.OrdinalIgnoreCase);
}
