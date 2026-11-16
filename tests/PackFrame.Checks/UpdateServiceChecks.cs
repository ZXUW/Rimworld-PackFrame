using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PackFrame;

internal static class UpdateServiceChecks
{
    private const string Tag = "v1.0.12";
    private const string LegacyName = "Rimworld.PackFrame-Window64.zip";
    private const string Hash = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    public static async Task Run()
    {
        Require(UpdateService.IsNewer("v1.0.12", "1.0.11") &&
                UpdateService.IsNewer("1.10.0", "1.9.99") &&
                UpdateService.IsNewer("V1.0.12+build.4", "1.0.11"),
            "stable update versions compare numerically with optional prefix and build metadata");
        Require(!UpdateService.IsNewer("1.2", "1.2.0") &&
                !UpdateService.IsNewer("v1.0.11", "1.0.11") &&
                !UpdateService.IsNewer("v1.0.10", "1.0.11"),
            "equivalent and older releases are never offered as new updates");
        Require(new[] { "1.0.12-preview.1", "1.0.12-rc1", "latest", "vv1.0.12", "1..12", "1.0.012", "1.0.12+", "1.0.12+bad..meta" }
                .All(tag => !UpdateService.IsNewer(tag, "1.0.11")),
            "prerelease and malformed update tags cannot be promoted to stable updates");

        using var handler = new UpdateHttp();
        using var client = new HttpClient(handler);
        handler.Respond = (request, _) => request.RequestUri!.AbsoluteUri == UpdateService.LatestReleaseUrl
            ? Task.FromResult(Json(Release(Asset())))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        var update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update is { Tag: Tag, Name: "PackFrame 1.0.12", ReleaseNotes: "Fixes and improvements." } &&
                update.PublishedAt == DateTimeOffset.Parse("2026-11-07T12:00:00Z") &&
                update.Asset is { Name: LegacyName, Size: 13004008 } && update.Asset.Sha256 == Hash.ToLowerInvariant() &&
                handler.Urls.SequenceEqual(new[] { UpdateService.GiteeLatestReleaseUrl, UpdateService.LatestReleaseUrl }),
            "checking falls back from unavailable Gitee to GitHub and reads verified compatible asset without downloading it");

        handler.Respond = (request, _) => request.RequestUri!.AbsoluteUri == UpdateService.LatestReleaseUrl
            ? Task.FromResult(Json(Release(Asset(), Asset("PackFrame-win-x64.zip"), Asset("PackFrame-win-arm64.zip"))))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update?.Asset?.Name == "PackFrame-win-x64.zip",
            "canonical Windows assets take priority over legacy names independently of response order");
        update = await UpdateService.CheckAsync(client, "windows", Architecture.Arm64);
        Require(update?.Asset?.Name == "PackFrame-win-arm64.zip",
            "update asset selection follows the running architecture");
        update = await UpdateService.CheckAsync(client, "macos", Architecture.Arm64);
        Require(update is not null && update.Asset is null,
            "platforms without an in-app installer retain release information for manual updating");

        var invalidAssets = new (string Reason, object Asset)[]
        {
            ("source archive", Asset("Source-code.zip")),
            ("external host", Asset(url: "https://example.com/PackFrame.zip")),
            ("unencrypted URL", Asset(url: "http://github.com/ZXUW/Rimworld-PackFrame/releases/download/" + Tag + "/" + LegacyName)),
            ("other repository", Asset(url: "https://github.com/another/repo/releases/download/" + Tag + "/" + LegacyName)),
            ("different release", Asset(url: "https://github.com/ZXUW/Rimworld-PackFrame/releases/download/v1.0.13/" + LegacyName)),
            ("missing digest", Asset(digest: null)),
            ("invalid digest", Asset(digest: "sha256:invalid")),
            ("empty download", Asset(size: 0)),
            ("oversized download", Asset(size: UpdateService.MaximumArchiveSize + 1))
        };
        foreach (var (reason, invalid) in invalidAssets)
        {
            handler.Respond = (request, _) => request.RequestUri!.AbsoluteUri == UpdateService.LatestReleaseUrl
                ? Task.FromResult(Json(Release(invalid)))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
            Require(update is not null && update.Asset is null,
                "update discovery rejects " + reason + " while retaining the release page");
        }

        handler.Respond = (request, _) => request.RequestUri!.AbsoluteUri == UpdateService.LatestReleaseUrl
            ? Task.FromResult(Json(Release(Asset(), Asset())))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update?.Asset is null,
            "ambiguous duplicate release assets are not silently selected");
        handler.Respond = (_, _) => Task.FromResult(Json(ReleaseWithFlags(prerelease: true)));
        Require(await UpdateService.CheckAsync(client, "windows", Architecture.X64) is null,
            "GitHub prereleases are not offered on the stable update channel");
        handler.Respond = (_, _) => Task.FromResult(Json(ReleaseWithFlags(draft: true)));
        Require(await UpdateService.CheckAsync(client, "windows", Architecture.X64) is null,
            "draft releases are not offered for updating");

        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        Require(await UpdateService.CheckAsync(client, "windows", Architecture.X64) is null,
            "a repository without a published release has no update offer");
        handler.Respond = (_, _) => Task.FromResult(Json("{\"tag_name\":\"not-a-version\"}"));
        await ExpectFailure<InvalidDataException>(() => UpdateService.CheckAsync(client, "windows", Architecture.X64));
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await ExpectFailure<HttpRequestException>(() => UpdateService.CheckAsync(client, "windows", Architecture.X64));
        Require(true, "invalid metadata and rate limits remain visible failures rather than false latest-version claims");

        var newerTag = "v99.0.0";
        handler.Respond = (request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == UpdateService.GiteeLatestReleaseUrl)
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    tag_name = newerTag, name = "PackFrame 99.0.0", body = "Fixes and improvements.",
                    prerelease = false, created_at = "2026-11-07T12:00:00Z",
                    assets = new[]
                    {
                        new
                        {
                            name = LegacyName,
                            browser_download_url = UpdateService.GiteeRepositoryUrl +
                                "/releases/download/" + newerTag + "/" + LegacyName
                        }
                    }
                })));
            if (url == UpdateService.GiteeManifestUrl)
                return Task.FromResult(Json(JsonSerializer.Serialize(new[]
                {
                    new { Name = "manifest.json", Size = 750, Sha256 = Hash,
                        Version = "99.0.0", Commit = "abc" },
                    new { Name = LegacyName, Size = 13004008, Sha256 = Hash,
                        Version = "99.0.0", Commit = "abc" }
                })));
            if (url == UpdateService.LatestReleaseUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        var beforeGiteePrimary = handler.Urls.Count;
        update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update is { Tag: var gTag, Asset: { Name: LegacyName, Size: 13004008 } } &&
                gTag == newerTag &&
                update.Asset!.Sha256 == Hash.ToLowerInvariant() &&
                update.Asset.Url.StartsWith(
                    UpdateService.GiteeRepositoryUrl + "/releases/download/",
                    StringComparison.Ordinal) &&
                handler.Urls.Skip(beforeGiteePrimary).SequenceEqual(new[]
                {
                    UpdateService.GiteeLatestReleaseUrl,
                    UpdateService.GiteeManifestUrl
                }),
            "Gitee primary check returns newer verified Windows asset directly without hitting GitHub");

        handler.Respond = (request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == UpdateService.GiteeLatestReleaseUrl)
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    tag_name = Tag, name = "PackFrame 1.0.12", body = "Lagging Gitee.",
                    prerelease = false, created_at = "2026-11-07T12:00:00Z",
                    assets = Array.Empty<object>()
                })));
            if (url == UpdateService.LatestReleaseUrl)
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    tag_name = newerTag, html_url = UpdateService.RepositoryUrl + "/releases/tag/" + newerTag,
                    name = "PackFrame 99.0.0", body = "Newer GitHub.",
                    published_at = "2026-11-08T12:00:00Z", draft = false, prerelease = false,
                    assets = new[] { Asset(url: UpdateService.RepositoryUrl + "/releases/download/" + newerTag + "/" + LegacyName) }
                })));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update is { Tag: var ghNewer } && ghNewer == newerTag && update.Asset is not null,
            "lagging Gitee release falls through to newer GitHub release instead of hiding updates");

        handler.Respond = (request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == UpdateService.LatestReleaseUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (url == UpdateService.GiteeLatestReleaseUrl)
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    tag_name = Tag, prerelease = false,
                    assets = new[]
                    {
                        new
                        {
                            name = LegacyName,
                            browser_download_url = UpdateService.GiteeRepositoryUrl +
                                "/releases/download/" + Tag + "/" + LegacyName
                        }
                    }
                })));
            if (url == UpdateService.GiteeManifestUrl)
                return Task.FromResult(Json(JsonSerializer.Serialize(new[]
                {
                    new { Name = LegacyName, Size = 13004008, Sha256 = Hash,
                        Version = "1.0.11", Commit = "abc" }
                })));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update is { Tag: Tag, Asset: null },
            "a manifest for a different version never vouches for other release assets");

        handler.Respond = (request, _) => request.RequestUri!.AbsoluteUri == UpdateService.LatestReleaseUrl
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))
            : Task.FromResult(Json(JsonSerializer.Serialize(new
            {
                tag_name = Tag, name = "PackFrame 1.0.12", prerelease = false,
                assets = Array.Empty<object>()
            })));
        update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update is { Tag: Tag, Asset: null },
            "Gitee releases without assets keep the release page but offer no in-app package");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var before = handler.Urls.Count;
        await ExpectFailure<OperationCanceledException>(() => UpdateService.CheckAsync(client, "windows", Architecture.X64, cancelled.Token));
        Require(handler.Urls.Count == before,
            "cancelled update checks make no request or mirror fallback");
        using var midCancel = new CancellationTokenSource();
        handler.Respond = (_, _) =>
        {
            midCancel.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(midCancel.Token);
        };
        before = handler.Urls.Count;
        await ExpectFailure<OperationCanceledException>(() => UpdateService.CheckAsync(client, "windows", Architecture.X64, midCancel.Token));
        Require(handler.Urls.Count == before + 1,
            "caller cancellation during a request propagates without trying another update source");

        handler.Respond = (request, _) => request.RequestUri!.AbsoluteUri == UpdateService.GiteeLatestReleaseUrl
            ? Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true))
            : Task.FromResult(Json(Release(Asset())));
        update = await UpdateService.CheckAsync(client, "windows", Architecture.X64);
        Require(update is { Tag: Tag, Asset: not null },
            "internal Gitee timeout falls back cleanly to GitHub for overseas users");
    }

    private static object Asset(string name = LegacyName, string? url = null,
        string? digest = "sha256:" + Hash, long size = 13004008) => new
    {
        name,
        browser_download_url = url ?? UpdateService.RepositoryUrl + "/releases/download/" + Tag + "/" + name,
        digest,
        size
    };

    private static string Release(params object[] assets) => JsonSerializer.Serialize(new
    {
        tag_name = Tag, html_url = UpdateService.RepositoryUrl + "/releases/tag/" + Tag,
        name = "PackFrame 1.0.12", body = "Fixes and improvements.",
        published_at = "2026-11-07T12:00:00Z", draft = false, prerelease = false, assets
    });

    private static string ReleaseWithFlags(bool draft = false, bool prerelease = false) =>
        JsonSerializer.Serialize(new { tag_name = Tag, draft, prerelease, assets = new[] { Asset() } });

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "application/json")
    };

    private static async Task ExpectFailure<T>(Func<Task<UpdateInfo?>> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("FAIL: expected " + typeof(T).Name);
    }

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class UpdateHttp : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (_, _) => throw new InvalidOperationException("No fake update response configured.");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Respond(request, cancellationToken);
        }
    }
}
