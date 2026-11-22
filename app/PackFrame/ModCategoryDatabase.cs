using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

// 模组分类表：按 Workshop id / packageId / 名字 四级查找。
// 格式说明见 docs/ModCategories.md。
internal sealed class ModCategoryDocument
{
    public int Version { get; set; } = 1;
    public string? Generated { get; set; }
    public string? Title { get; set; }
    public Dictionary<string, string> Workshop { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PackageId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Name { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class ModCategoryDatabase
{
    public const string GitHubUrl =
        "https://raw.githubusercontent.com/ZXUW/Rimworld-PackFrame/main/update/modCategories.json";
    public const string GiteeMirrorUrl =
        "https://gitee.com/zxuw/Rimworld-PackFrame/raw/main/update/modCategories.json";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(4)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly object Gate = new();
    private static ModCategoryDocument? _table;
    private static Dictionary<string, string> _normalizedNames = new(StringComparer.OrdinalIgnoreCase);

    // 三层：用户改动 > 社区表 > 内置表
    private static string UserPath =>
        Path.Combine(AppContext.BaseDirectory, "runtime", "rules", "modCategories.user.json");

    internal static string CommunityPath =>
        Path.Combine(AppContext.BaseDirectory, "runtime", "rules", "modCategories.json");

    internal static ModCategoryDocument Load()
    {
        lock (Gate)
        {
            if (_table is not null) return _table;
            var table = Read();
            _normalizedNames = BuildNormalizedNames(table.Name);
            return _table = table;
        }
    }

    // 四级查找：workshop id → packageId → 精确名字 → 归一化名字
    internal static string? Resolve(ModInfo mod)
    {
        var table = Load();
        return Resolve(mod, table, _normalizedNames);
    }

    internal static string? Resolve(
        ModInfo mod,
        ModCategoryDocument table,
        IReadOnlyDictionary<string, string> normalizedNames)
    {
        var workshopId = ModpackService.ParseWorkshopId(mod.Link)?.ToString();
        if (workshopId is not null && table.Workshop.TryGetValue(workshopId, out var byId)) return byId;
        if (!string.IsNullOrWhiteSpace(mod.PackageId) && table.PackageId.TryGetValue(mod.PackageId, out var byPackage)) return byPackage;
        if (!string.IsNullOrWhiteSpace(mod.Name) && table.Name.TryGetValue(mod.Name.Trim(), out var byName)) return byName;
        var normalized = NormalizeName(mod.Name);
        if (normalized.Length > 0 && normalizedNames.TryGetValue(normalized, out var byNormalized)) return byNormalized;
        return null;
    }

    internal static ModCategoryDocument? LoadCommunityTemplate(out DateTime? generatedAt)
    {
        var path = CommunityPath;
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "update", "modCategories.json");
        }
        if (!File.Exists(path))
        {
            generatedAt = null;
            return null;
        }

        try
        {
            var document = Parse(File.ReadAllText(path));
            generatedAt = ParseGenerated(document?.Generated) ?? File.GetLastWriteTime(path);
            return document;
        }
        catch (Exception exception)
        {
            AppLog.Error("Load community mod categories: " + path, exception);
            generatedAt = null;
            return null;
        }
    }

    internal static DateTime? ParseGenerated(string? value)
    {
        if (DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed.LocalDateTime;
        }
        return null;
    }

    internal static int CountEntries(ModCategoryDocument document) =>
        document.Workshop.Count > 0
            ? document.Workshop.Count
            : document.PackageId.Count > 0
                ? document.PackageId.Count
                : document.Name.Count;

    public static async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        foreach (var url in new[] { GiteeMirrorUrl, GitHubUrl })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Rimworld-PackFrame");
                using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                var parsed = Parse(json);
                if (parsed is null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(CommunityPath)!);
                FileTransactions.WriteAtomic(CommunityPath, temp => File.WriteAllText(temp, json, new UTF8Encoding(false)));
                lock (Gate) _table = null;
                return;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    // 导出当前分类（packageId 段）到用户层，社区表更新不会覆盖它
    internal static string SaveExport(IReadOnlyDictionary<string, string> categories)
    {
        var document = new ModCategoryDocument
        {
            PackageId = categories.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        FileTransactions.WriteAtomic(UserPath, temp => File.WriteAllText(temp, json, new UTF8Encoding(false)));
        lock (Gate) _table = null;
        return UserPath;
    }

    private static ModCategoryDocument Read()
    {
        // 内置 → 社区 → 用户，后面的覆盖前面的
        var result = new ModCategoryDocument();
        try
        {
            using var stream = typeof(ModCategoryDatabase).Assembly
                .GetManifestResourceStream("PackFrame.Assets.ModCategories.json");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                if (Parse(reader.ReadToEnd()) is { } builtin) Overlay(result, builtin);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Load built-in mod category table", exception);
        }

        foreach (var path in new[] { CommunityPath, UserPath })
        {
            try
            {
                if (!File.Exists(path) || Parse(File.ReadAllText(path)) is not { } overlay) continue;
                Overlay(result, overlay);
            }
            catch (Exception exception)
            {
                AppLog.Error("Load mod category table: " + path, exception);
            }
        }
        return result;
    }

    private static void Overlay(ModCategoryDocument target, ModCategoryDocument source)
    {
        foreach (var (key, value) in source.Workshop) target.Workshop[key] = value;
        foreach (var (key, value) in source.PackageId) target.PackageId[key] = value;
        foreach (var (key, value) in source.Name) target.Name[key] = value;
    }

    internal static ModCategoryDocument? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var result = new ModCategoryDocument();
            foreach (var section in root.EnumerateObject())
            {
                switch (section.Name.ToLowerInvariant())
                {
                    case "version":
                        if (section.Value.ValueKind == JsonValueKind.Number &&
                            section.Value.TryGetInt32(out var version))
                        {
                            result.Version = version;
                        }
                        break;
                    case "generated":
                        if (section.Value.ValueKind == JsonValueKind.String)
                        {
                            result.Generated = section.Value.GetString();
                        }
                        break;
                    case "title":
                        if (section.Value.ValueKind == JsonValueKind.String)
                        {
                            result.Title = section.Value.GetString();
                        }
                        break;
                    case "count":
                        break;
                    case "workshop":
                        ReadSection(section.Value, result.Workshop);
                        break;
                    case "packageid":
                        ReadSection(section.Value, result.PackageId);
                        break;
                    case "name":
                        ReadSection(section.Value, result.Name);
                        break;
                    default:
                        // 兼容扁平表：纯数字键进 workshop 段，其余按 packageId
                        if (section.Value.ValueKind == JsonValueKind.String && IsCategory(section.Value.GetString()))
                        {
                            var key = section.Name.Trim();
                            if (key.All(char.IsDigit)) result.Workshop[key] = section.Value.GetString()!;
                            else result.PackageId[key] = section.Value.GetString()!;
                        }
                        break;
                }
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static void ReadSection(JsonElement section, Dictionary<string, string> target)
    {
        if (section.ValueKind != JsonValueKind.Object) return;
        foreach (var entry in section.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String) continue;
            var category = entry.Value.GetString();
            if (string.IsNullOrWhiteSpace(entry.Name) || !IsCategory(category)) continue;
            target[entry.Name.Trim()] = category!;
        }
    }

    private static bool IsCategory(string? value) => !string.IsNullOrWhiteSpace(value);

    internal static Dictionary<string, string> BuildNormalizedNames(IReadOnlyDictionary<string, string> names)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, category) in names)
        {
            var normalized = NormalizeName(name);
            if (normalized.Length == 0) continue;
            result.TryAdd(normalized, category);
        }
        return result;
    }

    // 只做低风险归一化：去首尾空白、去掉开头的 [标签]、压缩空格、忽略大小写
    internal static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var text = name.Trim();
        while (text.StartsWith('[') || text.StartsWith('【'))
        {
            var close = text.IndexOfAny(new[] { ']', '】' });
            if (close < 0) break;
            text = text[(close + 1)..].TrimStart();
        }
        var builder = new StringBuilder(text.Length);
        var lastSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace && builder.Length > 0) builder.Append(' ');
                lastSpace = true;
                continue;
            }
            builder.Append(ch);
            lastSpace = false;
        }
        return builder.ToString().TrimEnd();
    }
}
