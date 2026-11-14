using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

internal sealed record UpdateAsset(string Name, string Url, long Size, string Sha256);

internal sealed record UpdateInfo(
    string Tag,
    string Url,
    string? ReleaseNotes = null,
    UpdateAsset? Asset = null,
    string? Name = null,
    DateTimeOffset? PublishedAt = null);

internal static class UpdateService
{
    internal const string RepositoryUrl = "https://github.com/ZXUW/Rimworld-PackFrame";
    internal const string LatestReleaseUrl = "https://api.github.com/repos/ZXUW/Rimworld-PackFrame/releases/latest";
    internal const long MaximumArchiveSize = 1024L * 1024 * 1024;
    private const int MaximumMetadataSize = 4 * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string CurrentVersion
    {
        get
        {
            var version = typeof(UpdateService).Assembly.GetName().Version;
            return version is null
                ? "1.0.13"
                : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }

    public static Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(Http, OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsMacOS() ? "macos" : "linux",
            RuntimeInformation.ProcessArchitecture, cancellationToken);

    // This only discovers a published release. Downloading and installing are
    // separate actions owned by the update window, after the user's choice.
    internal static async Task<UpdateInfo?> CheckAsync(
        HttpClient client,
        string platform,
        Architecture architecture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.ParseAdd("PackFrame/" + CurrentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumMetadataSize)
            throw new InvalidDataException("GitHub release metadata is too large.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > MaximumMetadataSize)
                throw new InvalidDataException("GitHub release metadata is too large.");
            bytes.Write(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(bytes.ToArray());
        return ParseRelease(document.RootElement, platform, architecture);
    }

    public static bool IsNewer(string tag) => IsNewer(tag, CurrentVersion);

    internal static bool IsNewer(string tag, string installedVersion) =>
        TryStableVersion(tag, out var latest) &&
        TryStableVersion(installedVersion, out var installed) && latest > installed;

    private static UpdateInfo? ParseRelease(JsonElement release, string platform, Architecture architecture)
    {
        if (release.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GitHub release metadata is not an object.");
        if (IsTrue(release, "draft") || IsTrue(release, "prerelease")) return null;
        var tag = ReadString(release, "tag_name")?.Trim();
        if (tag is null || !TryStableVersion(tag, out _))
            throw new InvalidDataException("GitHub release has an unsupported version tag.");

        var url = RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(tag);
        var suppliedUrl = ReadString(release, "html_url");
        if (IsRepositoryUrl(suppliedUrl, new[] { "releases", "tag", tag })) url = suppliedUrl!;
        var asset = string.Equals(platform, "windows", StringComparison.OrdinalIgnoreCase)
            ? SelectWindowsAsset(release, tag, architecture) : null;
        DateTimeOffset? published = null;
        if (release.TryGetProperty("published_at", out var date) &&
            date.ValueKind == JsonValueKind.String && date.TryGetDateTimeOffset(out var parsed))
            published = parsed;
        return new UpdateInfo(tag, url, ReadString(release, "body"), asset,
            ReadString(release, "name"), published);
    }

    private static UpdateAsset? SelectWindowsAsset(JsonElement release, string tag, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (arch is null || !release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array) return null;
        var names = new List<string>
        {
            $"PackFrame-win-{arch}.zip",
            $"Rimworld.PackFrame-win-{arch}.zip",
            $"Rimworld-PackFrame-win-{arch}.zip"
        };
        if (architecture == Architecture.X64)
        {
            names.Add("Rimworld.PackFrame-Window64.zip");
            names.Add("Rimworld.PackFrame-Windows64.zip");
        }
        var candidates = new List<(int Rank, UpdateAsset Asset)>();
        foreach (var item in assets.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = ReadString(item, "name");
            var rank = names.FindIndex(expected => string.Equals(expected, name, StringComparison.OrdinalIgnoreCase));
            if (rank < 0 || name is null) continue;
            var url = ReadString(item, "browser_download_url");
            if (!IsRepositoryUrl(url, new[] { "releases", "download", tag, name })) continue;
            var digest = ReadString(item, "digest");
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) continue;
            var sha = digest[7..];
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) continue;
            if (!item.TryGetProperty("size", out var sizeValue) || sizeValue.ValueKind != JsonValueKind.Number ||
                !sizeValue.TryGetInt64(out var size) || size <= 0 || size > MaximumArchiveSize) continue;
            candidates.Add((rank, new UpdateAsset(name, url!, size, sha.ToLowerInvariant())));
        }
        if (candidates.Count == 0) return null;
        var firstRank = candidates.Min(candidate => candidate.Rank);
        var best = candidates.Where(candidate => candidate.Rank == firstRank).ToList();
        // Duplicate/case-aliased names are ambiguous; let the user use GitHub.
        return best.Count == 1 ? best[0].Asset : null;
    }

    private static bool IsRepositoryUrl(string? text, IReadOnlyList<string> suffix)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        var parts = Uri.UnescapeDataString(uri.AbsolutePath).Split('/');
        if (parts.Length != suffix.Count + 3 || parts[0].Length != 0 ||
            !string.Equals(parts[1], "ZXUW", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[2], "Rimworld-PackFrame", StringComparison.OrdinalIgnoreCase)) return false;
        for (var i = 0; i < suffix.Count; i++)
            if (!string.Equals(parts[i + 3], suffix[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool TryStableVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text[0] is 'v' or 'V') text = text[1..];
        var metadata = text.Split('+');
        if (metadata.Length > 2 || metadata.Length == 2 &&
            (metadata[1].Length == 0 || metadata[1].Split('.').Any(part => part.Length == 0 ||
                part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))) return false;
        var parts = metadata[0].Split('.');
        if (parts.Length is < 2 or > 4) return false;
        var values = new int[4];
        for (var i = 0; i < parts.Length; i++)
            if (parts[i].Length == 0 || parts[i].Length > 1 && parts[i][0] == '0' ||
                parts[i].Any(c => !char.IsAsciiDigit(c)) ||
                !int.TryParse(parts[i], out values[i])) return false;
        version = new Version(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    private static bool IsTrue(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
}


