using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PackFrame;

public sealed record RimWorldInstallation(
    bool IsValid,
    string? GamePath,
    string? ExecutablePath,
    string? ExecutableName,
    string? Version,
    string? WorkshopPath,
    bool WorkshopExists,
    string? Error
);

public static class RimWorldLocator
{
    private static readonly string[] WindowsExecutables =
    {
        "RimWorld.exe",
        "RimworldWin.exe",
        "RimworldWin64.exe"
    };

    private static readonly string[] LinuxExecutables =
    {
        "RimWorldLinux",
        "RimWorldLinux.x86_64"
    };

    public static string? FindGameDirectory()
    {
        foreach (var candidate in EnumerateGameCandidates())
        {
            if (Detect(candidate).IsValid)
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static string? FindPlayerLogPath()
    {
        var candidates = new List<string>();
        var configDirectory = FindConfigDirectory();
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            var root = Path.GetDirectoryName(
                configDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                )
            );
            if (!string.IsNullOrWhiteSpace(root))
            {
                candidates.Add(Path.Combine(root, "Logs", "Player.log"));
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            if (OperatingSystem.IsWindows())
            {
                candidates.Add(Path.Combine(
                    profile,
                    "AppData",
                    "LocalLow",
                    "Ludeon Studios",
                    "RimWorld by Ludeon Studios",
                    "Logs",
                    "Player.log"
                ));
            }
            else if (OperatingSystem.IsMacOS())
            {
                candidates.Add(Path.Combine(
                    profile,
                    "Library",
                    "Application Support",
                    "Ludeon Studios",
                    "RimWorld by Ludeon Studios",
                    "Logs",
                    "Player.log"
                ));
            }
            else
            {
                var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(configRoot))
                {
                    configRoot = Path.Combine(profile, ".config");
                }

                candidates.Add(Path.Combine(
                    configRoot,
                    "unity3d",
                    "Ludeon Studios",
                    "RimWorld by Ludeon Studios",
                    "Logs",
                    "Player.log"
                ));
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? FindConfigDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile))
        {
            candidates.Add(Path.Combine(
                profile,
                "AppData",
                "LocalLow",
                "Ludeon Studios",
                "RimWorld by Ludeon Studios",
                "Config"
            ));
            candidates.Add(Path.Combine(
                profile,
                ".config",
                "unity3d",
                "Ludeon Studios",
                "RimWorld by Ludeon Studios",
                "Config"
            ));
            candidates.Add(Path.Combine(
                profile,
                "Library",
                "Application Support",
                "RimWorld",
                "Config"
            ));
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateGameCandidates()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in GetSteamRoots())
        {
            foreach (var library in ReadSteamLibraries(root))
            {
                libraries.Add(library);
            }
        }

        foreach (var library in libraries)
        {
            yield return Path.Combine(library, "steamapps", "common", "RimWorld");
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady ||
                    (drive.DriveType != DriveType.Fixed &&
                     drive.DriveType != DriveType.Removable))
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(
                root,
                "SteamLibrary",
                "steamapps",
                "common",
                "RimWorld"
            );
            yield return Path.Combine(
                root,
                "Steam",
                "steamapps",
                "common",
                "RimWorld"
            );
            yield return Path.Combine(
                root,
                "Program Files (x86)",
                "Steam",
                "steamapps",
                "common",
                "RimWorld"
            );
            yield return Path.Combine(
                root,
                "Program Files",
                "Steam",
                "steamapps",
                "common",
                "RimWorld"
            );
            yield return Path.Combine(
                root,
                "Games",
                "Steam",
                "steamapps",
                "common",
                "RimWorld"
            );
            yield return Path.Combine(
                root,
                "GOG Games",
                "RimWorld"
            );
        }
    }

    private static List<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                roots.Add(Path.GetFullPath(path.Trim().Trim('"')));
            }
            catch
            {
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Valve\Steam"
                );
                Add(key?.GetValue("SteamPath") as string);
            }
            catch
            {
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Valve\Steam"
                );
                Add(key?.GetValue("InstallPath") as string);
            }
            catch
            {
            }

            Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam"
            ));
            Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Steam"
            ));
        }

        return roots.ToList();
    }

    private static List<string> ReadSteamLibraries(string steamRoot)
    {
        var result = new List<string> { steamRoot };
        var files = new[]
        {
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamRoot, "config", "libraryfolders.vdf")
        };
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(
                    text,
                    "\"path\"\\s+\"([^\"]+)\""
                ))
                {
                    var library = match.Groups[1].Value
                        .Replace("\\\\", "\\");
                    if (Directory.Exists(library))
                    {
                        result.Add(library);
                    }
                }
            }
            catch
            {
            }
        }

        return result;
    }

    public static RimWorldInstallation Detect(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Invalid("请选择 RimWorld 游戏目录。");
        }

        var clean = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
        clean = ExpandHome(clean);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(clean);
        }
        catch
        {
            return Invalid("路径格式无效。");
        }

        if (File.Exists(fullPath))
        {
            return Invalid("请选择文件夹，不是文件。");
        }

        if (!Directory.Exists(fullPath))
        {
            return Invalid($"文件夹不存在：{fullPath}");
        }

        var gameRoot = fullPath;
        string? executablePath = null;
        string? executableName = null;

        if (OperatingSystem.IsWindows())
        {
            foreach (var name in WindowsExecutables)
            {
                var candidate = Path.Combine(fullPath, name);
                if (File.Exists(candidate))
                {
                    executablePath = candidate;
                    executableName = name;
                    break;
                }
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            foreach (var name in LinuxExecutables)
            {
                var candidate = Path.Combine(fullPath, name);
                if (File.Exists(candidate))
                {
                    executablePath = candidate;
                    executableName = name;
                    break;
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var selectedIsBundle = Path.GetFileName(fullPath).Equals("RimWorld.app", StringComparison.OrdinalIgnoreCase);
            var bundlePath = selectedIsBundle ? fullPath : Path.Combine(fullPath, "RimWorld.app");
            var macExecutable = Path.Combine(bundlePath, "Contents", "MacOS", "RimWorld");

            if (Directory.Exists(bundlePath) && File.Exists(macExecutable))
            {
                executablePath = bundlePath;
                executableName = "RimWorld.app";
                if (selectedIsBundle)
                {
                    gameRoot = Directory.GetParent(fullPath)?.FullName ?? fullPath;
                }
            }
        }

        if (executablePath is null)
        {
            return Invalid(GetExpectedExecutableMessage());
        }

        var version = ReadVersion(gameRoot);
        var workshopPath = FindWorkshopPath(gameRoot);
        var workshopExists = workshopPath is not null && Directory.Exists(workshopPath);

        return new RimWorldInstallation(
            true,
            gameRoot,
            executablePath,
            executableName,
            version,
            workshopPath,
            workshopExists,
            null
        );
    }

    private static RimWorldInstallation Invalid(string error)
    {
        return new RimWorldInstallation(false, null, null, null, null, null, false, error);
    }

    private static string GetExpectedExecutableMessage()
    {
        if (OperatingSystem.IsWindows())
        {
            return "文件夹内未找到 RimWorld.exe、RimworldWin.exe 或 RimworldWin64.exe。";
        }
        if (OperatingSystem.IsLinux())
        {
            return "文件夹内未找到 RimWorldLinux 或 RimWorldLinux.x86_64。";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "文件夹内未找到 RimWorld.app。";
        }
        return "当前操作系统不受支持。";
    }

    private static string? ReadVersion(string gameRoot)
    {
        var versionFile = Path.Combine(gameRoot, "Version.txt");
        if (!File.Exists(versionFile))
        {
            return null;
        }

        try
        {
            var version = File.ReadAllText(versionFile).Trim();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindWorkshopPath(string gameRoot)
    {
        try
        {
            var directory = new DirectoryInfo(gameRoot);
            while (directory is not null)
            {
                if (directory.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(directory.FullName, "workshop", "content", "294100");
                }
                directory = directory.Parent;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('~'))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return path;
        }

        if (path == "~")
        {
            return home;
        }

        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            return Path.Combine(home, path[2..]);
        }

        return path;
    }
}

