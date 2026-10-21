using System.Net;
using System.Text.Json;
using PackFrame;

internal static class PublishingChecks
{
    public static async Task Run(string root)
    {
        var folder = Path.Combine(root, "publish");
        Directory.CreateDirectory(folder);
        var files = new[] { Path.Combine(folder, "pack.json"), Path.Combine(folder, "config.xml") };
        foreach (var file in files) File.WriteAllText(file, "content");
        using var handler = new FakeGitHub();
        using var client = new HttpClient(handler);
        await GitHubPublisher.PublishFilesAsync(client, "owner", "repo", folder, files, true, null, default);
        Require(handler.Commits == 1 && handler.RefUpdates == 1 && handler.Blobs == 2,
            "GitHub publication uses one commit and one ref update");
        Require(handler.HasBaseTree && handler.RemovesArchive && !handler.Forced,
            "GitHub preserves unrelated tree entries and removes archive in same commit without force");
        using var failedHandler = new FakeGitHub { FailBlob = true };
        using var failedClient = new HttpClient(failedHandler);
        try
        {
            await GitHubPublisher.PublishFilesAsync(failedClient, "owner", "repo", folder, files, false, null, default);
            throw new Exception("Expected upload rejection");
        }
        catch (InvalidOperationException) { }
        Require(failedHandler.Commits == 0 && failedHandler.RefUpdates == 0,
            "failed upload never changes GitHub branch");
        using var conflictHandler = new FakeGitHub { RejectRef = true };
        using var conflictClient = new HttpClient(conflictHandler);
        try
        {
            await GitHubPublisher.PublishFilesAsync(conflictClient, "owner", "repo", folder, files, false, null, default);
            throw new Exception("Expected branch conflict");
        }
        catch (InvalidOperationException) { }
        Require(conflictHandler.RefUpdates == 1 && !conflictHandler.Forced,
            "concurrent branch update is rejected without force retry");
        using var releaseHandler = new ReleaseApi();
        using var releaseClient = new HttpClient(releaseHandler);
        var first = await GitHubPublisher.UploadLargeArchiveAsReleaseAsync(releaseClient, "owner", "repo", files[0], default, "master");
        var second = await GitHubPublisher.UploadLargeArchiveAsReleaseAsync(releaseClient, "owner", "repo", files[0], default, "master");
        Require(first == second && releaseHandler.Uploads == 1, "fixed Release reuses identical archive asset without duplicate upload");
    }

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class ReleaseApi : HttpMessageHandler
    {
        public int Uploads;
        private string? _name;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string json;
            if (request.Method == HttpMethod.Get)
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("/releases/tags/packframe-mods")) throw new Exception("Unexpected release tag");
                json = JsonSerializer.Serialize(new { id = 1, assets = _name is null ? Array.Empty<object>() :
                    new object[] { new { name = _name, state = "uploaded", browser_download_url = "https://example.com/archive.zip" } } });
            }
            else
            {
                if (request.RequestUri!.Host != "uploads.github.com") throw new Exception("Unexpected release creation");
                Uploads++;
                _name = request.RequestUri.Query["?name=".Length..];
                json = "{\"browser_download_url\":\"https://example.com/archive.zip\"}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public int Blobs, Commits, RefUpdates;
        public bool HasBaseTree, RemovesArchive, Forced, FailBlob, RejectRef;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            using var body = JsonDocument.Parse(request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(token));
            var status = HttpStatusCode.OK;
            string json;
            if (path.EndsWith("/repos/owner/repo")) json = "{\"default_branch\":\"master\"}";
            else if (path.EndsWith("/git/ref/heads/master")) json = "{\"object\":{\"sha\":\"parent\"}}";
            else if (path.EndsWith("/git/commits/parent")) json = "{\"tree\":{\"sha\":\"base\"}}";
            else if (path.EndsWith("/git/trees/base")) json = "{\"tree\":[{\"path\":\"mods.zip\"}]}";
            else if (path.EndsWith("/git/blobs"))
            {
                Blobs++;
                status = FailBlob ? HttpStatusCode.Forbidden : HttpStatusCode.Created;
                json = "{\"sha\":\"blob\",\"message\":\"rejected\"}";
            }
            else if (path.EndsWith("/git/trees"))
            {
                HasBaseTree = body.RootElement.GetProperty("base_tree").GetString() == "base";
                RemovesArchive = body.RootElement.GetProperty("tree").EnumerateArray().Any(entry =>
                    entry.GetProperty("path").GetString() == "mods.zip" && entry.GetProperty("sha").ValueKind == JsonValueKind.Null);
                json = "{\"sha\":\"new-tree\"}";
            }
            else if (path.EndsWith("/git/commits")) { Commits++; json = "{\"sha\":\"new-commit\"}"; }
            else if (path.EndsWith("/git/refs/heads/master"))
            {
                RefUpdates++;
                Forced = body.RootElement.GetProperty("force").GetBoolean();
                status = RejectRef ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK;
                json = "{\"message\":\"non-fast-forward\"}";
            }
            else throw new Exception("Unexpected API: " + path);
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
    }
}
