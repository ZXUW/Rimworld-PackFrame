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

    public static SolidColorBrush PrimaryTextBrush { get; } =
        new(Color.Parse("#EAF1F8"));

    public static SolidColorBrush SecondaryTextBrush { get; } =
        new(Color.Parse("#C3D4E4"));

    public static SolidColorBrush MutedTextBrush { get; } =
        new(Color.Parse("#9AA7B8"));

    public static void Apply(string? hex)
    {
        var value = Normalize(hex);
        var background = Color.Parse(value);
        BackgroundBrush = new SolidColorBrush(background);
        SecondaryBrush.Color = Mix(background, Colors.White, 0.16);
        var textBase = Luminance(background) > 128 ? Colors.Black : Colors.White;
        PrimaryTextBrush.Color = Mix(background, textBase, 0.90);
        SecondaryTextBrush.Color = Mix(background, textBase, 0.75);
        MutedTextBrush.Color = Mix(background, textBase, 0.55);
        if (Application.Current is { } application)
        {
            application.Resources["AppBackgroundBrush"] = BackgroundBrush;
            UpdateResource(application, "SecondaryBrush", SecondaryBrush);
            UpdateResource(application, "PrimaryTextBrush", PrimaryTextBrush);
            UpdateResource(application, "SecondaryTextBrush", SecondaryTextBrush);
            UpdateResource(application, "MutedTextBrush", MutedTextBrush);
        }
    }

    private static double Luminance(Color color) =>
        (color.R + color.G + color.B) / 3.0;

    private static void UpdateResource(
        Application application,
        string key,
        SolidColorBrush brush
    )
    {
        if (application.Resources[key] is SolidColorBrush existing)
        {
            existing.Color = brush.Color;
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
