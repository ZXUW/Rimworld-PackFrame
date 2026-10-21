using System;
using System.Collections.Generic;
using System.Linq;

namespace PackFrame;

public partial class MainWindow
{
    private List<ModInfo> PrepareSearchList(IEnumerable<ModInfo> source, ModSearchState state)
    {
        var result = new List<ModInfo>();
        foreach (var mod in source)
        {
            var matched = MatchesSearch(mod, state);
            if (state.HideUnmatched && !matched) continue;
            result.Add(mod with { SearchMatched = matched });
        }
        return result;
    }

    private bool MatchesSearch(ModInfo mod, ModSearchState state)
    {
        if (state.SourceIndex == 1 && mod.Source != ModSource.Workshop) return false;
        if (state.SourceIndex == 2 && mod.Source != ModSource.Local) return false;
        var query = state.Query.Trim();
        if (query.Length == 0) return true;
        var value = state.FieldIndex switch
        {
            1 => mod.Author, 2 => mod.Path, 3 => mod.PackageId, 4 => mod.GameVersions, _ => mod.Name
        };
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private List<ModInfo> ApplyOrderFlags(List<ModInfo> active, int column)
    {
        if (active.Count == 0) return active;
        var beforeEdges = ModOrderRules.ReverseBefore(active);
        var conflicts = ModOrderRules.Conflicts(active);
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < active.Count; i++) positions[active[i].PackageId] = i;
        var result = new List<ModInfo>(active.Count);
        for (var i = 0; i < active.Count; i++)
        {
            var mod = active[i];
            var names = new List<string>();
            var ids = new List<string>();
            var missingNames = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddAfterName(string id, string name)
            {
                if (string.IsNullOrWhiteSpace(name)) name = id;
                if (seen.Add(name)) { names.Add(name); ids.Add(id); }
            }
            void AddRequiredAfter(IEnumerable<string> required)
            {
                foreach (var id in required)
                {
                    if (string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase) ||
                        !positions.TryGetValue(id, out var targetIndex) || targetIndex <= i) continue;
                    AddAfterName(id, active[targetIndex].Name);
                }
            }
            AddRequiredAfter(mod.ForceLoadAfter);
            AddRequiredAfter(mod.LoadAfter);
            if (beforeEdges.TryGetValue(mod.PackageId, out var predecessors)) AddRequiredAfter(predecessors);
            var incompatible = conflicts.TryGetValue(mod.PackageId, out var conflictIds)
                ? conflictIds.Select(id => active[positions[id]].Name).ToList() : new List<string>();
            foreach (var id in mod.ModDependencies)
            {
                if (string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase)) continue;
                if (positions.TryGetValue(id, out var targetIndex))
                {
                    if (targetIndex > i) AddAfterName(id, active[targetIndex].Name);
                    continue;
                }
                var name = _modsById.TryGetValue(id, out var installed) ? installed.Name : id;
                if (string.IsNullOrWhiteSpace(name)) name = id;
                if (missingSeen.Add(name)) missingNames.Add(name);
            }
            if (names.Count == 0 && missingNames.Count == 0 && incompatible.Count == 0)
            {
                result.Add(mod);
                continue;
            }
            var details = new List<string>();
            if (incompatible.Count > 0)
                details.Add(Localization.IncompatibleMods(_config.Language, string.Join(", ", incompatible)));
            if (names.Count > 0)
                details.Add(Localization.SortAfterTooltip(_config.Language, string.Join(", ", names)));
            if (missingNames.Count > 0)
                details.Add(Localization.MissingDependencies(_config.Language, string.Join(", ", missingNames)));
            result.Add(mod with
            {
                SortError = true, SortAfter = string.Join(Environment.NewLine, details),
                SortOrderAfterNames = names, SortOrderAfterIds = ids,
                BadgeColumn = column, MissingDependencyNames = missingNames, IncompatibleNames = incompatible
            });
        }
        return result;
    }
}
