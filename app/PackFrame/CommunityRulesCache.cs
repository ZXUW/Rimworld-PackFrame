using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

public sealed record CommunityModRule(
    IReadOnlyList<string> LoadAfter,
    IReadOnlyList<string> LoadBefore,
    IReadOnlyList<string> IncompatibleWith,
    bool LoadBottom,
    bool LoadTop
);

public sealed record CommunityRulesSnapshot(
    long Timestamp,
    IReadOnlyDictionary<string, CommunityModRule> Rules
);

internal sealed class CommunityRulesCacheEntry
{
    public string SourceUrl { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
}

internal sealed class CommunityRulesCache
{
    public const string GitHubRulesUrl =
        "https://raw.githubusercontent.com/ZXUW/Rimworld-PackFrame/main/update/communityRules.json";
    public const string GiteeMirrorRulesUrl =
        "https://gitee.com/zxuw/Rimworld-PackFrame/raw/main/update/communityRules.json";

    private const int MaxRulesBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly string _bundledJson;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private volatile CommunityRulesSnapshot? _current;

    public static CommunityRulesCache Shared { get; } = new(
        new HttpClient(),
        Path.Combine(AppContext.BaseDirectory, "runtime", "rules")
    );

    internal CommunityRulesCache(HttpClient http, string cacheDirectory, string? bundledJson = null)
    {
        _http = http;
        _cacheDirectory = cacheDirectory;
        _bundledJson = bundledJson ?? ReadEmbeddedRules();
    }

    public CommunityRulesSnapshot LoadLocal()
    {
        var existing = _current;
        if (existing is not null)
        {
            return existing;
        }

        var cached = TryLoadCachedSnapshot();
        if (cached is not null)
        {
            _current = cached;
            return cached;
        }

        var bundled = ParseSnapshot(_bundledJson);
        _current = bundled;
        return bundled;
    }

    public async Task<CommunityRulesSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var meta = ReadCacheMeta();
            var candidates = new[] { GitHubRulesUrl, GiteeMirrorRulesUrl };
            Exception? lastError = null;

            foreach (var url in candidates)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("Rimworld-PackFrame");
                    var sameSource = meta is not null &&
                                     string.Equals(meta.SourceUrl, url, StringComparison.OrdinalIgnoreCase) &&
                                     File.Exists(DataPath);
                    if (sameSource && !string.IsNullOrWhiteSpace(meta!.ETag) &&
                        EntityTagHeaderValue.TryParse(meta.ETag, out var etag))
                    {
                        request.Headers.IfNoneMatch.Add(etag);
                    }
                    else if (sameSource && meta!.LastModified is { } modified)
                    {
                        request.Headers.IfModifiedSince = modified;
                    }

                    using var response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token
                    ).ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotModified && sameSource)
                    {
                        var cached = TryLoadCachedSnapshot();
                        if (cached is not null)
                        {
                            _current = cached;
                            return cached;
                        }
                    }

                    response.EnsureSuccessStatusCode();
                    var json = await ReadLimitedJsonAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    var parsed = ParseSnapshot(json);

                    // Keep the higher timestamp if a mirror lags behind bundled or cached data
                    var baseline = LoadLocal();
                    if (parsed.Timestamp > 0 && baseline.Timestamp > parsed.Timestamp)
                    {
                        return baseline;
                    }

                    SaveCache(json, new CommunityRulesCacheEntry
                    {
                        SourceUrl = url,
                        ContentHash = Hash(json),
                        ETag = response.Headers.ETag?.ToString(),
                        LastModified = response.Content.Headers.LastModified
                    });
                    _current = parsed;
                    return parsed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or
                    OperationCanceledException or JsonException or InvalidOperationException or DecoderFallbackException)
                {
                    lastError = ex;
                }
            }

            if (lastError is not null)
            {
                throw lastError;
            }

            return LoadLocal();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public static CommunityRulesSnapshot ParseSnapshot(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("rules", out var rulesProp) ||
            rulesProp.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Community rules JSON has no rules object.");
        }

        long timestamp = 0;
        if (doc.RootElement.TryGetProperty("timestamp", out var tsProp) &&
            tsProp.ValueKind == JsonValueKind.Number &&
            tsProp.TryGetInt64(out var ts))
        {
            timestamp = ts;
        }

        var map = new Dictionary<string, CommunityModRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var modEntry in rulesProp.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(modEntry.Name) || modEntry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var loadAfter = ReadKeySet(modEntry.Value, "loadAfter");
            var loadBefore = ReadKeySet(modEntry.Value, "loadBefore");
            var incompatible = ReadKeySet(modEntry.Value, "incompatibleWith");
            var loadBottom = ReadBoolFlag(modEntry.Value, "loadBottom");
            var loadTop = ReadBoolFlag(modEntry.Value, "loadTop");

            if (loadAfter.Count == 0 && loadBefore.Count == 0 && incompatible.Count == 0 && !loadBottom && !loadTop)
            {
                continue;
            }

            map[modEntry.Name.Trim()] = new CommunityModRule(loadAfter, loadBefore, incompatible, loadBottom, loadTop);
        }

        return new CommunityRulesSnapshot(timestamp, map);
    }

    private static IReadOnlyList<string> ReadKeySet(JsonElement modObj, string propertyName)
    {
        if (!modObj.TryGetProperty(propertyName, out var prop))
        {
            return Array.Empty<string>();
        }

        if (prop.ValueKind == JsonValueKind.Object)
        {
            var list = new List<string>();
            foreach (var p in prop.EnumerateObject())
            {
                if (!string.IsNullOrWhiteSpace(p.Name))
                {
                    list.Add(p.Name.Trim());
                }
            }
            return list;
        }

        if (prop.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    list.Add(item.GetString()!.Trim());
                }
            }
            return list;
        }

        return Array.Empty<string>();
    }

    private static bool ReadBoolFlag(JsonElement modObj, string propertyName)
    {
        if (!modObj.TryGetProperty(propertyName, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.True) return true;
        if (prop.ValueKind == JsonValueKind.Object &&
            prop.TryGetProperty("value", out var val) &&
            val.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return false;
    }

    private string DataPath => Path.Combine(_cacheDirectory, "communityRules.json");
    private string MetaPath => Path.Combine(_cacheDirectory, "communityRules.meta.json");

    private CommunityRulesSnapshot? TryLoadCachedSnapshot()
    {
        try
        {
            var meta = ReadCacheMeta();
            if (meta is null || !File.Exists(DataPath)) return null;
            var info = new FileInfo(DataPath);
            if (info.Length <= 0 || info.Length > MaxRulesBytes) return null;
            var json = File.ReadAllText(DataPath, Utf8);
            if (!string.Equals(Hash(json), meta.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parsed = ParseSnapshot(json);
            var bundled = ParseSnapshot(_bundledJson);
            return bundled.Timestamp > parsed.Timestamp ? bundled : parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private CommunityRulesCacheEntry? ReadCacheMeta()
    {
        try
        {
            if (!File.Exists(MetaPath)) return null;
            var json = File.ReadAllText(MetaPath, Utf8);
            return JsonSerializer.Deserialize(json, PackFrameJsonContext.Default.CommunityRulesCacheEntry);
        }
        catch
        {
            return null;
        }
    }

    private void SaveCache(string json, CommunityRulesCacheEntry meta)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var metaJson = JsonSerializer.Serialize(meta, PackFrameJsonContext.Default.CommunityRulesCacheEntry);
            FileTransactions.WriteAtomic(DataPath, tmp => File.WriteAllText(tmp, json, Utf8));
            FileTransactions.WriteAtomic(MetaPath, tmp => File.WriteAllText(tmp, metaJson, Utf8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Save community rules cache", ex);
        }
    }

    private static async Task<string> ReadLimitedJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxRulesBytes)
        {
            throw new InvalidOperationException("Community rules JSON exceeds size limit.");
        }

        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > MaxRulesBytes)
            {
                throw new InvalidOperationException("Community rules JSON exceeds size limit.");
            }
            buffer.Write(chunk, 0, count);
        }

        return Utf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(value)));

    private static string ReadEmbeddedRules()
    {
        using var stream = typeof(CommunityRulesCache).Assembly
            .GetManifestResourceStream("PackFrame.Assets.CommunityRules.json")
            ?? throw new InvalidOperationException("Bundled CommunityRules.json is missing.");
        using var reader = new StreamReader(stream, Utf8);
        return reader.ReadToEnd();
    }
}
