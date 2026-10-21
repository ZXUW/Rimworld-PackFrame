using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PackFrame;

public sealed record ModColorChoice(
    bool Cancelled,
    bool Clear,
    string? Hex,
    IReadOnlyDictionary<string, string> Labels
);

public sealed class ModColorData
{
    public Dictionary<string, string> ModColors { get; set; } = new();
    public Dictionary<string, string> ColorLabels { get; set; } = new();
}

public static class ModColorStore
{
    public const string FileName = "ModColors.json";
    private static string? _cachedRoot;
    private static ModColorData? _cachedData;

    public static void Invalidate() => _cachedData = null;

    public static string GetPath(string packRoot)
    {
        return Path.Combine(
            packRoot,
            InstanceStore.InstancesFolderName,
            FileName
        );
    }

    public static ModColorData Load(string? packRoot)
    {
        var data = LoadCached(packRoot);
        return new ModColorData
        {
            ModColors = new(data.ModColors, StringComparer.OrdinalIgnoreCase),
            ColorLabels = new(data.ColorLabels, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static ModColorData LoadCached(string? packRoot)
    {
        if (_cachedData is not null && _cachedRoot == packRoot) return _cachedData;
        _cachedRoot = packRoot;
        var result = new ModColorData
        {
            ModColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ColorLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        if (string.IsNullOrWhiteSpace(packRoot))
        {
            return result;
        }

        var path = GetPath(packRoot);
        if (!File.Exists(path))
        {
            return _cachedData = result;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                PackFrameJsonContext.Default.ModColorData
            );
            if (loaded is null)
            {
                return result;
            }

            foreach (var pair in loaded.ModColors ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    result.ModColors[pair.Key] = pair.Value ?? string.Empty;
                }
            }
            foreach (var pair in loaded.ColorLabels ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    result.ColorLabels[pair.Key] = pair.Value ?? string.Empty;
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Read mod colors: " + path, exception);
        }

        return _cachedData = result;
    }

    public static void SetModColors(
        string? packRoot,
        IReadOnlyDictionary<string, string?> colors
    )
    {
        if (string.IsNullOrWhiteSpace(packRoot))
        {
            return;
        }

        var data = Load(packRoot);
        var changed = false;
        foreach (var pair in colors)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                changed |= data.ModColors.Remove(pair.Key);
            }
            else
            {
                data.ModColors[pair.Key] = pair.Value;
                changed = true;
            }
        }

        if (changed)
        {
            Write(packRoot, data);
        }
    }

    public static void SetLabels(
        string? packRoot,
        IReadOnlyDictionary<string, string> labels
    )
    {
        if (string.IsNullOrWhiteSpace(packRoot))
        {
            return;
        }

        var data = Load(packRoot);
        data.ColorLabels = new Dictionary<string, string>(
            labels,
            StringComparer.OrdinalIgnoreCase
        );
        Write(packRoot, data);
    }

    public static string? GetColor(string? packRoot, string packageId)
    {
        return LoadCached(packRoot).ModColors.TryGetValue(packageId, out var color)
            ? color
            : null;
    }

    private static void Write(string packRoot, ModColorData data)
    {
        var path = GetPath(packRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileTransactions.WriteAtomic(path, temporary => File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                data,
                PackFrameJsonContext.Default.ModColorData
            )
        ));
        _cachedRoot = packRoot;
        _cachedData = data;
    }
}
