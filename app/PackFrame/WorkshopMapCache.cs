using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

public sealed record WorkshopMapSnapshot(
    long Version,
    IReadOnlyDictionary<string, ulong> Mods
);

internal sealed class WorkshopMapCache
{
    public const string GitHubMapUrl =
        "https://raw.githubusercontent.com/ZXUW/Rimworld-PackFrame/main/update/workshopMap.json.gz";
    public const string GiteeMirrorMapUrl =
        "https://gitee.com/zxuw/Rimworld-PackFrame/raw/main/update/workshopMap.json.gz";

    private const int MaxCompressedBytes = 4 * 1024 * 1024;
    private const int MaxDecompressedBytes = 12 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, uint> OfficialAppIds =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["ludeon.rimworld"] = 294100,
            ["ludeon.rimworld.royalty"] = 1149640,
            ["ludeon.rimworld.ideology"] = 1392840,
            ["ludeon.rimworld.biotech"] = 1826140,
            ["ludeon.rimworld.anomaly"] = 2380740,
            ["ludeon.rimworld.odyssey"] = 3022790
        };

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly byte[] _bundledGzip;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private volatile WorkshopMapSnapshot? _current;

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(4)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    public static WorkshopMapCache Shared { get; } = new(
        CreateHttpClient(),
        Path.Combine(AppContext.BaseDirectory, "runtime", "rules")
    );

    internal WorkshopMapCache(HttpClient http, string cacheDirectory, byte[]? bundledGzip = null)
    {
        _http = http;
        _cacheDirectory = cacheDirectory;
        _bundledGzip = bundledGzip ?? ReadEmbeddedGzip();
    }

    public static uint? TryGetOfficialAppId(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return null;
        return OfficialAppIds.TryGetValue(packageId.Trim(), out var appId) ? appId : null;
    }

    public ulong? TryGetWorkshopId(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return null;
        var snapshot = LoadLocal();
        var trimmed = packageId.Trim();
        if (snapshot.Mods.TryGetValue(trimmed, out var id))
        {
            return id;
        }

        var stem = AutoSortPrerequisites.StripIdSuffix(trimmed);
        return !string.Equals(stem, trimmed, StringComparison.OrdinalIgnoreCase) &&
               snapshot.Mods.TryGetValue(stem, out var stemId)
            ? stemId
            : null;
    }

    public WorkshopMapSnapshot LoadLocal()
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

        var bundled = ParseGzip(_bundledGzip);
        _current = bundled;
        return bundled;
    }

    public async Task<WorkshopMapSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var meta = ReadCacheMeta();
            var candidates = new[] { GiteeMirrorMapUrl, GitHubMapUrl };
            Exception? lastError = null;
            var baseline = LoadLocal();
            var anyResponded = false;

            foreach (var url in candidates)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
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
                    var gzBytes = await ReadLimitedBytesAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    var parsed = ParseGzip(gzBytes);
                    anyResponded = true;

                    if (parsed.Version > 0 && baseline.Version > parsed.Version)
                    {
                        continue;
                    }

                    SaveCache(gzBytes, new CommunityRulesCacheEntry
                    {
                        SourceUrl = url,
                        ContentHash = Hash(gzBytes),
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
                    OperationCanceledException or JsonException or InvalidOperationException or InvalidDataException)
                {
                    lastError = ex;
                }
            }

            if (!anyResponded && lastError is not null)
            {
                throw lastError;
            }

            return baseline;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public static WorkshopMapSnapshot ParseGzip(byte[] gzBytes)
    {
        using var input = new MemoryStream(gzBytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (output.Length + read > MaxDecompressedBytes)
            {
                throw new InvalidOperationException("Decompressed workshop map exceeds size limit.");
            }
            output.Write(chunk, 0, read);
        }

        return ParseJson(output.GetBuffer().AsSpan(0, (int)output.Length));
    }

    public static WorkshopMapSnapshot ParseJson(ReadOnlySpan<byte> utf8Json)
    {
        using var doc = JsonDocument.Parse(utf8Json.ToArray());
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("mods", out var modsProp) ||
            modsProp.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Workshop map JSON has no 'mods' object.");
        }

        long version = 0;
        if (doc.RootElement.TryGetProperty("version", out var vProp) &&
            vProp.ValueKind == JsonValueKind.Number &&
            vProp.TryGetInt64(out var v))
        {
            version = v;
        }

        var map = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in modsProp.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;
            if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetUInt64(out var numId) && numId > 0)
            {
                map[entry.Name.Trim()] = numId;
            }
            else if (entry.Value.ValueKind == JsonValueKind.String &&
                     ulong.TryParse(entry.Value.GetString(), out var strId) && strId > 0)
            {
                map[entry.Name.Trim()] = strId;
            }
        }

        return new WorkshopMapSnapshot(version, map);
    }

    private string DataPath => Path.Combine(_cacheDirectory, "workshopMap.json.gz");
    private string MetaPath => Path.Combine(_cacheDirectory, "workshopMap.meta.json");

    private WorkshopMapSnapshot? TryLoadCachedSnapshot()
    {
        try
        {
            var meta = ReadCacheMeta();
            if (meta is null || !File.Exists(DataPath)) return null;
            var info = new FileInfo(DataPath);
            if (info.Length <= 0 || info.Length > MaxCompressedBytes) return null;
            var gzBytes = File.ReadAllBytes(DataPath);
            if (!string.Equals(Hash(gzBytes), meta.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parsed = ParseGzip(gzBytes);
            var bundled = ParseGzip(_bundledGzip);
            return bundled.Version > parsed.Version ? bundled : parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException)
        {
            return null;
        }
    }

    private CommunityRulesCacheEntry? ReadCacheMeta()
    {
        try
        {
            if (!File.Exists(MetaPath)) return null;
            var json = File.ReadAllText(MetaPath, Encoding.UTF8);
            return JsonSerializer.Deserialize(json, PackFrameJsonContext.Default.CommunityRulesCacheEntry);
        }
        catch
        {
            return null;
        }
    }

    private void SaveCache(byte[] gzBytes, CommunityRulesCacheEntry meta)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var metaJson = JsonSerializer.Serialize(meta, PackFrameJsonContext.Default.CommunityRulesCacheEntry);
            FileTransactions.WriteAtomic(DataPath, tmp => File.WriteAllBytes(tmp, gzBytes));
            FileTransactions.WriteAtomic(MetaPath, tmp => File.WriteAllText(tmp, metaJson, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Save workshop map cache", ex);
        }
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxCompressedBytes)
        {
            throw new InvalidOperationException("Workshop map archive exceeds size limit.");
        }

        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > MaxCompressedBytes)
            {
                throw new InvalidOperationException("Workshop map archive exceeds size limit.");
            }
            buffer.Write(chunk, 0, count);
        }

        return buffer.ToArray();
    }

    private static string Hash(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static byte[] ReadEmbeddedGzip()
    {
        using var stream = typeof(WorkshopMapCache).Assembly
            .GetManifestResourceStream("PackFrame.Assets.WorkshopMap.json.gz")
            ?? throw new InvalidOperationException("Bundled WorkshopMap.json.gz is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
