using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

const string RulesSourceUrl =
    "https://raw.githubusercontent.com/RimSort/Community-Rules-Database/main/communityRules.json";
const string SteamDbSourceUrl =
    "https://raw.githubusercontent.com/RimSort/Steam-Workshop-Database/main/steamDB.json";

var repoRoot = ResolveRepoRoot(args);
var updateDir = Path.Combine(repoRoot, "update");
var assetsDir = Path.Combine(repoRoot, "app", "PackFrame", "Assets");
Directory.CreateDirectory(updateDir);
Directory.CreateDirectory(assetsDir);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Rimworld-PackFrame-DbSync/1.0");

var localRulesSource = Environment.GetEnvironmentVariable("PF_LOCAL_RULES_JSON");
var localSteamDbSource = Environment.GetEnvironmentVariable("PF_LOCAL_STEAMDB_JSON");

Console.WriteLine("Fetching RimSort Community Rules...");
var rawRulesBytes = !string.IsNullOrWhiteSpace(localRulesSource) && File.Exists(localRulesSource)
    ? await File.ReadAllBytesAsync(localRulesSource)
    : await FetchBytesWithRetryAsync(http, RulesSourceUrl);

var trimmedRulesBytes = TrimCommunityRules(rawRulesBytes, out var rulesCount, out var rulesTs);
await WriteIfContentChangedAsync(Path.Combine(updateDir, "communityRules.json"), trimmedRulesBytes);
await WriteIfContentChangedAsync(Path.Combine(assetsDir, "CommunityRules.json"), trimmedRulesBytes);
Console.WriteLine(
    $"Community rules synced: {rulesCount} mod entries, timestamp={rulesTs}, size={trimmedRulesBytes.Length / 1024.0:F1} KB");

Console.WriteLine("Fetching RimSort Steam Workshop Database...");
var rawSteamDbBytes = !string.IsNullOrWhiteSpace(localSteamDbSource) && File.Exists(localSteamDbSource)
    ? await File.ReadAllBytesAsync(localSteamDbSource)
    : await FetchBytesWithRetryAsync(http, SteamDbSourceUrl);

var trimmedWorkshopJsonBytes = TrimSteamWorkshopDb(rawSteamDbBytes, out var workshopCount, out var workshopTs);
var trimmedWorkshopGzipBytes = CompressGzip(trimmedWorkshopJsonBytes);
await WriteIfContentChangedAsync(Path.Combine(updateDir, "workshopMap.json.gz"), trimmedWorkshopGzipBytes);
await WriteIfContentChangedAsync(Path.Combine(assetsDir, "WorkshopMap.json.gz"), trimmedWorkshopGzipBytes);
Console.WriteLine(
    $"Steam Workshop map synced: {workshopCount} unique packageIds, version={workshopTs}, json={trimmedWorkshopJsonBytes.Length / 1024.0:F1} KB, gzip={trimmedWorkshopGzipBytes.Length / 1024.0:F1} KB");

return 0;

static string ResolveRepoRoot(string[] cliArgs)
{
    if (cliArgs.Length > 0 && Directory.Exists(cliArgs[0]))
    {
        return Path.GetFullPath(cliArgs[0]);
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "app", "PackFrame")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static async Task<byte[]> FetchBytesWithRetryAsync(HttpClient client, string url)
{
    Exception? last = null;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            return await client.GetByteArrayAsync(url);
        }
        catch (Exception ex) when (attempt < 3)
        {
            last = ex;
            await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
        }
    }

    throw last ?? new InvalidOperationException("Failed to fetch " + url);
}

static byte[] TrimCommunityRules(byte[] rawUtf8, out int count, out long timestamp)
{
    using var doc = JsonDocument.Parse(rawUtf8);
    var root = doc.RootElement;
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("rules", out var rulesProp) ||
        rulesProp.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidOperationException("Upstream communityRules.json is missing 'rules' object.");
    }

    timestamp = root.TryGetProperty("timestamp", out var tsProp) && tsProp.TryGetInt64(out var ts)
        ? ts
        : 0;

    var entries = new SortedDictionary<string, ModRuleSlim>(StringComparer.OrdinalIgnoreCase);
    foreach (var mod in rulesProp.EnumerateObject())
    {
        if (string.IsNullOrWhiteSpace(mod.Name) || mod.Value.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var loadAfter = ReadRuleTargets(mod.Value, "loadAfter");
        var loadBefore = ReadRuleTargets(mod.Value, "loadBefore");
        var incompatible = ReadRuleTargets(mod.Value, "incompatibleWith");
        var loadBottom = ReadRuleBool(mod.Value, "loadBottom");
        var loadTop = ReadRuleBool(mod.Value, "loadTop");

        if (loadAfter.Count == 0 && loadBefore.Count == 0 && incompatible.Count == 0 && !loadBottom && !loadTop)
        {
            continue;
        }

        entries[mod.Name.Trim()] = new ModRuleSlim(loadAfter, loadBefore, incompatible, loadBottom, loadTop);
    }

    count = entries.Count;
    using var ms = new MemoryStream();
    using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
    {
        writer.WriteStartObject();
        writer.WriteNumber("timestamp", timestamp);
        writer.WriteStartObject("rules");
        foreach (var (pkg, slim) in entries)
        {
            writer.WriteStartObject(pkg);
            WriteStringArrayIfAny(writer, "loadAfter", slim.LoadAfter);
            WriteStringArrayIfAny(writer, "loadBefore", slim.LoadBefore);
            WriteStringArrayIfAny(writer, "incompatibleWith", slim.IncompatibleWith);
            if (slim.LoadBottom) writer.WriteBoolean("loadBottom", true);
            if (slim.LoadTop) writer.WriteBoolean("loadTop", true);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    return ms.ToArray();
}

static List<string> ReadRuleTargets(JsonElement modObj, string propName)
{
    if (!modObj.TryGetProperty(propName, out var prop))
    {
        return [];
    }

    var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    if (prop.ValueKind == JsonValueKind.Object)
    {
        foreach (var p in prop.EnumerateObject())
        {
            if (!string.IsNullOrWhiteSpace(p.Name))
            {
                set.Add(p.Name.Trim());
            }
        }
    }
    else if (prop.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                set.Add(item.GetString()!.Trim());
            }
        }
    }

    return [.. set];
}

static bool ReadRuleBool(JsonElement modObj, string propName)
{
    if (!modObj.TryGetProperty(propName, out var prop))
    {
        return false;
    }

    if (prop.ValueKind == JsonValueKind.True) return true;
    return prop.ValueKind == JsonValueKind.Object &&
           prop.TryGetProperty("value", out var v) &&
           v.ValueKind == JsonValueKind.True;
}

static void WriteStringArrayIfAny(Utf8JsonWriter writer, string name, List<string> values)
{
    if (values.Count == 0) return;
    writer.WriteStartArray(name);
    foreach (var v in values)
    {
        writer.WriteStringValue(v);
    }
    writer.WriteEndArray();
}

static byte[] TrimSteamWorkshopDb(byte[] rawUtf8, out int count, out long version)
{
    using var doc = JsonDocument.Parse(rawUtf8);
    var root = doc.RootElement;
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("database", out var dbProp) ||
        dbProp.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidOperationException("Upstream steamDB.json is missing 'database' object.");
    }

    version = root.TryGetProperty("version", out var vProp) && vProp.TryGetInt64(out var v)
        ? v
        : 0;

    // First pass: count inbound dependency references per PublishedFileId so canonical frameworks
    // (e.g. Harmony 2009463077, HugsLib 818773962) always beat reuploads/forks sharing the same packageId.
    var refCounts = new Dictionary<ulong, int>();
    foreach (var entry in dbProp.EnumerateObject())
    {
        if (entry.Value.ValueKind == JsonValueKind.Object &&
            entry.Value.TryGetProperty("dependencies", out var deps) &&
            deps.ValueKind == JsonValueKind.Object)
        {
            foreach (var dep in deps.EnumerateObject())
            {
                if (ulong.TryParse(dep.Name, out var depFileId))
                {
                    refCounts[depFileId] = refCounts.GetValueOrDefault(depFileId) + 1;
                }
            }
        }
    }

    var deprecatedPattern = new Regex(
        @"deprecated|obsolete|outdated|old\s*version|don'?t\s*download|do\s*not\s*download|discontinued|abandoned|废弃|弃用|停更|旧版",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    var bestByPackage = new SortedDictionary<string, WorkshopCandidate>(StringComparer.Ordinal);
    foreach (var entry in dbProp.EnumerateObject())
    {
        if (!ulong.TryParse(entry.Name, out var publishedFileId) || entry.Value.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var val = entry.Value;
        if (val.TryGetProperty("appid", out var appIdFlag) && appIdFlag.ValueKind == JsonValueKind.True)
        {
            continue;
        }

        string? pkg = null;
        if (val.TryGetProperty("packageId", out var pkgProp) && pkgProp.ValueKind == JsonValueKind.String)
        {
            pkg = pkgProp.GetString();
        }
        else if (val.TryGetProperty("packageid", out var pkgLower) && pkgLower.ValueKind == JsonValueKind.String)
        {
            pkg = pkgLower.GetString();
        }

        if (string.IsNullOrWhiteSpace(pkg))
        {
            continue;
        }

        var key = pkg.Trim().ToLowerInvariant();
        if (key.StartsWith("ludeon.rimworld", StringComparison.Ordinal))
        {
            continue;
        }

        var steamName = val.TryGetProperty("steamName", out var sn) && sn.ValueKind == JsonValueKind.String
            ? sn.GetString() ?? string.Empty
            : string.Empty;
        var isDeprecated = deprecatedPattern.IsMatch(steamName);
        var isTranslation = HasTag(val, "Translation");
        var maxVer = ReadMaxVersionScore(val);
        var refs = refCounts.GetValueOrDefault(publishedFileId);

        var candidate = new WorkshopCandidate(publishedFileId, isDeprecated, isTranslation, maxVer, refs);
        if (!bestByPackage.TryGetValue(key, out var existing) || candidate.IsBetterThan(existing))
        {
            bestByPackage[key] = candidate;
        }
    }

    count = bestByPackage.Count;
    using var ms = new MemoryStream();
    using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", version);
        writer.WriteStartObject("mods");
        foreach (var (pkg, cand) in bestByPackage)
        {
            writer.WriteNumber(pkg, cand.PublishedFileId);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    return ms.ToArray();
}

static bool HasTag(JsonElement val, string targetTag)
{
    if (!val.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
    {
        return false;
    }

    foreach (var t in tags.EnumerateArray())
    {
        if (t.ValueKind == JsonValueKind.Object &&
            t.TryGetProperty("tag", out var tn) &&
            string.Equals(tn.GetString(), targetTag, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static int ReadMaxVersionScore(JsonElement val)
{
    if (!val.TryGetProperty("gameVersions", out var vers) || vers.ValueKind != JsonValueKind.Array)
    {
        return 0;
    }

    var best = 0;
    foreach (var v in vers.EnumerateArray())
    {
        if (v.ValueKind != JsonValueKind.String) continue;
        var s = v.GetString();
        if (string.IsNullOrWhiteSpace(s)) continue;
        var dot = s.IndexOf('.');
        if (dot <= 0) continue;
        if (int.TryParse(s.AsSpan(0, dot), out var major))
        {
            var rest = s[(dot + 1)..];
            var end = 0;
            while (end < rest.Length && char.IsDigit(rest[end])) end++;
            if (end > 0 && int.TryParse(rest.AsSpan(0, end), out var minor))
            {
                var score = major * 100 + minor;
                if (score > best) best = score;
            }
        }
    }

    return best;
}

static byte[] CompressGzip(byte[] raw)
{
    using var ms = new MemoryStream();
    // Zero-out OS/mtime header fragility is handled by WriteIfContentChangedAsync comparing decompressed payload
    using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
    {
        gz.Write(raw, 0, raw.Length);
    }
    return ms.ToArray();
}

static async Task WriteIfContentChangedAsync(string path, byte[] newBytes)
{
    if (File.Exists(path))
    {
        var oldBytes = await File.ReadAllBytesAsync(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            if (DecompressEqual(oldBytes, newBytes))
            {
                return;
            }
        }
        else if (oldBytes.AsSpan().SequenceEqual(newBytes))
        {
            return;
        }
    }

    await File.WriteAllBytesAsync(path, newBytes);
}

static bool DecompressEqual(byte[] gzA, byte[] gzB)
{
    try
    {
        using var inA = new GZipStream(new MemoryStream(gzA), CompressionMode.Decompress);
        using var outA = new MemoryStream();
        inA.CopyTo(outA);

        using var inB = new GZipStream(new MemoryStream(gzB), CompressionMode.Decompress);
        using var outB = new MemoryStream();
        inB.CopyTo(outB);

        return outA.GetBuffer().AsSpan(0, (int)outA.Length)
            .SequenceEqual(outB.GetBuffer().AsSpan(0, (int)outB.Length));
    }
    catch
    {
        return false;
    }
}

internal sealed record ModRuleSlim(
    List<string> LoadAfter,
    List<string> LoadBefore,
    List<string> IncompatibleWith,
    bool LoadBottom,
    bool LoadTop);

internal readonly record struct WorkshopCandidate(
    ulong PublishedFileId,
    bool IsDeprecated,
    bool IsTranslation,
    int MaxVersionScore,
    int InboundReferences)
{
    public bool IsBetterThan(WorkshopCandidate other)
    {
        if (IsDeprecated != other.IsDeprecated) return !IsDeprecated;
        if (IsTranslation != other.IsTranslation) return !IsTranslation;
        if (MaxVersionScore != other.MaxVersionScore) return MaxVersionScore > other.MaxVersionScore;
        if (InboundReferences != other.InboundReferences) return InboundReferences > other.InboundReferences;
        return PublishedFileId > other.PublishedFileId;
    }
}
