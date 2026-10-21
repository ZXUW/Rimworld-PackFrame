using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PackFrame;

internal static class GameLaunchService
{
    public static bool IsRunning()
    {
        foreach (var name in new[] { "RimWorld", "RimWorldWin", "RimWorldWin64", "RimWorldLinux", "RimWorldLinux.x86_64" })
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

    // Link preparation remains a separate step so the UI can show its existing specific error.
    public static void Start(RimWorldInstallation game, ManagedInstance instance, string saveDataFolder)
    {
        instance.EnsureHealthy();
        try
        {
            InstanceStore.NormalizeConfigForLaunch(instance);
            var startedAt = DateTime.UtcNow;
            var process = Process.Start(BuildStartInfo(game.ExecutablePath!, game.GamePath,
                saveDataFolder, OperatingSystem.IsMacOS()))
                ?? throw new IOException("The game process could not be started.");
            _ = ObserveExitAsync(process, game.GamePath!, instance, saveDataFolder, startedAt);
        }
        catch
        {
            try { ModLinkService.Cleanup(game.GamePath!, instance); }
            catch (Exception exception) { AppLog.Error("Cleanup failed launch", exception); }
            throw;
        }
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
