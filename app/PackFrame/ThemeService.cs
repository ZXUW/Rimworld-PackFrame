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

    public static void Apply(string? hex)
    {
        var value = Normalize(hex);
        BackgroundBrush = new SolidColorBrush(Color.Parse(value));
        if (Application.Current is { } application)
        {
            application.Resources["AppBackgroundBrush"] = BackgroundBrush;
        }
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
