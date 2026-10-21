using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

public sealed record GitHubIndexPublishResult(string PullRequestUrl);

public sealed record GitHubPackInfo(
    string Owner,
    string Repository,
    string Id,
    string Name,
    string Summary,
    IReadOnlyList<string> PackageIds
);

public static class GitHubIndexPublisher
{
    private static readonly HttpClient Http = new();

    public static async Task<GitHubPackInfo> ResolvePackAsync(
        string token,
        string repositoryUrl,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryParseRepositoryUrl(repositoryUrl, out var owner, out var repository))
        {
            throw new InvalidOperationException("Invalid GitHub repository URL.");
        }

        using (var client = CreateClient(token))
        {
            try
            {
                var url =
                    $"https://api.github.com/repos/{owner}/{repository}/contents/pack.json";
                using var response = await client.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    using var document = await ReadDocumentAsync(
                        response,
                        cancellationToken
                    );
                    var root = document.RootElement;
                    var encoded = root.GetProperty("content").GetString() ?? string.Empty;
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    return ParsePackInfo(owner, repository, json);
                }

                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    using var document = await ReadDocumentAsync(
                        response,
                        cancellationToken
                    );
                    throw ApiError("GET pack.json", response, document);
                }
            }
            catch (HttpRequestException)
            {
            }
        }

        foreach (var branch in new[] { "main", "master" })
        {
            try
            {
                using var response = await Http.GetAsync(
                    $"https://raw.githubusercontent.com/{owner}/{repository}/{branch}/pack.json",
                    cancellationToken
                );
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParsePackInfo(owner, repository, json);
            }
            catch
            {
            }
        }

        throw new InvalidOperationException(
            "pack.json was not found in the repository root."
        );
    }

    public static bool TryParseRepositoryUrl(
        string? url,
        out string owner,
        out string repository
    )
    {
        owner = string.Empty;
        repository = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
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
        return !string.IsNullOrWhiteSpace(owner) &&
            !string.IsNullOrWhiteSpace(repository);
    }

    private static GitHubPackInfo ParsePackInfo(
        string owner,
        string repository,
        string json
    )
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
        var name = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var summary = root.TryGetProperty("description", out var summaryElement)
            ? summaryElement.GetString()
            : null;
        var packageIds = root.TryGetProperty("packageIds", out var packages) &&
            packages.ValueKind == JsonValueKind.Array
            ? packages.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        return new GitHubPackInfo(
            owner,
            repository,
            string.IsNullOrWhiteSpace(id) ? repository : id.Trim(),
            string.IsNullOrWhiteSpace(name) ? repository : name.Trim(),
            summary?.Trim() ?? string.Empty,
            packageIds
        );
    }
    public const string DefaultIndexUrl =
        "https://raw.githubusercontent.com/ZXUW/PackFrame-Modpacks/main/index.json";

    public static async Task<GitHubIndexPublishResult> PublishAsync(
        string token,
        string indexUrl,
        string packOwner,
        string packRepository,
        string packId,
        string packName,
        string summary,
        IReadOnlyList<string> packageIds,
        string collectionUrl,
        Action<string>? status,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GitHub token is required.");
        }

        if (!TryParseIndexUrl(indexUrl, out var upstreamOwner, out var upstreamRepo, out var sourceBranch))
        {
            throw new InvalidOperationException(
                "The index URL must be a raw.githubusercontent.com index.json URL."
            );
        }

        using var client = CreateClient(token);
        var login = await GetLoginAsync(client, cancellationToken);
        var baseBranch = await GetDefaultBranchAsync(
            client,
            upstreamOwner,
            upstreamRepo,
            sourceBranch,
            cancellationToken
        );

        status?.Invoke("正在准备整合包列表审核...");
        string headOwner = upstreamOwner;
        if (!string.Equals(login, upstreamOwner, StringComparison.OrdinalIgnoreCase))
        {
            status?.Invoke("正在创建整合包索引仓库分支...");
            await EnsureForkAsync(
                client,
                upstreamOwner,
                upstreamRepo,
                login,
                cancellationToken
            );
            headOwner = login;
        }

        var baseSha = await GetBranchShaAsync(
            client,
            headOwner,
            upstreamRepo,
            baseBranch,
            cancellationToken
        );
        var branch = "packframe/index-"
            + Slug(packId)
            + "-"
            + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        await CreateBranchAsync(
            client,
            headOwner,
            upstreamRepo,
            branch,
            baseSha,
            cancellationToken
        );

        status?.Invoke("正在写入整合包文件...");
        var packPath = "packs/" + Slug(packId) + ".json";
        var contentUrl =
            $"https://api.github.com/repos/{headOwner}/{upstreamRepo}/contents/{packPath}"
            + "?ref=" + Uri.EscapeDataString(branch);
        var existing = await GetContentsAsync(
            client,
            contentUrl,
            cancellationToken
        );
        var repoFullName = $"{packOwner}/{packRepository}";
        var entry = new JsonObject
        {
            ["id"] = packId,
            ["name"] = packName,
            ["summary"] = summary,
            ["repo"] = repoFullName,
            ["ref"] = "main",
            ["manifest"] = "pack.json",
            ["file"] =
                $"https://raw.githubusercontent.com/{repoFullName}/main/pack.json",
            ["modCount"] = packageIds.Count
        };
        var updated = entry.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NewLine = "\n"
        });
        await PutContentsAsync(
            client,
            headOwner,
            upstreamRepo,
            packPath,
            branch,
            existing.Sha,
            updated,
            "Add or update modpack " + packId,
            cancellationToken
        );
        status?.Invoke("正在创建审核 PR...");
        var prUrl = await CreatePullRequestAsync(
            client,
            upstreamOwner,
            upstreamRepo,
            headOwner,
            branch,
            baseBranch,
            packName,
            repoFullName,
            collectionUrl,
            cancellationToken
        );
        status?.Invoke("审核 PR 已创建。");
        return new GitHubIndexPublishResult(prUrl);
    }

    public static bool TryParseIndexUrl(
        string? url,
        out string owner,
        out string repository,
        out string branch
    )
    {
        owner = string.Empty;
        repository = string.Empty;
        branch = "main";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 4)
        {
            return false;
        }

        owner = parts[0];
        repository = parts[1];
        branch = parts[2];
        return true;
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
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
            throw ApiError("GET /user", response, document);
        }

        return document.RootElement.GetProperty("login").GetString()
            ?? throw new InvalidOperationException("GitHub login not found.");
    }

    private static async Task<string> GetDefaultBranchAsync(
        HttpClient client,
        string owner,
        string repository,
        string fallback,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{repository}",
            cancellationToken
        );
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiError($"GET /repos/{owner}/{repository}", response, document);
        }

        return document.RootElement.TryGetProperty("default_branch", out var branch) &&
            !string.IsNullOrWhiteSpace(branch.GetString())
            ? branch.GetString()!
            : fallback;
    }

    private static async Task EnsureForkAsync(
        HttpClient client,
        string owner,
        string repository,
        string login,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.PostAsync(
            $"https://api.github.com/repos/{owner}/{repository}/forks",
            JsonContent(new JsonObject()),
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw ApiError(
                $"POST /repos/{owner}/{repository}/forks",
                response,
                document
            );
        }

        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var check = await client.GetAsync(
                $"https://api.github.com/repos/{login}/{repository}",
                cancellationToken
            );
            if (check.IsSuccessStatusCode)
            {
                return;
            }

            await Task.Delay(1000, cancellationToken);
        }

        throw new InvalidOperationException("GitHub fork was not ready in time.");
    }

    private static async Task<string> GetBranchShaAsync(
        HttpClient client,
        string owner,
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{repository}/git/ref/heads/"
            + Uri.EscapeDataString(branch),
            cancellationToken
        );
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiError(
                $"GET /repos/{owner}/{repository}/git/ref/heads/{branch}",
                response,
                document
            );
        }

        return document.RootElement.GetProperty("object")
            .GetProperty("sha").GetString()
            ?? throw new InvalidOperationException("Index branch SHA not found.");
    }

    private static async Task CreateBranchAsync(
        HttpClient client,
        string owner,
        string repository,
        string branch,
        string sha,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.PostAsync(
            $"https://api.github.com/repos/{owner}/{repository}/git/refs",
            JsonContent(new JsonObject
            {
                ["ref"] = "refs/heads/" + branch,
                ["sha"] = sha
            }),
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw ApiError(
                $"POST /repos/{owner}/{repository}/git/refs",
                response,
                document
            );
        }
    }

    private static async Task<(byte[]? Content, string? Sha)> GetContentsAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }

        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiError("GET pack file", response, document);
        }

        var root = document.RootElement;
        var content = root.GetProperty("content").GetString() ?? string.Empty;
        var sha = root.GetProperty("sha").GetString();
        return (Convert.FromBase64String(content), sha);
    }

    private static async Task PutContentsAsync(
        HttpClient client,
        string owner,
        string repository,
        string path,
        string branch,
        string? sha,
        string content,
        string message,
        CancellationToken cancellationToken
    )
    {
        var payload = new JsonObject
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            ["branch"] = branch
        };
        if (!string.IsNullOrWhiteSpace(sha))
        {
            payload["sha"] = sha;
        }

        using var response = await client.PutAsync(
            $"https://api.github.com/repos/{owner}/{repository}/contents/{path}",
            new StringContent(
                payload.ToJsonString(),
                Encoding.UTF8,
                "application/json"
            ),
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            throw ApiError(
                $"PUT /repos/{owner}/{repository}/contents/{path}",
                response,
                document
            );
        }
    }

    private static async Task<string> CreatePullRequestAsync(
        HttpClient client,
        string upstreamOwner,
        string repository,
        string headOwner,
        string branch,
        string baseBranch,
        string packName,
        string repoFullName,
        string collectionUrl,
        CancellationToken cancellationToken
    )
    {
        var head = string.Equals(
            upstreamOwner,
            headOwner,
            StringComparison.OrdinalIgnoreCase
        )
            ? branch
            : headOwner + ":" + branch;
        var body = "PackFrame modpack index addition.\n\n"
            + "Pack repository: https://github.com/" + repoFullName + "\n"
            + (string.IsNullOrWhiteSpace(collectionUrl)
                ? string.Empty
                : "Workshop collection: " + collectionUrl + "\n")
            + "\nGenerated by PackFrame.";
        using var response = await client.PostAsync(
            $"https://api.github.com/repos/{upstreamOwner}/{repository}/pulls",
            JsonContent(new JsonObject
            {
                ["title"] = "Add modpack: " + packName,
                ["head"] = head,
                ["base"] = baseBranch,
                ["body"] = body
            }),
            cancellationToken
        );
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiError(
                $"POST /repos/{upstreamOwner}/{repository}/pulls",
                response,
                document
            );
        }

        return document.RootElement.GetProperty("html_url").GetString()
            ?? throw new InvalidOperationException("Pull request URL not found.");
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

    private static InvalidOperationException ApiError(
        string operation,
        HttpResponseMessage response,
        JsonDocument document
    )
    {
        var message = document.RootElement.TryGetProperty("message", out var value)
            ? value.GetString() ?? "GitHub request failed."
            : "GitHub request failed.";
        return new InvalidOperationException(
            $"{operation} -> {(int)response.StatusCode} {response.ReasonPhrase}: {message}"
        );
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if ((character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '-')
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

        return builder.ToString().Trim('-') is { Length: > 0 } slug
            ? slug
            : "pack";
    }
}
