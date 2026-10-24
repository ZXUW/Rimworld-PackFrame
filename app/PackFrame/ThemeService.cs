using System;
using Avalonia;
using Avalonia.Media;

namespace PackFrame;

internal static class ThemeService
{
    public const string DefaultBackground = "#20354A";

    public static readonly (string Key, string Hex)[] BackgroundPresets =
    {
        ("default", "#20354A"),
        ("dark", "#182938"),
        ("black", "#15181D"),
        ("purple", "#251F33"),
        ("green", "#1D2C27"),
        ("brown", "#2B2421")
    };

    public static SolidColorBrush BackgroundBrush { get; private set; } =
        new(Color.Parse(DefaultBackground));

    public static SolidColorBrush SecondaryBrush { get; } =
        new(Color.Parse("#455364"));

    public static void Apply(string? hex)
    {
        var value = Normalize(hex);
        var background = Color.Parse(value);
        BackgroundBrush = new SolidColorBrush(background);
        SecondaryBrush.Color = Mix(background, Colors.White, 0.16);
        if (Application.Current is { } application)
        {
            application.Resources["AppBackgroundBrush"] = BackgroundBrush;
            if (application.Resources["SecondaryBrush"] is SolidColorBrush secondary)
            {
                secondary.Color = SecondaryBrush.Color;
            }
        }
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromArgb(
            255,
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount)
        );
    }

    private static string Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return DefaultBackground;
        }

        try
        {
            Color.Parse(hex.Trim());
            return hex.Trim();
        }
        catch
        {
            return DefaultBackground;
        }
    }
}
