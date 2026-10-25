using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

public sealed record GiteeIndexPublishResult(string PullRequestUrl);

public static class GiteeIndexPublisher
{
    private const string ApiRoot = "https://gitee.com/api/v5";
    private static readonly HttpClient Http = GiteeHttp.CreateClient();

    public static async Task<GiteeIndexPublishResult> PublishAsync(
        string token,
        string indexUrl,
        string packId,
        string packName,
        string summary,
        string? githubFile,
        string? giteeFile,
        string repoFullName,
        int modCount,
        Action<string>? status,
        CancellationToken cancellationToken = default,
        string? language = null
    )
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Gitee token is required.");
        }
        if (!TryParseIndexUrl(indexUrl, out var owner, out var repository, out var branch))
        {
            throw new InvalidOperationException("Invalid Gitee index URL.");
        }

        var index = await ReadIndexAsync(indexUrl, cancellationToken);
        var entry = new JsonObject
        {
            ["id"] = packId,
            ["name"] = packName,
            ["summary"] = summary,
            ["repo"] = repoFullName,
            ["ref"] = "main",
            ["manifest"] = "pack.json",
            ["modCount"] = modCount
        };
        if (!string.IsNullOrWhiteSpace(githubFile))
        {
            entry["file"] = githubFile.Trim();
        }
        if (!string.IsNullOrWhiteSpace(giteeFile))
        {
            entry["giteeFile"] = giteeFile.Trim();
        }

        var slug = Slug(packId);
        var packPath = "packs/" + slug + ".json";
        var indexObject = JsonNode.Parse(index) as JsonObject
            ?? throw new InvalidOperationException("Gitee index is invalid.");
        var packs = indexObject["packs"] as JsonArray ?? new JsonArray();
        indexObject["packs"] = packs;
        var existingIndex = -1;
        for (var i = 0; i < packs.Count; i++)
        {
            if (packs[i] is JsonObject candidate &&
                string.Equals(candidate["id"]?.GetValue<string>(), packId, StringComparison.OrdinalIgnoreCase))
            {
                existingIndex = i;
                break;
            }
        }
        if (existingIndex >= 0)
        {
            packs[existingIndex] = entry.DeepClone();
        }
        else
        {
            packs.Add(entry.DeepClone());
        }

        var packExists = await ResourceExistsAsync(
            $"https://gitee.com/{owner}/{repository}/raw/{branch}/{packPath}",
            cancellationToken
        );
        var actions = new JsonArray
        {
            (JsonNode)new JsonObject
            {
                ["action"] = packExists ? "update" : "create",
                ["path"] = packPath,
                ["content"] = Base64(entry.ToJsonString(Options)),
                ["encoding"] = "base64"
            },
            (JsonNode)new JsonObject
            {
                ["action"] = "update",
                ["path"] = "index.json",
                ["content"] = Base64(indexObject.ToJsonString(Options)),
                ["encoding"] = "base64"
            }
        };
        var newBranch = "packframe/index-" + slug + "-" +
            DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        using var client = CreateClient(token);
        status?.Invoke(OperationText.IndexGiteePrepare(language));
        var commit = new JsonObject
        {
            ["branch"] = newBranch,
            ["start_branch"] = branch,
            ["message"] = "Add or update modpack " + packId,
            ["actions"] = actions
        };
        using (var response = await client.PostAsync(
            $"{ApiRoot}/repos/{owner}/{repository}/commits",
            JsonContent(commit),
            cancellationToken
        ))
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(ApiError("POST /commits", response, document));
            }
        }

        status?.Invoke(OperationText.IndexGiteePr(language));
        var pr = new JsonObject
        {
            ["title"] = "Add or update modpack " + packName,
            ["head"] = newBranch,
            ["base"] = branch,
            ["body"] = "PackFrame index update\n\n"
                + "GitHub: " + (githubFile ?? "-") + "\n"
                + "Gitee: " + (giteeFile ?? "-")
        };
        using (var response = await client.PostAsync(
            $"{ApiRoot}/repos/{owner}/{repository}/pulls",
            JsonContent(pr),
            cancellationToken
        ))
        {
            using var document = await ReadDocumentAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(ApiError("POST /pulls", response, document));
            }

            var url = document.RootElement.TryGetProperty("html_url", out var html)
                ? html.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("Gitee did not return a pull request URL.");
            }
            status?.Invoke(OperationText.IndexGiteeDone(language));
            return new GiteeIndexPublishResult(url);
        }
    }

    private static async Task<string> ReadIndexAsync(
        string indexUrl,
        CancellationToken cancellationToken
    )
    {
        using var response = await Http.GetAsync(indexUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task<bool> ResourceExistsAsync(
        string url,
        CancellationToken cancellationToken
    )
    {
        using var response = await Http.GetAsync(url, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static bool TryParseIndexUrl(
        string url,
        out string owner,
        out string repository,
        out string branch
    )
    {
        owner = string.Empty;
        repository = string.Empty;
        branch = "main";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 4 || !parts[2].Equals("raw", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        owner = parts[0];
        repository = parts[1];
        branch = parts[3];
        return owner.Length > 0 && repository.Length > 0;
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-');
        }
        return builder.ToString().Trim('-') is { Length: > 0 } result
            ? result
            : "pack";
    }

    private static string Base64(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n"
    };

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient(GiteeHttp.CreateHandler()) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("token", token.Trim());
        return client;
    }

    private static StringContent JsonContent(JsonNode value) =>
        new(value.ToJsonString(Options), Encoding.UTF8, "application/json");

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
    ) =>
        $"Gitee API {operation} returned {(int)response.StatusCode}: {document.RootElement.GetRawText()}";
}
