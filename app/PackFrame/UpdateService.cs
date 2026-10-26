using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

internal sealed record UpdateInfo(string Tag, string Url);

internal static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public static string CurrentVersion
    {
        get
        {
            var version = typeof(UpdateService).Assembly.GetName().Version;
            return version is null
                ? "1.0.4"
                : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }

    public static async Task<UpdateInfo?> CheckAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await CheckGiteeAsync(cancellationToken);
        }
        catch
        {
            return await CheckGitHubAsync(cancellationToken);
        }
    }

    public static bool IsNewer(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V').Split('-')[0];
        var current = CurrentVersion.Split('-')[0];
        return Version.TryParse(text, out var latest) &&
            Version.TryParse(current, out var installed) &&
            latest > installed;
    }

    private static async Task<UpdateInfo> CheckGiteeAsync(
        CancellationToken cancellationToken
    )
    {
        using var response = await Http.GetAsync(
            "https://gitee.com/api/v5/repos/zxuw/Rimworld-PackFrame/releases/latest",
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );
        var tag = document.RootElement.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("Gitee release tag is empty.");
        return new UpdateInfo(
            tag,
            $"https://gitee.com/zxuw/Rimworld-PackFrame/releases/tag/{Uri.EscapeDataString(tag)}"
        );
    }

    private static async Task<UpdateInfo> CheckGitHubAsync(
        CancellationToken cancellationToken
    )
    {
        using var response = await Http.GetAsync(
            "https://api.github.com/repos/ZXUW/Rimworld-PackFrame/releases/latest",
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );
        var tag = document.RootElement.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("GitHub release tag is empty.");
        return new UpdateInfo(
            tag,
            $"https://github.com/ZXUW/Rimworld-PackFrame/releases/tag/{Uri.EscapeDataString(tag)}"
        );
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame");
        return client;
    }
}

