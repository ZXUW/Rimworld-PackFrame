using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PackFrame;

public sealed class ModBadge : Control
{
    public static readonly StyledProperty<IReadOnlyList<string>?> BadgesProperty =
        AvaloniaProperty.Register<ModBadge, IReadOnlyList<string>?>(nameof(Badges));

    private static readonly Typeface BadgeTypeface = new(FontFamily.Default);
    private static readonly IBrush BadgeBrush =
        new SolidColorBrush(Color.Parse("#EAF1F8"));
    // Lazily populated on the UI render thread, after Avalonia's font services exist.
    private static readonly Dictionary<(string, double), FormattedText> TextCache = new();

    static ModBadge()
    {
        AffectsRender<ModBadge>(BadgesProperty);
        AffectsMeasure<ModBadge>(BadgesProperty);
    }

    public IReadOnlyList<string>? Badges
    {
        get => GetValue(BadgesProperty);
        set => SetValue(BadgesProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(22, 22);
    }

    public override void Render(DrawingContext context)
    {
        var badges = Badges;
        if (badges is null || badges.Count == 0)
        {
            return;
        }

        const double width = 22;
        const double height = 22;
        if (badges.Count == 1)
        {
            DrawLetter(context, badges[0], new Rect(0, 0, width, height), 18);
        }
        else if (badges.Count == 2)
        {
            DrawLetter(context, badges[0], new Rect(0, 0, width / 2, height), 14);
            DrawLetter(
                context,
                badges[1],
                new Rect(width / 2, 0, width / 2, height),
                14
            );
        }
        else
        {
            for (var i = 0; i < Math.Min(4, badges.Count); i++)
            {
                var cell = new Rect(
                    i % 2 * width / 2,
                    i / 2 * height / 2,
                    width / 2,
                    height / 2
                );
                DrawLetter(context, badges[i], cell, 10);
            }
        }
    }

    private static void DrawLetter(
        DrawingContext context,
        string text,
        Rect cell,
        double fontSize
    )
    {
        if (!TextCache.TryGetValue((text, fontSize), out var formatted))
        {
        formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BadgeTypeface,
            fontSize,
            BadgeBrush
        );
        if (text is "S" or "V" or "R" or "L") TextCache[(text, fontSize)] = formatted;
        }
        var x = cell.X + (cell.Width - formatted.Width) / 2;
        var y = cell.Y + (cell.Height - formatted.Height) / 2;
        context.DrawText(formatted, new Point(x, y));
    }
}
