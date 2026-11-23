using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>Extra palette entries the user added, as #RRGGBB.</summary>
    public List<string> CustomColors { get; set; } = new();
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
            ColorLabels = new(data.ColorLabels, StringComparer.OrdinalIgnoreCase),
            CustomColors = data.CustomColors.ToList()
        };
    }

    private static ModColorData LoadCached(string? packRoot)
    {
        if (_cachedData is not null && _cachedRoot == packRoot) return _cachedData;
        _cachedRoot = packRoot;
        var result = new ModColorData
        {
            ModColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ColorLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CustomColors = new List<string>()
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
            foreach (var hex in loaded.CustomColors ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(hex)) result.CustomColors.Add(hex.Trim());
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

    public static IReadOnlyList<string> GetCustomColors(string? packRoot) =>
        Load(packRoot).CustomColors;

    public static void AddCustomColor(string? packRoot, string hex)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || string.IsNullOrWhiteSpace(hex)) return;
        var normalized = hex.Trim().ToUpperInvariant();
        if (normalized.Length != 7 || normalized[0] != '#') return;
        foreach (var character in normalized[1..])
        {
            if (!Uri.IsHexDigit(character)) return;
        }

        var data = Load(packRoot);
        if (data.CustomColors.Any(existing =>
                string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        data.CustomColors.Add(normalized);
        Write(packRoot, data);
    }

    public static void RemoveCustomColor(string? packRoot, string hex)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || string.IsNullOrWhiteSpace(hex)) return;
        var data = Load(packRoot);
        var removed = data.CustomColors.RemoveAll(existing =>
            string.Equals(existing, hex.Trim(), StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            data.ColorLabels.Remove(hex.Trim());
            Write(packRoot, data);
        }
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
