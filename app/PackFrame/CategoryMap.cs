using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace PackFrame;

internal sealed class CategoryMapNode
{
    public CategoryMapNode(string path, string leaf, CategoryMapNode? parent)
    {
        Path = path;
        Leaf = leaf;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    public string Path { get; }
    public string Leaf { get; }
    public CategoryMapNode? Parent { get; }
    public int Depth { get; }
    public List<CategoryMapNode> Children { get; } = new();
    public int Count { get; internal set; }

    /// <summary>Mods assigned exactly to this category.</summary>
    public List<string> Mods { get; } = new();

    /// <summary>Mods in this category and everything below it.</summary>
    public int TotalMods { get; internal set; }

    public bool IsLeaf => Children.Count == 0;

    // Big levels become a drum. Expanding one of its children turns it back
    // into a normal tree so the subtree has somewhere to grow.
    public bool CanBand => Children.Count > CategoryMap.BandThreshold;
}

internal sealed record CategoryMapPlacement(
    CategoryMapNode Node,
    Rect Bounds,
    int Depth,
    double Scale,
    bool BandItem,
    CategoryMapNode? BandOwner,
    double Opacity = 1);

internal sealed record CategoryMapLink(Point From, Point To, bool Band, double Opacity = 1);

internal sealed record CategoryMapZone(CategoryMapNode Owner, Rect Bounds);

/// <summary>Fading bar left behind by an open folder that scrolled off the front of a drum.</summary>
internal sealed record CategoryMapGhost(Rect Bounds, double StartOpacity);

internal sealed record CategoryMapLayoutResult(
    IReadOnlyList<CategoryMapPlacement> Placements,
    IReadOnlyList<CategoryMapLink> Links,
    IReadOnlyList<CategoryMapZone> Zones,
    IReadOnlyList<CategoryMapGhost> Ghosts,
    Rect Bounds)
{
    public static CategoryMapLayoutResult Empty { get; } = new(
        Array.Empty<CategoryMapPlacement>(),
        Array.Empty<CategoryMapLink>(),
        Array.Empty<CategoryMapZone>(),
        Array.Empty<CategoryMapGhost>(),
        default);
}

internal static class CategoryMap
{
    public const int BandThreshold = 8;
    public const int BandVisible = 9;
    public const double NodeHeight = 24;
    public const double RowGap = 4;
    public const double ColumnGap = 34;

    /// <summary>Longer lead for the drum item that is open at the front.</summary>
    public const double BandOpenGap = 64;

    /// <summary>Lead between a drum owner and its column of items.</summary>
    public const double DrumGap = 60;
    // Horizontal-axis drum: items wrap around a roller seen from the front,
    // so they scroll vertically, flatten in width (cos) and fade (cos^2).
    public const double DrumRadius = 112;
    public const double DrumStep = Math.PI / 12;       // 15 degrees between items
    public const double DrumHalfSpan = DrumStep * (BandVisible - 1) / 2.0;
    public static readonly double BandHeight =
        2 * DrumRadius * Math.Sin(DrumHalfSpan) + NodeHeight;

    // Breathing room above and below a drum so it never touches neighbouring rows.
    public const double BandPadding = 20;

    // Root 2x, first-level groups halfway, deeper levels at base size.
    public static double NodeScale(int depth) => depth switch
    {
        0 => 2.0,
        1 => 1.5,
        _ => 1.0
    };

    public static double NodeHeightFor(int depth) => NodeHeight * NodeScale(depth);

    /// <summary>True when a level is big enough for a drum.</summary>
    public static bool UsesBand(CategoryMapNode node) => node.CanBand;

    public static CategoryMapNode Build(IReadOnlyList<string> paths, IReadOnlyList<ModInfo>? mods = null)
    {
        var root = new CategoryMapNode(string.Empty, string.Empty, null);
        var index = new Dictionary<string, CategoryMapNode>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = root
        };

        foreach (var raw in paths)
        {
            var normalized = CategoryTree.NormalizePath(raw);
            if (string.IsNullOrWhiteSpace(normalized)) continue;

            var node = Wire(normalized, root, index);
            for (var current = node; current is not null; current = current.Parent)
            {
                current.Count++;
            }
        }

        if (mods is not null)
        {
            foreach (var mod in mods)
            {
                var normalized = CategoryTree.NormalizePath(mod.Category);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                var node = Wire(normalized, root, index);
                node.Mods.Add(string.IsNullOrWhiteSpace(mod.Name) ? mod.PackageId : mod.Name);
                for (var current = node; current is not null; current = current.Parent)
                {
                    current.TotalMods++;
                }
            }
        }

        SortRecursive(root);
        return root;
    }

    private static CategoryMapNode Wire(
        string normalized,
        CategoryMapNode root,
        Dictionary<string, CategoryMapNode> index)
    {
        var node = root;
        var accumulated = string.Empty;
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = accumulated.Length == 0 ? segment : accumulated + "/" + segment;
            if (!index.TryGetValue(accumulated, out var child))
            {
                child = new CategoryMapNode(accumulated, segment, node);
                index[accumulated] = child;
                node.Children.Add(child);
            }

            node = child;
        }

        return node;
    }

    /// <summary>Mods of a node itself, then of its subfolders in tree order.</summary>
    public static IEnumerable<(string Path, string Leaf, IReadOnlyList<string> Mods, bool Direct)> CollectMods(
        CategoryMapNode node)
    {
        if (node.Mods.Count > 0) yield return (node.Path, node.Leaf, node.Mods, true);

        foreach (var child in node.Children)
        {
            foreach (var entry in CollectMods(child))
            {
                yield return (entry.Path, entry.Leaf, entry.Mods, false);
            }
        }
    }

    private static void SortRecursive(CategoryMapNode node)
    {
        node.Children.Sort((left, right) => string.Compare(
            left.Leaf,
            right.Leaf,
            StringComparison.CurrentCultureIgnoreCase));
        foreach (var child in node.Children) SortRecursive(child);
    }

    public static CategoryMapLayoutResult Layout(
        CategoryMapNode root,
        IReadOnlySet<string> expanded,
        Func<CategoryMapNode, double> width,
        IReadOnlyDictionary<string, double> bandRotations)
    {
        var placements = new List<CategoryMapPlacement>();
        var links = new List<CategoryMapLink>();
        var zones = new List<CategoryMapZone>();
        var ghosts = new List<CategoryMapGhost>();
        var heights = new Dictionary<CategoryMapNode, double>();

        double MeasureHeight(CategoryMapNode node)
        {
            if (heights.TryGetValue(node, out var cached)) return cached;
            double height;
            if (node.IsLeaf || !expanded.Contains(node.Path))
            {
                height = NodeHeightFor(node.Depth);
            }
            else if (UsesBand(node))
            {
                height = BandHeight + 2 * BandPadding;
            }
            else
            {
                var total = 0d;
                foreach (var child in node.Children)
                {
                    total += MeasureHeight(child) + RowGap;
                }
                height = Math.Max(NodeHeightFor(node.Depth), total - RowGap);
            }

            heights[node] = height;
            return height;
        }

        Rect AddBox(CategoryMapNode node, double x, double centerY, int depth)
        {
            var nodeWidth = width(node);
            var nodeHeight = NodeHeightFor(node.Depth);
            var rect = new Rect(x, centerY - nodeHeight / 2, nodeWidth, nodeHeight);
            placements.Add(new CategoryMapPlacement(
                node,
                rect,
                depth,
                NodeScale(node.Depth),
                false,
                null));
            return rect;
        }

        void Place(CategoryMapNode node, double x, double centerY, int depth)
        {
            var rect = AddBox(node, x, centerY, depth);
            if (node.IsLeaf || !expanded.Contains(node.Path)) return;

            if (UsesBand(node))
            {
                PlaceBand(node, rect, centerY, depth);
                return;
            }

            PlaceChildren(node, rect, centerY, depth);
        }

        void PlaceChildren(
            CategoryMapNode node,
            Rect rect,
            double centerY,
            int depth,
            double gap = ColumnGap)
        {
            var total = 0d;
            foreach (var child in node.Children) total += MeasureHeight(child) + RowGap;
            total -= RowGap;
            var y = centerY - total / 2;
            foreach (var child in node.Children)
            {
                var childHeight = MeasureHeight(child);
                var childCenter = y + childHeight / 2;
                Place(child, rect.Right + gap, childCenter, depth + 1);
                links.Add(new CategoryMapLink(
                    new Point(rect.Right, centerY),
                    new Point(rect.Right + gap, childCenter),
                    false));
                y += childHeight + RowGap;
            }
        }

        void PlaceBand(
            CategoryMapNode node,
            Rect ownerRect,
            double centerY,
            int depth,
            double gap = DrumGap)
        {
            var rotation = bandRotations.TryGetValue(node.Path, out var value) ? value : 0;
            var limit = (BandVisible - 1) / 2.0 + 1;
            var columnX = ownerRect.Right + gap;
            var drumWidth = Math.Min(node.Children.Max(width), 210);
            CategoryMapPlacement? front = null;
            var frontDistance = double.MaxValue;

            for (var index = 0; index < node.Children.Count; index++)
            {
                var offset = index - rotation;
                if (Math.Abs(offset) > limit) continue;
                var angle = offset * DrumStep;
                if (Math.Abs(angle) >= Math.PI / 2) continue;   // turned behind the drum

                var child = node.Children[index];
                var cos = Math.Cos(angle);
                // Same box shape as a normal node: only width narrows on the drum.
                var childWidth = Math.Max(6, drumWidth * (0.5 + 0.5 * cos));
                var childHeight = NodeHeightFor(child.Depth);
                var childX = columnX + (drumWidth - childWidth) / 2;
                var childY = centerY + DrumRadius * Math.Sin(angle);
                var bounds = new Rect(childX, childY - childHeight / 2, childWidth, childHeight);
                var placement = new CategoryMapPlacement(
                    child,
                    bounds,
                    depth + 1,
                    1,
                    true,
                    node,
                    Math.Max(0.08, cos * cos));
                placements.Add(placement);

                var distance = Math.Abs(offset);
                if (distance < frontDistance)
                {
                    frontDistance = distance;
                    front = placement;
                }

                if (child.IsLeaf || !expanded.Contains(child.Path)) continue;
                if (Math.Abs(offset) < 0.5)
                {
                    // Only the item facing the viewer opens; it moves with the drum.
                    // A long lead keeps the open subtree clear of the drum itself.
                    if (UsesBand(child))
                    {
                        PlaceBand(child, bounds, bounds.Center.Y, depth + 1, BandOpenGap);
                    }
                    else
                    {
                        PlaceChildren(child, bounds, bounds.Center.Y, depth + 1, BandOpenGap);
                    }
                }
                else
                {
                    // Everything else collapses into one bar that starts at the
                    // item's own opacity and fades to nothing towards the right.
                    // It also shortens gradually the further it scrolls away.
                    var fade = Math.Clamp((Math.Abs(offset) - 0.5) / 3.0, 0, 1);
                    var barWidth = 9 + (1 - fade) * 26;
                    var barBounds = new Rect(
                        bounds.Right + 10,
                        bounds.Center.Y - bounds.Height / 2,
                        barWidth,
                        Math.Max(4, bounds.Height));
                    ghosts.Add(new CategoryMapGhost(barBounds, Math.Max(0.03, cos * cos * 0.55)));
                    links.Add(new CategoryMapLink(
                        new Point(bounds.Right, bounds.Center.Y),
                        new Point(barBounds.X, barBounds.Center.Y),
                        true,
                        Math.Max(0.05, cos * cos)));
                }
            }

            // One connector to the item facing the viewer; a line per item reads as a comb.
            if (front is not null)
            {
                links.Add(new CategoryMapLink(
                    new Point(ownerRect.Right, centerY),
                    new Point(front.Bounds.X, front.Bounds.Center.Y),
                    true));
            }

            // The whole drum column swallows the wheel, gaps and padding included.
            var reach = DrumRadius * Math.Sin(DrumHalfSpan) + BandPadding;
            zones.Add(new CategoryMapZone(
                node,
                new Rect(
                    columnX - 8,
                    centerY - reach - 12,
                    drumWidth + 16,
                    2 * reach + 24)));
        }

        Place(root, 0, NodeHeightFor(0) / 2, 0);

        var bounds = default(Rect);
        foreach (var placement in placements)
        {
            bounds = bounds == default ? placement.Bounds : bounds.Union(placement.Bounds);
        }
        foreach (var ghost in ghosts)
        {
            bounds = bounds == default ? ghost.Bounds : bounds.Union(ghost.Bounds);
        }

        return new CategoryMapLayoutResult(placements, links, zones, ghosts, bounds);
    }

    public static IReadOnlyList<CategoryMapNode> Flatten(CategoryMapNode root)
    {
        var result = new List<CategoryMapNode>();
        void Walk(CategoryMapNode node)
        {
            if (node.Path.Length > 0) result.Add(node);
            foreach (var child in node.Children) Walk(child);
        }
        Walk(root);
        return result;
    }

    // Which shelf items a band shows for a given scroll center.
    public static (int Start, int Count, int Before, int After) BandWindow(
        CategoryMapNode node,
        double rotation)
    {
        var half = (BandVisible - 1) / 2;
        var first = Math.Max(0, (int)Math.Round(rotation) - half);
        var last = Math.Min(node.Children.Count - 1, (int)Math.Round(rotation) + half);
        if (last < first) return (0, 0, 0, node.Children.Count);
        return (first, last - first + 1, first, node.Children.Count - 1 - last);
    }
}
