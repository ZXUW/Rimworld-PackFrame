using System;
using System.Collections.Generic;
using System.Linq;

namespace PackFrame;

internal sealed record InstanceSnapshot(string FolderName, List<string> ActivePackageIds);
internal sealed record ModInsertionPreview(
    bool HasChanges, int AddedCount, int ExistingCount, string? BeforeId, string? AfterId);

internal static class ModListOperations
{
    public static List<ModInfo> WithLoadOrder(
        IReadOnlyList<ModInfo> resolvedMods,
        IReadOnlyList<string> activePackageIds)
    {
        // Size once from the full order, including unresolved entries, so a
        // search or a scroll into three-digit positions cannot shift names.
        var gutterWidth = 16d;
        for (var remaining = activePackageIds.Count / 100; remaining > 0; remaining /= 10)
            gutterWidth += 8;

        var positions = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < activePackageIds.Count; index++)
        {
            var id = activePackageIds[index];
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!positions.TryGetValue(id, out var occurrences))
            {
                occurrences = new Queue<int>();
                positions.Add(id, occurrences);
            }
            occurrences.Enqueue(index + 1);
        }

        // Number before filtering and clone each occurrence so two views can
        // display different orders without changing the shared mod catalogue.
        return resolvedMods.Select(mod =>
        {
            int? position = positions.TryGetValue(mod.PackageId, out var occurrences) && occurrences.Count > 0
                ? occurrences.Dequeue()
                : null;
            return mod with
            {
                LoadOrder = position,
                LoadOrderGutterWidth = position is not null ? gutterWidth : 0
            };
        }).ToList();
    }

    public static List<string> NudgeSelection(
        IReadOnlyList<string> original,
        IReadOnlyCollection<string> selectedIds,
        int direction,
        IReadOnlyCollection<string>? visibleIds = null)
    {
        var updated = original.ToList();
        if (direction == 0 || updated.Count < 2 || selectedIds.Count == 0) return updated;

        var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        var visible = visibleIds is null
            ? null
            : new HashSet<string>(visibleIds, StringComparer.OrdinalIgnoreCase);
        bool IsMovable(string id) => !string.IsNullOrWhiteSpace(id) && (visible is null || visible.Contains(id));

        var slots = new List<int>();
        for (var i = 0; i < updated.Count; i++)
            if (IsMovable(updated[i])) slots.Add(i);
        if (slots.Count < 2) return updated;

        if (direction < 0)
        {
            for (var s = 1; s < slots.Count; s++)
            {
                var curr = slots[s];
                var prev = slots[s - 1];
                if (selected.Contains(updated[curr]) && !selected.Contains(updated[prev]))
                    (updated[prev], updated[curr]) = (updated[curr], updated[prev]);
            }
        }
        else
        {
            for (var s = slots.Count - 2; s >= 0; s--)
            {
                var curr = slots[s];
                var next = slots[s + 1];
                if (selected.Contains(updated[curr]) && !selected.Contains(updated[next]))
                    (updated[curr], updated[next]) = (updated[next], updated[curr]);
            }
        }

        return updated;
    }

    public static List<string> InsertSelection(IReadOnlyList<string> original,
        IReadOnlyList<string> selectedIds, int targetIndex)
    {
        var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        var limit = Math.Clamp(targetIndex, 0, original.Count);
        var selectedBefore = original.Take(limit).Count(selected.Contains);
        var updated = original.Where(id => !selected.Contains(id)).ToList();
        updated.InsertRange(Math.Clamp(limit - selectedBefore, 0, updated.Count), selectedIds);
        return updated;
    }

    public static ModInsertionPreview PreviewInsertion(
        IReadOnlyList<string> original,
        IReadOnlyList<string> selectedIds,
        int targetIndex)
    {
        var ids = selectedIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return new ModInsertionPreview(false, 0, 0, null, null);

        var selected = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var existing = new HashSet<string>(original, StringComparer.OrdinalIgnoreCase);
        var addedCount = ids.Count(id => !existing.Contains(id));
        var updated = InsertSelection(original, ids, targetIndex);
        var start = updated.FindIndex(selected.Contains);
        var before = updated.Skip(start + ids.Count)
            .FirstOrDefault(id => !selected.Contains(id) && !string.IsNullOrWhiteSpace(id));
        var after = before is null
            ? updated.Take(start).LastOrDefault(id => !selected.Contains(id) && !string.IsNullOrWhiteSpace(id))
            : null;

        return new ModInsertionPreview(
            !original.SequenceEqual(updated, StringComparer.OrdinalIgnoreCase),
            addedCount, ids.Count - addedCount, before, after);
    }

    public static List<string> ApplySortSuggestions(
        IReadOnlyList<string> original,
        IReadOnlyList<ModInfo> mods)
    {
        var result = original.ToList();
        var byId = mods
            .Where(mod => !string.IsNullOrWhiteSpace(mod.PackageId))
            .GroupBy(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (var pass = 0; pass < original.Count; pass++)
        {
            var changed = false;
            foreach (var mod in byId.Values)
            {
                if (mod.SortOrderAfterIds.Count == 0)
                {
                    continue;
                }

                var current = result.FindIndex(id =>
                    string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase));
                if (current < 0)
                {
                    continue;
                }

                var target = -1;
                foreach (var id in mod.SortOrderAfterIds)
                {
                    target = Math.Max(target, result.FindIndex(existing =>
                        string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)));
                }

                if (target <= current)
                {
                    continue;
                }

                result.RemoveAt(current);
                if (current < target)
                {
                    target--;
                }

                result.Insert(Math.Min(target + 1, result.Count), mod.PackageId);
                changed = true;
            }

            if (!changed)
            {
                break;
            }
        }

        return result;
    }

    // Returns the inverse snapshot for redo; validates all instances before changing any.
    public static List<InstanceSnapshot> Restore(IReadOnlyList<InstanceSnapshot> snapshots,
        IEnumerable<ManagedInstance> instances)
    {
        var map = instances.ToDictionary(instance => instance.FolderName, StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            if (!map.ContainsKey(snapshot.FolderName))
                throw new InvalidOperationException("Instance no longer exists: " + snapshot.FolderName);
            map[snapshot.FolderName].EnsureHealthy();
        }
        var inverse = snapshots.Select(snapshot => new InstanceSnapshot(snapshot.FolderName,
            map[snapshot.FolderName].ActivePackageIds.ToList())).ToList();
        foreach (var snapshot in snapshots)
            InstanceStore.StageActiveMods(map[snapshot.FolderName], snapshot.ActivePackageIds);
        return inverse;
    }
}
