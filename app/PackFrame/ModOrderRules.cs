using System;
using System.Collections.Generic;
using System.Linq;

namespace PackFrame;

internal static class ModOrderRules
{
    // Convert Before constraints to After edges so existing move-after actions remain correct.
    public static Dictionary<string, List<string>> ReverseBefore(IReadOnlyList<ModInfo> active)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in active)
            foreach (var target in mod.LoadBefore.Concat(mod.ForceLoadBefore))
            {
                if (!result.TryGetValue(target, out var ids)) result[target] = ids = new();
                ids.Add(mod.PackageId);
            }
        return result;
    }

    public static Dictionary<string, List<string>> Conflicts(IReadOnlyList<ModInfo> active)
    {
        var enabled = new HashSet<string>(active.Select(mod => mod.PackageId), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, string other)
        {
            if (!result.TryGetValue(id, out var ids)) result[id] = ids = new();
            if (!ids.Contains(other, StringComparer.OrdinalIgnoreCase)) ids.Add(other);
        }
        foreach (var mod in active)
            foreach (var other in mod.IncompatibleWith)
                if (enabled.Contains(other) && !mod.PackageId.Equals(other, StringComparison.OrdinalIgnoreCase))
                {
                    Add(mod.PackageId, other);
                    Add(other, mod.PackageId);
                }
        return result;
    }
}
