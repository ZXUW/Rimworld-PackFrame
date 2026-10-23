using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PackFrame;

public sealed class ModpackIndex
{
    public int SchemaVersion { get; set; } = 1;
    public List<ModpackEntry> Packs { get; set; } = new();
}

public sealed class ModpackEntry
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? Image { get; set; }
    public string? File { get; set; }
    public string? GiteeFile { get; set; }
    public int ModCount { get; set; }
}

public sealed class ModpackDefinition
{
    public long? Revision { get; set; }
    public string? Version { get; set; }
    public string? Changelog { get; set; }
    public string? PackSource { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Image { get; set; }
    public string? GameVersion { get; set; }
    public string? WorkshopCollectionId { get; set; }
    public string? WorkshopUrl { get; set; }
    public string? ConfigBaseUrl { get; set; }
    public string? Repo { get; set; }
    public string? Ref { get; set; }
    public string? ModsArchive { get; set; }
    public List<string> ConfigFiles { get; set; } = new();
    public List<ModpackMod> Mods { get; set; } = new();
    public List<string> PackageIds { get; set; } = new();
}

public sealed class ModpackMod
{
    public string? Source { get; set; }
    public string? Name { get; set; }
    public string? WorkshopId { get; set; }
    public string? WorkshopUrl { get; set; }
    public string? PackageId { get; set; }
    public string? Repo { get; set; }
    public string? Asset { get; set; }
    public string? Tag { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Path { get; set; }
}

public sealed record ModpackInstallResult(
    string Name,
    IReadOnlyList<string> PackageIds,
    int WorkshopCount,
    int GithubCount,
    int LocalCount,
    int UnresolvedWorkshopCount,
    string? ModsSourcePath,
    string? ConfigSourcePath,
    string? Id,
    string? ConfigBaseUrl,
    IReadOnlyList<string> ConfigFiles,
    bool SteamCmd,
    string? PackSource = null,
    long? Revision = null,
    string? Version = null
);

public static class ModpackService
{
    public const string GiteeIndexUrl =
        "https://gitee.com/zxuw/PackFrame-Modpacks/raw/main/index.json";
    public const string GitHubIndexUrl =
        "https://raw.githubusercontent.com/ZXUW/PackFrame-Modpacks/main/index.json";

    private const long MaxDownloadBytes = 512L * 1024 * 1024;

    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly Regex WorkshopIdPattern = new(
        @"[?&]id=(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static string NormalizeIndexUrl(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("Modpack repository URL is empty.");
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Modpack repository URL is invalid.");
        }

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
            );
            if (parts.Length >= 2)
            {
                if (parts.Length >= 4 &&
                    parts[2].Equals("blob", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = string.Join('/', parts.Skip(4));
                    return $"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{parts[3]}/{rest}";
                }

                return $"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/main/index.json";
            }
        }

        if (text.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return text.TrimEnd('/') + "/index.json";
    }

    private static bool IsKnownIndex(string url)
    {
        return url.Equals(GiteeIndexUrl, StringComparison.OrdinalIgnoreCase) ||
            url.Equals(GitHubIndexUrl, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<ModpackIndex> LoadIndexAsync(
        string? url,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = NormalizeIndexUrl(url);
        var candidates = new List<string>();
        if (IsKnownIndex(normalized))
        {
            if (normalized.Contains("gitee.com", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(GiteeIndexUrl);
                candidates.Add(GitHubIndexUrl);
            }
            else
            {
                candidates.Add(GitHubIndexUrl);
                candidates.Add(GiteeIndexUrl);
            }
        }
        else
        {
            candidates.Add(normalized);
            if (normalized.Contains(
                    "raw.githubusercontent.com/",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                normalized.Contains("/main/index.json", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(normalized.Replace(
                    "/main/index.json",
                    "/master/index.json",
                    StringComparison.OrdinalIgnoreCase
                ));
            }
        }

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var json = await Http.GetStringAsync(candidate, cancellationToken);
                var index = JsonSerializer.Deserialize(
                    json,
                    PackFrameJsonContext.Default.ModpackIndex
                );
                if (index is null)
                {
                    throw new InvalidOperationException("Modpack index is empty.");
                }

                index.Packs ??= new List<ModpackEntry>();
                return index;
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode == HttpStatusCode.NotFound &&
                candidate != candidates[^1]
            )
            {
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException("Modpack index could not be loaded.");
    }

    public static async Task<ModpackDefinition> LoadPackAsync(
        ModpackEntry entry,
        Uri indexUri,
        CancellationToken cancellationToken = default
    )
    {
        var files = new List<string>();
        var giteeFirst = indexUri.Host.Contains("gitee.com", StringComparison.OrdinalIgnoreCase);
        void AddFile(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) files.Add(value);
        }
        if (giteeFirst)
        {
            AddFile(entry.GiteeFile);
            AddFile(entry.File);
        }
        else
        {
            AddFile(entry.File);
            AddFile(entry.GiteeFile);
        }
        if (files.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                throw new InvalidOperationException("Modpack entry has no id or file.");
            }

            files.Add($"packs/{entry.Id}.json");
        }

        Exception? lastError = null;
        foreach (var file in files)
        {
            try
            {
                var packUri = new Uri(indexUri, file);
                using var request = new HttpRequestMessage(HttpMethod.Get, packUri);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                using var response = await Http.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var pack = JsonSerializer.Deserialize(
                    json,
                    PackFrameJsonContext.Default.ModpackDefinition
                ) ?? throw new InvalidOperationException("Modpack file is empty.");

                pack.Mods ??= new List<ModpackMod>();
                pack.PackageIds ??= new List<string>();
                pack.ConfigFiles ??= new List<string>();
                pack.Id ??= entry.Id;
                pack.PackSource = indexUri.AbsoluteUri;
                pack.Name = string.IsNullOrWhiteSpace(pack.Name) ? entry.Name : pack.Name;
                pack.Description = string.IsNullOrWhiteSpace(pack.Description)
                    ? entry.Summary
                    : pack.Description;
                pack.Image = !string.IsNullOrWhiteSpace(pack.Image)
                    ? ResolveImage(packUri, pack.Image)
                    : ResolveImage(indexUri, entry.Image);
                if (!string.IsNullOrWhiteSpace(pack.ModsArchive) &&
                    !Uri.TryCreate(pack.ModsArchive, UriKind.Absolute, out _))
                {
                    pack.ModsArchive = new Uri(packUri, pack.ModsArchive).AbsoluteUri;
                }
                if (!string.IsNullOrWhiteSpace(pack.ConfigBaseUrl) &&
                    !Uri.TryCreate(pack.ConfigBaseUrl, UriKind.Absolute, out _))
                {
                    pack.ConfigBaseUrl = new Uri(packUri, pack.ConfigBaseUrl).AbsoluteUri;
                }
                else if (string.IsNullOrWhiteSpace(pack.ConfigBaseUrl) &&
                    pack.ConfigFiles.Count > 0)
                {
                    pack.ConfigBaseUrl = new Uri(packUri, "config/").AbsoluteUri;
                }
                return pack;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException("Modpack file could not be loaded.");
    }

    internal static string? ResolveImage(Uri baseUri, string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        return Uri.TryCreate(baseUri, image, out var resolved)
            ? resolved.AbsoluteUri
            : image;
    }

    public static async Task<ModpackInstallResult> InstallAsync(
        ModpackDefinition pack,
        RimWorldInstallation game,
        string packRoot,
        string modsRoot,
        string? workshopLink = null,
        bool useSteamCmd = false,
        bool skipModsDownload = false,
        Action<string>? status = null,
        CancellationToken cancellationToken = default,
        string? language = null
    )
    {
        if (string.IsNullOrWhiteSpace(game.GamePath))
        {
            throw new InvalidOperationException("RimWorld game path is not set.");
        }

        Directory.CreateDirectory(modsRoot);
        var modsArchiveExtracted = false;
        if (!skipModsDownload && !string.IsNullOrWhiteSpace(pack.ModsArchive))
        {
            var archiveUri = ResolveModsArchiveUri(pack);
            if (archiveUri is null)
            {
                throw new InvalidOperationException(
                    "The local mods archive has no download URL."
                );
            }

            var temporaryArchive = Path.Combine(
                Path.GetTempPath(),
                "packframe-mods-" + Guid.NewGuid().ToString("N") + ".zip"
            );
            try
            {
                status?.Invoke(OperationText.Download(language));
                await DownloadAsync(
                    archiveUri,
                    temporaryArchive,
                    status,
                    cancellationToken, language
                );
                status?.Invoke(OperationText.Extract(language));
                ZipFile.ExtractToDirectory(
                    temporaryArchive,
                    modsRoot,
                    overwriteFiles: true
                );
                modsArchiveExtracted = true;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryArchive))
                    {
                        File.Delete(temporaryArchive);
                    }
                }
                catch
                {
                }
            }
        }

        var stagingRoot = Path.GetDirectoryName(modsRoot);
        string? configSourcePath = null;
        if (!string.IsNullOrWhiteSpace(stagingRoot) &&
            pack.ConfigFiles.Count > 0 &&
            !string.IsNullOrWhiteSpace(pack.ConfigBaseUrl))
        {
            configSourcePath = Path.Combine(stagingRoot, "Config");
            await DownloadConfigAsync(
                pack,
                configSourcePath,
                status,
                cancellationToken, language
            );
        }

        var packageIds = new List<string>();
        var workshopIds = new List<ulong>();
        var explicitWorkshopPackageIds = new Dictionary<ulong, string>();
        var resolvedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mods = pack.Mods ?? new List<ModpackMod>();
        var githubCount = 0;
        var localCount = 0;

        foreach (var mod in mods.Where(item => item is not null))
        {
            var source = NormalizeSource(mod);
            var packageId = NormalizePackageId(mod.PackageId);
            if (source == "github")
            {
                if (skipModsDownload)
                {
                    AddPackageId(packageId, Array.Empty<string>(), packageIds, resolvedPackageIds);
                    continue;
                }

                var installedIds = await InstallGithubModAsync(
                    mod,
                    modsRoot,
                    cancellationToken
                );
                githubCount++;
                AddPackageId(packageId, installedIds, packageIds, resolvedPackageIds);
                continue;
            }

            if (source == "local")
            {
                localCount++;
                if (skipModsDownload)
                {
                    AddPackageId(packageId, Array.Empty<string>(), packageIds, resolvedPackageIds);
                    continue;
                }

                if (modsArchiveExtracted)
                {
                    AddPackageId(
                        packageId,
                        Array.Empty<string>(),
                        packageIds,
                        resolvedPackageIds
                    );
                    continue;
                }

                var installedIds = InstallLocalMod(mod, modsRoot, game, packRoot);
                AddPackageId(packageId, installedIds, packageIds, resolvedPackageIds);
                continue;
            }

            if (source == "workshop")
            {
                var id = ParseWorkshopId(mod.WorkshopId) ??
                    ParseWorkshopId(mod.WorkshopUrl);
                if (id is null)
                {
                    throw new InvalidOperationException(
                        $"Workshop mod '{mod.Name ?? mod.PackageId ?? "unknown"}' has no valid id."
                    );
                }

                AddUnique(workshopIds, id.Value);
                if (!string.IsNullOrWhiteSpace(packageId))
                {
                    explicitWorkshopPackageIds[id.Value] = packageId;
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(packageId))
            {
                AddPackageId(packageId, Array.Empty<string>(), packageIds, resolvedPackageIds);
            }
        }

        foreach (var packageId in pack.PackageIds ?? new List<string>())
        {
            AddPackageId(
                NormalizePackageId(packageId),
                Array.Empty<string>(),
                packageIds,
                resolvedPackageIds
            );
        }

        var collectionIds = new List<ulong>();
        var collectionId = ParseWorkshopId(pack.WorkshopCollectionId) ??
            ParseWorkshopId(pack.WorkshopUrl);
        if (collectionId is not null)
        {
            await AddWorkshopTargetAsync(
                collectionId.Value,
                workshopIds,
                collectionIds,
                cancellationToken
            );
        }

        var linkId = ParseWorkshopId(workshopLink);
        if (!string.IsNullOrWhiteSpace(workshopLink) && linkId is null)
        {
            throw new InvalidOperationException("Workshop link is invalid.");
        }
        if (linkId is not null)
        {
            await AddWorkshopTargetAsync(
                linkId.Value,
                workshopIds,
                collectionIds,
                cancellationToken
            );
        }

        if (workshopIds.Count > 0)
        {
            if (useSteamCmd)
            {
                var steamCmd = await SteamCmdClient.EnsureAsync(
                    packRoot,
                    status,
                    cancellationToken, language
                );
                await SteamCmdClient.DownloadWorkshopAsync(
                    steamCmd,
                    modsRoot,
                    workshopIds,
                    status,
                    cancellationToken, language
                );
            }
            else
            {
                var gamePath = game.GamePath;
                var subscription = await Task.Run(() =>
                {
                    var success = SteamWorkshopClient.SubscribeMany(
                        gamePath,
                        workshopIds,
                        out var error
                    );
                    return (success, error);
                }, cancellationToken);
                if (!subscription.success)
                {
                    throw new InvalidOperationException(subscription.error);
                }
            }
        }

        foreach (var id in workshopIds)
        {
            if (explicitWorkshopPackageIds.TryGetValue(id, out var explicitId))
            {
                AddPackageId(explicitId, Array.Empty<string>(), packageIds, resolvedPackageIds);
            }

            var installedId = ReadWorkshopPackageId(game.WorkshopPath, id);
            if (string.IsNullOrWhiteSpace(installedId) && useSteamCmd)
            {
                installedId = ReadWorkshopPackageId(modsRoot, id);
            }
            if (!string.IsNullOrWhiteSpace(installedId))
            {
                AddPackageId(installedId, Array.Empty<string>(), packageIds, resolvedPackageIds);
            }
        }

        var unresolvedWorkshopCount = workshopIds.Count(id =>
            !explicitWorkshopPackageIds.ContainsKey(id) &&
            string.IsNullOrWhiteSpace(ReadWorkshopPackageId(game.WorkshopPath, id)) &&
            (!useSteamCmd || string.IsNullOrWhiteSpace(
                ReadWorkshopPackageId(modsRoot, id)
            ))
        );

        if (pack.PackageIds is { Count: > 0 })
        {
            var order = pack.PackageIds
                .Select(NormalizePackageId)
                .Where(id => id is not null)
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var orderSet = new HashSet<string>(order, StringComparer.OrdinalIgnoreCase);
            packageIds = order
                .Where(id => packageIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                .Concat(packageIds.Where(id => !orderSet.Contains(id)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var name = string.IsNullOrWhiteSpace(pack.Name)
            ? "Modpack"
            : pack.Name.Trim();
        return new ModpackInstallResult(
            name,
            packageIds,
            workshopIds.Count,
            githubCount,
            localCount,
            unresolvedWorkshopCount,
            modsRoot,
            configSourcePath,
            pack.Id,
            pack.ConfigBaseUrl,
            pack.ConfigFiles,
            useSteamCmd, pack.PackSource, pack.Revision, pack.Version
        );
    }

    public static async Task DownloadConfigToAsync(
        string configBaseUrl,
        IReadOnlyList<string> configFiles,
        string targetConfigDirectory,
        Action<string>? status = null,
        CancellationToken cancellationToken = default,
        string? language = null
    )
    {
        if (string.IsNullOrWhiteSpace(configBaseUrl) || configFiles.Count == 0)
        {
            throw new InvalidOperationException("This instance has no config source.");
        }

        var staging = Path.Combine(Path.GetTempPath(), "packframe-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var baseUrl = configBaseUrl.TrimEnd('/');
            foreach (var file in configFiles)
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
                    throw new InvalidDataException("Invalid config file name.");
                status?.Invoke(OperationText.Config(language, name));
                var url = baseUrl + "/" + Uri.EscapeDataString(name);
                var destination = Path.Combine(staging, name);
                await DownloadAsync(new Uri(url), destination, null, cancellationToken);
                if (Path.GetExtension(name).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    XDocument.Load(destination);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Once commit starts, finish or roll back rather than interrupting it.
            await Task.Run(() => FileTransactions.ApplyFiles(staging, targetConfigDirectory));
        }
        finally { FileTransactions.TryDeleteDirectory(staging); }
    }

    private static async Task DownloadConfigAsync(
        ModpackDefinition pack,
        string configRoot,
        Action<string>? status,
        CancellationToken cancellationToken, string? language
    )
    {
        await DownloadConfigToAsync(pack.ConfigBaseUrl!, pack.ConfigFiles,
            configRoot, status, cancellationToken, language);
    }

    public static ulong? ParseWorkshopId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (ulong.TryParse(text, out var direct))
        {
            return direct;
        }

        var match = WorkshopIdPattern.Match(text);
        return match.Success && ulong.TryParse(match.Groups[1].Value, out var id)
            ? id
            : null;
    }

    private static async Task AddWorkshopTargetAsync(
        ulong id,
        List<ulong> workshopIds,
        List<ulong> collectionIds,
        CancellationToken cancellationToken
    )
    {
        var children = await ResolveCollectionAsync(id, cancellationToken);
        if (children.Count == 0)
        {
            AddUnique(workshopIds, id);
            return;
        }

        AddUnique(collectionIds, id);
        foreach (var child in children)
        {
            AddUnique(workshopIds, child);
        }
    }

    private static async Task<List<ulong>> ResolveCollectionAsync(
        ulong collectionId,
        CancellationToken cancellationToken
    )
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["collectioncount"] = "1",
            ["publishedfileids[0]"] = collectionId.ToString()
        });
        using var response = await Http.PostAsync(
            "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/",
            content,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var responseElement) ||
            !responseElement.TryGetProperty("collectiondetails", out var details) ||
            details.ValueKind != JsonValueKind.Array)
        {
            return new List<ulong>();
        }

        var result = new List<ulong>();
        foreach (var detail in details.EnumerateArray())
        {
            if (!detail.TryGetProperty("result", out var resultElement) ||
                resultElement.GetInt32() != 1 ||
                !detail.TryGetProperty("children", out var children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var child in children.EnumerateArray())
            {
                if (!child.TryGetProperty("publishedfileid", out var idElement))
                {
                    continue;
                }

                if (child.TryGetProperty("filetype", out var fileType) &&
                    fileType.GetInt32() != 0)
                {
                    continue;
                }

                if (ulong.TryParse(idElement.GetString(), out var childId))
                {
                    AddUnique(result, childId);
                }
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> InstallGithubModAsync(
        ModpackMod mod,
        string modsRoot,
        CancellationToken cancellationToken
    )
    {
        var uri = await ResolveGithubDownloadAsync(mod, cancellationToken);
        return await InstallZipAsync(uri, modsRoot, cancellationToken);
    }

    private static async Task<Uri> ResolveGithubDownloadAsync(
        ModpackMod mod,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(mod.DownloadUrl))
        {
            return new Uri(mod.DownloadUrl.Trim());
        }

        var repo = (mod.Repo ?? string.Empty).Trim();
        if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo["https://github.com/".Length..];
        }
        repo = repo.Trim('/');
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }
        if (repo.Length == 0 || repo.Count(character => character == '/') != 1)
        {
            throw new InvalidOperationException("GitHub mod has no valid repo.");
        }

        var releaseUrl = string.IsNullOrWhiteSpace(mod.Tag)
            ? $"https://api.github.com/repos/{repo}/releases/latest"
            : $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(mod.Tag.Trim())}";
        try
        {
            var json = await Http.GetStringAsync(releaseUrl, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                var candidates = assets.EnumerateArray()
                    .Select(asset => new
                    {
                        Name = asset.TryGetProperty("name", out var name)
                            ? name.GetString() ?? string.Empty
                            : string.Empty,
                        Url = asset.TryGetProperty("browser_download_url", out var url)
                            ? url.GetString()
                            : null
                    })
                    .Where(asset => !string.IsNullOrWhiteSpace(asset.Url))
                    .ToList();

                var selected = string.IsNullOrWhiteSpace(mod.Asset)
                    ? candidates.FirstOrDefault(candidate =>
                        candidate.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    : candidates.FirstOrDefault(candidate =>
                          candidate.Name.Equals(
                              mod.Asset.Trim(),
                              StringComparison.OrdinalIgnoreCase
                          )) ??
                      candidates.FirstOrDefault(candidate =>
                          candidate.Name.EndsWith(
                              mod.Asset.Trim(),
                              StringComparison.OrdinalIgnoreCase
                          ));

                if (selected?.Url is not null)
                {
                    return new Uri(selected.Url);
                }
            }

            if (root.TryGetProperty("zipball_url", out var zipball) &&
                zipball.GetString() is { Length: > 0 } zipballUrl)
            {
                return new Uri(zipballUrl);
            }
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode == HttpStatusCode.NotFound &&
            string.IsNullOrWhiteSpace(mod.Tag)
        )
        {
            var repoJson = await Http.GetStringAsync(
                $"https://api.github.com/repos/{repo}",
                cancellationToken
            );
            using var document = JsonDocument.Parse(repoJson);
            var branch = document.RootElement.TryGetProperty("default_branch", out var branchElement)
                ? branchElement.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(branch))
            {
                return new Uri(
                    $"https://codeload.github.com/{repo}/zip/refs/heads/{Uri.EscapeDataString(branch)}"
                );
            }
        }

        throw new InvalidOperationException($"No download asset was found for GitHub mod {repo}.");
    }

    private static async Task<IReadOnlyList<string>> InstallZipAsync(
        Uri uri,
        string modsRoot,
        CancellationToken cancellationToken
    )
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "PackFrame",
            "modpack-" + Guid.NewGuid().ToString("N")
        );
        var zipPath = tempRoot + ".zip";
        var extractRoot = tempRoot + "-extract";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            await DownloadAsync(uri, zipPath, null, cancellationToken);
            Directory.CreateDirectory(extractRoot);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var destination = Path.GetFullPath(
                        Path.Combine(extractRoot, entry.FullName)
                    );
                    if (!destination.StartsWith(
                            Path.GetFullPath(extractRoot) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        throw new InvalidOperationException("Downloaded archive is unsafe.");
                    }
                }
            }

            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
            var modRoots = Directory.EnumerateFiles(
                    extractRoot,
                    "About.xml",
                    SearchOption.AllDirectories
                )
                .Where(path => string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(path)),
                    "About",
                    StringComparison.OrdinalIgnoreCase
                ))
                .Select(path => Path.GetDirectoryName(Path.GetDirectoryName(path)))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var installedIds = new List<string>();
            foreach (var source in modRoots)
            {
                var target = GetAvailableTarget(modsRoot, Path.GetFileName(source));
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }

                CopyDirectory(source, target);
                var packageId = ModCatalog.ReadPackageId(target);
                if (!string.IsNullOrWhiteSpace(packageId))
                {
                    installedIds.Add(packageId);
                }
            }

            if (installedIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "The downloaded archive contains no RimWorld mod."
                );
            }

            return installedIds;
        }
        finally
        {
            TryDeleteDirectory(extractRoot);
            TryDeleteFile(zipPath);
        }
    }

    private static IReadOnlyList<string> InstallLocalMod(
        ModpackMod mod,
        string modsRoot,
        RimWorldInstallation game,
        string packRoot
    )
    {
        if (string.IsNullOrWhiteSpace(mod.Path))
        {
            return Array.Empty<string>();
        }

        var source = Environment.ExpandEnvironmentVariables(mod.Path.Trim());
        if (!Path.IsPathRooted(source))
        {
            throw new InvalidOperationException(
                $"Local mod path must be absolute: {mod.Path}"
            );
        }

        if (!File.Exists(Path.Combine(source, "About", "About.xml")))
        {
            if (!string.IsNullOrWhiteSpace(mod.PackageId))
            {
                return Array.Empty<string>();
            }

            throw new InvalidOperationException($"Local mod was not found: {source}");
        }

        var packageId = ModCatalog.ReadPackageId(source);
        var sourceFull = Path.GetFullPath(source);
        var modsRootFull = Path.GetFullPath(modsRoot);
        var gameMods = string.IsNullOrWhiteSpace(game.GamePath)
            ? null
            : Path.GetFullPath(Path.Combine(game.GamePath, "Mods"));
        if (sourceFull.StartsWith(
                modsRootFull + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            ) ||
            (gameMods is not null &&
             sourceFull.StartsWith(
                 gameMods + Path.DirectorySeparatorChar,
                 StringComparison.OrdinalIgnoreCase
             )))
        {
            return string.IsNullOrWhiteSpace(packageId)
                ? Array.Empty<string>()
                : new[] { packageId };
        }

        var target = GetAvailableTarget(modsRoot, Path.GetFileName(sourceFull));
        if (Directory.Exists(target))
        {
            Directory.Delete(target, true);
        }

        CopyDirectory(sourceFull, target);
        var installedId = ModCatalog.ReadPackageId(target);
        return string.IsNullOrWhiteSpace(installedId)
            ? Array.Empty<string>()
            : new[] { installedId };
    }

    private static Uri? ResolveModsArchiveUri(ModpackDefinition pack)
    {
        var archive = pack.ModsArchive?.Trim();
        if (string.IsNullOrWhiteSpace(archive))
        {
            return null;
        }

        if (Uri.TryCreate(archive, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (string.IsNullOrWhiteSpace(pack.Repo) ||
            string.IsNullOrWhiteSpace(pack.Ref))
        {
            return null;
        }

        var path = string.Join(
            "/",
            archive.TrimStart('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString)
        );
        return new Uri(
            $"https://raw.githubusercontent.com/{pack.Repo.Trim()}/{pack.Ref.Trim()}/{path}"
        );
    }
    private static async Task DownloadAsync(
        Uri uri,
        string path,
        Action<string>? status,
        CancellationToken cancellationToken, string? language = null
    )
    {
        using var response = await Http.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            throw new InvalidOperationException("Downloaded file is too large.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(path);
        var totalLength = response.Content.Headers.ContentLength ?? 0;
        var stopwatch = Stopwatch.StartNew();
        var lastBytes = 0L;
        var lastSeconds = 0d;
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes)
            {
                throw new InvalidOperationException("Downloaded file is too large.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            var now = stopwatch.Elapsed.TotalSeconds;
            if (now - lastSeconds >= 0.5 || total == totalLength)
            {
                var speed = (total - lastBytes) / Math.Max(0.001, now - lastSeconds);
                var progress = totalLength > 0
                    ? $"{FormatBytes(total)} / {FormatBytes(totalLength)} ({total * 100d / totalLength:0.0}%)"
                    : FormatBytes(total);
                status?.Invoke($"{OperationText.Download(language)} {progress}  {FormatBytes(speed)}/s");
                lastBytes = total;
                lastSeconds = now;
            }
        }
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes >= 1024d * 1024d * 1024d)
        {
            return $"{bytes / (1024d * 1024d * 1024d):0.0} GB";
        }
        if (bytes >= 1024d * 1024d)
        {
            return $"{bytes / (1024d * 1024d):0.0} MB";
        }
        if (bytes >= 1024d)
        {
            return $"{bytes / 1024d:0.0} KB";
        }
        return $"{bytes:0} B";
    }

    private static string NormalizeSource(ModpackMod mod)
    {
        var source = (mod.Source ?? string.Empty).Trim().ToLowerInvariant();
        if (source.Length > 0)
        {
            return source;
        }

        if (!string.IsNullOrWhiteSpace(mod.WorkshopId) ||
            !string.IsNullOrWhiteSpace(mod.WorkshopUrl))
        {
            return "workshop";
        }
        if (!string.IsNullOrWhiteSpace(mod.Repo) ||
            !string.IsNullOrWhiteSpace(mod.DownloadUrl))
        {
            return "github";
        }
        if (!string.IsNullOrWhiteSpace(mod.Path))
        {
            return "local";
        }

        return "package";
    }

    private static string? NormalizePackageId(string? packageId)
    {
        return string.IsNullOrWhiteSpace(packageId) ? null : packageId.Trim();
    }

    private static void AddPackageId(
        string? explicitPackageId,
        IReadOnlyList<string> discoveredPackageIds,
        List<string> packageIds,
        HashSet<string> resolvedPackageIds
    )
    {
        if (!string.IsNullOrWhiteSpace(explicitPackageId))
        {
            AddUniquePackageId(explicitPackageId, packageIds, resolvedPackageIds);
        }

        foreach (var packageId in discoveredPackageIds)
        {
            AddUniquePackageId(packageId, packageIds, resolvedPackageIds);
        }
    }

    private static void AddUniquePackageId(
        string packageId,
        List<string> packageIds,
        HashSet<string> resolvedPackageIds
    )
    {
        var normalized = NormalizePackageId(packageId);
        if (normalized is not null && resolvedPackageIds.Add(normalized))
        {
            packageIds.Add(normalized);
        }
    }

    private static string? ReadWorkshopPackageId(string? workshopPath, ulong id)
    {
        if (string.IsNullOrWhiteSpace(workshopPath))
        {
            return null;
        }

        return ModCatalog.ReadPackageId(Path.Combine(workshopPath, id.ToString()));
    }

    private static string GetAvailableTarget(string root, string name)
    {
        var safeName = string.Concat(
            (name ?? string.Empty)
                .Select(character => Path.GetInvalidFileNameChars().Contains(character)
                    ? '_'
                    : character)
        ).Trim();
        if (safeName.Length == 0)
        {
            safeName = "mod";
        }

        var target = Path.Combine(root, safeName);
        var suffix = 2;
        while (Directory.Exists(target))
        {
            target = Path.Combine(root, $"{safeName}_{suffix}");
            suffix++;
        }

        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(
            source,
            "*",
            SearchOption.AllDirectories
        ))
        {
            Directory.CreateDirectory(Path.Combine(
                target,
                Path.GetRelativePath(source, directory)
            ));
        }

        foreach (var file in Directory.EnumerateFiles(
            source,
            "*",
            SearchOption.AllDirectories
        ))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void AddUnique<T>(ICollection<T> target, T value)
    {
        if (!target.Contains(value))
        {
            target.Add(value);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}





