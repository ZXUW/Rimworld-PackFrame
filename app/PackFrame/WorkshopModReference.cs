using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PackFrame;

// A requested Workshop item can be known before its PackageId is available on disk.
public sealed record WorkshopModReference(ulong WorkshopId, string? PackageId = null, string? Name = null);

internal static class WorkshopModReferences
{
    internal static List<WorkshopModReference> ForTargets(IEnumerable<ulong> targets,
        IReadOnlyDictionary<ulong, WorkshopModReference> declared,
        IReadOnlyDictionary<ulong, string> resolvedPackageIds)
    {
        var result = new List<WorkshopModReference>();
        foreach (var id in targets.Where(id => id != 0).Distinct())
        {
            declared.TryGetValue(id, out var reference);
            resolvedPackageIds.TryGetValue(id, out var resolved);
            result.Add(new WorkshopModReference(id,
                NormalizePackage(reference?.PackageId) ?? NormalizePackage(resolved), NormalizeText(reference?.Name)));
        }
        return result;
    }

    internal static List<WorkshopModReference> Normalize(IEnumerable<WorkshopModReference>? references)
    {
        var result = new Dictionary<ulong, WorkshopModReference>();
        foreach (var reference in references ?? Array.Empty<WorkshopModReference>())
        {
            if (reference is null || reference.WorkshopId == 0) continue;
            result.TryGetValue(reference.WorkshopId, out var previous);
            result[reference.WorkshopId] = new WorkshopModReference(reference.WorkshopId,
                NormalizePackage(reference.PackageId) ?? previous?.PackageId,
                NormalizeText(reference.Name) ?? previous?.Name);
        }
        return result.Values.ToList();
    }

    // Old instances can still be monitored when their saved labels contain an
    // explicit Steam item link. Never infer Workshop origin from a missing ID.
    internal static List<WorkshopModReference> ForInstance(ManagedInstance instance)
    {
        var result = Normalize(instance.Metadata?.WorkshopMods);
        var active = new HashSet<string>(instance.ActivePackageIds, StringComparer.OrdinalIgnoreCase);
        var legacy = ModFullNameList.LoadHistory(instance)
            .Select(entry => (entry.PackageId, entry.Label, entry.Link))
            .Concat(ModFullNameList.Load(instance).Values.Select(entry => (entry.PackageId, entry.Label, entry.Link)));
        foreach (var entry in legacy)
        {
            if (!active.Contains(entry.PackageId)) continue;
            var id = ParseItemLink(entry.Link);
            if (id is null) continue;
            var existing = result.FindIndex(reference => reference.WorkshopId == id.Value);
            var reference = existing >= 0 ? result[existing] : new WorkshopModReference(id.Value);
            var updated = reference with
            {
                PackageId = reference.PackageId ?? NormalizePackage(entry.PackageId),
                Name = reference.Name ?? NormalizeText(entry.Label)
            };
            if (existing >= 0) result[existing] = updated;
            else result.Add(updated);
        }
        return result;
    }

    internal static bool ResolveInstalled(ManagedInstance instance, IReadOnlyList<ModInfo> catalog)
    {
        if (instance.IsScratch || instance.IsOfficial || instance.IsDamaged) return false;
        var references = Normalize(instance.Metadata?.WorkshopMods);
        if (references.All(reference => reference.PackageId is not null)) return false;
        var installed = catalog
            .Where(mod => mod.Source == ModSource.Workshop && !string.IsNullOrWhiteSpace(mod.PackageId))
            .Select(mod => (Id: ItemId(mod), Mod: mod))
            .Where(item => item.Id is not null)
            .GroupBy(item => item.Id!.Value)
            .Where(group => group.Select(item => NormalizePackage(item.Mod.PackageId))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Mod);
        var changed = false;
        for (var index = 0; index < references.Count; index++)
        {
            var reference = references[index];
            if (reference.PackageId is not null || !installed.TryGetValue(reference.WorkshopId, out var mod)) continue;
            references[index] = reference with
            {
                PackageId = NormalizePackage(mod.PackageId),
                Name = reference.Name ?? NormalizeText(mod.Name)
            };
            changed = true;
        }
        if (!changed) return false;
        return InstanceStore.ResolveWorkshopReferences(instance, references);
    }

    private static ulong? ItemId(ModInfo mod)
    {
        var leaf = Path.GetFileName(mod.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return ulong.TryParse(leaf, out var id) && id != 0 ? id : ParseItemLink(mod.Link);
    }

    internal static bool HasNonWorkshopSource(string? link)
    {
        var text = NormalizeText(link);
        if (text is null || text == "—") return false;
        if (IsAbsoluteLocalPath(text)) return true;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.IsFile)
            return text.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && IsAbsoluteLocalPath(uri.LocalPath);
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        return !new[] { "steamcommunity.com", "steampowered.com", "steamstatic.com", "steamcontent.com" }
            .Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAbsoluteLocalPath(string text) =>
        Path.IsPathFullyQualified(text) || text.StartsWith('/') ||
        (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/') ||
        (text.StartsWith("\\\\", StringComparison.Ordinal) &&
         text[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 2);

    private static ulong? ParseItemLink(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return null;
        if ((uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            (uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("www.steamcommunity.com", StringComparison.OrdinalIgnoreCase)) &&
            uri.AbsolutePath.TrimEnd('/').Equals("/sharedfiles/filedetails", StringComparison.OrdinalIgnoreCase))
        {
            var id = ModpackService.ParseWorkshopId(link);
            return id is > 0 ? id : null;
        }
        if (uri.Scheme.Equals("steam", StringComparison.OrdinalIgnoreCase) &&
            uri.Host.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length == 2 && parts[0].Equals("CommunityFilePage", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(parts[1], out var id) && id != 0) return id;
        }
        return null;
    }

    private static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizePackage(string? value) => NormalizeText(value)?.ToLowerInvariant();
}
