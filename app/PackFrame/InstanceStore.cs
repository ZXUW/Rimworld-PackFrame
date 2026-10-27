using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PackFrame;

public sealed class ManagedInstance
{
    public required string FolderName { get; init; }
    public required string DirectoryPath { get; init; }
    public List<string> ActivePackageIds { get; set; } = new();
    public string? GameVersion { get; set; }
    public List<string> KnownExpansions { get; set; } = new();
    public InstanceMetadata? Metadata { get; set; }
    public bool IsOfficial { get; init; }
    public bool IsScratch { get; init; }
    public string? LoadError { get; init; }
    public bool IsDamaged => LoadError is not null;

    public void EnsureHealthy()
    {
        if (IsDamaged) throw new IOException($"Instance is damaged: {DirectoryPath}\n{LoadError}");
    }
    public bool IsSteamCmd => Metadata?.SteamCmd == true;
    public string? DisplayNameOverride { get; init; }

    public string DisplayName
    {
        get
        {
            if (IsScratch)
            {
                return string.Empty;
            }

            var name = !string.IsNullOrWhiteSpace(DisplayNameOverride)
                ? DisplayNameOverride.Trim()
                : string.IsNullOrWhiteSpace(Metadata?.Name)
                    ? FolderName
                    : Metadata.Name.Trim();
            if (IsDamaged) return "⚠ " + name;
            return IsSteamCmd &&
                   !name.EndsWith("(cmd)", StringComparison.OrdinalIgnoreCase)
                ? name + " (cmd)"
                : name;
        }
    }

    public string ConfigDirectoryPath => IsScratch
        ? string.Empty
        : IsOfficial
            ? DirectoryPath
            : Path.Combine(DirectoryPath, "Config");
}

public sealed class InstanceMetadata
{
    public string? WorkshopCollectionId { get; set; }
    public string? PackSource { get; set; }
    public long? InstalledRevision { get; set; }
    public string? InstalledVersion { get; set; }
    public Dictionary<string, string> ManagedFiles { get; set; } = new();
    public List<string> ManagedModFolders { get; set; } = new();
    public List<string> InstalledOrder { get; set; } = new();
    public string? Name { get; set; }
    public string? Source { get; set; }
    public string? PackId { get; set; }
    public string? ConfigBaseUrl { get; set; }
    public List<string> ConfigFiles { get; set; } = new();
    public bool SteamCmd { get; set; }
}

public static class InstanceStore
{
    public const string MetadataFileName = "packframe.instance.json";
    public const string OfficialFolderName = "__official__";

    public const string InstancesFolderName = "instance";

    private static readonly Regex ProfileFolderPattern = new(
        "^profile(\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly string[] KnownExpansionIds =
    {
        "ludeon.rimworld",
        "ludeon.rimworld.royalty",
        "ludeon.rimworld.ideology",
        "ludeon.rimworld.biotech",
        "ludeon.rimworld.anomaly",
        "ludeon.rimworld.odyssey"
    };

    public static List<ManagedInstance> Discover(string packRoot)
    {
        var instances = new List<ManagedInstance>();
        if (!Directory.Exists(packRoot))
        {
            return instances;
        }

        var instancesRoot = Path.Combine(packRoot, InstancesFolderName);
        if (!Directory.Exists(instancesRoot))
        {
            return instances;
        }

        foreach (var directory in Directory.EnumerateDirectories(instancesRoot, "profile*"))
        {
            var match = ProfileFolderPattern.Match(Path.GetFileName(directory));
            if (!match.Success)
            {
                continue;
            }

            try { instances.Add(LoadInstance(directory)); }
            catch (IOException exception)
            {
                var damaged = new ManagedInstance { FolderName = Path.GetFileName(directory),
                    DirectoryPath = Path.GetFullPath(directory), LoadError = exception.Message };
                RemovePending(damaged);
                instances.Add(damaged);
            }
        }

        return instances
            .OrderBy(instance => GetProfileNumber(instance.FolderName))
            .ToList();
    }

    public static void EnsureSeed(string packRoot, string? gameVersion)
    {
        if (Discover(packRoot).Count > 0)
        {
            return;
        }

        var target = Path.Combine(packRoot, InstancesFolderName, "profile1");
        var configDirectory = Path.Combine(target, "Config");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(target, "Saves"));

        var legacyConfig = Path.Combine(packRoot, "profile", "Config");
        if (Directory.Exists(legacyConfig))
        {
            CopyDirectory(legacyConfig, configDirectory, overwrite: false);
        }

        var legacyModsConfig = Path.Combine(packRoot, "profile", "ModsConfig.xml");
        if (File.Exists(legacyModsConfig))
        {
            File.Copy(legacyModsConfig, Path.Combine(configDirectory, "ModsConfig.xml"), true);
        }

        if (!File.Exists(Path.Combine(configDirectory, "ModsConfig.xml")))
        {
            WriteModsConfig(
                Path.Combine(configDirectory, "ModsConfig.xml"),
                Array.Empty<string>(),
                gameVersion,
                Array.Empty<string>()
            );
        }

        WriteMetadata(target, "profile1", "seed");
    }

    public static ManagedInstance CreateNew(
        string packRoot,
        ManagedInstance? source,
        string? gameVersion,
        IReadOnlyList<string>? activePackageIds = null
    )
    {
        var folderName = $"profile{GetNextIndex(packRoot)}";
        var directory = Path.Combine(packRoot, InstancesFolderName, folderName);
        var configDirectory = Path.Combine(directory, "Config");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(directory, "Saves"));

        var version = !string.IsNullOrWhiteSpace(gameVersion)
            ? gameVersion
            : source?.GameVersion;
        var knownExpansions = source?.KnownExpansions ?? new List<string>();
        WriteModsConfig(
            Path.Combine(configDirectory, "ModsConfig.xml"),
            activePackageIds ?? Array.Empty<string>(),
            version,
            knownExpansions
        );
        WriteMetadata(directory, folderName, source?.FolderName ?? "new");

        return LoadInstance(directory);
    }

    public static ManagedInstance CopyInstance(
        string packRoot,
        ManagedInstance source,
        string? gameVersion = null
    )
    {
        source.EnsureHealthy();
        var folderName = $"profile{GetNextIndex(packRoot)}";
        var directory = Path.Combine(packRoot, InstancesFolderName, folderName);
        if (Directory.Exists(directory))
        {
            throw new IOException($"Instance folder already exists: {directory}");
        }

        CopyDirectory(source.DirectoryPath, directory, overwrite: true);
        File.Delete(Path.Combine(directory, "mod-links.txt"));
        var configPath = Path.Combine(directory, "Config", "ModsConfig.xml");
        if (!string.IsNullOrWhiteSpace(gameVersion) && File.Exists(configPath))
        {
            var (active, _, known) = ReadModsConfig(configPath);
            WriteModsConfig(configPath, active, gameVersion, known);
        }

        WriteMetadata(directory, folderName, source.FolderName);
        return LoadInstance(directory);
    }

    public static ManagedInstance CopyOfficial(string packRoot, ManagedInstance source)
    {
        source.EnsureHealthy();
        var folderName = $"profile{GetNextIndex(packRoot)}";
        var directory = Path.Combine(packRoot, InstancesFolderName, folderName);
        if (Directory.Exists(directory))
        {
            throw new IOException($"Instance folder already exists: {directory}");
        }

        var configDirectory = Path.Combine(directory, "Config");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(directory, "Saves"));

        if (Directory.Exists(source.ConfigDirectoryPath))
        {
            CopyDirectory(source.ConfigDirectoryPath, configDirectory, overwrite: true);
        }

        if (!File.Exists(Path.Combine(configDirectory, "ModsConfig.xml")))
        {
            WriteModsConfig(
                Path.Combine(configDirectory, "ModsConfig.xml"),
                source.ActivePackageIds,
                source.GameVersion,
                source.KnownExpansions
            );
        }

        WriteMetadata(directory, folderName, "official");
        return LoadInstance(directory);
    }

    public static void DeleteInstance(ManagedInstance instance)
    {
        RemovePending(instance);
        if (Directory.Exists(instance.DirectoryPath))
        {
            Directory.Delete(instance.DirectoryPath, true);
        }
    }

    public static void SetPackBinding(
        ManagedInstance instance,
        string? packId,
        string? configBaseUrl,
        IReadOnlyList<string> configFiles,
        bool steamCmd
    )
    {
        instance.EnsureHealthy();
        if (instance.IsScratch || instance.IsOfficial ||
            string.IsNullOrWhiteSpace(instance.DirectoryPath))
        {
            return;
        }

        var metadata = ReadMetadata(instance.DirectoryPath) ?? new InstanceMetadata();
        metadata.PackId = packId;
        metadata.ConfigBaseUrl = configBaseUrl;
        metadata.SteamCmd = steamCmd;
        metadata.ConfigFiles = configFiles
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(Path.GetFileName)
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(file => file!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        FileTransactions.WriteAtomic(
            Path.Combine(instance.DirectoryPath, MetadataFileName), temporary => File.WriteAllText(temporary,
            JsonSerializer.Serialize(
                metadata,
                PackFrameJsonContext.Default.InstanceMetadata
            )
        ));
        instance.Metadata = metadata;
    }

    public static void RenameInstance(ManagedInstance instance, string? name)
    {
        instance.EnsureHealthy();
        var source = instance.Metadata?.Source;
        WriteMetadata(
            instance.DirectoryPath,
            string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim(),
            source ?? string.Empty
        );
        instance.Metadata = ReadMetadata(instance.DirectoryPath);
    }

    internal static void SaveMetadata(ManagedInstance instance, InstanceMetadata metadata)
    {
        instance.EnsureHealthy();
        FileTransactions.WriteAtomic(Path.Combine(instance.DirectoryPath, MetadataFileName), path =>
            File.WriteAllText(path, JsonSerializer.Serialize(metadata, PackFrameJsonContext.Default.InstanceMetadata)));
        instance.Metadata = metadata;
    }

    private sealed record PendingActiveMods(
        List<string> Active,
        string? Version,
        List<string> Known
    );

    private static readonly Dictionary<string, PendingActiveMods> Pending =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool HasPendingMods => Pending.Count > 0;

    public static void StageActiveMods(
        ManagedInstance instance,
        IReadOnlyList<string> activePackageIds
    )
    {
        instance.EnsureHealthy();
        var active = activePackageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (instance.IsScratch)
        {
            instance.ActivePackageIds.Clear();
            instance.ActivePackageIds.AddRange(active);
            return;
        }

        var configDirectory = instance.ConfigDirectoryPath;
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            return;
        }

        var key = Path.GetFullPath(configDirectory);
        instance.ActivePackageIds = active;
        Pending[key] = new PendingActiveMods(
            active,
            instance.GameVersion,
            instance.KnownExpansions.ToList()
        );
    }

    public static void SavePendingMods()
    {
        foreach (var entry in Pending.ToList())
        {
            Directory.CreateDirectory(entry.Key);
            WriteModsConfig(
                Path.Combine(entry.Key, "ModsConfig.xml"),
                entry.Value.Active,
                entry.Value.Version,
                entry.Value.Known
            );
        }

        Pending.Clear();
    }

    public static void DiscardPendingMods()
    {
        Pending.Clear();
    }

    public static void RemovePending(ManagedInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.ConfigDirectoryPath))
        {
            return;
        }

        Pending.Remove(Path.GetFullPath(instance.ConfigDirectoryPath));
    }
    public static void UpdateActiveMods(
        ManagedInstance instance,
        IReadOnlyList<string> activePackageIds
    )
    {
        instance.EnsureHealthy();
        var configDirectory = instance.ConfigDirectoryPath;
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            return;
        }

        Directory.CreateDirectory(configDirectory);
        WriteModsConfig(
            Path.Combine(configDirectory, "ModsConfig.xml"),
            activePackageIds,
            instance.GameVersion,
            instance.KnownExpansions
        );
        instance.ActivePackageIds = activePackageIds.ToList();
    }

    public static string ExportInstance(string packRoot, ManagedInstance instance)
    {
        var outputDirectory = Path.Combine(packRoot, "Output profile");
        Directory.CreateDirectory(outputDirectory);

        var zipName = string.IsNullOrWhiteSpace(instance.Metadata?.Name)
            ? instance.DisplayName
            : instance.Metadata.Name.Trim();
        zipName = SanitizeFileName(zipName);
        if (zipName.Length == 0)
        {
            zipName = instance.FolderName;
        }

        var zipPath = Path.Combine(outputDirectory, $"{zipName}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var root = Path.GetFullPath(instance.DirectoryPath);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(
            root,
            "*",
            SearchOption.AllDirectories
        ))
        {
            var relative = Path.GetRelativePath(root, file);
            if (IsCachePath(relative) ||
                relative.Equals("mod-links.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryName = Path.Combine(instance.FolderName, relative)
                .Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        return zipPath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) ? '_' : character
        )).Trim();
    }

    private static bool IsCachePath(string relativePath)
    {
        foreach (var segment in relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries
        ))
        {
            if (segment.Contains("Cache", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string ExportOfficial(string packRoot, ManagedInstance instance)
    {
        var outputDirectory = Path.Combine(packRoot, "Output profile");
        Directory.CreateDirectory(outputDirectory);

        var zipPath = Path.Combine(outputDirectory, "official.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var staging = Path.Combine(
            Path.GetTempPath(),
            "packframe-export-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var configDirectory = Path.Combine(staging, "Config");
            Directory.CreateDirectory(configDirectory);
            CopyDirectory(instance.ConfigDirectoryPath, configDirectory, overwrite: true);
            ZipFile.CreateFromDirectory(
                staging,
                zipPath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false
            );
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
            catch
            {
            }
        }

        return zipPath;
    }

    public static bool ImportOfficialOverwrite(
        ManagedInstance target,
        string file,
        string? gameVersion
    )
    {
        if (!target.IsOfficial || !Directory.Exists(target.ConfigDirectoryPath))
        {
            return false;
        }

        if (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var temporary = Path.Combine(
                Path.GetTempPath(),
                "packframe-import-" + Guid.NewGuid().ToString("N")
            );

            try
            {
                Directory.CreateDirectory(temporary);
                ZipFile.ExtractToDirectory(file, temporary, true);
                var root = FindInstanceRoot(temporary);
                if (root is null)
                {
                    return false;
                }

                var sourceConfig = Path.Combine(root, "Config");
                if (!Directory.Exists(sourceConfig))
                {
                    return false;
                }

                CopyDirectory(sourceConfig, target.ConfigDirectoryPath, overwrite: true);
                return true;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporary))
                    {
                        Directory.Delete(temporary, true);
                    }
                }
                catch
                {
                }
            }
        }

        var profile = ParseProfileFile(file);
        if (profile is null)
        {
            return false;
        }

        var knownExpansions = profile.KnownExpansions.Count > 0
            ? profile.KnownExpansions
            : DeriveKnownExpansions(profile.ActivePackageIds);
        WriteModsConfig(
            Path.Combine(target.ConfigDirectoryPath, "ModsConfig.xml"),
            profile.ActivePackageIds,
            !string.IsNullOrWhiteSpace(gameVersion)
                ? gameVersion
                : profile.GameVersion,
            knownExpansions
        );
        return true;
    }

    public static bool IsSupportedProfileFile(string file)
    {
        return File.Exists(file) && ParseProfileFile(file) is not null;
    }

    public static bool IsSupportedImportFile(string file)
    {
        if (!File.Exists(file))
        {
            return false;
        }

        if (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ZipContainsInstance(file);
        }

        return ParseProfileFile(file) is not null;
    }

    public static ManagedInstance? ImportArchiveNew(string packRoot, string file)
    {
        return ImportZip(packRoot, file);
    }

    public static bool ImportArchiveOverwrite(ManagedInstance target, string file)
    {
        OverwriteFromZip(
            target,
            file,
            target.Metadata?.Name ?? string.Empty,
            Path.GetFileNameWithoutExtension(file)
        );
        return true;
    }

    public static ManagedInstance? ImportFileNew(string packRoot, string file, string? gameVersion)
    {
        var profile = ParseProfileFile(file);
        return profile is null
            ? null
            : CreateFromProfile(
                packRoot,
                profile,
                Path.GetFileNameWithoutExtension(file),
                gameVersion
            );
    }

    public static bool ImportFileOverwrite(
        ManagedInstance target,
        string file,
        string? gameVersion
    )
    {
        var profile = ParseProfileFile(file);
        if (profile is null)
        {
            return false;
        }

        OverwriteFromProfile(
            target,
            profile,
            Path.GetFileNameWithoutExtension(file),
            gameVersion,
            target.Metadata?.Name ?? string.Empty
        );
        return true;
    }

    public static int CountImportSources(string folder)
    {
        return GetImportSources(folder).Count;
    }

    public static List<ManagedInstance> ImportFolderNew(string packRoot, string folder, string? gameVersion)
    {
        var imported = new List<ManagedInstance>();
        foreach (var source in GetImportSources(folder))
        {
            var instance = source.Kind switch
            {
                ImportSourceKind.Folder => ImportDirectory(
                    packRoot,
                    source.Path,
                    Path.GetFileName(source.Path)
                ),
                ImportSourceKind.Zip => ImportZip(packRoot, source.Path),
                ImportSourceKind.List => CreateFromProfile(
                    packRoot,
                    source.Profile!,
                    Path.GetFileNameWithoutExtension(source.Path),
                    gameVersion
                ),
                _ => null
            };

            if (instance is not null)
            {
                imported.Add(instance);
            }
        }

        return imported;
    }

    public static void ImportFolderOverwrite(
        string packRoot,
        ManagedInstance target,
        string folder,
        string? gameVersion
    )
    {
        var sources = GetImportSources(folder);
        if (sources.Count != 1)
        {
            throw new InvalidOperationException("Overwrite requires exactly one import source.");
        }

        var source = sources[0];
        var metadata = target.Metadata ?? ReadMetadata(target.DirectoryPath);
        var displayName = metadata?.Name ?? string.Empty;
        var sourceName = source.Kind == ImportSourceKind.List
            ? Path.GetFileNameWithoutExtension(source.Path)
            : Path.GetFileName(source.Path);

        switch (source.Kind)
        {
            case ImportSourceKind.Folder:
                OverwriteFromDirectory(target, source.Path, displayName, sourceName);
                break;
            case ImportSourceKind.Zip:
                OverwriteFromZip(target, source.Path, displayName, sourceName);
                break;
            case ImportSourceKind.List:
                OverwriteFromProfile(
                    target,
                    source.Profile!,
                    sourceName,
                    gameVersion,
                    displayName
                );
                break;
        }
    }

    public static List<ManagedInstance> ImportAll(string packRoot, string? gameVersion)
    {
        var inputDirectory = Path.Combine(packRoot, "Input profile");
        Directory.CreateDirectory(inputDirectory);
        return ImportFolderNew(packRoot, inputDirectory, gameVersion);
    }

    private static List<ImportSource> GetImportSources(string folder)
    {
        var sources = new List<ImportSource>();
        if (!Directory.Exists(folder))
        {
            return sources;
        }

        if (File.Exists(Path.Combine(folder, "Config", "ModsConfig.xml")))
        {
            sources.Add(new ImportSource(ImportSourceKind.Folder, folder, null));
            return sources;
        }

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(path => path))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(new ImportSource(ImportSourceKind.Zip, file, null));
                continue;
            }

            if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".rml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".rws", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var profile = ParseProfileFile(file);
            if (profile is not null)
            {
                sources.Add(new ImportSource(ImportSourceKind.List, file, profile));
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(folder).OrderBy(path => path))
        {
            if (File.Exists(Path.Combine(directory, "Config", "ModsConfig.xml")))
            {
                sources.Add(new ImportSource(ImportSourceKind.Folder, directory, null));
            }
        }

        if (sources.Count == 0)
        {
            var root = FindInstanceRoot(folder);
            if (root is not null && !PathsEqual(root, folder))
            {
                sources.Add(new ImportSource(ImportSourceKind.Folder, root, null));
            }
        }

        return sources;
    }

    private static void OverwriteFromDirectory(
        ManagedInstance target,
        string sourceDirectory,
        string displayName,
        string sourceName
    )
    {
        var targetPath = Path.GetFullPath(target.DirectoryPath);
        var sourcePath = Path.GetFullPath(sourceDirectory);
        if (IsSameOrSubPath(targetPath, sourcePath) || IsSameOrSubPath(sourcePath, targetPath))
        {
            throw new IOException("Source and target instance folders overlap.");
        }

        var backupPath = targetPath + ".backup-" + Guid.NewGuid().ToString("N");
        Directory.Move(targetPath, backupPath);
        try
        {
            CopyDirectory(sourcePath, targetPath, overwrite: true);
            Directory.CreateDirectory(Path.Combine(targetPath, "Config"));
            Directory.CreateDirectory(Path.Combine(targetPath, "Saves"));
            WriteMetadata(targetPath, displayName, sourceName);
        }
        catch (Exception failure)
        {
            try
            {
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }
                if (Directory.Exists(backupPath))
                {
                    Directory.Move(backupPath, targetPath);
                }
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    $"Instance update and rollback failed. Backup: {backupPath}",
                    failure, rollbackFailure);
            }

            throw;
        }

        // Cleanup failure must not roll back a successfully installed instance.
        try { Directory.Delete(backupPath, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void OverwriteFromZip(
        ManagedInstance target,
        string zipPath,
        string displayName,
        string sourceName
    )
    {
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "packframe-import-" + Guid.NewGuid().ToString("N")
        );

        try
        {
            Directory.CreateDirectory(temporary);
            ZipFile.ExtractToDirectory(zipPath, temporary, true);
            var root = FindInstanceRoot(temporary);
            if (root is null)
            {
                throw new IOException("Zip does not contain an instance.");
            }

            OverwriteFromDirectory(target, root, displayName, sourceName);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, true);
                }
            }
            catch
            {
            }
        }
    }

    private static void OverwriteFromProfile(
        ManagedInstance target,
        ImportedProfile profile,
        string sourceName,
        string? gameVersion,
        string displayName
    )
    {
        var configDirectory = Path.Combine(target.DirectoryPath, "Config");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(target.DirectoryPath, "Saves"));

        var knownExpansions = profile.KnownExpansions.Count > 0
            ? profile.KnownExpansions
            : DeriveKnownExpansions(profile.ActivePackageIds);

        WriteModsConfig(
            Path.Combine(configDirectory, "ModsConfig.xml"),
            profile.ActivePackageIds,
            !string.IsNullOrWhiteSpace(gameVersion)
                ? gameVersion
                : profile.GameVersion,
            knownExpansions
        );
        WriteMetadata(target.DirectoryPath, displayName, sourceName);
    }

    private static bool IsSameOrSubPath(string path, string parent)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedPath.Equals(normalizedParent, comparison) ||
               normalizedPath.StartsWith(
                   normalizedParent + Path.DirectorySeparatorChar,
                   comparison
               );
    }

    private static bool PathsEqual(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Path.GetFullPath(first).Equals(Path.GetFullPath(second), comparison);
    }

    public static void OpenInstance(ManagedInstance instance)
    {
        OpenFolder(instance.ConfigDirectoryPath);
    }

    public static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
            return;
        }

        var command = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(folder);
        Process.Start(startInfo);
    }

    private static bool ZipContainsInstance(string file)
    {
        try
        {
            using var archive = ZipFile.OpenRead(file);
            return archive.Entries.Any(entry =>
                entry.FullName
                    .Replace('\\', '/')
                    .EndsWith("Config/ModsConfig.xml", StringComparison.OrdinalIgnoreCase)
            );
        }
        catch
        {
            return false;
        }
    }

    private static ManagedInstance? ImportZip(string packRoot, string file)
    {
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "packframe-import-" + Guid.NewGuid().ToString("N")
        );

        try
        {
            Directory.CreateDirectory(temporary);
            ZipFile.ExtractToDirectory(file, temporary, true);
            var root = FindInstanceRoot(temporary);
            return root is null
                ? null
                : ImportDirectory(packRoot, root, Path.GetFileNameWithoutExtension(file));
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, true);
                }
            }
            catch
            {
            }
        }
    }

    private static ManagedInstance ImportDirectory(string packRoot, string source, string sourceName)
    {
        var folderName = $"profile{GetNextIndex(packRoot)}";
        var directory = Path.Combine(packRoot, InstancesFolderName, folderName);
        if (Directory.Exists(directory))
        {
            throw new IOException($"Instance folder already exists: {directory}");
        }

        CopyDirectory(source, directory, overwrite: true);
        Directory.CreateDirectory(Path.Combine(directory, "Config"));
        Directory.CreateDirectory(Path.Combine(directory, "Saves"));

        var sourceMetadata = ReadMetadata(source);
        var sourceFolderName = Path.GetFileName(
            source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        );
        var displayName = !string.IsNullOrWhiteSpace(sourceMetadata?.Name) &&
                          !sourceMetadata.Name.Trim().Equals(
                              sourceFolderName,
                              StringComparison.OrdinalIgnoreCase
                          )
            ? sourceMetadata.Name.Trim()
            : folderName;
        WriteMetadata(directory, displayName, sourceName);
        return LoadInstance(directory);
    }

    private static ManagedInstance CreateFromProfile(
        string packRoot,
        ImportedProfile profile,
        string sourceName,
        string? gameVersion
    )
    {
        var folderName = $"profile{GetNextIndex(packRoot)}";
        var directory = Path.Combine(packRoot, InstancesFolderName, folderName);
        var configDirectory = Path.Combine(directory, "Config");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(Path.Combine(directory, "Saves"));

        var knownExpansions = profile.KnownExpansions.Count > 0
            ? profile.KnownExpansions
            : DeriveKnownExpansions(profile.ActivePackageIds);

        WriteModsConfig(
            Path.Combine(configDirectory, "ModsConfig.xml"),
            profile.ActivePackageIds,
            !string.IsNullOrWhiteSpace(gameVersion)
                ? gameVersion
                : profile.GameVersion,
            knownExpansions
        );
        WriteMetadata(directory, folderName, sourceName);

        return LoadInstance(directory);
    }

    private static ImportedProfile? ParseProfileFile(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(
                    ".rws",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return ParseSaveFile(path);
            }

            var text = File.ReadAllText(path);
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                return ParseJsonProfile(text);
            }

            return ParseXmlProfile(text);
        }
        catch
        {
            return null;
        }
    }

    private static ImportedProfile? ParseSaveFile(string path)
    {
        using var reader = XmlReader.Create(
            path,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true
            }
        );

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !reader.Name.Equals("meta", StringComparison.Ordinal))
            {
                continue;
            }

            using var subtree = reader.ReadSubtree();
            var meta = XElement.Load(subtree);
            var modIds = meta.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "modIds");
            if (modIds is null)
            {
                return null;
            }

            var version = meta.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "gameVersion")
                ?.Value.Trim();
            return new ImportedProfile(
                ReadListItems(modIds),
                version,
                new List<string>()
            );
        }

        return null;
    }

    private static ImportedProfile? ParseJsonProfile(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("activeMods", out var activeMods) ||
            activeMods.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = activeMods.EnumerateArray()
            .Select(element => element.GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .ToList();

        var version = root.TryGetProperty("version", out var versionElement)
            ? versionElement.GetString()
            : null;

        var known = root.TryGetProperty("knownExpansions", out var knownElement) &&
                    knownElement.ValueKind == JsonValueKind.Array
            ? knownElement.EnumerateArray()
                .Select(element => element.GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!.Trim())
                .ToList()
            : new List<string>();

        return new ImportedProfile(ids, version, known);
    }

    private static ImportedProfile? ParseXmlProfile(string text)
    {
        var document = XDocument.Parse(text);
        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        switch (root.Name.LocalName)
        {
            case "ModsConfigData":
            {
                var activeMods = root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "activeMods");
                var knownExpansions = root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "knownExpansions");
                var version = root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "version")
                    ?.Value.Trim();

                return new ImportedProfile(
                    ReadListItems(activeMods),
                    version,
                    ReadListItems(knownExpansions)
                );
            }
            case "ModList":
            {
                var modIds = root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "modIds");
                return new ImportedProfile(ReadListItems(modIds), null, new List<string>());
            }
            case "savedModList":
            case "savegame":
            {
                var meta = root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "meta");
                var modIds = meta?.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "modIds");
                if (modIds is null)
                {
                    return null;
                }

                var version = meta?.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "gameVersion")
                    ?.Value.Trim();
                return new ImportedProfile(ReadListItems(modIds), version, new List<string>());
            }
            default:
                return null;
        }
    }

    private static List<string> ReadListItems(XElement? parent)
    {
        if (parent is null)
        {
            return new List<string>();
        }

        return parent.Elements()
            .Where(element => element.Name.LocalName == "li")
            .Select(element => element.Value.Trim())
            .Where(id => id.Length > 0)
            .ToList();
    }

    private static List<string> DeriveKnownExpansions(IEnumerable<string> activeIds)
    {
        var active = new HashSet<string>(activeIds, StringComparer.OrdinalIgnoreCase);
        return KnownExpansionIds.Where(active.Contains).ToList();
    }

    private static string? FindInstanceRoot(string directory)
    {
        if (File.Exists(Path.Combine(directory, "Config", "ModsConfig.xml")))
        {
            return directory;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var root = FindInstanceRoot(child);
            if (root is not null)
            {
                return root;
            }
        }

        return null;
    }

    public static ManagedInstance LoadOfficial(string? configDirectory, string displayName)
    {
        var directory = string.IsNullOrWhiteSpace(configDirectory)
            ? string.Empty
            : Path.GetFullPath(configDirectory);
        try
        {
        var (active, version, known) = directory.Length == 0
            ? (new List<string>(), null, new List<string>())
            : ReadModsConfig(Path.Combine(directory, "ModsConfig.xml"));
        if (directory.Length > 0)
        {
            ApplyPending(directory, ref active, ref version, ref known);
        }

        return new ManagedInstance
        {
            FolderName = OfficialFolderName,
            DirectoryPath = directory,
            ActivePackageIds = active,
            GameVersion = version,
            KnownExpansions = known,
            IsOfficial = true,
            DisplayNameOverride = displayName
        };
        }
        catch (IOException exception)
        {
            var damaged = new ManagedInstance { FolderName = OfficialFolderName,
                DirectoryPath = directory, IsOfficial = true, DisplayNameOverride = displayName,
                LoadError = exception.Message };
            RemovePending(damaged);
            return damaged;
        }
    }

    internal static ManagedInstance LoadInstance(string directory)
    {
        var folderName = Path.GetFileName(directory);
        var (active, version, known) = ReadModsConfig(
            Path.Combine(directory, "Config", "ModsConfig.xml")
        );
        ApplyPending(
            Path.Combine(directory, "Config"),
            ref active,
            ref version,
            ref known
        );

        return new ManagedInstance
        {
            FolderName = folderName,
            DirectoryPath = Path.GetFullPath(directory),
            ActivePackageIds = active,
            GameVersion = version,
            KnownExpansions = known,
            Metadata = ReadMetadata(directory)
        };
    }

    private static void ApplyPending(
        string configDirectory,
        ref List<string> active,
        ref string? version,
        ref List<string> known
    )
    {
        var key = Path.GetFullPath(configDirectory);
        if (Pending.TryGetValue(key, out var pending))
        {
            active = pending.Active.ToList();
            version = pending.Version ?? version;
            known = pending.Known.ToList();
        }
    }
    private static (List<string> Active, string? Version, List<string> Known) ReadModsConfig(
        string modsConfigPath
    )
    {
        var active = new List<string>();
        string? version = null;
        var known = new List<string>();

        if (File.Exists(modsConfigPath))
        {
            try
            {
                var document = XDocument.Load(modsConfigPath);
                var root = document.Root;
                if (root?.Name.LocalName != "ModsConfigData" ||
                    !root.Elements().Any(element => element.Name.LocalName == "activeMods"))
                    throw new InvalidDataException("Invalid ModsConfig structure: " + modsConfigPath);
                if (root is not null)
                {
                    active = ReadListItems(root.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "activeMods"));
                    known = ReadListItems(root.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "knownExpansions"));
                    version = root.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "version")
                        ?.Value.Trim();
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Read mod list: " + modsConfigPath, exception);
                throw new IOException($"Could not read mod list: {modsConfigPath}", exception);
            }
        }

        var normalizedActive = active.Select(NormalizePackageId).ToList();
        var normalizedKnown = known.Select(NormalizePackageId).ToList();
        return (normalizedActive, version, normalizedKnown);
    }

    internal static void NormalizeConfigForLaunch(ManagedInstance instance)
    {
        instance.EnsureHealthy();
        var path = Path.Combine(instance.ConfigDirectoryPath, "ModsConfig.xml");
        if (!File.Exists(path)) return;
        // Read from disk, not the in-memory list: preserve external edits and unrelated fields.
        ReadModsConfig(path); // Validate before making any changes.
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var changed = false;
        foreach (var list in document.Root!.Elements().Where(element =>
            element.Name.LocalName is "activeMods" or "knownExpansions"))
        {
            foreach (var item in list.Elements().Where(element => element.Name.LocalName == "li"))
            {
                var normalized = NormalizePackageId(item.Value);
                if (item.Value == normalized) continue;
                item.Value = normalized;
                changed = true;
            }
        }
        if (!changed) return;
        File.Copy(path, path + ".before-normalize-" + Guid.NewGuid().ToString("N") + ".bak");
        FileTransactions.WriteAtomic(path, temporary => document.Save(temporary, SaveOptions.DisableFormatting));
    }

    private static string NormalizePackageId(string id)
    {
        return id.Trim().ToLowerInvariant();
    }

    private static void WriteModsConfig(
        string path,
        IReadOnlyList<string> activeIds,
        string? version,
        IReadOnlyList<string> knownExpansions
    )
    {
        var activeElements = activeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => new XElement("li", NormalizePackageId(id)))
            .ToArray();

        var known = new List<string>(KnownExpansionIds);
        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        foreach (var id in knownExpansions)
        {
            if (!string.IsNullOrWhiteSpace(id) && knownSet.Add(id))
            {
                known.Add(NormalizePackageId(id));
            }
        }

        var knownElements = known
            .Select(id => new XElement("li", id))
            .ToArray();

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "ModsConfigData",
                new XElement("version", string.IsNullOrWhiteSpace(version) ? "1.7" : version),
                new XElement("activeMods", activeElements),
                new XElement("knownExpansions", knownElements)
            )
        );

        FileTransactions.WriteAtomic(path, temporary => document.Save(temporary));
    }

    private static InstanceMetadata? ReadMetadata(string directory)
    {
        var path = Path.Combine(directory, MetadataFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path),
                PackFrameJsonContext.Default.InstanceMetadata
            ) ?? throw new InvalidDataException("Instance metadata is null: " + path);
        }
        catch (Exception exception)
        {
            AppLog.Error("Read instance metadata: " + path, exception);
            throw new IOException($"Could not read instance metadata: {path}", exception);
        }
    }

    private static void WriteMetadata(string directory, string name, string source)
    {
        var metadata = ReadMetadata(directory) ?? new InstanceMetadata();
        metadata.Name = name;
        metadata.Source = source;
        FileTransactions.WriteAtomic(
            Path.Combine(directory, MetadataFileName), temporary => File.WriteAllText(temporary,
            JsonSerializer.Serialize(
                metadata,
                PackFrameJsonContext.Default.InstanceMetadata
            )
        ));
    }

    internal static int GetNextIndex(string packRoot)
    {
        var indices = new List<int>();
        if (Directory.Exists(packRoot))
        {
            var instancesRoot = Path.Combine(packRoot, InstancesFolderName);
            if (!Directory.Exists(instancesRoot))
            {
                return indices.Count == 0 ? 1 : indices.Max() + 1;
            }

            foreach (var directory in Directory.EnumerateDirectories(instancesRoot, "profile*"))
            {
                var match = ProfileFolderPattern.Match(Path.GetFileName(directory));
                if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
                {
                    indices.Add(index);
                }
            }
        }

        return indices.Count == 0 ? 1 : indices.Max() + 1;
    }

    private static int GetProfileNumber(string folderName)
    {
        var match = ProfileFolderPattern.Match(folderName);
        return match.Success && int.TryParse(match.Groups[1].Value, out var index)
            ? index
            : int.MaxValue;
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(directory));
            CopyDirectory(directory, target, overwrite);
        }
    }

    private enum ImportSourceKind
    {
        Folder,
        Zip,
        List
    }

    private sealed record ImportSource(
        ImportSourceKind Kind,
        string Path,
        ImportedProfile? Profile
    );

    private sealed record ImportedProfile(
        List<string> ActivePackageIds,
        string? GameVersion,
        List<string> KnownExpansions
    );
}




