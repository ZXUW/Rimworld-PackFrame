using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

internal static class GameLaunchService
{
    private const string RimWorldAppId = "294100";
    private static readonly TimeSpan SteamLaunchTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SteamPollInterval = TimeSpan.FromSeconds(2);
    private static readonly string[] GameProcessNames =
        { "RimWorld", "RimWorldWin", "RimWorldWin64", "RimWorldLinux", "RimWorldLinux.x86_64" };

    public static bool IsRunning()
    {
        foreach (var name in GameProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                var running = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                if (running) return true;
            }
            catch (Exception exception) { AppLog.Error("Check game process: " + name, exception); }
        }
        return false;
    }

    public static string? GetSaveDataFolder(ManagedInstance instance) => instance.IsOfficial
        ? Path.GetDirectoryName(instance.ConfigDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        : instance.DirectoryPath;

    internal static ProcessStartInfo BuildStartInfo(string executable, string? gamePath, string saveDataFolder, bool macOS)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = gamePath ?? Path.GetDirectoryName(executable) ?? string.Empty,
            FileName = macOS ? "open" : executable
        };
        if (macOS)
            foreach (var arg in new[] { "-W", "-n", "-a", executable, "--args" }) info.ArgumentList.Add(arg);
        info.ArgumentList.Add("-logfile");
        info.ArgumentList.Add(Path.Combine(saveDataFolder, "player.log"));
        info.ArgumentList.Add($"-savedatafolder={saveDataFolder}");
        return info;
    }

    internal static ProcessStartInfo BuildSteamStartInfo(string steamExecutable, string saveDataFolder)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(steamExecutable) ?? string.Empty,
            FileName = steamExecutable
        };
        info.ArgumentList.Add("-applaunch");
        info.ArgumentList.Add(RimWorldAppId);
        info.ArgumentList.Add("-logfile");
        info.ArgumentList.Add(Path.Combine(saveDataFolder, "player.log"));
        info.ArgumentList.Add($"-savedatafolder={saveDataFolder}");
        return info;
    }

    // Link preparation remains a separate step so the UI can show its existing specific error.
    public static Task Start(RimWorldInstallation game, ManagedInstance instance, string saveDataFolder,
        CancellationToken cancellationToken = default)
    {
        instance.EnsureHealthy();
        try
        {
            InstanceStore.NormalizeConfigForLaunch(instance);
            var startedAt = DateTime.UtcNow;
            string? steam = FindSteamExecutable(game.GamePath);
            if (steam != null)
            {
                var existing = GetRunningGameProcessIds();
                if (existing.Count > 0)
                {
                    throw new IOException("RimWorld is already running.");
                }

                var steamProcess = Process.Start(BuildSteamStartInfo(steam, saveDataFolder))
                    ?? throw new IOException("The Steam process could not be started.");
                return ObserveSteamLaunchAsync(steamProcess, existing, game.GamePath!,
                    instance, saveDataFolder, startedAt, cancellationToken);
            }

            var process = Process.Start(BuildStartInfo(game.ExecutablePath!, game.GamePath,
                saveDataFolder, OperatingSystem.IsMacOS()))
                ?? throw new IOException("The game process could not be started.");
            return ObserveExitAsync(process, game.GamePath!, instance, saveDataFolder, startedAt);
        }
        catch
        {
            try { ModLinkService.Cleanup(game.GamePath!, instance); }
            catch (Exception exception) { AppLog.Error("Cleanup failed launch", exception); }
            throw;
        }
    }

    internal static string? FindSteamExecutable(string? gamePath)
    {
        return OperatingSystem.IsWindows() ? FindSteamExecutable(gamePath, true) : null;
    }

    internal static string? FindSteamExecutable(string? gamePath, bool windows)
    {
        if (!windows || string.IsNullOrWhiteSpace(gamePath))
        {
            return null;
        }

        try
        {
            var fullGamePath = Path.GetFullPath(gamePath);
            var directory = new DirectoryInfo(fullGamePath);
            for (var current = directory; current != null; current = current.Parent)
            {
                if (!current.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var steamRoot = current.Parent;
                if (steamRoot is null)
                {
                    return null;
                }

                var steamExecutable = Path.Combine(steamRoot.FullName, "steam.exe");
                var manifest = Path.Combine(current.FullName, "appmanifest_294100.acf");
                var common = Path.Combine(current.FullName, "common");
                if (File.Exists(steamExecutable) &&
                    File.Exists(manifest) &&
                    IsUnder(fullGamePath, common, windows))
                {
                    return steamExecutable;
                }

                return null;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Find Steam executable: " + gamePath, exception);
        }

        return null;
    }

    private static bool IsUnder(string path, string root, bool windows)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.Equals(fullRoot, comparison) ||
            fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static HashSet<int> GetRunningGameProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var name in GameProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    ids.Add(process.Id);
                    process.Dispose();
                }
            }
            catch (Exception exception) { AppLog.Error("Snapshot game process: " + name, exception); }
        }
        return ids;
    }

    private static async Task ObserveSteamLaunchAsync(Process steamProcess, HashSet<int> existingIds,
        string gamePath, ManagedInstance instance, string saveDataFolder, DateTime startedAt,
        CancellationToken cancellationToken)
    {
        steamProcess.Dispose();
        try
        {
            Process? gameProcess = null;
            var deadline = DateTime.UtcNow + SteamLaunchTimeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                gameProcess = FindNewGameProcess(existingIds);
                if (gameProcess != null) break;
                await Task.Delay(SteamPollInterval, cancellationToken);
            }

            if (gameProcess == null)
            {
                AppLog.Error("Steam launch timed out waiting for RimWorld",
                    new TimeoutException("RimWorld did not start within " +
                        SteamLaunchTimeout.TotalSeconds + " seconds."));
                return;
            }

            await ObserveExitAsync(gameProcess, gamePath, instance, saveDataFolder, startedAt);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error("Observe Steam launch", exception);
        }
    }

    private static Process? FindNewGameProcess(HashSet<int> existingIds)
    {
        foreach (var name in GameProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    if (!existingIds.Contains(process.Id)) return process;
                    process.Dispose();
                }
            }
            catch (Exception exception) { AppLog.Error("Find game process: " + name, exception); }
        }
        return null;
    }

    private static async Task ObserveExitAsync(Process process, string gamePath,
        ManagedInstance instance, string saveDataFolder, DateTime startedAt)
    {
        using (process)
        {
            try
            {
                await process.WaitForExitAsync();
                await Task.Delay(300);
                try { ModLinkService.Cleanup(gamePath, instance); }
                catch (Exception exception) { AppLog.Error("Cleanup links after game exit", exception); }
                await MirrorLogAsync(saveDataFolder, startedAt);
            }
            catch (Exception exception) { AppLog.Error("Observe game exit", exception); }
        }
    }

    private static async Task MirrorLogAsync(string saveDataFolder, DateTime startedAt)
    {
        var target = Path.Combine(saveDataFolder, "player.log");
        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= startedAt) return;
        string? source = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            source = RimWorldLocator.FindPlayerLogPath();
            if (!string.IsNullOrWhiteSpace(source) && File.Exists(source)) break;
            await Task.Delay(300);
        }
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
        Directory.CreateDirectory(saveDataFolder);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
                await input.CopyToAsync(output);
                return;
            }
            catch (IOException) when (attempt < 19) { await Task.Delay(200); }
        }
    }
}
