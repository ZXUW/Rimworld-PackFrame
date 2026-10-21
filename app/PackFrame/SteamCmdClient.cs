using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

internal static class SteamCmdClient
{
    private const uint RimWorldAppId = 294100;
    private static string DownloadUrl => OperatingSystem.IsWindows()
        ? "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"
        : OperatingSystem.IsMacOS()
            ? "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz"
            : "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";

    private static readonly HttpClient Http = CreateHttpClient();

    public static string? Find(string packRoot)
    {
        var executable = OperatingSystem.IsWindows()
            ? "steamcmd.exe"
            : "steamcmd.sh";
        var candidates = new[]
        {
            Path.Combine(packRoot, "tools", "steamcmd", executable),
            Path.Combine(packRoot, "steamcmd", executable),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "SteamCMD",
                executable
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "SteamCMD",
                executable
            ),
            Path.Combine("C:\\", "steamcmd", executable)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<string> EnsureAsync(
        string packRoot,
        Action<string>? status = null,
        CancellationToken cancellationToken = default, string? language = null
    )
    {
        var existing = Find(packRoot);
        if (existing is not null)
        {
            return existing;
        }

        var root = Path.Combine(packRoot, "tools", "steamcmd");
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(
            Path.GetTempPath(),
            "packframe-steamcmd-" + Guid.NewGuid().ToString("N") +
                (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz")
        );

        try
        {
            status?.Invoke(OperationText.SteamDownload(language));
            await using (var input = await Http.GetStreamAsync(
                             DownloadUrl,
                             cancellationToken
                         ))
            await using (var output = File.Create(zipPath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            status?.Invoke(OperationText.SteamExtract(language));
            await Task.Run(
                () =>
                {
                    if (OperatingSystem.IsWindows())
                        ZipFile.ExtractToDirectory(zipPath, root, overwriteFiles: true);
                    else
                    {
                        ExtractUnixArchive(zipPath, root);
                    }
                },
                cancellationToken
            );

            var executable = Path.Combine(
                root,
                OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh"
            );
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException(
                    "SteamCMD executable was not found after extraction.",
                    executable
                );
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    executable,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead |
                    UnixFileMode.OtherExecute
                );
            }

            status?.Invoke(OperationText.SteamInit(language));
            try
            {
                await RunAsync(
                    executable,
                    new[] { "+login", "anonymous", "+quit" },
                    cancellationToken,
                    timeout: TimeSpan.FromMinutes(10)
                );
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(1000, cancellationToken);
                await RunAsync(
                    executable,
                    new[] { "+login", "anonymous", "+quit" },
                    cancellationToken,
                    timeout: TimeSpan.FromMinutes(10)
                );
            }

            return executable;
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
            }
        }
    }

    internal static void ExtractUnixArchive(string path, string destination)
    {
        using var archive = File.OpenRead(path);
        using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    }

    public static async Task<IReadOnlyList<string>> DownloadWorkshopAsync(
        string steamCmdPath,
        string modsRoot,
        IReadOnlyList<ulong> workshopIds,
        Action<string>? status = null,
        CancellationToken cancellationToken = default, string? language = null
    )
    {
        if (workshopIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        Directory.CreateDirectory(modsRoot);
        var args = new List<string> { "+login", "anonymous" };
        foreach (var id in workshopIds)
        {
            args.Add("+workshop_download_item");
            args.Add(RimWorldAppId.ToString());
            args.Add(id.ToString());
            args.Add("validate");
        }
        args.Add("+quit");

        var steamCmdRoot = Path.GetDirectoryName(steamCmdPath)
            ?? throw new InvalidOperationException("SteamCMD root was not found.");
        var cacheRoot = Path.Combine(
            steamCmdRoot,
            "steamapps",
            "workshop",
            "content",
            RimWorldAppId.ToString()
        );

        status?.Invoke($"{OperationText.SteamProgress(language)} 0/{workshopIds.Count}");
        using var pollSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var pollTask = PollDownloadsAsync(
            cacheRoot,
            workshopIds,
            status,
            pollSource.Token, language
        );
        try
        {
            await RunAsync(
                steamCmdPath,
                args,
                cancellationToken,
                timeout: TimeSpan.FromMinutes(60)
            );
        }
        finally
        {
            pollSource.Cancel();
            try
            {
                await pollTask;
            }
            catch
            {
            }
        }

        var installed = new List<string>();
        foreach (var id in workshopIds)
        {
            var source = Path.Combine(cacheRoot, id.ToString());
            if (!Directory.Exists(source))
            {
                throw new InvalidOperationException(
                    $"SteamCMD did not download Workshop item {id}."
                );
            }

            var target = Path.Combine(modsRoot, id.ToString());
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }

            CopyDirectory(source, target);
            installed.Add(target);
        }

        return installed;
    }

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        TimeSpan timeout
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("SteamCMD could not be started.");
        var output = new StringBuilder();
        var outputLock = new object();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (outputLock)
            {
                output.AppendLine(eventArgs.Data);
            }


        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (outputLock)
            {
                output.AppendLine(eventArgs.Data);
            }


        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException("SteamCMD timed out.");
        }

        string text;
        lock (outputLock)
        {
            text = output.ToString();
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"SteamCMD exited with code {process.ExitCode}.{Environment.NewLine}{Tail(text)}"
            );
        }

        return text;
    }

    private static async Task PollDownloadsAsync(
        string cacheRoot,
        IReadOnlyList<ulong> workshopIds,
        Action<string>? status,
        CancellationToken cancellationToken, string? language
    )
    {
        var last = -1;
        while (!cancellationToken.IsCancellationRequested)
        {
            var completed = 0;
            foreach (var id in workshopIds)
            {
                if (Directory.Exists(Path.Combine(cacheRoot, id.ToString())))
                {
                    completed++;
                }
            }

            if (completed != last)
            {
                last = completed;
                status?.Invoke(
                    $"{OperationText.SteamProgress(language)} {completed}/{workshopIds.Count}"
                );
            }

            try
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static string Tail(string text)
    {
        const int max = 2000;
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[^max..];
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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame/1.0");
        return client;
    }
}






