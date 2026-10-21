using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

public sealed record GitHubPublishResult(
    string Owner,
    string Repository,
    string Url,
    int FileCount
);

public static class GitHubPublisher
{
    public static async Task<GitHubPublishResult> PublishAsync(
        string repoPath,
        string token,
        string repoName,
        string description,
        string collectionUrl,
        Action<string>? status,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GitHub token is required.");
        }

        if (!Directory.Exists(repoPath))
        {
            throw new DirectoryNotFoundException(repoPath);
        }

        using var client = CreateClient(token);
        status?.Invoke("正在验证 GitHub 登录...");
        var owner = await GetLoginAsync(client, cancellationToken);
        var name = SanitizeRepositoryName(repoName);
        if (name.Length == 0)
        {
            throw new InvalidOperationException("GitHub repository name is empty.");
        }

        status?.Invoke("正在创建 GitHub 仓库...");
        await EnsureRepositoryAsync(
            client,
            owner,
            name,
            description,
            collectionUrl,
            cancellationToken
        );

        ModpackRepoExporter.SetRepoInfo(repoPath, owner, name, "main");
        var files = Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories)
            .Where(file => !IsIgnored(repoPath, file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var largeArchive = files.FirstOrDefault(file =>
            Path.GetFileName(file).Equals("mods.zip", StringComparison.OrdinalIgnoreCase) &&
            new FileInfo(file).Length > 95L * 1024 * 1024
        );
        if (largeArchive is not null)
        {
            status?.Invoke("mods.zip is larger than 95 MB; uploading as a GitHub Release asset...");
            var archiveUrl = await UploadLargeArchiveAsReleaseAsync(
                client,
                owner,
                name,
                largeArchive,
                cancellationToken
            );
            SetModsArchive(repoPath, archiveUrl);
            await DeleteRemoteModsArchiveAsync(
                client,
                owner,
                name,
                cancellationToken
            );
            files.Remove(largeArchive);
        }

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var relative = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
            status?.Invoke($"正在上传文件 {index + 1}/{files.Count}：{relative}");
            await UploadFileAsync(client, owner, name, relative, file, cancellationToken);
        }

        await UpdateRepositoryAsync(
            client,
            owner,
            name,
            description,
            collectionUrl,
            cancellationToken
        );
        status?.Invoke("GitHub 上传完成。");
        return new GitHubPublishResult(
            owner,
            name,
            $"https://github.com/{owner}/{name}",
            files.Count
        );
    }

    public static string NormalizeRepositoryName(string name)
    {
        return SanitizeRepositoryName(name);
    }
    public static string DefaultRepositoryName(string packName)
    {
        return SanitizeRepositoryName(packName + "-Rimworld-PackFrame");
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Trim());
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task<string> GetLoginAsync(
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(
            "https://api.github.com/user",
            cancellationToken
        );
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                ApiError("GET /user", response, document)
            );
        }

        return document.RootElement.GetProperty("login").GetString()
            ?? throw new InvalidOperationException("GitHub login not found.");
    }

    private static async Task EnsureRepositoryAsync(
        HttpClient client,
        string owner,
        string name,
        string description,
        string collectionUrl,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.PostAsync(
            "https://api.github.com/user/repos",
            JsonContent(new JsonObject
            {
                ["name"] = name,
                ["description"] = BuildDescription(description, collectionUrl),
                ["private"] = false,
                ["auto_init"] = true
            }),
            cancellationToken
        );
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? createError = null;
        using (var document = await ReadDocumentAsync(response, cancellationToken))
        {
            createError = ApiError("POST /user/repos", response, document);
        }

        if (response.StatusCode != HttpStatusCode.UnprocessableEntity)
        {
            throw new InvalidOperationException(createError);
        }

        createError +=
            "\nGitHub rejected repository creation. Check that the token can create repositories "
            + "(classic: repo; fine-grained: All repositories + Administration read/write) "
            + "and that the repository name is not already taken.";

        using var existing = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{name}",
            cancellationToken
        );
        if (!existing.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(existing, cancellationToken);
            var getError = ApiError(
                $"GET /repos/{owner}/{name}",
                existing,
                document
            );
            throw new InvalidOperationException(
                createError + "\n" + getError
                + "\nThe repository name may already exist but the token cannot access it. "
                + "Use a classic token with repo scope, or add this repository to a fine-grained token."
            );
        }

        using var existingDocument = await ReadDocumentAsync(
            existing,
            cancellationToken
        );
        var isEmpty = existingDocument.RootElement.TryGetProperty("size", out var size) &&
            size.GetInt64() == 0;
        var hasBranch = existingDocument.RootElement.TryGetProperty(
            "default_branch",
            out var branch
        ) && !string.IsNullOrWhiteSpace(branch.GetString());
        if (isEmpty || !hasBranch)
        {
            await CreateInitialCommitAsync(
                client,
                owner,
                name,
                cancellationToken
            );
        }
    }

    private static async Task CreateInitialCommitAsync(
        HttpClient client,
        string owner,
        string name,
        CancellationToken cancellationToken
    )
    {
        var root = $"https://api.github.com/repos/{owner}/{name}";
        var url = root + "/contents/README.md";
        var readme = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("# PackFrame\n")
        );
        var sha = await GetFileShaAsync(client, url, cancellationToken);
        JsonObject payload = string.IsNullOrWhiteSpace(sha)
            ? new JsonObject
            {
                ["message"] = "Initial commit",
                ["content"] = readme
            }
            : new JsonObject
            {
                ["message"] = "Initial commit",
                ["content"] = readme,
                ["sha"] = sha
            };

        using var response = await client.PutAsync(
            url,
            JsonContent(payload),
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw new InvalidOperationException(
                ApiError($"PUT /repos/{owner}/{name}/contents/README.md", response, document)
            );
        }
    }
    private static async Task UpdateRepositoryAsync(
        HttpClient client,
        string owner,
        string name,
        string description,
        string collectionUrl,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.PatchAsync(
            $"https://api.github.com/repos/{owner}/{name}",
            JsonContent(new JsonObject
            {
                ["description"] = BuildDescription(description, collectionUrl)
            }),
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw new InvalidOperationException(
                ApiError($"PATCH /repos/{owner}/{name}", response, document)
            );
        }
    }

    private static async Task UploadFileAsync(
        HttpClient client,
        string owner,
        string name,
        string relative,
        string path,
        CancellationToken cancellationToken
    )
    {
        var content = Convert.ToBase64String(await File.ReadAllBytesAsync(
            path,
            cancellationToken
        ));
        var encodedPath = string.Join(
            "/",
            relative.Split('/').Select(Uri.EscapeDataString)
        );
        var url = $"https://api.github.com/repos/{owner}/{name}/contents/{encodedPath}";
        var sha = await GetFileShaAsync(client, url, cancellationToken);
        JsonObject payload = string.IsNullOrWhiteSpace(sha)
            ? new JsonObject
            {
                ["message"] = "Add " + relative,
                ["content"] = content
            }
            : new JsonObject
            {
                ["message"] = "Update " + relative,
                ["content"] = content,
                ["sha"] = sha
            };

        using var response = await client.PutAsync(
            url,
            JsonContent(payload),
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw new InvalidOperationException(
                ApiError("PUT " + url, response, document)
            );
        }
    }
    private static async Task<string> UploadLargeArchiveAsReleaseAsync(
        HttpClient client,
        string owner,
        string name,
        string archivePath,
        CancellationToken cancellationToken
    )
    {
        var tag = "packframe-mods-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        using var releaseResponse = await client.PostAsync(
            $"https://api.github.com/repos/{owner}/{name}/releases",
            JsonContent(new JsonObject
            {
                ["tag_name"] = tag,
                ["target_commitish"] = "main",
                ["name"] = "PackFrame local mods",
                ["body"] = "Local mods archive uploaded by PackFrame.",
                ["draft"] = false,
                ["prerelease"] = false
            }),
            cancellationToken
        );
        using var releaseDocument = await ReadDocumentAsync(releaseResponse, cancellationToken);
        if (!releaseResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                ApiError(
                    $"POST /repos/{owner}/{name}/releases",
                    releaseResponse,
                    releaseDocument
                )
            );
        }

        var releaseId = releaseDocument.RootElement.GetProperty("id").GetInt64();
        using var stream = File.OpenRead(archivePath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Headers.ContentLength = stream.Length;

        using var assetResponse = await client.PostAsync(
            $"https://uploads.github.com/repos/{owner}/{name}/releases/{releaseId}/assets?name=mods.zip",
            content,
            cancellationToken
        );
        using var assetDocument = await ReadDocumentAsync(assetResponse, cancellationToken);
        if (!assetResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                ApiError(
                    $"POST release asset {tag}/mods.zip",
                    assetResponse,
                    assetDocument
                )
            );
        }

        return assetDocument.RootElement.TryGetProperty(
                "browser_download_url",
                out var url
            ) && !string.IsNullOrWhiteSpace(url.GetString())
            ? url.GetString()!
            : $"https://github.com/{owner}/{name}/releases/download/{tag}/mods.zip";
    }

    private static async Task DeleteRemoteModsArchiveAsync(
        HttpClient client,
        string owner,
        string name,
        CancellationToken cancellationToken
    )
    {
        var url = $"https://api.github.com/repos/{owner}/{name}/contents/mods.zip";
        var sha = await GetFileShaAsync(client, url, cancellationToken);
        if (string.IsNullOrWhiteSpace(sha))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = JsonContent(new JsonObject
            {
                ["message"] = "Move mods archive to GitHub Release",
                ["sha"] = sha
            })
        };
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw new InvalidOperationException(
                ApiError("DELETE mods.zip", response, document)
            );
        }
    }

    private static void SetModsArchive(string repoPath, string url)
    {
        var path = Path.Combine(repoPath, "pack.json");
        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest is null)
        {
            throw new InvalidOperationException("pack.json could not be read.");
        }

        manifest.ModsArchive = url;
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        );
    }

    private static async Task<string?> GetFileShaAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw new InvalidOperationException(
                ApiError("GET " + url, response, document)
            );
        }

        using var content = await ReadDocumentAsync(response, cancellationToken);
        return content.RootElement.TryGetProperty("sha", out var sha)
            ? sha.GetString()
            : null;
    }

    private static StringContent JsonContent(JsonNode value)
    {
        return new StringContent(
            value.ToJsonString(),
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
        return JsonDocument.Parse(
            string.IsNullOrWhiteSpace(text) ? "{}" : text
        );
    }

    private static string ReadError(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("message", out var message))
        {
            return "GitHub request failed.";
        }

        var result = message.GetString() ?? "GitHub request failed.";
        if (document.RootElement.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array)
        {
            var details = errors.EnumerateArray()
                .Select(error =>
                {
                    if (error.ValueKind == JsonValueKind.String)
                    {
                        return error.GetString();
                    }

                    var text = new List<string>();
                    foreach (var name in new[]
                             {
                                 "resource",
                                 "field",
                                 "code",
                                 "message"
                             })
                    {
                        if (error.TryGetProperty(name, out var value) &&
                            value.ValueKind != JsonValueKind.Null)
                        {
                            text.Add(value.ToString());
                        }
                    }

                    return string.Join(" ", text);
                })
                .Where(text => !string.IsNullOrWhiteSpace(text));
            var detailText = string.Join("; ", details);
            if (!string.IsNullOrWhiteSpace(detailText))
            {
                result += " (" + detailText + ")";
            }
        }

        return result;
    }

    private static string ApiError(
        string operation,
        HttpResponseMessage response,
        JsonDocument document
    )
    {
        var result = $"{operation} -> {(int)response.StatusCode} {response.ReasonPhrase}: {ReadError(document)}";
        if (response.Headers.TryGetValues("X-OAuth-Scopes", out var scopes))
        {
            result += " (scopes: " + string.Join(",", scopes) + ")";
        }
        return result;
    }
    private static string BuildDescription(string description, string collectionUrl)
    {
        var normalized = string.Concat(
            description.Select(character =>
                char.IsControl(character) ? ' ' : character
            )
        );
        normalized = string.Join(
            " ",
            normalized.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries
            )
        ).Trim();
        var url = collectionUrl.Trim();
        if (url.Length > 0)
        {
            var available = Math.Max(0, 350 - url.Length - 1);
            if (normalized.Length > available)
            {
                normalized = available == 0
                    ? string.Empty
                    : normalized[..available].TrimEnd();
            }

            normalized = string.IsNullOrWhiteSpace(normalized)
                ? url
                : normalized + " " + url;
        }

        return normalized.Length > 350
            ? normalized[..350].TrimEnd()
            : normalized;
    }

    private static bool IsIgnored(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        foreach (var segment in relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        ))
        {
            if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeRepositoryName(string name)
    {
        var builder = new StringBuilder();
        foreach (var character in name.Trim())
        {
            if ((character >= 'a' && character <= 'z') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9') ||
                character is '-' or '_' or '.')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('-');
            }
        }

        while (builder.ToString().Contains("--", StringComparison.Ordinal))
        {
            builder.Replace("--", "-");
        }

        var result = builder.ToString().Trim('-', '.', ' ');
        return result.Length == 0 ? "Rimworld-PackFrame" : result;
    }
}
