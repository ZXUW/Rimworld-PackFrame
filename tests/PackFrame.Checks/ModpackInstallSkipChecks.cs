using System.Net;
using System.Text;
using PackFrame;

internal static class ModpackInstallSkipChecks
{
    public static async Task Run(string root)
    {
        var serverRoot = new Dictionary<string, (int Status, string Body)>(StringComparer.OrdinalIgnoreCase)
        {
            ["/config/Good.xml"] = (200, "<settings><ok>true</ok></settings>"),
            ["/config/ModsConfig.xml"] = (200, "<brokenRoot />")
        };

        using var listener = new HttpListener();
        var port = FreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        var serveTask = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                var path = context.Request.Url?.AbsolutePath ?? "/";
                if (serverRoot.TryGetValue(path, out var entry))
                {
                    context.Response.StatusCode = entry.Status;
                    var bytes = Encoding.UTF8.GetBytes(entry.Body);
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
                context.Response.Close();
            }
        });

        try
        {
            var packRoot = Path.Combine(root, "skip-install");
            var gamePath = Path.Combine(packRoot, "Game");
            Directory.CreateDirectory(Path.Combine(gamePath, "Mods"));
            var validLocalMod = Path.Combine(packRoot, "ExternalMods", "ValidLocal");
            Directory.CreateDirectory(Path.Combine(validLocalMod, "About"));
            File.WriteAllText(
                Path.Combine(validLocalMod, "About", "About.xml"),
                "<ModMetaData><name>Valid Local</name><packageId>valid.local</packageId></ModMetaData>"
            );

            var game = new RimWorldInstallation(true, gamePath, null, null, "1.6", null, false, null);
            var pack = new ModpackDefinition
            {
                Id = "skip-pack",
                Name = "Skip Pack",
                Revision = 1,
                ModsArchive = prefix + "missing/archive.zip",
                ConfigBaseUrl = prefix + "config",
                ConfigFiles = new List<string> { "Missing.xml", "ModsConfig.xml", "Good.xml" },
                Mods = new List<ModpackMod>
                {
                    new() { Source = "github", Name = "BadGithub", DownloadUrl = prefix + "missing/mod.zip" },
                    new() { Source = "local", Name = "BadLocal", Path = Path.Combine(packRoot, "NoSuchMod") },
                    new() { Source = "local", Name = "ValidLocal", PackageId = "valid.local", Path = validLocalMod },
                    new() { Source = "workshop", Name = "NoIdWorkshop" }
                },
                PackageIds = new List<string> { "valid.local" }
            };

            var strictModsRoot = Path.Combine(packRoot, "runtime", "install-strict", "Mods");
            var strictFailed = false;
            try
            {
                await ModpackService.InstallAsync(pack, game, packRoot, strictModsRoot);
            }
            catch
            {
                strictFailed = true;
            }
            Require(strictFailed, "default InstallAsync remains strict for update flows");

            var skipModsRoot = Path.Combine(packRoot, "runtime", "install-skip", "Mods");
            var result = await ModpackService.InstallAsync(
                pack,
                game,
                packRoot,
                skipModsRoot,
                workshopLink: "not-a-valid-link",
                language: "zh-CN",
                continueOnStageFailure: true,
                initialSkippedStages: new[] { "刷新合集详情（已使用本地缓存详情）：offline" }
            );

            Require(result.ModsArchiveName is null,
                "failed base archive download does not record archive as installed");
            Require(result.PackageIds.Contains("valid.local", StringComparer.OrdinalIgnoreCase) &&
                    Directory.Exists(Path.Combine(skipModsRoot, "ValidLocal")),
                "subsequent local mod stage continues after earlier archive/config/github failures");
            Require(result.ConfigSourcePath is not null &&
                    File.Exists(Path.Combine(result.ConfigSourcePath, "Good.xml")) &&
                    !File.Exists(Path.Combine(result.ConfigSourcePath, "ModsConfig.xml")),
                "valid config files still apply while missing or invalid ModsConfig.xml is skipped");
            Require(result.SkippedStages is { Count: 8 },
                "all failed stages are collected in SkippedStages");

            var installed = ModpackInstanceInstaller.Create(packRoot, "1.6", result);
            Require(installed.ActivePackageIds.Contains("valid.local", StringComparer.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(installed.ConfigDirectoryPath, "Good.xml")) &&
                    installed.Metadata?.ModsArchiveName is null,
                "ModpackInstanceInstaller creates healthy instance from partial result");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancelPropagated = false;
            try
            {
                await ModpackService.InstallAsync(
                    pack,
                    game,
                    packRoot,
                    Path.Combine(packRoot, "runtime", "cancel", "Mods"),
                    cancellationToken: cancelled.Token,
                    continueOnStageFailure: true
                );
            }
            catch (OperationCanceledException)
            {
                cancelPropagated = true;
            }
            Require(cancelPropagated, "user cancellation is never swallowed as a skipped stage");
            Console.WriteLine("PASS: modpack stage failure skip and reporting checks");
        }
        finally
        {
            listener.Stop();
            try { await serveTask; } catch { }
        }
    }

    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
