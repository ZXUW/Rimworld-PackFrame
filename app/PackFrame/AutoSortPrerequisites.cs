using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PackFrame;

public sealed record PrerequisiteItem(
    string PackageId,
    string DisplayName,
    ModInfo? InstalledMod,
    IReadOnlyList<string> RequiredBy,
    bool IsTranslationOnly,
    string? OriginalDepId = null,
    string? OriginalDepName = null,
    bool IsFuzzyMatch = false,
    ulong? WorkshopFileId = null,
    uint? SteamStoreAppId = null
);

public sealed record PrerequisiteScanResult(
    int EnabledCount,
    IReadOnlyList<PrerequisiteItem> InstalledMissing,
    IReadOnlyList<PrerequisiteItem> FuzzyMatches,
    IReadOnlyList<PrerequisiteItem> UninstalledMissing,
    IReadOnlyList<PrerequisiteItem> IgnoredTranslation
);

public sealed record CorePrerequisiteItem(
    ModInfo Mod,
    string TierLabel,
    string Reason,
    int CurrentIndex,
    int TargetIndex,
    bool NeedsMove
);

public enum SortRuleKind
{
    Dependency,
    LoadAfter,
    LoadBefore,
    CommunityRule,
    ImplicitCoreDlc,
    ImplicitTranslation,
    ImplicitRjw
}

public sealed record CollectedSortRule(
    string BeforeId,
    string BeforeName,
    int BeforeIndex,
    string AfterId,
    string AfterName,
    int AfterIndex,
    SortRuleKind Kind,
    string KindLabel,
    bool IsViolated
);

public sealed record RuleConflictPair(
    ModInfo FirstMod,
    ModInfo SecondMod,
    string SourceLabel
)
{
    public string FirstId => FirstMod.PackageId;
    public string FirstName => string.IsNullOrWhiteSpace(FirstMod.RowName) ? FirstMod.Name : FirstMod.RowName;
    public string SecondId => SecondMod.PackageId;
    public string SecondName => string.IsNullOrWhiteSpace(SecondMod.RowName) ? SecondMod.Name : SecondMod.RowName;
}

public sealed record SortPreviewRow(
    ModInfo Mod,
    int OldIndex,
    int NewIndex,
    int Delta,
    bool IsBottomPinned
);

public sealed record TopologicalSortPlan(
    IReadOnlyList<string> OrderedPackageIds,
    IReadOnlyList<SortPreviewRow> Rows,
    IReadOnlyList<SortPreviewRow> MovedRows,
    IReadOnlyList<CollectedSortRule> BrokenCycleEdges,
    int FixedRuleCount
);

public sealed record RuleCyclePair(
    ModInfo FirstMod,
    ModInfo SecondMod,
    CollectedSortRule ForwardRule,
    CollectedSortRule ReverseRule
);

public sealed record CollectedRulesResult(
    int ActiveCount,
    IReadOnlyList<CollectedSortRule> Rules,
    IReadOnlyList<CollectedSortRule> ViolatedRules,
    IReadOnlyList<CollectedSortRule> CycleRules,
    IReadOnlyList<RuleCyclePair> CyclePairs,
    IReadOnlyList<RuleConflictPair> Conflicts,
    IReadOnlyList<ModInfo> BottomTierMods,
    int DependencyRuleCount,
    int LoadAfterRuleCount,
    int LoadBeforeRuleCount,
    int CommunityRuleCount,
    int ImplicitCoreDlcRuleCount,
    int ImplicitTranslationRuleCount,
    int ImplicitRjwRuleCount
);

internal static class AutoSortPrerequisites
{
    private static readonly HashSet<string> CommonBaseIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "brrainz.harmony",
        "ludeon.rimworld",
        "ludeon.rimworld.royalty",
        "ludeon.rimworld.ideology",
        "ludeon.rimworld.biotech",
        "ludeon.rimworld.anomaly",
        "ludeon.rimworld.odyssey",
        "unlimitedhugs.hugslib"
    };

    public static bool IsCommonBaseId(string id) => CommonBaseIds.Contains(id);

    private static readonly string[] IdSuffixes =
    {
        "_steam", "_copy", ".continued", "_continued", ".16", "_16", ".15", "_15", ".17", "_17", "_fork", ".fork"
    };

    private static readonly HashSet<string> GenericModSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "core", "framework", "library", "lib", "patch", "patches", "common", "base", "main", "mod", "rimworld"
    };

    private static readonly Regex TrailingNoise = new(
        @"(\s*[\(\[（【]\s*(continued|cont\.?|1\.\d+|\d+\.\d+|fork|reupload|temp|steam|更新|延续|重置)\s*[\)\]）】])+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ModInfo? ResolveDependency(
        string depId,
        string? displayName,
        string? url,
        IReadOnlyList<ModInfo> candidates,
        Func<string, ModInfo?> exactLookup) =>
        ResolveDependencyDetailed(depId, displayName, url, null, candidates, exactLookup).Mod;

    public static (ModInfo? Mod, bool IsFuzzy) ResolveDependencyDetailed(
        string depId,
        string? displayName,
        string? url,
        ModInfo? requester,
        IReadOnlyList<ModInfo> candidates,
        Func<string, ModInfo?> exactLookup)
    {
        // Level 0: Exact PackageId match
        var exact = exactLookup(depId);
        if (exact is not null) return (exact, false);
        var exactCandidate = candidates.FirstOrDefault(m =>
            string.Equals(m.PackageId, depId, StringComparison.OrdinalIgnoreCase));
        if (exactCandidate is not null) return (exactCandidate, false);

        // Level 1: Exact Steam Workshop ID match
        var workshopId = ModpackService.ParseWorkshopId(url);
        if (workshopId is not null)
        {
            var idText = workshopId.Value.ToString();
            var byWorkshop = candidates.FirstOrDefault(m =>
                string.Equals(Path.GetFileName(m.Path), idText, StringComparison.OrdinalIgnoreCase) ||
                ModpackService.ParseWorkshopId(m.Link) == workshopId);
            if (byWorkshop is not null) return (byWorkshop, false);
        }

        // Level 2: Same packageId after stripping channel/version suffix (_steam, _copy, _continued, .16)
        var stem = StripIdSuffix(depId);
        var byStem = candidates.FirstOrDefault(m =>
            !m.IsTranslationMod &&
            string.Equals(StripIdSuffix(m.PackageId), stem, StringComparison.OrdinalIgnoreCase));
        if (byStem is not null)
        {
            var isChannelOnly = string.Equals(stem + "_steam", byStem.PackageId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(stem + "_copy", byStem.PackageId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(byStem.PackageId + "_steam", depId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(byStem.PackageId + "_copy", depId, StringComparison.OrdinalIgnoreCase);
            return (byStem, !isChannelOnly);
        }

        // Level 3: Explicit loadAfter substitute on requester sharing the same mod slug
        var depSlug = ExtractModSlug(stem);
        if (requester is not null && depSlug.Length >= 4 && !GenericModSlugs.Contains(depSlug))
        {
            foreach (var afterId in requester.LoadAfter.Concat(requester.ForceLoadAfter))
            {
                if (string.Equals(afterId, depId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(ExtractModSlug(StripIdSuffix(afterId)), depSlug, StringComparison.OrdinalIgnoreCase))
                {
                    var sub = exactLookup(afterId) ?? candidates.FirstOrDefault(m =>
                        string.Equals(m.PackageId, afterId, StringComparison.OrdinalIgnoreCase));
                    if (sub is not null && !sub.IsTranslationMod)
                    {
                        return (sub, true);
                    }
                }
            }
        }

        // Level 4: Author-changed PackageId with identical non-generic mod slug (e.g., Author.ModName -> Mlie.ModName)
        if (depSlug.Length >= 5 && !GenericModSlugs.Contains(depSlug))
        {
            var slugMatches = candidates
                .Where(m => !m.IsTranslationMod &&
                            string.Equals(ExtractModSlug(StripIdSuffix(m.PackageId)), depSlug, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (slugMatches.Count == 1)
            {
                return (slugMatches[0], true);
            }
        }

        // Level 5: Normalized displayName match
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var normTarget = NormalizeName(displayName);
            if (normTarget.Length >= 3)
            {
                var byName = candidates.FirstOrDefault(m =>
                    !m.IsTranslationMod &&
                    string.Equals(NormalizeName(m.Name), normTarget, StringComparison.OrdinalIgnoreCase));
                if (byName is not null) return (byName, true);
            }
        }

        return (null, false);
    }

    internal static string ExtractModSlug(string id)
    {
        var dot = id.IndexOf('.');
        return dot >= 0 && dot < id.Length - 1 ? id[(dot + 1)..] : id;
    }

    internal static string StripIdSuffix(string id)
    {
        var result = id.Trim();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in IdSuffixes)
            {
                if (result.Length > suffix.Length &&
                    result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[..^suffix.Length];
                    changed = true;
                }
            }
        }

        return result;
    }

    internal static string NormalizeName(string name)
    {
        var cleaned = TrailingNoise.Replace(name.Trim(), string.Empty).Trim();
        return new string(cleaned.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());
    }

    public static PrerequisiteScanResult Scan(
        IReadOnlyList<ModInfo> activeMods,
        Func<string, ModInfo?> resolveInstalled,
        IReadOnlyList<ModInfo>? allInstalled = null,
        IReadOnlySet<string>? confirmedFuzzyDeps = null,
        WorkshopMapCache? workshopMap = null)
    {
        var mapLookup = workshopMap ?? WorkshopMapCache.Shared;
        var candidates = allInstalled ?? activeMods;
        var enabled = new HashSet<string>(
            activeMods.Select(m => m.PackageId),
            StringComparer.OrdinalIgnoreCase);
        var normalReq = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var fuzzyReq = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var transReq = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var resolvedMods = new Dictionary<string, ModInfo?>(StringComparer.OrdinalIgnoreCase);
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var origIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var origNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var declaredWorkshopIds = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        (ModInfo? Mod, bool IsFuzzy) Resolve(ModInfo source, string depId)
        {
            source.ModDependencyNames.TryGetValue(depId, out var display);
            source.ModDependencyUrls.TryGetValue(depId, out var url);
            var (resolved, isFuzzy) = ResolveDependencyDetailed(
                depId, display, url, source, candidates, resolveInstalled);
            if (isFuzzy && confirmedFuzzyDeps is not null &&
                (confirmedFuzzyDeps.Contains(depId) ||
                 (resolved is not null && confirmedFuzzyDeps.Contains(resolved.PackageId))))
            {
                isFuzzy = false;
            }
            return (resolved, isFuzzy);
        }

        void Record(
            Dictionary<string, List<string>> map,
            string keyId,
            ModInfo? installed,
            string requesterName,
            string? fallbackTitle,
            string? rawDepId = null,
            string? rawDepName = null,
            string? rawDepUrl = null)
        {
            if (!map.TryGetValue(keyId, out var list))
            {
                map[keyId] = list = new List<string>();
                if (!order.Contains(keyId, StringComparer.OrdinalIgnoreCase))
                {
                    order.Add(keyId);
                }
            }

            if (!list.Contains(requesterName, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(requesterName);
            }

            if (installed is not null)
            {
                resolvedMods[keyId] = installed;
            }

            if (!titles.ContainsKey(keyId) && !string.IsNullOrWhiteSpace(fallbackTitle))
            {
                titles[keyId] = fallbackTitle!;
            }

            if (!string.IsNullOrWhiteSpace(rawDepId) && !origIds.ContainsKey(keyId))
            {
                origIds[keyId] = rawDepId!;
            }

            if (!string.IsNullOrWhiteSpace(rawDepName) && !origNames.ContainsKey(keyId))
            {
                origNames[keyId] = rawDepName!;
            }

            if (!declaredWorkshopIds.ContainsKey(keyId) &&
                ModpackService.ParseWorkshopId(rawDepUrl) is { } parsedWsId &&
                parsedWsId >= 10_000_000)
            {
                declaredWorkshopIds[keyId] = parsedWsId;
            }
        }

        foreach (var mod in activeMods)
        {
            var modTitle = string.IsNullOrWhiteSpace(mod.RowName) ? mod.Name : mod.RowName;
            if (mod.IsTranslationMod)
            {
                var nonBaseDeps = mod.RequiredDependenciesForSort
                    .Where(d => !string.Equals(d, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                                !CommonBaseIds.Contains(d))
                    .ToList();
                var nonBaseAfter = mod.LoadAfter
                    .Concat(mod.ForceLoadAfter)
                    .Where(d => !string.Equals(d, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                                !CommonBaseIds.Contains(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var isOneToMany = nonBaseDeps.Count > 1 ||
                                  (nonBaseDeps.Count >= 1 && nonBaseAfter.Count > 1);
                var anyTargetEnabled = nonBaseDeps
                    .Concat(nonBaseAfter)
                    .Any(d =>
                    {
                        if (enabled.Contains(d)) return true;
                        var (resolved, isFuzzy) = Resolve(mod, d);
                        return resolved is not null && !isFuzzy && enabled.Contains(resolved.PackageId);
                    });

                foreach (var depId in mod.RequiredDependenciesForSort)
                {
                    if (string.Equals(depId, mod.PackageId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var (resolved, isFuzzy) = Resolve(mod, depId);
                    var effectiveId = resolved?.PackageId ?? depId;
                    if (enabled.Contains(depId) || (!isFuzzy && enabled.Contains(effectiveId)))
                    {
                        continue;
                    }

                    mod.ModDependencyNames.TryGetValue(depId, out var depTitle);
                    mod.ModDependencyUrls.TryGetValue(depId, out var depUrl);
                    if (isOneToMany && anyTargetEnabled && !CommonBaseIds.Contains(depId))
                    {
                        Record(transReq, effectiveId, resolved, modTitle, depTitle, depId, depTitle, depUrl);
                    }
                    else if (isFuzzy && resolved is not null)
                    {
                        Record(fuzzyReq, effectiveId, resolved, modTitle, depTitle, depId, depTitle, depUrl);
                    }
                    else
                    {
                        Record(normalReq, effectiveId, resolved, modTitle, depTitle, depId, depTitle, depUrl);
                    }
                }

                continue;
            }

            var queue = new Queue<(ModInfo Source, string RequesterTitle)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mod.PackageId };
            queue.Enqueue((mod, modTitle));

            while (queue.Count > 0)
            {
                var (current, requesterTitle) = queue.Dequeue();
                foreach (var depId in current.RequiredDependenciesForSort)
                {
                    if (string.IsNullOrWhiteSpace(depId)) continue;
                    var (resolved, isFuzzy) = Resolve(current, depId);
                    var effectiveId = resolved?.PackageId ?? depId;
                    if (!visited.Add(effectiveId)) continue;

                    current.ModDependencyNames.TryGetValue(depId, out var depTitle);
                    current.ModDependencyUrls.TryGetValue(depId, out var depUrl);
                    if (isFuzzy && resolved is not null)
                    {
                        // Requires explicit user confirmation even if already enabled or installed
                        Record(fuzzyReq, effectiveId, resolved, requesterTitle, depTitle, depId, depTitle, depUrl);
                        continue;
                    }

                    if (!enabled.Contains(depId) && !enabled.Contains(effectiveId))
                    {
                        Record(normalReq, effectiveId, resolved, requesterTitle, depTitle, depId, depTitle, depUrl);
                    }

                    if (resolved is not null && !resolved.IsTranslationMod)
                    {
                        queue.Enqueue((resolved, requesterTitle));
                    }
                }
            }
        }

        var installedMissing = new List<PrerequisiteItem>();
        var fuzzyMatches = new List<PrerequisiteItem>();
        var uninstalledMissing = new List<PrerequisiteItem>();
        var ignoredTranslation = new List<PrerequisiteItem>();

        foreach (var depId in order)
        {
            resolvedMods.TryGetValue(depId, out var installed);
            installed ??= resolveInstalled(depId);
            var displayName = installed is not null
                ? (string.IsNullOrWhiteSpace(installed.RowName) ? installed.Name : installed.RowName)
                : (titles.TryGetValue(depId, out var hint) && !string.IsNullOrWhiteSpace(hint)
                    ? hint
                    : depId);
            origIds.TryGetValue(depId, out var rawId);
            origNames.TryGetValue(depId, out var rawName);

            var officialAppId = WorkshopMapCache.TryGetOfficialAppId(depId)
                                ?? WorkshopMapCache.TryGetOfficialAppId(rawId);
            ulong? workshopFileId = null;
            if (officialAppId is null)
            {
                workshopFileId = declaredWorkshopIds.TryGetValue(depId, out var explicitId)
                    ? explicitId
                    : (mapLookup.TryGetWorkshopId(depId) ?? mapLookup.TryGetWorkshopId(rawId));
            }

            if (fuzzyReq.TryGetValue(depId, out var fuzzyRequesters) && installed is not null)
            {
                fuzzyMatches.Add(new PrerequisiteItem(
                    depId, displayName, installed, fuzzyRequesters, false, rawId, rawName, true, workshopFileId, officialAppId));
            }
            else if (normalReq.TryGetValue(depId, out var requesters))
            {
                var item = new PrerequisiteItem(
                    depId, displayName, installed, requesters, false, rawId, rawName, false, workshopFileId, officialAppId);
                if (installed is not null) installedMissing.Add(item);
                else uninstalledMissing.Add(item);
            }
            else if (transReq.TryGetValue(depId, out var transRequesters))
            {
                ignoredTranslation.Add(
                    new PrerequisiteItem(
                        depId, displayName, installed, transRequesters, true, rawId, rawName, false, workshopFileId, officialAppId));
            }
        }

        return new PrerequisiteScanResult(
            activeMods.Count,
            installedMissing,
            fuzzyMatches,
            uninstalledMissing,
            ignoredTranslation);
    }

    public static List<string> InsertPrerequisites(
        IReadOnlyList<string> activePackageIds,
        IReadOnlyList<string> selectedIds,
        IReadOnlyList<ModInfo> activeMods,
        Func<string, ModInfo?> resolveInstalled,
        IReadOnlyList<ModInfo>? allInstalled = null)
    {
        var candidates = allInstalled ?? activeMods;
        var result = activePackageIds.ToList();
        var enabled = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        var toInsert = selectedIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !enabled.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (toInsert.Count == 0) return result;

        var ordered = OrderPrerequisites(toInsert, resolveInstalled);
        ordered.Reverse();
        var activeById = activeMods.ToDictionary(m => m.PackageId, StringComparer.OrdinalIgnoreCase);

        foreach (var depId in ordered)
        {
            if (!enabled.Add(depId)) continue;
            var earliestDependentIndex = -1;
            for (var i = 0; i < result.Count; i++)
            {
                var currentId = result[i];
                var mod = activeById.TryGetValue(currentId, out var activeMod)
                    ? activeMod
                    : resolveInstalled(currentId);
                if (mod is not null && DependsOn(mod, depId, candidates, resolveInstalled))
                {
                    earliestDependentIndex = i;
                    break;
                }
            }

            if (earliestDependentIndex >= 0)
            {
                result.Insert(earliestDependentIndex, depId);
            }
            else
            {
                result.Add(depId);
            }
        }

        return result;
    }

    private static bool DependsOn(
        ModInfo mod,
        string targetPackageId,
        IReadOnlyList<ModInfo> candidates,
        Func<string, ModInfo?> resolveInstalled)
    {
        foreach (var dep in mod.RequiredDependenciesForSort)
        {
            if (string.Equals(dep, targetPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            mod.ModDependencyNames.TryGetValue(dep, out var display);
            mod.ModDependencyUrls.TryGetValue(dep, out var url);
            var (resolved, _) = ResolveDependencyDetailed(dep, display, url, mod, candidates, resolveInstalled);
            if (resolved is not null &&
                string.Equals(resolved.PackageId, targetPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> OrderPrerequisites(
        List<string> ids,
        Func<string, ModInfo?> resolveInstalled)
    {
        var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(ids.Count);

        void Visit(string id)
        {
            if (!visited.Add(id)) return;
            var mod = resolveInstalled(id);
            if (mod is not null)
            {
                foreach (var dep in mod.RequiredDependenciesForSort)
                {
                    if (set.Contains(dep)) Visit(dep);
                }
            }

            ordered.Add(id);
        }

        foreach (var id in ids) Visit(id);
        return ordered;
    }

    private static readonly string[] OfficialDlcOrder =
    {
        "ludeon.rimworld.royalty",
        "ludeon.rimworld.ideology",
        "ludeon.rimworld.biotech",
        "ludeon.rimworld.anomaly",
        "ludeon.rimworld.odyssey"
    };

    private static readonly HashSet<string> KnownPreCoreIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "brrainz.harmony",
        "zetrith.prepatcher",
        "jikulopo.prepatcher",
        "taranchuk.fastergameloading",
        "chezhou.rimunity"
    };

    public static List<CorePrerequisiteItem> ScanCorePrerequisites(
        IReadOnlyList<ModInfo> activeMods,
        string? language)
    {
        string T(string en, string zh, string tw, string ja) =>
            OperationText.Choose(language, en, zh, tw, ja);

        var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < activeMods.Count; i++)
        {
            indexById[activeMods[i].PackageId] = i;
        }

        var preCoreMods = new List<ModInfo>();
        var coreList = new List<(ModInfo Mod, string Tier, string Reason)>();
        var dlcList = new List<(ModInfo Mod, string Tier, string Reason)>();
        var hugsList = new List<(ModInfo Mod, string Tier, string Reason)>();
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Harmony first, then explicit loadBefore Core / known Pre-Core patches (+ any 1-to-1 Pre-Core translations)
        var harmonyMod = activeMods.FirstOrDefault(m =>
            string.Equals(m.PackageId, "brrainz.harmony", StringComparison.OrdinalIgnoreCase));
        if (harmonyMod is not null && assigned.Add(harmonyMod.PackageId))
        {
            preCoreMods.Add(harmonyMod);
        }

        foreach (var mod in activeMods)
        {
            if (assigned.Contains(mod.PackageId)) continue;
            var loadsBeforeCore = mod.LoadBefore.Concat(mod.ForceLoadBefore)
                .Any(id => string.Equals(id, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
            if (loadsBeforeCore || KnownPreCoreIds.Contains(mod.PackageId))
            {
                assigned.Add(mod.PackageId);
                preCoreMods.Add(mod);
            }
        }

        // Keep 1-to-1 translation mods paired with Pre-Core patches inside Pre-Core so they don't get orphaned
        var preCoreIdSet = new HashSet<string>(preCoreMods.Select(m => m.PackageId), StringComparer.OrdinalIgnoreCase);
        var preCoreTransParent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in activeMods)
        {
            if (assigned.Contains(mod.PackageId) || !mod.IsTranslationMod) continue;
            var nonBaseTargets = mod.RequiredDependenciesForSort.Concat(mod.LoadAfter).Concat(mod.ForceLoadAfter)
                .Where(id => !string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                             !IsCommonBaseId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var loadsAfterCore = mod.LoadAfter.Concat(mod.ForceLoadAfter)
                .Any(id => string.Equals(id, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
            if (!loadsAfterCore && nonBaseTargets.Count > 0 && nonBaseTargets.All(id => preCoreIdSet.Contains(id)))
            {
                assigned.Add(mod.PackageId);
                preCoreMods.Add(mod);
                preCoreTransParent[mod.PackageId] = nonBaseTargets[0];
            }
        }

        var orderedPreCoreIds = OrderTierByDependencies(preCoreMods, preCoreTransParent);
        var preCoreById = preCoreMods.ToDictionary(m => m.PackageId, StringComparer.OrdinalIgnoreCase);
        var preCore = new List<(ModInfo Mod, string Tier, string Reason)>(orderedPreCoreIds.Count);
        foreach (var preId in orderedPreCoreIds)
        {
            var mod = preCoreById[preId];
            var isHarmony = string.Equals(mod.PackageId, "brrainz.harmony", StringComparison.OrdinalIgnoreCase);
            var isPreCoreTrans = preCoreTransParent.ContainsKey(mod.PackageId);
            preCore.Add((
                mod,
                T("Pre-Core Patch", "Harmony 系前置", "Harmony 系前置", "コア前パッチ"),
                isHarmony
                    ? T("Base Harmony patcher (loads before Core)", "Harmony 基础补丁库（排在 Core 之前）", "Harmony 基礎補丁庫（排在 Core 之前）", "Harmony基盤（Coreより前）")
                    : isPreCoreTrans
                        ? T("Follows Pre-Core parent mod", "紧随 Harmony 系前置本体之后", "緊隨 Harmony 系前置本體之後", "コア前パッチ本体の直後")
                        : T("Loads before Core", "声明排在 Core 之前", "聲明排在 Core 之前", "Coreより前に読み込む")));
        }

        // 2. Core (ludeon.rimworld)
        var coreMod = activeMods.FirstOrDefault(m =>
            string.Equals(m.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
        if (coreMod is not null && assigned.Add(coreMod.PackageId))
        {
            coreList.Add((
                coreMod,
                T("Game Core", "游戏本体", "遊戲本體", "ゲーム本体"),
                T("RimWorld Core", "RimWorld 核心本体", "RimWorld 核心本體", "RimWorld コア")));
        }

        // 3. Official DLCs in canonical release order
        foreach (var dlcId in OfficialDlcOrder)
        {
            var dlcMod = activeMods.FirstOrDefault(m =>
                string.Equals(m.PackageId, dlcId, StringComparison.OrdinalIgnoreCase));
            if (dlcMod is not null && assigned.Add(dlcMod.PackageId))
            {
                dlcList.Add((
                    dlcMod,
                    T("Official DLC", "官方 DLC", "官方 DLC", "公式DLC"),
                    T("Official Expansion", "官方扩展包", "官方擴展包", "公式拡張パック")));
            }
        }

        // 4. HugsLib only (Plan A: pure system-level top tier; all other frameworks stay in topological sort)
        var hugsMod = activeMods.FirstOrDefault(m =>
            string.Equals(m.PackageId, "unlimitedhugs.hugslib", StringComparison.OrdinalIgnoreCase));
        if (hugsMod is not null && assigned.Add(hugsMod.PackageId))
        {
            hugsList.Add((
                hugsMod,
                T("Base Library", "基础运行库", "基礎運行庫", "基本ライブラリ"),
                T("HugsLib base library (immediately after official DLCs)", "HugsLib 基础库（紧随官方 DLC 之后）", "HugsLib 基礎庫（緊隨官方 DLC 之後）", "HugsLib基盤（公式DLC直後）")));
        }

        var combined = preCore.Concat(coreList).Concat(dlcList).Concat(hugsList).ToList();

        var result = new List<CorePrerequisiteItem>(combined.Count);
        for (var targetIdx = 0; targetIdx < combined.Count; targetIdx++)
        {
            var (mod, tier, reason) = combined[targetIdx];
            var currentIdx = indexById[mod.PackageId];
            var needsMove = currentIdx != targetIdx;
            result.Add(new CorePrerequisiteItem(mod, tier, reason, currentIdx, targetIdx, needsMove));
        }

        return result;
    }

    public static List<string> ApplyCorePrerequisitesOrder(
        IReadOnlyList<string> activePackageIds,
        IReadOnlyList<ModInfo> activeMods,
        IReadOnlySet<string> selectedCoreIds,
        string? language)
    {
        if (selectedCoreIds.Count == 0) return activePackageIds.ToList();
        var items = ScanCorePrerequisites(activeMods, language);
        var fullCoreIds = items.Select(i => i.Mod.PackageId).ToList();

        // Include any already-positioned core items above or selected ones so the core header stays contiguous and ordered
        var pinnedIds = fullCoreIds
            .Where(id => selectedCoreIds.Contains(id) ||
                         items.First(x => string.Equals(x.Mod.PackageId, id, StringComparison.OrdinalIgnoreCase)).NeedsMove == false)
            .ToList();
        var pinnedSet = new HashSet<string>(pinnedIds, StringComparer.OrdinalIgnoreCase);
        var remainder = activePackageIds.Where(id => !pinnedSet.Contains(id)).ToList();
        return pinnedIds.Concat(remainder).ToList();
    }

    private static List<string> OrderTierByDependencies(
        IReadOnlyList<ModInfo> tierMods,
        IReadOnlyDictionary<string, string>? transParents = null)
    {
        var byId = tierMods.ToDictionary(m => m.PackageId, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(tierMods.Count);

        void Visit(ModInfo mod)
        {
            if (!visited.Add(mod.PackageId)) return;
            if (!string.Equals(mod.PackageId, "brrainz.harmony", StringComparison.OrdinalIgnoreCase) &&
                byId.TryGetValue("brrainz.harmony", out var harmony))
            {
                Visit(harmony);
            }

            foreach (var dep in mod.RequiredDependenciesForSort.Concat(mod.LoadAfter).Concat(mod.ForceLoadAfter))
            {
                if (byId.TryGetValue(dep, out var target))
                {
                    Visit(target);
                }
            }

            foreach (var other in tierMods)
            {
                if (other.LoadBefore.Concat(other.ForceLoadBefore)
                    .Any(id => string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase)))
                {
                    Visit(other);
                }
            }
            ordered.Add(mod.PackageId);

            // Glue 1-to-1 Pre-Core translations right beneath their parent patch so later Pre-Core patches don't split them
            if (transParents is not null)
            {
                foreach (var (transId, parentId) in transParents)
                {
                    if (string.Equals(parentId, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                        byId.TryGetValue(transId, out var transMod))
                    {
                        Visit(transMod);
                    }
                }
            }
        }

        foreach (var mod in tierMods) Visit(mod);
        return ordered;
    }

    private static readonly Regex TranslationIdSuffix = new(
        @"(\.(zh|chs|cht|cn|sc|tc|zhpatch|chinesepack|jp|ja|ko|ru|fr|de|es)|_(zh|chs|cht|cn|zhpatch))+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TranslationIdPrefix = new(
        @"^(rwzh\.chinesepack\.|rwzh\.[a-z0-9_]+\.|zh\.new\.|zh\.|chs\.|cht\.|cn\.|soulwolf\.ch\.|seigai\.chinesepack\.)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TranslationNameSuffix = new(
        @"(\s*[-_—–·:：]?\s*(简体中文|繁體中文|繁体中文|中文汉化|汉化包|汉化补丁|汉化|漢化|中文化|本地化|语言包|chinese\s*translation|chinese\s*pack|simplified\s*chinese|traditional\s*chinese|zh[-_ ]?cn|zh[-_ ]?hans|chs|cht))+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> RjwBasePackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "rim.job.world"
    };

    private static readonly Regex StandaloneRjwToken = new(
        @"(?<![A-Za-z0-9])rjw(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsRjwBaseId(string packageId) =>
        RjwBasePackageIds.Contains(StripIdSuffix(packageId));

    public static bool HasStandaloneRjwToken(ModInfo mod)
    {
        if (StandaloneRjwToken.IsMatch(mod.PackageId) ||
            StandaloneRjwToken.IsMatch(mod.Name) ||
            (!string.IsNullOrWhiteSpace(mod.RowName) && StandaloneRjwToken.IsMatch(mod.RowName)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(mod.Path))
        {
            var folderName = Path.GetFileName(mod.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (StandaloneRjwToken.IsMatch(folderName))
            {
                return true;
            }
        }

        return false;
    }

    public static CollectedRulesResult CollectSortRules(
        IReadOnlyList<ModInfo> activeMods,
        string? language,
        CommunityRulesSnapshot? communityRules = null,
        bool useCommunityRules = true,
        IReadOnlyDictionary<string, string>? cycleResolutions = null)
    {
        string T(string en, string zh, string tw, string ja) =>
            OperationText.Choose(language, en, zh, tw, ja);

        string KindText(SortRuleKind kind) => kind switch
        {
            SortRuleKind.Dependency => T("modDependencies", "前置依赖", "前置依賴", "前提依存"),
            SortRuleKind.LoadAfter => T("loadAfter", "声明置后", "聲明置後", "loadAfter"),
            SortRuleKind.LoadBefore => T("loadBefore", "声明置前", "聲明置前", "loadBefore"),
            SortRuleKind.CommunityRule => T("Community Rule", "社区规则", "社區規則", "コミュニティルール"),
            SortRuleKind.ImplicitCoreDlc => T("Implicit Core/DLC", "内置本体/DLC", "內置本體/DLC", "内蔵Core/DLC"),
            SortRuleKind.ImplicitRjw => T("Implicit RJW", "内置 RJW 置后", "內置 RJW 置後", "内蔵RJW"),
            _ => T("Implicit Translation", "内置汉化配对", "內置漢化配對", "内蔵翻訳ペア")
        };

        var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modById = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < activeMods.Count; i++)
        {
            indexById[activeMods[i].PackageId] = i;
            modById[activeMods[i].PackageId] = activeMods[i];
        }

        string TitleOf(ModInfo m) => string.IsNullOrWhiteSpace(m.RowName) ? m.Name : m.RowName;

        // Edge key: "beforeLower\0afterLower" -> highest priority SortRuleKind
        var edgeKinds = new Dictionary<string, (string BeforeId, string AfterId, SortRuleKind Kind)>(StringComparer.OrdinalIgnoreCase);
        void AddEdge(string beforeId, string afterId, SortRuleKind kind)
        {
            if (string.Equals(beforeId, afterId, StringComparison.OrdinalIgnoreCase)) return;
            if (!modById.TryGetValue(beforeId, out var bMod) || !modById.TryGetValue(afterId, out var aMod)) return;
            var key = $"{bMod.PackageId.ToLowerInvariant()}\0{aMod.PackageId.ToLowerInvariant()}";
            if (!edgeKinds.TryGetValue(key, out var existing) || kind < existing.Kind)
            {
                edgeKinds[key] = (bMod.PackageId, aMod.PackageId, kind);
            }
        }

        string? ResolveActiveTarget(ModInfo? requester, string rawTargetId, bool allowResolvedFallback)
        {
            if (modById.ContainsKey(rawTargetId)) return modById[rawTargetId].PackageId;
            var stripped = StripIdSuffix(rawTargetId);
            foreach (var candidate in activeMods)
            {
                if (string.Equals(StripIdSuffix(candidate.PackageId), stripped, StringComparison.OrdinalIgnoreCase))
                {
                    var isChannelOnly =
                        string.Equals(stripped + "_steam", candidate.PackageId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(stripped + "_copy", candidate.PackageId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.PackageId + "_steam", rawTargetId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.PackageId + "_copy", rawTargetId, StringComparison.OrdinalIgnoreCase);
                    if (isChannelOnly) return candidate.PackageId;
                }
            }

            if (!allowResolvedFallback || requester is null) return null;
            requester.ModDependencyNames.TryGetValue(rawTargetId, out var depName);
            requester.ModDependencyUrls.TryGetValue(rawTargetId, out var depUrl);
            var (resolved, isFuzzy) = ResolveDependencyDetailed(
                rawTargetId, depName, depUrl, requester, activeMods, id => modById.TryGetValue(id, out var m) ? m : null);
            return !isFuzzy && resolved is not null ? resolved.PackageId : null;
        }

        // 1. Explicit modDependencies, loadAfter/forceLoadAfter, loadBefore/forceLoadBefore
        foreach (var mod in activeMods)
        {
            foreach (var depId in mod.RequiredDependenciesForSort)
            {
                var target = ResolveActiveTarget(mod, depId, allowResolvedFallback: true);
                if (target is not null) AddEdge(target, mod.PackageId, SortRuleKind.Dependency);
            }

            foreach (var afterId in mod.ForceLoadAfter.Concat(mod.LoadAfter))
            {
                var target = ResolveActiveTarget(mod, afterId, allowResolvedFallback: false);
                if (target is not null) AddEdge(target, mod.PackageId, SortRuleKind.LoadAfter);
            }

            foreach (var beforeId in mod.ForceLoadBefore.Concat(mod.LoadBefore))
            {
                var target = ResolveActiveTarget(mod, beforeId, allowResolvedFallback: false);
                if (target is not null) AddEdge(mod.PackageId, target, SortRuleKind.LoadBefore);
            }
        }

        // 1b. Community Rules (RimSort CC0 database): explicit About.xml wins when directly opposing
        var bottomTierMods = new List<ModInfo>();
        var communityConflictPairs = new List<(string IdA, string IdB)>();
        if (useCommunityRules)
        {
            var db = communityRules ?? CommunityRulesCache.Shared.LoadLocal();
            bool HasExplicitOpposite(string beforeId, string afterId)
            {
                var revKey = $"{afterId.ToLowerInvariant()}\0{beforeId.ToLowerInvariant()}";
                return edgeKinds.TryGetValue(revKey, out var rev) && rev.Kind < SortRuleKind.CommunityRule;
            }

            foreach (var mod in activeMods)
            {
                var lookupId = db.Rules.ContainsKey(mod.PackageId)
                    ? mod.PackageId
                    : StripIdSuffix(mod.PackageId);
                if (!db.Rules.TryGetValue(lookupId, out var cRule)) continue;

                if (cRule.LoadBottom)
                {
                    bottomTierMods.Add(mod);
                }

                foreach (var afterRaw in cRule.LoadAfter)
                {
                    var target = ResolveActiveTarget(mod, afterRaw, allowResolvedFallback: false);
                    if (target is not null && !HasExplicitOpposite(target, mod.PackageId))
                    {
                        AddEdge(target, mod.PackageId, SortRuleKind.CommunityRule);
                    }
                }

                foreach (var beforeRaw in cRule.LoadBefore)
                {
                    var target = ResolveActiveTarget(mod, beforeRaw, allowResolvedFallback: false);
                    if (target is not null && !HasExplicitOpposite(mod.PackageId, target))
                    {
                        AddEdge(mod.PackageId, target, SortRuleKind.CommunityRule);
                    }
                }

                foreach (var incRaw in cRule.IncompatibleWith)
                {
                    var target = ResolveActiveTarget(mod, incRaw, allowResolvedFallback: false);
                    if (target is not null && !string.Equals(target, mod.PackageId, StringComparison.OrdinalIgnoreCase))
                    {
                        communityConflictPairs.Add((mod.PackageId, target));
                    }
                }
            }
        }

        // 2. Plan B1: Implicit Core & Official DLC rules
        var preCoreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (modById.ContainsKey("brrainz.harmony")) preCoreSet.Add("brrainz.harmony");
        foreach (var mod in activeMods)
        {
            var explicitBeforeCore = mod.LoadBefore.Concat(mod.ForceLoadBefore)
                .Any(id => string.Equals(id, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
            if (explicitBeforeCore || KnownPreCoreIds.Contains(mod.PackageId))
            {
                preCoreSet.Add(mod.PackageId);
            }
        }
        foreach (var mod in activeMods)
        {
            if (!mod.IsTranslationMod || preCoreSet.Contains(mod.PackageId)) continue;
            var nonBaseTargets = mod.RequiredDependenciesForSort.Concat(mod.LoadAfter).Concat(mod.ForceLoadAfter)
                .Where(id => !string.Equals(id, mod.PackageId, StringComparison.OrdinalIgnoreCase) && !IsCommonBaseId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var loadsAfterCore = mod.LoadAfter.Concat(mod.ForceLoadAfter)
                .Any(id => string.Equals(id, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
            if (!loadsAfterCore && nonBaseTargets.Count > 0 && nonBaseTargets.All(id => preCoreSet.Contains(id)))
            {
                preCoreSet.Add(mod.PackageId);
            }
        }

        if (modById.ContainsKey("ludeon.rimworld"))
        {
            foreach (var preId in preCoreSet)
            {
                AddEdge(preId, "ludeon.rimworld", SortRuleKind.ImplicitCoreDlc);
            }

            foreach (var mod in activeMods)
            {
                if (string.Equals(mod.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase) ||
                    preCoreSet.Contains(mod.PackageId))
                {
                    continue;
                }
                AddEdge("ludeon.rimworld", mod.PackageId, SortRuleKind.ImplicitCoreDlc);
            }
        }

        var activeDlcs = OfficialDlcOrder.Where(modById.ContainsKey).ToList();
        for (var i = 0; i < activeDlcs.Count - 1; i++)
        {
            AddEdge(activeDlcs[i], activeDlcs[i + 1], SortRuleKind.ImplicitCoreDlc);
        }

        if (modById.ContainsKey("unlimitedhugs.hugslib"))
        {
            foreach (var dlcId in activeDlcs)
            {
                AddEdge(dlcId, "unlimitedhugs.hugslib", SortRuleKind.ImplicitCoreDlc);
            }
        }

        // 3. Plan B1: Implicit Translation -> Parent mod rules (for translation mods missing explicit dependency/loadAfter)
        var nonTransMods = activeMods.Where(m => !m.IsTranslationMod && !IsCommonBaseId(m.PackageId)).ToList();
        foreach (var transMod in activeMods)
        {
            if (!transMod.IsTranslationMod) continue;
            var hasExplicitNonBaseParent = edgeKinds.Values.Any(e =>
                string.Equals(e.AfterId, transMod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                !IsCommonBaseId(e.BeforeId));
            if (hasExplicitNonBaseParent) continue;

            var inferredParent = InferTranslationParent(transMod, nonTransMods);
            if (inferredParent is not null && !transMod.IsPrerequisiteIgnored(inferredParent.PackageId) &&
                !(transMod.IgnoredPrerequisites?.Any(id => string.Equals(
                    ResolveActiveTarget(transMod, id, allowResolvedFallback: true), inferredParent.PackageId,
                    StringComparison.OrdinalIgnoreCase)) ?? false))
            {
                AddEdge(inferredParent.PackageId, transMod.PackageId, SortRuleKind.ImplicitTranslation);
            }
        }

        // 4. Implicit RJW rule: any mod whose name, packageId, or folder name has standalone "rjw" loads after active RJW base
        var activeRjwBases = activeMods.Where(m => IsRjwBaseId(m.PackageId)).ToList();
        if (activeRjwBases.Count > 0)
        {
            foreach (var mod in activeMods)
            {
                if (IsRjwBaseId(mod.PackageId) || preCoreSet.Contains(mod.PackageId) || !HasStandaloneRjwToken(mod))
                {
                    continue;
                }

                foreach (var rjwBase in activeRjwBases)
                {
                    var revKey = $"{mod.PackageId.ToLowerInvariant()}\0{rjwBase.PackageId.ToLowerInvariant()}";
                    if (edgeKinds.TryGetValue(revKey, out var rev) && rev.Kind < SortRuleKind.ImplicitRjw)
                    {
                        continue;
                    }

                    AddEdge(rjwBase.PackageId, mod.PackageId, SortRuleKind.ImplicitRjw);
                }
            }
        }

        // Build raw rule records (before user cycle-resolution overrides)
        CollectedSortRule MakeRule((string BeforeId, string AfterId, SortRuleKind Kind) e)
        {
            var bIdx = indexById[e.BeforeId];
            var aIdx = indexById[e.AfterId];
            return new CollectedSortRule(
                e.BeforeId,
                TitleOf(modById[e.BeforeId]),
                bIdx,
                e.AfterId,
                TitleOf(modById[e.AfterId]),
                aIdx,
                e.Kind,
                KindText(e.Kind),
                bIdx > aIdx);
        }

        // Detect direct mutual cycle pairs (A <-> B) before applying user overrides so the top selector remains visible
        var cyclePairs = new List<RuleCyclePair>();
        var seenCyclePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in edgeKinds.Values.ToList())
        {
            var revKey = $"{e.AfterId.ToLowerInvariant()}\0{e.BeforeId.ToLowerInvariant()}";
            if (!edgeKinds.TryGetValue(revKey, out var rev)) continue;

            var firstId = indexById[e.BeforeId] <= indexById[e.AfterId] ? e.BeforeId : e.AfterId;
            var secondId = indexById[e.BeforeId] <= indexById[e.AfterId] ? e.AfterId : e.BeforeId;
            var pairKey = MakePairKey(firstId, secondId);
            if (!seenCyclePairs.Add(pairKey)) continue;

            var forward = string.Equals(e.BeforeId, firstId, StringComparison.OrdinalIgnoreCase) ? e : rev;
            var reverse = string.Equals(e.BeforeId, firstId, StringComparison.OrdinalIgnoreCase) ? rev : e;
            cyclePairs.Add(new RuleCyclePair(
                modById[firstId],
                modById[secondId],
                MakeRule(forward),
                MakeRule(reverse)));
        }

        // Apply user cycle-resolution choices: keep chosenFirstId -> otherId, drop otherId -> chosenFirstId
        if (cycleResolutions is not null && cycleResolutions.Count > 0)
        {
            foreach (var pair in cyclePairs)
            {
                var pairKey = MakePairKey(pair.FirstMod.PackageId, pair.SecondMod.PackageId);
                if (!cycleResolutions.TryGetValue(pairKey, out var chosenBeforeId)) continue;
                var otherId = string.Equals(chosenBeforeId, pair.FirstMod.PackageId, StringComparison.OrdinalIgnoreCase)
                    ? pair.SecondMod.PackageId
                    : pair.FirstMod.PackageId;
                var dropKey = $"{otherId.ToLowerInvariant()}\0{chosenBeforeId.ToLowerInvariant()}";
                edgeKinds.Remove(dropKey);
            }
        }

        var rules = edgeKinds.Values
            .Select(MakeRule)
            .OrderByDescending(r => r.IsViolated)
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.AfterIndex)
            .ThenBy(r => r.BeforeIndex)
            .ToList();

        var violated = rules.Where(r => r.IsViolated).ToList();

        // Detect remaining cycle edges (edges inside strongly connected components of size >= 2)
        var cycleNodeIds = FindCycleNodes(activeMods.Select(m => m.PackageId), rules);
        var cycleRules = rules
            .Where(r => cycleNodeIds.Contains(r.BeforeId) && cycleNodeIds.Contains(r.AfterId))
            .ToList();

        // Collect mutual incompatibility conflicts (About.xml + Community Rules)
        var rawConflicts = ModOrderRules.Conflicts(activeMods);
        var seenConflictPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<RuleConflictPair>();
        var aboutSource = T("About.xml", "About.xml 声明", "About.xml 聲明", "About.xml");
        var communitySource = T("Community Rule", "社区规则", "社區規則", "コミュニティルール");

        void AddConflictPair(string id1, string id2, string source)
        {
            if (!modById.TryGetValue(id1, out var m1) || !modById.TryGetValue(id2, out var m2)) return;
            var first = indexById[m1.PackageId] <= indexById[m2.PackageId] ? m1 : m2;
            var second = indexById[m1.PackageId] <= indexById[m2.PackageId] ? m2 : m1;
            if (seenConflictPairs.Add(MakePairKey(first.PackageId, second.PackageId)))
            {
                conflicts.Add(new RuleConflictPair(first, second, source));
            }
        }

        foreach (var (id, others) in rawConflicts)
        {
            foreach (var other in others)
            {
                AddConflictPair(id, other, aboutSource);
            }
        }

        foreach (var (idA, idB) in communityConflictPairs)
        {
            AddConflictPair(idA, idB, communitySource);
        }

        return new CollectedRulesResult(
            activeMods.Count,
            rules,
            violated,
            cycleRules,
            cyclePairs,
            conflicts,
            bottomTierMods,
            rules.Count(r => r.Kind == SortRuleKind.Dependency),
            rules.Count(r => r.Kind == SortRuleKind.LoadAfter),
            rules.Count(r => r.Kind == SortRuleKind.LoadBefore),
            rules.Count(r => r.Kind == SortRuleKind.CommunityRule),
            rules.Count(r => r.Kind == SortRuleKind.ImplicitCoreDlc),
            rules.Count(r => r.Kind == SortRuleKind.ImplicitTranslation),
            rules.Count(r => r.Kind == SortRuleKind.ImplicitRjw));
    }

    public static string MakePairKey(string id1, string id2)
    {
        var a = id1.ToLowerInvariant();
        var b = id2.ToLowerInvariant();
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}\0{b}" : $"{b}\0{a}";
    }

    private static ModInfo? InferTranslationParent(ModInfo transMod, IReadOnlyList<ModInfo> nonTransMods)
    {
        var transIdClean = StripIdSuffix(transMod.PackageId);
        transIdClean = TranslationIdPrefix.Replace(transIdClean, "");
        transIdClean = TranslationIdSuffix.Replace(transIdClean, "");

        if (transIdClean.Length >= 4 && !string.Equals(transIdClean, transMod.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            var exactIdMatch = nonTransMods.FirstOrDefault(m =>
                string.Equals(StripIdSuffix(m.PackageId), transIdClean, StringComparison.OrdinalIgnoreCase));
            if (exactIdMatch is not null) return exactIdMatch;

            var slug = ExtractModSlug(transIdClean);
            if (slug.Length >= 5 && !GenericModSlugs.Contains(slug))
            {
                var slugMatches = nonTransMods
                    .Where(m => string.Equals(ExtractModSlug(StripIdSuffix(m.PackageId)), slug, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (slugMatches.Count == 1) return slugMatches[0];
            }
        }

        var baseName = TranslationNameSuffix.Replace(NormalizeName(transMod.Name), "").Trim();
        if (baseName.Length >= 4 && !string.Equals(baseName, NormalizeName(transMod.Name), StringComparison.OrdinalIgnoreCase))
        {
            var nameMatches = nonTransMods
                .Where(m => string.Equals(NormalizeName(m.Name), baseName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (nameMatches.Count == 1) return nameMatches[0];
        }

        return null;
    }

    private static HashSet<string> FindCycleNodes(IEnumerable<string> nodeIds, IReadOnlyList<CollectedSortRule> rules)
    {
        var nodes = nodeIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var adj = nodes.ToDictionary(id => id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var inDegree = nodes.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules)
        {
            if (!adj.ContainsKey(r.BeforeId) || !inDegree.ContainsKey(r.AfterId)) continue;
            adj[r.BeforeId].Add(r.AfterId);
            inDegree[r.AfterId]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var v in adj[u])
            {
                inDegree[v]--;
                if (inDegree[v] == 0) queue.Enqueue(v);
            }
        }

        // Remaining nodes have inDegree > 0; filter out nodes merely downstream of a cycle by checking reverse reachability
        var remaining = new HashSet<string>(inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
        if (remaining.Count == 0) return remaining;

        var outDegree = remaining.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var revAdj = remaining.ToDictionary(id => id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var u in remaining)
        {
            foreach (var v in adj[u])
            {
                if (!remaining.Contains(v)) continue;
                outDegree[u]++;
                revAdj[v].Add(u);
            }
        }

        var revQueue = new Queue<string>(outDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (revQueue.Count > 0)
        {
            var v = revQueue.Dequeue();
            remaining.Remove(v);
            foreach (var u in revAdj[v])
            {
                outDegree[u]--;
                if (outDegree[u] == 0) revQueue.Enqueue(u);
            }
        }

        return remaining;
    }

    public static TopologicalSortPlan BuildTopologicalOrder(
        IReadOnlyList<string> activePackageIds,
        IReadOnlyList<ModInfo> activeMods,
        CollectedRulesResult rulesResult)
    {
        if (activeMods.Count == 0)
        {
            return new TopologicalSortPlan(
                activePackageIds.ToList(),
                Array.Empty<SortPreviewRow>(),
                Array.Empty<SortPreviewRow>(),
                Array.Empty<CollectedSortRule>(),
                0);
        }

        var origIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modById = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < activeMods.Count; i++)
        {
            origIndex[activeMods[i].PackageId] = i;
            modById[activeMods[i].PackageId] = activeMods[i];
        }

        var bottomSet = new HashSet<string>(
            rulesResult.BottomTierMods.Select(m => m.PackageId),
            StringComparer.OrdinalIgnoreCase);

        // Iteratively remove weakest cycle edges until the graph is a strict DAG
        var workingRules = rulesResult.Rules.ToList();
        var brokenEdges = new List<CollectedSortRule>();
        while (true)
        {
            var cycleNodes = FindCycleNodes(activeMods.Select(m => m.PackageId), workingRules);
            if (cycleNodes.Count == 0) break;

            var victim = workingRules
                .Where(r => cycleNodes.Contains(r.BeforeId) && cycleNodes.Contains(r.AfterId))
                .OrderByDescending(r => r.Kind)       // break weakest soft edge first (Implicit > Community > LoadBefore/After > Dependency)
                .ThenByDescending(r => r.IsViolated)  // among equal strength, break the edge against current user order
                .ThenByDescending(r => origIndex[r.BeforeId])
                .FirstOrDefault();
            if (victim is null) break;

            workingRules.Remove(victim);
            brokenEdges.Add(victim);
        }

        var adj = activeMods.ToDictionary(m => m.PackageId, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var inDegree = activeMods.ToDictionary(m => m.PackageId, _ => 0, StringComparer.OrdinalIgnoreCase);
        var transChildByParent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in workingRules)
        {
            if (!adj.ContainsKey(r.BeforeId) || !inDegree.ContainsKey(r.AfterId)) continue;
            adj[r.BeforeId].Add(r.AfterId);
            inDegree[r.AfterId]++;

            if (r.Kind == SortRuleKind.ImplicitTranslation ||
                (modById.TryGetValue(r.AfterId, out var afterMod) && afterMod.IsTranslationMod &&
                 r.Kind is SortRuleKind.Dependency or SortRuleKind.LoadAfter))
            {
                if (!transChildByParent.TryGetValue(r.BeforeId, out var list))
                {
                    transChildByParent[r.BeforeId] = list = new List<string>();
                }
                list.Add(r.AfterId);
            }
        }

        // Kahn's topological sort with stability:
        // Tier 0 = immediate ready translation of the mod just emitted (keeps translation glued right beneath parent)
        // Tier 1 = normal mod in original load order
        // Tier 2 = loadBottom mod in original load order
        var ready = new HashSet<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);
        var sortedMods = new List<ModInfo>(activeMods.Count);
        var immediateTranslations = new Queue<string>();

        while (ready.Count > 0)
        {
            string? picked = null;
            while (immediateTranslations.Count > 0)
            {
                var candidate = immediateTranslations.Dequeue();
                if (ready.Contains(candidate) && !bottomSet.Contains(candidate))
                {
                    picked = candidate;
                    break;
                }
            }

            picked ??= ready
                .OrderBy(id => bottomSet.Contains(id) ? 1 : 0)
                .ThenBy(id => origIndex[id])
                .First();

            ready.Remove(picked);
            sortedMods.Add(modById[picked]);

            foreach (var next in adj[picked])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                {
                    ready.Add(next);
                }
            }

            if (transChildByParent.TryGetValue(picked, out var transChildren))
            {
                foreach (var childId in transChildren.OrderBy(id => origIndex[id]))
                {
                    if (ready.Contains(childId))
                    {
                        immediateTranslations.Enqueue(childId);
                    }
                }
            }
        }

        var newIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<SortPreviewRow>(sortedMods.Count);
        for (var i = 0; i < sortedMods.Count; i++)
        {
            var m = sortedMods[i];
            newIndex[m.PackageId] = i;
            var oldIdx = origIndex[m.PackageId];
            rows.Add(new SortPreviewRow(m, oldIdx, i, oldIdx - i, bottomSet.Contains(m.PackageId)));
        }

        var movedRows = rows.Where(r => r.OldIndex != r.NewIndex).ToList();
        var fixedCount = workingRules.Count(r =>
            r.IsViolated &&
            newIndex.TryGetValue(r.BeforeId, out var bNew) &&
            newIndex.TryGetValue(r.AfterId, out var aNew) &&
            bNew < aNew);

        // Preserve any unresolved/missing package entries in activePackageIds at their original slots
        var sortedIdQueue = new Queue<string>(sortedMods.Select(m => m.PackageId));
        var resolvedSet = new HashSet<string>(modById.Keys, StringComparer.OrdinalIgnoreCase);
        var finalPackageIds = new List<string>(activePackageIds.Count);
        var seenResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawId in activePackageIds)
        {
            if (resolvedSet.Contains(rawId))
            {
                if (seenResolved.Add(rawId) && sortedIdQueue.Count > 0)
                {
                    finalPackageIds.Add(sortedIdQueue.Dequeue());
                }
            }
            else
            {
                finalPackageIds.Add(rawId);
            }
        }
        while (sortedIdQueue.Count > 0)
        {
            finalPackageIds.Add(sortedIdQueue.Dequeue());
        }

        return new TopologicalSortPlan(finalPackageIds, rows, movedRows, brokenEdges, fixedCount);
    }
}
