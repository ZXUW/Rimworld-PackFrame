using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

public sealed record GiteePublishResult(
    string Owner,
    string Repository,
    string Branch,
    string Url,
    string RawUrl,
    int FileCount
);

public static class GiteePublisher
{
    private const string ApiRoot = "https://gitee.com/api/v5";
    private const string ArchiveTag = "packframe-mods";

    public static async Task<GiteePublishResult> PublishAsync(
        string repoPath,
        string token,
        string repoName,
        string description,
        Action<string>? status,
        CancellationToken cancellationToken = default,
        string? language = null
    )
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Gitee token is required.");
        }
        if (!Directory.Exists(repoPath))
        {
            throw new DirectoryNotFoundException(repoPath);
        }

        using var client = CreateClient(token);
        status?.Invoke(OperationText.GiteeLogin(language));
        var owner = await GetLoginAsync(client, cancellationToken);
        var name = GitHubPublisher.NormalizeRepositoryName(repoName);
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Gitee repository name is empty.");
        }

        status?.Invoke(OperationText.GiteeRepo(language));
        var branch = await EnsureRepositoryAsync(
            client,
            owner,
            name,
            description,
            cancellationToken
        );

        ModpackRepoExporter.OptimizeCoverForUpload(repoPath);
        PackLicenses.Ensure(repoPath, Array.Empty<string>());
        var files = Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories)
            .Where(file => !IsIgnored(repoPath, file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var largeArchive = files.FirstOrDefault(file =>
            Path.GetFileName(file).Equals("mods.zip", StringComparison.OrdinalIgnoreCase)
        );
        if (largeArchive is not null)
        {
            files.Remove(largeArchive);
            SetModsArchive(repoPath, null);
        }

        var relativeFiles = files
            .Select(file => Path.GetRelativePath(repoPath, file).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var existing = await GetTreeAsync(client, owner, name, branch, cancellationToken);
        var actions = new JsonArray();
        for (var index = 0; index < files.Count; index++)
        {
            var relative = Path.GetRelativePath(repoPath, files[index]).Replace('\\', '/');
            status?.Invoke($"{OperationText.GiteeFiles(language)} {index + 1}/{files.Count}: {relative}");
            actions.Add((JsonNode)new JsonObject
            {
                ["action"] = existing.Contains(relative) ? "update" : "create",
                ["path"] = relative,
                ["content"] = Convert.ToBase64String(
                    await File.ReadAllBytesAsync(files[index], cancellationToken)
                ),
                ["encoding"] = "base64"
            });
        }
        foreach (var stale in existing.Where(path => !relativeFiles.Contains(path)))
        {
            actions.Add((JsonNode)new JsonObject
            {
                ["action"] = "delete",
                ["path"] = stale
            });
        }

        if (actions.Count > 0)
        {
            status?.Invoke(OperationText.GiteeCommit(language));
            await CommitAsync(client, owner, name, branch, actions, cancellationToken);
        }

        if (largeArchive is not null)
        {
            status?.Invoke(OperationText.Choose(
                language,
                "Uploading large mods archive to Gitee Release...",
                "正在上传本地模组压缩包到 Gitee Release...",
                "正在上傳本地模組壓縮包到 Gitee Release...",
                "ローカル MOD のアーカイブを Gitee Release にアップロードしています..."
            ));
            var archiveUrl = await UploadLargeArchiveAsReleaseAsync(
                client,
                owner,
                name,
                largeArchive,
                branch,
                cancellationToken
            );
            SetModsArchive(repoPath, archiveUrl);
            status?.Invoke(OperationText.GiteeCommit(language));
            await CommitAsync(
                client,
                owner,
                name,
                branch,
                new JsonArray
                {
                    (JsonNode)new JsonObject
                    {
                        ["action"] = "update",
                        ["path"] = "pack.json",
                        ["content"] = Convert.ToBase64String(
                            await File.ReadAllBytesAsync(
                                Path.Combine(repoPath, "pack.json"),
                                cancellationToken
                            )
                        ),
                        ["encoding"] = "base64"
                    }
                },
                cancellationToken
            );
        }

        status?.Invoke(OperationText.GiteeDone(language));
        return BuildResult(owner, name, branch, files.Count);
    }

    public static async Task<GitHubPackInfo> ResolvePackAsync(
        string repositoryUrl,
        CancellationToken cancellationToken = default
    )
    {
        var raw = NormalizePackRawUrl(repositoryUrl);
        using var response = await Http.GetAsync(raw, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var summary = root.TryGetProperty("description", out var summaryElement) ? summaryElement.GetString() : null;
        var packages = root.TryGetProperty("packageIds", out var packageElement) &&
            packageElement.ValueKind == JsonValueKind.Array
            ? packageElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();
        TryParseRepositoryUrl(repositoryUrl, out var owner, out var repository);
        return new GitHubPackInfo(
            owner,
            repository,
            string.IsNullOrWhiteSpace(id) ? repository : id.Trim(),
            string.IsNullOrWhiteSpace(name) ? repository : name.Trim(),
            summary?.Trim() ?? string.Empty,
            packages
        );
    }

    public static string NormalizePackRawUrl(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid Gitee repository URL.");
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.Contains("/raw/", StringComparison.OrdinalIgnoreCase))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }
        if (TryParseRepositoryUrl(repositoryUrl, out var owner, out var repository))
        {
            return $"https://gitee.com/{owner}/{repository}/raw/main/pack.json";
        }
        throw new InvalidOperationException("Invalid Gitee repository URL.");
    }

    private static bool TryParseRepositoryUrl(
        string? url,
        out string owner,
        out string repository
    )
    {
        owner = string.Empty;
        repository = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
        {
            return false;
        }
        owner = parts[0];
        repository = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? parts[1][..^4]
            : parts[1];
        return owner.Length > 0 && repository.Length > 0;
    }

    private static readonly HttpClient Http = GiteeHttp.CreateClient();
    public static string NormalizeRepositoryName(string name)
    {
        return GitHubPublisher.NormalizeRepositoryName(name);
    }

    public static string DefaultRepositoryName(string packName)
    {
        return GitHubPublisher.DefaultRepositoryName(packName);
    }

    private static GiteePublishResult BuildResult(
        string owner,
        string name,
        string branch,
        int fileCount
    )
    {
        return new GiteePublishResult(
            owner,
            name,
            branch,
            $"https://gitee.com/{owner}/{name}",
            $"https://gitee.com/{owner}/{name}/raw/{branch}/pack.json",
            fileCount
        );
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient(GiteeHttp.CreateHandler())
        {
            Timeout = TimeSpan.FromMinutes(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("token", token.Trim());
        return client;
    }

    private static async Task<string> GetLoginAsync(
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync($"{ApiRoot}/user", cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ApiError("GET /user", response, document));
        }
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty("login", out var login) &&
               !string.IsNullOrWhiteSpace(login.GetString())
            ? login.GetString()!
            : throw new InvalidOperationException("Gitee login not found.");
    }

    private static async Task<string> EnsureRepositoryAsync(
        HttpClient client,
        string owner,
        string name,
        string description,
        CancellationToken cancellationToken
    )
    {
        using (var create = await client.PostAsync(
            $"{ApiRoot}/user/repos",
            JsonContent(new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["private"] = false,
                ["auto_init"] = false
            }),
            cancellationToken
        ))
        {
            if (!create.IsSuccessStatusCode)
            {
                using var document = await ReadDocumentAsync(create, cancellationToken);
                var error = ApiError("POST /user/repos", create, document);
                using var existing = await client.GetAsync(
                    $"{ApiRoot}/repos/{owner}/{name}",
                    cancellationToken
                );
                if (!existing.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(error);
                }
            }
        }

        using var repo = await client.GetAsync(
            $"{ApiRoot}/repos/{owner}/{name}",
            cancellationToken
        );
        using var repoDocument = await ReadDocumentAsync(repo, cancellationToken);
        if (!repo.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ApiError("GET /repos", repo, repoDocument));
        }

        var branch = repoDocument.RootElement.ValueKind == JsonValueKind.Object &&
                     repoDocument.RootElement.TryGetProperty("default_branch", out var value)
            ? value.GetString()
            : null;
        return string.IsNullOrWhiteSpace(branch) ? "main" : branch;
    }

    private static async Task<string> UploadLargeArchiveAsReleaseAsync(
        HttpClient client,
        string owner,
        string name,
        string archivePath,
        string branch,
        CancellationToken cancellationToken
    )
    {
        var assetName = await ArchiveAssetNameAsync(archivePath, cancellationToken);
        var digest = Path.GetFileNameWithoutExtension(assetName)["mods-".Length..];
        var tag = ArchiveTag + "-" + digest[..16];
        long? releaseId = null;
        using (var existing = await client.GetAsync(
            $"{ApiRoot}/repos/{owner}/{name}/releases/tags/{Uri.EscapeDataString(tag)}",
            cancellationToken
        ))
        {
            if (existing.IsSuccessStatusCode)
            {
                using var document = await ReadDocumentAsync(existing, cancellationToken);
                releaseId = ReadLong(document.RootElement, "id");
            }
            else if (existing.StatusCode != HttpStatusCode.NotFound)
            {
                using var document = await ReadDocumentAsync(existing, cancellationToken);
                throw new InvalidOperationException(
                    ApiError("GET /releases/tags", existing, document)
                );
            }
        }

        if (releaseId is null)
        {
            using var created = await client.PostAsync(
                $"{ApiRoot}/repos/{owner}/{name}/releases",
                JsonContent(new JsonObject
                {
                    ["tag_name"] = tag,
                    ["name"] = "PackFrame local mods",
                    ["body"] = "Local mods archive uploaded by PackFrame.",
                    ["prerelease"] = false,
                    ["target_commitish"] = branch
                }),
                cancellationToken
            );
            using var document = await ReadDocumentAsync(created, cancellationToken);
            if (!created.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    ApiError($"POST /repos/{owner}/{name}/releases", created, document)
                );
            }

            releaseId = ReadLong(document.RootElement, "id")
                ?? throw new InvalidOperationException(
                    "Gitee release response did not contain an id."
                );
        }

        using var stream = File.OpenRead(archivePath);
        using var fileContent = new StreamContent(stream);
        using var content = new MultipartFormDataContent();
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", assetName);
        using var response = await client.PostAsync(
            $"{ApiRoot}/repos/{owner}/{name}/releases/{releaseId}/attach_files",
            content,
            cancellationToken
        );
        using var responseDocument = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                ApiError(
                    $"POST /releases/{releaseId}/attach_files",
                    response,
                    responseDocument
                )
            );
        }

        return responseDocument.RootElement.ValueKind == JsonValueKind.Object &&
               responseDocument.RootElement.TryGetProperty(
                   "browser_download_url",
                   out var url
               ) &&
               !string.IsNullOrWhiteSpace(url.GetString())
            ? url.GetString()!
            : throw new InvalidOperationException("Gitee attachment URL is empty.");
    }

    private static long? ReadLong(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out var value) &&
               value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static async Task<string> ArchiveAssetNameAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = File.OpenRead(path);
        return "mods-" + Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken)
        ).ToLowerInvariant() + ".zip";
    }

    private static async Task CommitAsync(
        HttpClient client,
        string owner,
        string name,
        string branch,
        JsonArray actions,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.PostAsync(
            $"{ApiRoot}/repos/{owner}/{name}/commits",
            JsonContent(new JsonObject
            {
                ["branch"] = branch,
                ["message"] = "Update PackFrame modpack",
                ["actions"] = actions
            }),
            cancellationToken
        );
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ApiError("POST /commits", response, document));
        }
    }

    private static void SetModsArchive(string repoPath, string? url)
    {
        var path = Path.Combine(repoPath, "pack.json");
        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        ) ?? throw new InvalidOperationException("pack.json could not be read.");

        manifest.ModsArchive = url;
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        );
    }

    private static async Task<HashSet<string>> GetTreeAsync(
        HttpClient client,
        string owner,
        string name,
        string branch,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(
            $"{ApiRoot}/repos/{owner}/{name}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1",
            cancellationToken
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ApiError("GET /git/trees", response, document));
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("tree", out var tree))
        {
            foreach (var item in tree.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("type", out var type) &&
                    type.GetString() == "blob" &&
                    item.TryGetProperty("path", out var path) &&
                    path.GetString() is { Length: > 0 } value)
                {
                    paths.Add(value);
                }
            }
        }
        return paths;
    }

    private static bool IsIgnored(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
        if (segments.Any(segment =>
                segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("upload", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("finished", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("mods", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static StringContent JsonContent(JsonNode value)
    {
        return new StringContent(
            value.ToJsonString(new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            Encoding.UTF8,
            "application/json"
        );
    }

    private static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static string ApiError(
        string operation,
        HttpResponseMessage response,
        JsonDocument document
    )
    {
        var body = document.RootElement.GetRawText();
        return $"Gitee API {operation} returned {(int)response.StatusCode}: {body}";
    }
}
