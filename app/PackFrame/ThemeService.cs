using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace PackFrame;

internal static class ThemeService
{
    public const string DefaultBackground = "#20354A";

    public static readonly (string Key, string Hex, string? Light)[] BackgroundPresets =
    {
        ("default", "#20354A", null),
        ("dark", "#182938", null),
        ("black", "#15181D", null),
        ("rimworld", "#15191D", "#3C4A50"),
        ("purple", "#251F33", null),
        ("green", "#1D2C27", null),
        ("brown", "#2B2421", null)
    };

    public static SolidColorBrush BackgroundBrush { get; } =
        new(Color.Parse(DefaultBackground));

    public static SolidColorBrush ToastBackgroundBrush { get; } =
        new(Color.Parse("#E620354A"));

    public static SolidColorBrush SecondaryBrush { get; } =
        new(Color.Parse("#455364"));

    public static SolidColorBrush PrimaryTextBrush { get; } =
        new(Color.Parse("#EAF1F8"));

    public static SolidColorBrush SecondaryTextBrush { get; } =
        new(Color.Parse("#C3D4E4"));

    public static SolidColorBrush MutedTextBrush { get; } =
        new(Color.Parse("#9AA7B8"));

    public static SolidColorBrush HoverBrush { get; } =
        new(Color.Parse("#4D6378"));

    public static SolidColorBrush SelectedBrush { get; } =
        new(Color.Parse("#5C6E82"));

    public static void Apply(string? hex, string? lightHex = null)
    {
        var value = Normalize(hex);
        var background = Color.Parse(value);
        BackgroundBrush.Color = background;
        ToastBackgroundBrush.Color = Color.FromArgb(230, background.R, background.G, background.B);
        if (string.IsNullOrWhiteSpace(lightHex))
        {
            lightHex = BackgroundPresets
                .FirstOrDefault(preset =>
                    preset.Hex.Equals(value, StringComparison.OrdinalIgnoreCase))
                .Light;
        }
        SecondaryBrush.Color = TryParseColor(lightHex) ??
            Mix(background, Colors.White, 0.16);
        var textBase = Luminance(background) > 128 ? Colors.Black : Colors.White;
        PrimaryTextBrush.Color = Mix(background, textBase, 0.90);
        SecondaryTextBrush.Color = Mix(background, textBase, 0.75);
        MutedTextBrush.Color = Mix(background, textBase, 0.55);
        HoverBrush.Color = Mix(background, textBase, 0.20);
        SelectedBrush.Color = TryParseColor(lightHex) ??
            Mix(background, textBase, 0.27);
        if (Application.Current is { } application)
        {
            UpdateResource(application, "AppBackgroundBrush", BackgroundBrush);
            UpdateResource(application, "ToastBackgroundBrush", ToastBackgroundBrush);
            UpdateResource(application, "SecondaryBrush", SecondaryBrush);
            UpdateResource(application, "PrimaryTextBrush", PrimaryTextBrush);
            UpdateResource(application, "SecondaryTextBrush", SecondaryTextBrush);
            UpdateResource(application, "MutedTextBrush", MutedTextBrush);
            UpdateResource(application, "ItemHoverBrush", HoverBrush);
            UpdateResource(application, "ItemSelectedBrush", SelectedBrush);

            foreach (var key in new[]
                     {
                         "ComboBoxDropDownBackground",
                         "FlyoutPresenterBackground",
                         "MenuFlyoutPresenterBackground"
                     })
            {
                SetBrushResource(application, key, BackgroundBrush);
            }

            foreach (var key in new[]
                     {
                         "ComboBoxBackground",
                         "ComboBoxBorderBrush",
                         "ComboBoxDropDownBorderBrush",
                         "FlyoutBorderThemeBrush",
                         "MenuFlyoutPresenterBorderBrush"
                     })
            {
                SetBrushResource(application, key, SecondaryBrush);
            }

            foreach (var key in new[]
                     {
                         "ComboBoxBackgroundPointerOver",
                         "ComboBoxBackgroundPressed",
                         "ComboBoxItemBackgroundPointerOver",
                         "ComboBoxItemBackgroundPressed",
                         "ComboBoxItemBackgroundSelectedPointerOver",
                         "ComboBoxItemBackgroundSelectedPressed",
                         "MenuFlyoutItemBackgroundPointerOver",
                         "MenuFlyoutItemBackgroundPressed"
                     })
            {
                SetBrushResource(application, key, HoverBrush);
            }

            foreach (var key in new[]
                     {
                         "ComboBoxItemBackgroundSelected"
                     })
            {
                SetBrushResource(application, key, SelectedBrush);
            }

            foreach (var key in new[]
                     {
                         "ComboBoxForeground",
                         "ComboBoxForegroundFocused",
                         "ComboBoxForegroundFocusedPressed",
                         "ComboBoxItemForeground",
                         "ComboBoxItemForegroundPointerOver",
                         "ComboBoxItemForegroundPressed",
                         "ComboBoxItemForegroundSelected",
                         "ComboBoxItemForegroundSelectedPointerOver",
                         "ComboBoxItemForegroundSelectedPressed",
                         "MenuFlyoutItemForeground",
                         "MenuFlyoutItemForegroundPointerOver",
                         "MenuFlyoutItemForegroundPressed"
                     })
            {
                SetBrushResource(application, key, PrimaryTextBrush);
            }
        }
    }

    private static void SetBrushResource(Application application, string key, SolidColorBrush brush)
    {
        if (application.Resources.TryGetValue(key, out var raw) && raw is SolidColorBrush existing)
        {
            existing.Color = brush.Color;
        }
        else
        {
            application.Resources[key] = brush;
        }
    }

    private static Color? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            return Color.Parse(hex.Trim());
        }
        catch
        {
            return null;
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
