using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PackFrame;

internal static class ModLinkService
{
    private const string ManifestFileName = "mod-links.txt";

    public static void Prepare(string gamePath, ManagedInstance instance, string? packRoot = null)
    {
        instance.EnsureHealthy();
        if (packRoot is not null)
        {
            var failures = Recover(gamePath, packRoot);
            if (failures.Count > 0 && Directory.Exists(Path.Combine(gamePath, "Mods")))
            {
                // A stale link can affect the requested package even if it came from another profile.
                foreach (var directory in Directory.EnumerateDirectories(Path.Combine(gamePath, "Mods")))
                {
                    var id = ModCatalog.ReadPackageId(directory);
                    if (id is not null && instance.ActivePackageIds.Contains(id, StringComparer.OrdinalIgnoreCase) &&
                        Path.GetFileName(directory).StartsWith("PF_", StringComparison.Ordinal))
                        throw new AggregateException("Unresolved mod links affect this instance.", failures);
                }
            }
        }
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
        var planned = new List<(string Link, string Target)>();
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var manifestPath = Path.Combine(instance.DirectoryPath, ManifestFileName);
        try
        {
        foreach (var packageId in instance.ActivePackageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!localMods.TryGetValue(packageId, out var target) ||
                gamePackages.ContainsKey(packageId))
            {
                continue;
            }

            var linkName = "PF_" + Sanitize(packageId);
            var linkPath = Path.Combine(gameMods, linkName);
            if (Directory.Exists(linkPath) || File.Exists(linkPath) || new DirectoryInfo(linkPath).LinkTarget is not null)
            {
                throw new IOException($"Mod link path is already occupied: {linkPath}");
            }

            if (!names.Add(linkPath)) throw new IOException("Duplicate planned link path: " + linkPath);
            links.Add(linkPath + "|" + target);
            planned.Add((linkPath, target));
        }
        if (links.Count > 0)
        {
            FileTransactions.WriteAtomic(manifestPath, temporary => File.WriteAllLines(temporary, links));
            foreach (var item in planned) CreateLink(item.Link, item.Target);
        }
        }
        catch (Exception failure)
        {
            AppLog.Error("Prepare mod links: " + instance.DirectoryPath, failure);
            try { Cleanup(gamePath, instance); }
            catch (Exception cleanupFailure) { AppLog.Error("Rollback mod links", cleanupFailure); }
            throw;
        }
    }

    // Called only after the launcher has checked that the game is not running.
    public static List<Exception> Recover(string gamePath, string packRoot)
    {
        var failures = new List<Exception>();
        var root = Path.Combine(packRoot, InstanceStore.InstancesFolderName);
        if (!Directory.Exists(root)) return failures;
        foreach (var directory in Directory.EnumerateDirectories(root, "profile*"))
        {
            try
            {
            Cleanup(gamePath, new ManagedInstance
            {
                FolderName = Path.GetFileName(directory), DirectoryPath = directory
            });
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                AppLog.Error("Recover instance links: " + directory, exception);
            }
        }
        // Recover links created by older versions before they wrote their manifest.
        var gameMods = Path.Combine(gamePath, "Mods");
        if (!Directory.Exists(gameMods)) return failures;
        foreach (var path in Directory.EnumerateDirectories(gameMods, "PF_*"))
        {
            try
            {
            var info = new DirectoryInfo(path);
            if (info.LinkTarget is null) continue;
            var target = Path.GetFullPath(Path.Combine(info.Parent!.FullName, info.LinkTarget));
            if (!IsUnder(target, root)) continue;
            var modRoot = Directory.GetParent(target);
            if (modRoot?.Name != "Mods" || modRoot.Parent?.Parent?.FullName != Path.GetFullPath(root)) continue;
            var packageId = ModCatalog.ReadPackageId(target);
            if (packageId is not null && info.Name == "PF_" + Sanitize(packageId))
                DeleteLink(path);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                AppLog.Error("Recover orphan link: " + path, exception);
            }
        }
        if (failures.Count > 0) AppLog.Error("Link recovery summary", new AggregateException(failures));
        return failures;
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

        var remaining = new List<string>();
        var failures = new List<Exception>();
        foreach (var line in File.ReadAllLines(manifestPath))
        {
            try
            {
                var separator = line.IndexOf('|');
                if (separator <= 0)
                {
                    continue;
                }

                var linkPath = line[..separator];
                var target = line[(separator + 1)..];
                if (!IsUnder(linkPath, Path.Combine(gamePath, "Mods")))
                {
                    remaining.Add(line); // May belong to another game installation.
                    continue;
                }
                var info = new DirectoryInfo(linkPath);
                var isCurrentProfileTarget = IsUnder(
                    target,
                    Path.Combine(instance.DirectoryPath, "Mods")
                );
                var isSiblingProfileTarget = !isCurrentProfileTarget &&
                    IsManagedProfileModsTarget(
                        target,
                        Path.GetDirectoryName(instance.DirectoryPath)
                    );
                if (!Path.GetFileName(linkPath).StartsWith("PF_", StringComparison.Ordinal) ||
                    (!isCurrentProfileTarget && !isSiblingProfileTarget))
                    throw new IOException($"Invalid managed link record: {linkPath}");
                if (info.LinkTarget is null)
                {
                    if (info.Exists || File.Exists(linkPath))
                        throw new IOException($"Managed link was replaced by a regular path: {linkPath}");
                    continue;
                }
                var actual = Path.GetFullPath(Path.Combine(info.Parent!.FullName, info.LinkTarget));
                if (!actual.Equals(Path.GetFullPath(target), OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException($"Managed link target changed: {linkPath}");
                DeleteLink(linkPath);
            }
            catch (Exception exception)
            {
                remaining.Add(line);
                failures.Add(exception);
                AppLog.Error("Cleanup mod link: " + line, exception);
            }
        }
        if (remaining.Count == 0) File.Delete(manifestPath);
        else FileTransactions.WriteAtomic(manifestPath, temporary => File.WriteAllLines(temporary, remaining));
        if (failures.Count > 0) throw new AggregateException("Some mod links could not be cleaned up.", failures);
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

        Directory.CreateSymbolicLink(linkPath, targetPath);
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

    private static bool IsManagedProfileModsTarget(string target, string? instancesRoot)
    {
        if (string.IsNullOrWhiteSpace(instancesRoot))
        {
            return false;
        }

        var fullTarget = Path.GetFullPath(target);
        var modsDirectory = Path.GetDirectoryName(fullTarget);
        var profileDirectory = modsDirectory is null
            ? null
            : Path.GetDirectoryName(modsDirectory);
        var profileRoot = profileDirectory is null
            ? null
            : Path.GetDirectoryName(profileDirectory);
        return profileRoot is not null &&
            string.Equals(
                Path.GetFullPath(profileRoot),
                Path.GetFullPath(instancesRoot),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            ) &&
            Path.GetFileName(modsDirectory)?.Equals(
                "Mods",
                StringComparison.OrdinalIgnoreCase
            ) == true &&
            Path.GetFileName(profileDirectory)?.StartsWith(
                "profile",
                StringComparison.OrdinalIgnoreCase
            ) == true;
    }

    private static void DeleteLink(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.Delete(path);
        }
        else
        {
            File.Delete(path);
        }
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


