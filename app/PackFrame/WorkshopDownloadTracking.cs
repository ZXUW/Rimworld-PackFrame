using System.Collections.Generic;
using System.Linq;

namespace PackFrame;

public enum WorkshopModAvailability { Missing, CheckingSteam, Unknown, Queued, Downloading, Completing }

// Allows completed downloads a few applied scans to become readable. Deferred
// scans and scans that fail before applying do not consume this retry budget.
internal sealed class WorkshopDownloadTracking
{
    internal const int MaximumCompletionScans = 3;
    private readonly Dictionary<ulong, WorkshopDownloadItem> _items = new();
    private readonly HashSet<ulong> _refresh = new();
    private readonly HashSet<ulong> _observedDownloading = new();
    private readonly Dictionary<ulong, int> _scanAttempts = new();
    internal bool HasQueried { get; private set; }
    internal bool Available { get; private set; }
    internal IReadOnlyList<ulong> ReadyToRefresh => _refresh
        .Where(id => _items.TryGetValue(id, out var item) && item.IsInstalled).ToList();

    internal void Observe(WorkshopDownloadSnapshot? snapshot)
    {
        HasQueried = true;
        Available = snapshot is not null;
        if (snapshot is null) { _items.Clear(); return; }
        foreach (var (id, item) in snapshot.Items)
        {
            var hadPrevious = _items.TryGetValue(id, out var previous);
            if (item.IsDownloading)
            {
                _observedDownloading.Add(id);
                _scanAttempts.Remove(id);
                _refresh.Remove(id);
            }
            if (item.IsInstalled && hadPrevious && !previous!.IsInstalled)
                _scanAttempts.Remove(id);
            if (item.IsInstalled && _scanAttempts.GetValueOrDefault(id) < MaximumCompletionScans &&
                (_observedDownloading.Contains(id) ||
                !hadPrevious || !previous!.IsInstalled))
                _refresh.Add(id);
        }
        _items.Clear();
        foreach (var pair in snapshot.Items) _items[pair.Key] = pair.Value;
    }

    internal void Refreshed(IEnumerable<ulong> scannedIds, IEnumerable<ulong> discoveredIds)
    {
        var discovered = new HashSet<ulong>(discoveredIds);
        foreach (var id in scannedIds.Distinct())
        {
            // Do not acknowledge a stale scan after another download started,
            // or count unrelated items that were not awaiting completion.
            if (!_refresh.Contains(id) || !_items.TryGetValue(id, out var item) || !item.IsInstalled) continue;
            if (discovered.Contains(id))
            {
                _refresh.Remove(id);
                _observedDownloading.Remove(id);
                _scanAttempts.Remove(id);
                continue;
            }
            var attempts = _scanAttempts.GetValueOrDefault(id) + 1;
            _scanAttempts[id] = attempts;
            if (attempts >= MaximumCompletionScans)
            {
                _refresh.Remove(id);
                _observedDownloading.Remove(id);
            }
        }
    }

    internal WorkshopDownloadItem? Item(ulong id) => _items.GetValueOrDefault(id);

    internal WorkshopModAvailability Availability(ulong id, bool foundOnDisk)
    {
        if (!HasQueried) return WorkshopModAvailability.CheckingSteam;
        if (!Available || !_items.TryGetValue(id, out var item)) return WorkshopModAvailability.Unknown;
        if (item.IsDownloading)
            return item.IsQueued ? WorkshopModAvailability.Queued : WorkshopModAvailability.Downloading;
        if (item.IsInstalled && !foundOnDisk && _refresh.Contains(id)) return WorkshopModAvailability.Completing;
        if (item.NeedsUpdate) return WorkshopModAvailability.Unknown;
        return WorkshopModAvailability.Missing;
    }
}
