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
    public List<string> ProfileOrder { get; set; } = new();
    public string? ModpackIndexUrl { get; set; }

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "packframe.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize(
                json,
                PackFrameJsonContext.Default.AppConfig
            ) ?? new AppConfig();
        }
        catch (Exception exception)
        {
            AppLog.Error("Read app config: " + ConfigPath, exception);
            return new AppConfig();
        }
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
