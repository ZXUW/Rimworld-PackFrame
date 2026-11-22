using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace PackFrame;

public sealed record CategoryFrameLayer(int Depth, bool IsTop, bool IsBottom);

public sealed record CategoryHeaderRow(
    string PathKey,
    string Title,
    int Depth,
    bool IsExpanded,
    int TotalModCount,
    bool IsUncategorized,
    IReadOnlyList<ModInfo> Mods
)
{
    public Thickness IndentMargin => new(6 + Depth * 16, 0, 6 + Depth * 4, 0);
    public IReadOnlyList<CategoryFrameLayer> CategoryFrames { get; init; } = Array.Empty<CategoryFrameLayer>();
}

public static class CategoryTree
{
    public const string UncategorizedKey = "__uncategorized__";

    public static string? NormalizePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split(new[] { '/', '\\', '>' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length == 0 ? null : string.Join("/", parts);
    }

    public static string[] SplitSegments(string? normalizedPath) =>
        string.IsNullOrWhiteSpace(normalizedPath)
            ? Array.Empty<string>()
            : normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public static IReadOnlyList<string> ExpandAllPaths(IEnumerable<string> rawCategories)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawCategories)
        {
            var segments = SplitSegments(NormalizePath(raw));
            for (var i = 0; i < segments.Length; i++)
            {
                all.Add(string.Join("/", segments.Take(i + 1)));
            }
        }
        return all.OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string ParentKeyOf(string? pathKey)
    {
        if (string.IsNullOrWhiteSpace(pathKey) || pathKey == UncategorizedKey) return "";
        var slash = pathKey.LastIndexOf('/');
        return slash < 0 ? "" : pathKey[..slash];
    }

    public static List<object> BuildRows(
        IReadOnlyList<ModInfo> mods,
        ISet<string> collapsedPaths,
        bool autoExpandAll,
        string uncategorizedTitle,
        IReadOnlyList<string>? categoryOrder = null,
        IReadOnlyList<string>? modOrder = null)
    {
        var catRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (categoryOrder is not null)
        {
            for (var i = 0; i < categoryOrder.Count; i++)
                catRank.TryAdd(categoryOrder[i], i);
        }

        var modRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (modOrder is not null)
        {
            for (var i = 0; i < modOrder.Count; i++)
                modRank.TryAdd(modOrder[i], i);
        }

        IReadOnlyList<ModInfo> OrderMods(IEnumerable<ModInfo> source) =>
            source
                .OrderBy(m => modRank.TryGetValue(m.PackageId, out var r) ? r : int.MaxValue)
                .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        IReadOnlyList<Node> OrderNodes(IEnumerable<Node> source) =>
            source
                .OrderBy(n => catRank.TryGetValue(n.PathKey, out var r) ? r : int.MaxValue)
                .ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var root = new Node("", "", 0);
        var uncategorized = new List<ModInfo>();

        foreach (var mod in mods)
        {
            var segments = SplitSegments(NormalizePath(mod.Category));
            if (segments.Length == 0)
            {
                uncategorized.Add(mod);
                continue;
            }

            var current = root;
            var currentPath = "";
            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                currentPath = i == 0 ? seg : currentPath + "/" + seg;
                if (!current.Children.TryGetValue(seg, out var child))
                {
                    child = new Node(currentPath, seg, i);
                    current.Children[seg] = child;
                }
                current = child;
            }
            current.DirectMods.Add(mod);
        }

        var rows = new List<object>(mods.Count + 16);
        foreach (var top in OrderNodes(root.Children.Values))
        {
            EmitNode(top, rows, collapsedPaths, autoExpandAll, OrderNodes, OrderMods);
        }

        if (uncategorized.Count > 0)
        {
            var orderedUncat = OrderMods(uncategorized);
            var expanded = autoExpandAll || !collapsedPaths.Contains(UncategorizedKey);
            rows.Add(new CategoryHeaderRow(
                UncategorizedKey,
                uncategorizedTitle,
                0,
                expanded,
                orderedUncat.Count,
                true,
                orderedUncat));
            if (expanded)
            {
                foreach (var mod in orderedUncat)
                {
                    rows.Add(mod with { TreeIndent = new Thickness(24, 0, 8, 0), ShowCategoryBadge = false });
                }
            }
        }

        return AddFrames(rows);
    }

    private static List<object> AddFrames(List<object> rows)
    {
        var ends = new int[rows.Count];
        var folders = new List<(int Start, int Depth)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var depth = rows[i] is CategoryHeaderRow header
                ? header.Depth
                : Math.Max(1, SplitSegments(NormalizePath(((ModInfo)rows[i]).Category)).Length);
            // A parent mod after its subfolders also closes their frames.
            while (folders.Count > 0 && folders[^1].Depth >= depth)
            {
                ends[folders[^1].Start] = i - 1;
                folders.RemoveAt(folders.Count - 1);
            }
            if (rows[i] is CategoryHeaderRow folder)
            {
                ends[i] = i;
                if (folder.IsExpanded) folders.Add((i, folder.Depth));
            }
        }
        foreach (var folder in folders) ends[folder.Start] = rows.Count - 1;
        folders.Clear();

        for (var i = 0; i < rows.Count; i++)
        {
            while (folders.Count > 0 && ends[folders[^1].Start] < i) folders.RemoveAt(folders.Count - 1);
            if (rows[i] is CategoryHeaderRow folder) folders.Add((i, folder.Depth));
            var layers = folders.Select(folder => new CategoryFrameLayer(folder.Depth, i == folder.Start, i == ends[folder.Start])).ToArray();
            rows[i] = rows[i] switch
            {
                CategoryHeaderRow header => header with { CategoryFrames = layers },
                ModInfo mod => mod with { CategoryFrames = layers },
                _ => rows[i]
            };
        }
        return rows;
    }

    private static void EmitNode(
        Node node,
        List<object> rows,
        ISet<string> collapsedPaths,
        bool autoExpandAll,
        Func<IEnumerable<Node>, IReadOnlyList<Node>> orderNodes,
        Func<IEnumerable<ModInfo>, IReadOnlyList<ModInfo>> orderMods)
    {
        var allMods = CollectSubtreeMods(node, orderNodes, orderMods);
        var expanded = autoExpandAll || !collapsedPaths.Contains(node.PathKey);
        rows.Add(new CategoryHeaderRow(
            node.PathKey,
            node.Title,
            node.Depth,
            expanded,
            allMods.Count,
            false,
            allMods));
        if (!expanded) return;

        foreach (var child in orderNodes(node.Children.Values))
        {
            EmitNode(child, rows, collapsedPaths, autoExpandAll, orderNodes, orderMods);
        }

        var modIndent = new Thickness((node.Depth + 1) * 16 + 10, 0, 8 + node.Depth * 4, 0);
        foreach (var mod in orderMods(node.DirectMods))
        {
            rows.Add(mod with { TreeIndent = modIndent, ShowCategoryBadge = false });
        }
    }

    private static List<ModInfo> CollectSubtreeMods(
        Node node,
        Func<IEnumerable<Node>, IReadOnlyList<Node>> orderNodes,
        Func<IEnumerable<ModInfo>, IReadOnlyList<ModInfo>> orderMods)
    {
        var list = new List<ModInfo>();
        void Walk(Node n)
        {
            foreach (var child in orderNodes(n.Children.Values))
            {
                Walk(child);
            }
            list.AddRange(orderMods(n.DirectMods));
        }
        Walk(node);
        return list;
    }

    private sealed class Node(string pathKey, string title, int depth)
    {
        public string PathKey { get; } = pathKey;
        public string Title { get; } = title;
        public int Depth { get; } = depth;
        public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ModInfo> DirectMods { get; } = new();
    }
}
