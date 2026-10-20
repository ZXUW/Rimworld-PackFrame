using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PackFrame;

internal static class ModLinkService
{
    private const string ManifestFileName = "mod-links.txt";

    public static void Prepare(string gamePath, ManagedInstance instance)
    {
        if (instance.IsOfficial || instance.IsScratch ||
            string.IsNullOrWhiteSpace(instance.DirectoryPath))
        {
            return;
        }

        Cleanup(gamePath, instance);
        var modsRoot = Path.Combine(instance.DirectoryPath, "Mods");
        if (!Directory.Exists(modsRoot) || instance.ActivePackageIds.Count == 0)
        {
            return;
        }

        var gameMods = Path.Combine(gamePath, "Mods");
        Directory.CreateDirectory(gameMods);

        var localMods = ReadPackageFolders(modsRoot);
        var gamePackages = ReadPackageFolders(gameMods);
        var links = new List<string>();
        foreach (var packageId in instance.ActivePackageIds)
        {
            if (!localMods.TryGetValue(packageId, out var target) ||
                gamePackages.ContainsKey(packageId))
            {
                continue;
            }

            var linkName = "PF_" + Sanitize(packageId);
            var linkPath = Path.Combine(gameMods, linkName);
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
            {
                continue;
            }

            CreateLink(linkPath, target);
            links.Add(linkPath + "|" + target);
        }

        if (links.Count == 0)
        {
            return;
        }

        var manifestPath = Path.Combine(instance.DirectoryPath, ManifestFileName);
        File.WriteAllLines(manifestPath, links);
    }

    public static void Cleanup(string gamePath, ManagedInstance instance)
    {
        if (instance.IsOfficial || instance.IsScratch ||
            string.IsNullOrWhiteSpace(instance.DirectoryPath))
        {
            return;
        }

        var manifestPath = Path.Combine(instance.DirectoryPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadAllLines(manifestPath))
            {
                var separator = line.IndexOf('|');
                if (separator <= 0)
                {
                    continue;
                }

                var linkPath = line[..separator];
                if (!IsUnder(linkPath, Path.Combine(gamePath, "Mods")))
                {
                    continue;
                }

                RemoveLink(linkPath);
            }
        }
        finally
        {
            try
            {
                File.Delete(manifestPath);
            }
            catch
            {
            }
        }
    }

    private static Dictionary<string, string> ReadPackageFolders(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (IsReparsePoint(directory))
            {
                continue;
            }

            var packageId = ModCatalog.ReadPackageId(directory);
            if (!string.IsNullOrWhiteSpace(packageId) && !result.ContainsKey(packageId))
            {
                result[packageId] = directory;
            }
        }

        return result;
    }

    private static void CreateLink(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Run(
                "cmd.exe",
                $"/c mklink /J \"{linkPath}\" \"{targetPath}\""
            );
            return;
        }

        Run("ln", $"-s \"{targetPath}\" \"{linkPath}\"");
    }

    private static void RemoveLink(string linkPath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (Directory.Exists(linkPath) && IsReparsePoint(linkPath))
            {
                Run("cmd.exe", $"/c rmdir \"{linkPath}\"");
            }

            return;
        }

        if (Directory.Exists(linkPath) || File.Exists(linkPath))
        {
            File.Delete(linkPath);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string parent)
    {
        var fullPath = Path.GetFullPath(path);
        var fullParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(
            fullParent + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) ? '_' : character
        ));
    }

    private static void Run(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"{fileName} could not be started.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} failed with exit code {process.ExitCode}."
            );
        }
    }
}


