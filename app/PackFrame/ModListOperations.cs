using System;
using System.Collections.Generic;
using System.Linq;

namespace PackFrame;

internal sealed record InstanceSnapshot(string FolderName, List<string> ActivePackageIds);

internal static class ModListOperations
{
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
