using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PackFrame;

public sealed class AppConfig
{
    public string? Language { get; set; }
    public string? GamePath { get; set; }
    public string? OfficialConfigPath { get; set; }
    public string? ProfileFolder1 { get; set; }
    public string? ProfileFolder2 { get; set; }
    public bool ShowSecondaryPane { get; set; }
    public bool ShowThumbnails { get; set; } = true;
    public bool InactiveCategoryMode { get; set; }
    public List<string> CollapsedCategories { get; set; } = new();
    public List<string> ProfileOrder { get; set; } = new();
    public string? ModpackIndexUrl { get; set; }
    public string? BackgroundColor { get; set; }
    public string? OssEndpoint { get; set; }
    public string? OssBucket { get; set; }
    public string? OssPrefix { get; set; }
    public string? OssAccessKeyId { get; set; }
    public string? OssAccessKeySecret { get; set; }
    public bool OssEnabled { get; set; } = true;

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "packframe.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return WithDetectedLanguage(new AppConfig());
            }

            var json = File.ReadAllText(ConfigPath);
            return WithDetectedLanguage(JsonSerializer.Deserialize(
                json,
                PackFrameJsonContext.Default.AppConfig
            ) ?? new AppConfig());
        }
        catch (Exception exception)
        {
            AppLog.Error("Read app config: " + ConfigPath, exception);
            return WithDetectedLanguage(new AppConfig());
        }
    }

    // First run: follow the OS UI language instead of defaulting to English.
    private static AppConfig WithDetectedLanguage(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Language))
        {
            config.Language = DetectLanguage();
        }

        return config;
    }

    private static string DetectLanguage()
    {
        try
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            var name = culture.Name;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                var traditional =
                    name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("MO", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Hant", StringComparison.OrdinalIgnoreCase);
                return traditional
                    ? Localization.TraditionalChinese
                    : Localization.SimplifiedChinese;
            }

            if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return Localization.Japanese;
            }
        }
        catch
        {
        }

        return Localization.English;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                this,
                PackFrameJsonContext.Default.AppConfig
            );
            var temporaryPath = ConfigPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, ConfigPath, true);
        }
        catch (Exception exception)
        {
            AppLog.Error("Save app config: " + ConfigPath, exception);
        }
    }
}
