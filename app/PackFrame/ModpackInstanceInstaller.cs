using System;
using System.IO;

namespace PackFrame;

internal static class ModpackInstanceInstaller
{
    public static ManagedInstance Create(string packRoot, string? gameVersion, ModpackInstallResult result)
    {
        var staging = ValidateStaging(packRoot, result.ModsSourcePath, result.ConfigSourcePath);
        var instances = Path.Combine(packRoot, InstanceStore.InstancesFolderName);
        Directory.CreateDirectory(instances);
        var temporary = Path.Combine(instances, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            // Build under a hidden temporary root; Discover never sees a partial profile.
            var created = InstanceStore.CreateNew(temporary, null, gameVersion, result.PackageIds);
            InstanceStore.RenameInstance(created, result.Name);
            InstanceStore.SetPackBinding(created, result.Id, result.ConfigBaseUrl, result.ConfigFiles, result.SteamCmd);
            CopyTree(result.ModsSourcePath, Path.Combine(created.DirectoryPath, "Mods"));
            CopyTree(result.ConfigSourcePath, created.ConfigDirectoryPath);
            var validated = InstanceStore.LoadInstance(created.DirectoryPath);
            var destination = Path.Combine(instances, $"profile{InstanceStore.GetNextIndex(packRoot)}");
            Directory.Move(created.DirectoryPath, destination);
            var installed = new ManagedInstance
            {
                FolderName = Path.GetFileName(destination), DirectoryPath = Path.GetFullPath(destination),
                ActivePackageIds = validated.ActivePackageIds, GameVersion = validated.GameVersion,
                KnownExpansions = validated.KnownExpansions, Metadata = validated.Metadata
            };
            if (staging is not null) FileTransactions.TryDeleteDirectory(staging);
            return installed;
        }
        finally { FileTransactions.TryDeleteDirectory(temporary); }
    }

    internal static string? ValidateStaging(string packRoot, string? mods, string? config)
    {
        string? staging = null;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var runtime = Path.GetFullPath(Path.Combine(packRoot, "runtime"));
        foreach (var (path, name) in new[] { (mods, "Mods"), (config, "Config") })
        {
            if (path is null) continue;
            var full = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(full)!;
            if (!Path.GetFileName(full).Equals(name, comparison) ||
                !string.Equals(Path.GetDirectoryName(parent), runtime, comparison) ||
                !Path.GetFileName(parent).StartsWith("install-", StringComparison.Ordinal) ||
                (staging is not null && !staging.Equals(parent, comparison)))
                throw new IOException("Invalid install staging path: " + path);
            RejectLinks(runtime);
            RejectLinks(parent);
            RejectLinks(full);
            staging = parent;
        }
        return staging;
    }

    private static void RejectLinks(string path)
    {
        if (new DirectoryInfo(path).LinkTarget is not null)
            throw new IOException("Install staging must not contain directory links: " + path);
    }

    private static void CopyTree(string? source, string destination)
    {
        if (source is null) return;
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        RejectLinks(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (new FileInfo(file).LinkTarget is not null) throw new IOException("Install file is a link: " + file);
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
