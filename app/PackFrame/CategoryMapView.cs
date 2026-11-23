using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace PackFrame;

/// <summary>
/// Zoomable category map: tidy tree on the left-to-right axis, with a rotating
/// shelf for levels made only of leaves. Self-rendered so thousands of nodes
/// stay cheap; only the visible part is drawn.
/// </summary>
internal sealed class CategoryMapView : Control
{
    private const double BaseFontSize = 13;
    private const double MinScale = 0.25;
    private const double MaxScale = 2.5;

    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly IBrush NodeFill = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
    private static readonly IBrush NodeFillHover = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255));
    private static readonly IBrush NodeFillSelected = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
    private static readonly IPen NodeBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
    private static readonly IPen LinkPen = new Pen(new SolidColorBrush(Color.FromArgb(76, 255, 255, 255)), 1);
    private static readonly IPen SelectedPen = new Pen(ThemeService.PrimaryTextBrush, 2);
    private static readonly IPen MatchPen = new Pen(ThemeService.PrimaryTextBrush, 1.4);
    private static readonly Dictionary<(string Text, double Size), FormattedText> TextCache = new();
    private static readonly Dictionary<(string Text, double Size), FormattedText> MutedCache = new();
    private static readonly Dictionary<(string Text, double Size, int Width), FormattedText> FitCache = new();

    private sealed record BandChevron(Rect Rect, CategoryMapNode Owner, int Delta);

    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    // When a drum's centre is collapsed, the neighbour opened last takes over;
    // stamps keep that order.
    private readonly Dictionary<string, long> _expandOrder = new(StringComparer.OrdinalIgnoreCase);
    private long _expandSeq;
    private readonly Dictionary<string, double> _bandRotations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _bandTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BandChevron> _chevrons = new();
    private readonly DispatcherTimer _spin;

    private CategoryMapNode? _root;
    private CategoryMapLayoutResult _layout = CategoryMapLayoutResult.Empty;
    private bool _layoutDirty = true;
    private HashSet<string>? _matches;
    private CategoryMapNode? _firstMatch;
    private string? _selectedPath;
    private string? _hoverPath;
    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private Point _pressPoint;
    private Point _pressOffset;
    private bool _pressed;
    private bool _dragging;

    public CategoryMapView()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        _spin = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _spin.Tick += (_, _) => SpinStep();
    }

    public string RootLabel { get; set; } = "All";

    public string? SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (string.Equals(_selectedPath, value, StringComparison.Ordinal)) return;
            _selectedPath = value;
            InvalidateLayout();
            InvalidateVisual();
        }
    }

    public event EventHandler? SelectionChanged;

    public void SetRoot(CategoryMapNode root)
    {
        _root = root;
        _expanded.Clear();
        _expandOrder.Clear();
        _expandSeq = 0;
        MarkExpanded(string.Empty);
        foreach (var child in root.Children) MarkExpanded(child.Path);
        _bandRotations.Clear();
        _bandTargets.Clear();
        _spin.Stop();
        InvalidateLayout();
        InvalidateVisual();
    }

    public void SetQuery(string? query)
    {
        if (_root is null) return;
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            _matches = null;
            _firstMatch = null;
            InvalidateVisual();
            return;
        }

        var matches = CategoryMap.Flatten(_root)
            .Where(node => node.Path.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            _matches = null;
            _firstMatch = null;
            InvalidateVisual();
            return;
        }

        _firstMatch = matches[0];
        _matches = new HashSet<string>(
            matches.Take(400).Select(node => node.Path),
            StringComparer.OrdinalIgnoreCase);
        // Reveal the first matches so the dimmed map actually shows them.
        foreach (var match in matches.Take(25))
        {
            for (var parent = match.Parent; parent is not null; parent = parent.Parent)
            {
                MarkExpanded(parent.Path);
            }

            // A match hidden on a drum has to be rotated into view.
            if (match.Parent is { } owner && CategoryMap.UsesBand(owner))
            {
                var index = owner.Children.IndexOf(match);
                if (index >= 0)
                {
                    _bandRotations[owner.Path] = index;
                    _bandTargets[owner.Path] = index;
                }
            }
        }

        InvalidateLayout();
        InvalidateVisual();
        CenterOn(matches[0]);
    }

    public void CollapseAll()
    {
        _expanded.Clear();
        _expandOrder.Clear();
        MarkExpanded(string.Empty);
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>True when no folder below the root is open.</summary>
    public bool AllCollapsed
    {
        get
        {
            if (_root is null) return true;
            return CategoryMap.Flatten(_root)
                .Where(node => !node.IsLeaf)
                .All(node => !_expanded.Contains(node.Path));
        }
    }

    /// <summary>Collapses everything, or expands everything when already collapsed.</summary>
    public void ToggleCollapse()
    {
        if (_root is null) return;
        if (AllCollapsed)
        {
            foreach (var node in CategoryMap.Flatten(_root))
            {
                if (!node.IsLeaf) MarkExpanded(node.Path);
            }
        }
        else
        {
            _expanded.Clear();
            _expandOrder.Clear();
            MarkExpanded(string.Empty);
        }

        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>Pans by a delta given in this view's coordinates.</summary>
    public void PanBy(double dx, double dy)
    {
        if (_root is null) return;
        _offsetX += dx;
        _offsetY += dy;
        InvalidateVisual();
    }

    public void ExpandTo(int depth)
    {
        if (_root is null) return;
        void Walk(CategoryMapNode node, int level)
        {
            if (level > depth || node.IsLeaf) return;
            MarkExpanded(node.Path);
            foreach (var child in node.Children) Walk(child, level + 1);
        }
        Walk(_root, 0);
        InvalidateLayout();
        InvalidateVisual();
    }

    public void FitToWindow()
    {
        EnsureLayout();
        var bounds = _layout.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = Math.Min(
            (Bounds.Width - 48) / bounds.Width,
            (Bounds.Height - 48) / bounds.Height);
        _scale = Math.Clamp(scale, MinScale, 1.15);
        _offsetX = (Bounds.Width - bounds.Width * _scale) / 2 - bounds.X * _scale;
        _offsetY = (Bounds.Height - bounds.Height * _scale) / 2 - bounds.Y * _scale;
        InvalidateVisual();
    }

    /// <summary>Brings the picked node (or the first search match) into view.</summary>
    public void Locate()
    {
        if (_root is null) return;
        var nodes = CategoryMap.Flatten(_root);
        CategoryMapNode? target = null;
        if (!string.IsNullOrWhiteSpace(_selectedPath))
        {
            target = nodes.FirstOrDefault(node =>
                         string.Equals(node.Path, _selectedPath, StringComparison.OrdinalIgnoreCase))
                     ?? nodes.FirstOrDefault(node => node.Path.StartsWith(
                         _selectedPath + "/", StringComparison.OrdinalIgnoreCase));
        }

        target ??= _firstMatch;
        if (target is null)
        {
            FitToWindow();
            return;
        }

        for (var parent = target.Parent; parent is not null; parent = parent.Parent)
        {
            MarkExpanded(parent.Path);
        }

        if (target.Parent is { } bandOwner && CategoryMap.UsesBand(bandOwner))
        {
            var index = bandOwner.Children.IndexOf(target);
            if (index >= 0)
            {
                _bandRotations[bandOwner.Path] = index;
                _bandTargets[bandOwner.Path] = index;
                _spin.Stop();
            }
        }

        InvalidateLayout();
        EnsureLayout();
        CenterOn(target);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_root is null || e.Delta.Y == 0) return;
        var point = e.GetPosition(this);
        if (TryRotateAt(point, e.Delta.Y, e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            e.Handled = true;
            return;
        }

        ZoomAt(point, e.Delta.Y);
        e.Handled = true;
    }

    /// <summary>
    /// Rotates the drum under the point. Returns false when the point is not on a drum.
    /// </summary>
    public bool TryRotateAt(Point point, double delta, bool forceZoom = false)
    {
        if (_root is null || delta == 0 || forceZoom) return false;
        var band = HitBand(ToLogical(point));
        if (band is null) return false;
        Rotate(band, delta > 0 ? -1 : 1);
        return true;
    }

    /// <summary>Zooms around a point given in this view's coordinates.</summary>
    public void ZoomAt(Point point, double delta)
    {
        if (_root is null || delta == 0) return;
        var logical = ToLogical(point);
        var factor = delta > 0 ? 1.12 : 1 / 1.12;
        var next = Math.Clamp(_scale * factor, MinScale, MaxScale);
        if (Math.Abs(next - _scale) < 0.0001) return;
        _offsetX = point.X - logical.X * next;
        _offsetY = point.Y - logical.Y * next;
        _scale = next;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_root is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        var logical = ToLogical(point);

        var chevron = _chevrons.FirstOrDefault(item => item.Rect.Contains(logical));
        if (chevron is not null)
        {
            Rotate(chevron.Owner, chevron.Delta);
            e.Handled = true;
            return;
        }

        var hit = HitPlacement(logical);
        _pressPoint = point;
        _pressOffset = new Point(_offsetX, _offsetY);
        _pressed = true;
        _dragging = false;
        e.Pointer.Capture(this);

        // The +/- handle opens or closes a folder on a single click.
        if (hit is not null && !hit.Node.IsLeaf && IsOnHandle(hit, logical))
        {
            ToggleExpanded(hit.Node);
            _pressed = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        _pressedHit = hit;
        e.Handled = true;
    }

    private static bool IsOnHandle(CategoryMapPlacement placement, Point point)
    {
        var offset = 13 * placement.Scale;
        var dx = point.X - (placement.Bounds.Right - offset);
        var dy = point.Y - placement.Bounds.Center.Y;
        var radius = 10 * placement.Scale;
        return dx * dx + dy * dy <= radius * radius;
    }

    private CategoryMapPlacement? _pressedHit;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_root is null) return;
        var point = e.GetPosition(this);

        if (_pressed && !_dragging &&
            (Math.Abs(point.X - _pressPoint.X) > 3 || Math.Abs(point.Y - _pressPoint.Y) > 3))
        {
            _dragging = true;
        }

        if (_dragging)
        {
            _offsetX = _pressOffset.X + (point.X - _pressPoint.X);
            _offsetY = _pressOffset.Y + (point.Y - _pressPoint.Y);
            InvalidateVisual();
            return;
        }

        var hover = HitPlacement(ToLogical(point))?.Node.Path;
        if (!string.Equals(hover, _hoverPath, StringComparison.Ordinal))
        {
            _hoverPath = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        _pressed = false;
        e.Pointer.Capture(null);
        var hit = _pressedHit;
        _pressedHit = null;
        if (_dragging)
        {
            _dragging = false;
            return;
        }

        if (hit is null) return;
        SelectedPath = hit.Node.Path;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Rotate(CategoryMapNode owner, int step)
    {
        var count = owner.Children.Count;
        if (count == 0) return;
        var current = _bandTargets.TryGetValue(owner.Path, out var target)
            ? target
            : _bandRotations.TryGetValue(owner.Path, out var value) ? value : 0;
        var next = Math.Clamp(current + step, 0, Math.Max(0, count - 1));
        if (Math.Abs(next - current) < 0.001) return;
        _bandTargets[owner.Path] = next;
        if (!_bandRotations.ContainsKey(owner.Path)) _bandRotations[owner.Path] = current;
        if (!_spin.IsEnabled) _spin.Start();
    }

    private void SpinStep()
    {
        var moving = false;
        foreach (var pair in _bandTargets.ToList())
        {
            var path = pair.Key;
            var target = pair.Value;
            var current = _bandRotations.TryGetValue(path, out var value) ? value : 0;
            var delta = target - current;
            if (Math.Abs(delta) < 0.002)
            {
                _bandRotations[path] = target;
                continue;
            }

            _bandRotations[path] = current + delta * 0.28;
            moving = true;
        }

        if (!moving) _spin.Stop();
        InvalidateGeometry();
        InvalidateVisual();
    }

    private void ToggleExpanded(CategoryMapNode node)
    {
        if (node.IsLeaf) return;
        if (_expanded.Remove(node.Path)) _expandOrder.Remove(node.Path);
        else MarkExpanded(node.Path);
        InvalidateLayout();
        InvalidateVisual();
    }

    private void MarkExpanded(string path)
    {
        if (!_expanded.Add(path) || path.Length == 0) return;
        _expandOrder[path] = ++_expandSeq;
    }

    private void InvalidateLayout()
    {
        _layoutDirty = true;
    }

    private void InvalidateGeometry()
    {
        _layoutDirty = true;
    }

    private void EnsureLayout()
    {
        if (_root is null) return;
        if (!_layoutDirty) return;
        _layout = CategoryMap.Layout(_root, _expanded, NodeWidth, _bandRotations, _selectedPath, _expandOrder);
        _layoutDirty = false;
    }

    private void CenterOn(CategoryMapNode node)
    {
        EnsureLayout();
        // A shelf item may still be off-window; fall back to its nearest placed ancestor.
        for (var target = node; target is not null; target = target.Parent)
        {
            var placement = _layout.Placements.FirstOrDefault(item =>
                string.Equals(item.Node.Path, target.Path, StringComparison.OrdinalIgnoreCase));
            if (placement is null) continue;
            if (_scale < 0.7) _scale = 0.7;
            var center = placement.Bounds.Center;
            _offsetX = Bounds.Width / 2 - center.X * _scale;
            _offsetY = Bounds.Height / 2 - center.Y * _scale;
            InvalidateVisual();
            return;
        }
    }

    private Point ToLogical(Point point) => new(
        (point.X - _offsetX) / _scale,
        (point.Y - _offsetY) / _scale);

    private CategoryMapPlacement? HitPlacement(Point logical)
    {
        EnsureLayout();
        for (var i = _layout.Placements.Count - 1; i >= 0; i--)
        {
            if (_layout.Placements[i].Bounds.Contains(logical)) return _layout.Placements[i];
        }
        return null;
    }

    private CategoryMapNode? HitBand(Point logical)
    {
        EnsureLayout();
        foreach (var zone in _layout.Zones)
        {
            if (zone.Bounds.Contains(logical)) return zone.Owner;
        }
        return null;
    }

    private const double NodeBoxWidth = 170;

    private double NodeWidth(CategoryMapNode node) =>
        NodeBoxWidth * CategoryMap.NodeScale(node.Depth);

    private static string CountText(CategoryMapNode node)
    {
        if (node.TotalMods == 0) return string.Empty;
        // Leaf: mods in it. Folder: mods directly in it / including subfolders.
        return node.IsLeaf
            ? node.TotalMods.ToString(CultureInfo.InvariantCulture)
            : $"{node.Mods.Count}/{node.TotalMods}";
    }

    private static FormattedText GetText(string text, double size, bool muted)
    {
        var key = (text, Math.Round(size * 2) / 2);
        var cache = muted ? MutedCache : TextCache;
        if (cache.TryGetValue(key, out var cached)) return cached;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Face,
            key.Item2,
            muted ? ThemeService.MutedTextBrush : ThemeService.PrimaryTextBrush);
        if (cache.Count > 4000) cache.Clear();
        return cache[key] = formatted;
    }

    private static FormattedText GetFittedText(string text, double size, double maxWidth)
    {
        var bucket = Math.Max(1, (int)Math.Round(maxWidth));
        var key = (text, Math.Round(size * 2) / 2, bucket);
        if (FitCache.TryGetValue(key, out var cached)) return cached;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Face,
            key.Item2,
            ThemeService.PrimaryTextBrush)
        {
            MaxTextWidth = bucket,
            Trimming = TextTrimming.CharacterEllipsis
        };
        if (FitCache.Count > 4000) FitCache.Clear();
        return FitCache[key] = formatted;
    }

    public override void Render(DrawingContext context)
    {
        if (_root is null) return;
        EnsureLayout();
        _chevrons.Clear();

        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(
                   Matrix.CreateScale(_scale, _scale) *
                   Matrix.CreateTranslation(_offsetX, _offsetY)))
        {
            var visible = new Rect(
                ToLogical(default),
                ToLogical(new Point(Bounds.Width, Bounds.Height)));

            foreach (var link in _layout.Links)
            {
                var span = new Rect(link.From, link.To);
                if (!visible.Intersects(span)) continue;
                using (context.PushOpacity(link.Opacity))
                {
                    if (link.Band)
                    {
                        context.DrawLine(LinkPen, link.From, link.To);
                        continue;
                    }

                    DrawElbow(context, link.From, link.To);
                }
            }

            var byPath = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            foreach (var ghost in _layout.Ghosts)
            {
                if (!visible.Intersects(ghost.Bounds)) continue;
                var alpha = Math.Clamp(Math.Round(ghost.StartOpacity * 255), 0, 255);
                IBrush Fade(byte scale) => new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(
                            Color.FromArgb((byte)Math.Clamp(alpha * scale / 255, 0, 255), 255, 255, 255),
                            0),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                    }
                };

                // Same look as a normal node, but the frame and fill fade out sideways.
                context.DrawRectangle(Fade(26), new Pen(Fade(90), 1), ghost.Bounds, 4, 4);
            }

            foreach (var placement in _layout.Placements)
            {
                byPath[placement.Node.Path] = placement.Bounds;
                if (!visible.Intersects(placement.Bounds)) continue;
                DrawNode(context, placement);
            }

            var decorated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var placement in _layout.Placements)
            {
                if (placement.BandOwner is null) continue;
                if (!visible.Intersects(placement.Bounds)) continue;
                if (!decorated.Add(placement.BandOwner.Path)) continue;
                DrawBandEdges(context, placement.BandOwner, byPath);
            }
        }
    }

    // Rounded elbow: horizontal, quarter-turn, vertical, quarter-turn, horizontal.
    private static void DrawElbow(DrawingContext context, Point from, Point to)
    {
        var midX = (from.X + to.X) / 2;
        var dy = to.Y - from.Y;
        var radius = Math.Min(9, Math.Abs(dy) / 2);
        radius = Math.Min(radius, Math.Min(midX - from.X, to.X - midX));
        if (radius < 0.5 || Math.Abs(dy) < 1)
        {
            context.DrawLine(LinkPen, from, new Point(midX, from.Y));
            context.DrawLine(LinkPen, new Point(midX, from.Y), new Point(midX, to.Y));
            context.DrawLine(LinkPen, new Point(midX, to.Y), to);
            return;
        }

        var dir = Math.Sign(dy);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(from, false);
            path.LineTo(new Point(midX - radius, from.Y));
            path.QuadraticBezierTo(
                new Point(midX, from.Y),
                new Point(midX, from.Y + dir * radius));
            path.LineTo(new Point(midX, to.Y - dir * radius));
            path.QuadraticBezierTo(
                new Point(midX, to.Y),
                new Point(midX + radius, to.Y));
            path.LineTo(to);
            path.EndFigure(false);
        }

        context.DrawGeometry(null, LinkPen, geometry);
    }

    private void DrawNode(DrawingContext context, CategoryMapPlacement placement)
    {
        var node = placement.Node;
        var rect = placement.Bounds;
        var selected = string.Equals(node.Path, _selectedPath, StringComparison.Ordinal);
        var isRoot = node.Path.Length == 0;
        var matched = _matches is null || isRoot || _matches.Contains(node.Path);
        var dim = _matches is not null && !matched;
        var hover = string.Equals(node.Path, _hoverPath, StringComparison.Ordinal);

        using (context.PushOpacity(placement.Opacity * (dim ? 0.16 : 1)))
        {
            var fill = selected ? NodeFillSelected : hover ? NodeFillHover : NodeFill;
            var pen = selected ? SelectedPen : _matches is not null && matched && !isRoot ? MatchPen : NodeBorderPen;
            context.DrawRectangle(fill, pen, rect, 4, 4);

            var label = isRoot ? RootLabel : node.Leaf;
            var size = BaseFontSize * placement.Scale;
            var text = GetText(label, size, false);
            var pad = 9 * placement.Scale;
            var handleX = rect.Right - 13 * placement.Scale;
            var countText = CountText(node);
            var count = countText.Length > 0 && rect.Width > 60 * placement.Scale
                ? GetText(countText, 10.5 * placement.Scale, true)
                : null;
            var countRight = node.IsLeaf ? rect.Right - pad : handleX - 10 * placement.Scale;
            var labelRight = count is null
                ? (node.IsLeaf ? rect.Right - pad : handleX - 8 * placement.Scale)
                : countRight - count.Width - 6 * placement.Scale;
            var inner = new Rect(rect.X + pad, rect.Y, Math.Max(0, labelRight - rect.X - pad), rect.Height);
            using (context.PushClip(inner))
            {
                context.DrawText(text, new Point(inner.X, rect.Center.Y - text.Height / 2));
            }

            if (count is not null)
            {
                context.DrawText(
                    count,
                    new Point(countRight - count.Width, rect.Center.Y - count.Height / 2));
            }

            if (!node.IsLeaf)
            {
                // Expand handle: a small circle that reads as +/-.
                var cx = handleX;
                var cy = rect.Center.Y;
                var r = 5.5 * placement.Scale;
                context.DrawEllipse(null, NodeBorderPen, new Point(cx, cy), r, r);
                var open = _expanded.Contains(node.Path);
                context.DrawLine(LinkPen, new Point(cx - r / 2, cy), new Point(cx + r / 2, cy));
                if (!open)
                {
                    context.DrawLine(LinkPen, new Point(cx, cy - r / 2), new Point(cx, cy + r / 2));
                }
            }
        }
    }

    private void DrawBandEdges(DrawingContext context, CategoryMapNode owner, Dictionary<string, Rect> byPath)
    {
        if (!byPath.TryGetValue(owner.Path, out var ownerRect)) return;
        var rotation = _bandRotations.TryGetValue(owner.Path, out var value) ? value : 0;
        var window = CategoryMap.BandWindow(owner, rotation);
        // Centre on the drum column actually drawn, whatever gap that level used.
        var columnCenter = _layout.Placements
            .Where(item => item.BandOwner is not null &&
                           string.Equals(item.BandOwner.Path, owner.Path, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Bounds.Center.X)
            .DefaultIfEmpty(ownerRect.Right + CategoryMap.DrumGap + NodeWidth(owner) / 2)
            .First();
        var reach = CategoryMap.DrumRadius * Math.Sin(CategoryMap.DrumHalfSpan);

        if (window.Before > 0)
        {
            var text = GetText($"▲ {window.Before}", 11, true);
            var rect = new Rect(columnCenter - 24, ownerRect.Center.Y - reach - 26, 48, 18);
            context.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
            _chevrons.Add(new BandChevron(rect, owner, -10));
        }

        if (window.After > 0)
        {
            var text = GetText($"{window.After} ▼", 11, true);
            var rect = new Rect(columnCenter - 24, ownerRect.Center.Y + reach + 8, 48, 18);
            context.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
            _chevrons.Add(new BandChevron(rect, owner, 10));
        }
    }
}
