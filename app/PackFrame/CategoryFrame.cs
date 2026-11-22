using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PackFrame;

public sealed class CategoryFrame : Control
{
    public static readonly StyledProperty<IReadOnlyList<CategoryFrameLayer>?> LayersProperty =
        AvaloniaProperty.Register<CategoryFrame, IReadOnlyList<CategoryFrameLayer>?>(nameof(Layers));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CategoryFrame, IBrush?>(nameof(Stroke));

    static CategoryFrame() => AffectsRender<CategoryFrame>(LayersProperty, StrokeProperty);

    public IReadOnlyList<CategoryFrameLayer>? Layers
    {
        get => GetValue(LayersProperty);
        set => SetValue(LayersProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Layers is not { Count: > 0 } layers || Stroke is not { } brush || Bounds.Height < 1) return;
        foreach (var layer in layers)
        {
            var left = 6 + 16.0 * layer.Depth;
            var right = Bounds.Width - 6 - 4.0 * layer.Depth;
            var width = Math.Max(0, right - left);
            if (width < 2) continue;
            const double verticalInset = 2;
            var topInset = layer.IsTop ? verticalInset : 0;
            var bottomInset = layer.IsBottom ? verticalInset : 0;
            var sideHeight = Math.Max(0, Bounds.Height - topInset - bottomInset);
            context.FillRectangle(brush, new Rect(left, topInset, 1, sideHeight));
            context.FillRectangle(brush, new Rect(right - 1, topInset, 1, sideHeight));
            if (layer.IsTop) context.FillRectangle(brush, new Rect(left, verticalInset, width, 1));
            if (layer.IsBottom) context.FillRectangle(brush, new Rect(left, Bounds.Height - verticalInset - 1, width, 1));
        }
    }
}
