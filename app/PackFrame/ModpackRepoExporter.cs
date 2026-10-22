using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PackFrame;

public sealed record ModpackRepoWorkshopMod(ulong Id, string Name);

public sealed class ModpackRepoManifest
{
    public long? Revision { get; set; }
    public string? Version { get; set; }
    public string? Changelog { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Image { get; set; }
    public string? GameVersion { get; set; }
    public string? Config { get; set; } = "config";
    public string? ConfigBaseUrl { get; set; }
    public string? WorkshopCollectionId { get; set; }
    public string? WorkshopUrl { get; set; }
    public string? Repo { get; set; }
    public string? Ref { get; set; }
    public string? ModsArchive { get; set; }
    public List<string> ConfigFiles { get; set; } = new();
    public List<ModpackRepoMod> Mods { get; set; } = new();
    public List<string> PackageIds { get; set; } = new();
}

public sealed class ModpackRepoMod
{
    public string? Source { get; set; } = "local";
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? PackageId { get; set; }
}

public sealed record ModpackRepoExportResult(
    string RepoPath,
    string Name,
    string Description,
    IReadOnlyList<string> PackageIds,
    IReadOnlyList<ulong> WorkshopIds,
    IReadOnlyList<ModpackRepoWorkshopMod> WorkshopMods,
    IReadOnlyList<string> LocalMods,
    IReadOnlyList<string> MissingMods
);

public static class ModpackRepoExporter
{
    public static ModpackRepoExportResult Export(
        string packRoot,
        ManagedInstance instance,
        Func<string, ModInfo?> resolveMod
    )
    {
        var displayName = string.IsNullOrWhiteSpace(instance.Metadata?.Name)
            ? instance.DisplayName
            : instance.Metadata.Name.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = instance.FolderName;
        }

        var repoName = SanitizePathName(displayName);
        if (repoName.Length == 0)
        {
            repoName = instance.FolderName;
        }

        var outputRoot = Path.Combine(packRoot, "Output profile");
        var repoPath = Path.Combine(outputRoot, repoName);
        if (Directory.Exists(repoPath))
        {
            Directory.Delete(repoPath, true);
        }

        Directory.CreateDirectory(repoPath);
        var configPath = Path.Combine(repoPath, "config");
        var modsPath = Path.Combine(repoPath, "mods");
        var imagesPath = Path.Combine(repoPath, "images");
        Directory.CreateDirectory(configPath);
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(imagesPath);

        var configFiles = CopyConfig(instance.ConfigDirectoryPath, configPath);
        var localMods = CopyLocalMods(instance, modsPath);
        var localIds = new HashSet<string>(
            localMods
                .Select(mod => mod.PackageId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!),
            StringComparer.OrdinalIgnoreCase
        );

        var packageIds = instance.ActivePackageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var workshopIds = new List<ulong>();
        var workshopMods = new List<ModpackRepoWorkshopMod>();
        var missingMods = new List<string>();
        foreach (var packageId in packageIds)
        {
            var mod = resolveMod(packageId);
            var workshopId = TryGetWorkshopId(mod, packageId);
            if (workshopId is not null)
            {
                if (!workshopIds.Contains(workshopId.Value))
                {
                    workshopIds.Add(workshopId.Value);
                    workshopMods.Add(new ModpackRepoWorkshopMod(
                        workshopId.Value,
                        string.IsNullOrWhiteSpace(mod?.Name) ? packageId : mod.Name
                    ));
                }
                continue;
            }

            if (mod is { Source: ModSource.Local } &&
                Directory.Exists(mod.Path) &&
                !localIds.Contains(packageId))
            {
                var local = CopyLocalMod(mod, modsPath);
                if (local is not null)
                {
                    localMods.Add(local);
                    localIds.Add(packageId);
                    continue;
                }
            }

            if (localIds.Contains(packageId) ||
                packageId.StartsWith("ludeon.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            missingMods.Add(packageId);
        }

        string? modsArchive = null;
        if (localMods.Count > 0 && Directory.Exists(modsPath))
        {
            modsArchive = "mods.zip";
            var archivePath = Path.Combine(repoPath, modsArchive);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            ZipFile.CreateFromDirectory(
                modsPath,
                archivePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false
            );
            Directory.Delete(modsPath, true);
        }
        else if (Directory.Exists(modsPath))
        {
            Directory.Delete(modsPath, true);
        }

        var id = string.IsNullOrWhiteSpace(instance.Metadata?.PackId)
            ? Slugify(displayName, instance.FolderName)
            : instance.Metadata.PackId.Trim();
        var manifest = new ModpackRepoManifest
        {
            Revision = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), checked((instance.Metadata?.InstalledRevision ?? 0) + 1)),
            Version = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss"),
            Changelog = string.Empty,
            ModsArchive = modsArchive,
            Id = id,
            WorkshopCollectionId = instance.Metadata?.WorkshopCollectionId,
            WorkshopUrl = instance.Metadata?.WorkshopCollectionId is { Length: > 0 } collectionId
                ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + collectionId : null,
            Name = displayName,
            Author = string.Empty,
            Description = displayName,
            GameVersion = instance.GameVersion,
            ConfigBaseUrl = string.IsNullOrWhiteSpace(instance.Metadata?.ConfigBaseUrl)
                ? null
                : instance.Metadata.ConfigBaseUrl.Trim(),
            ConfigFiles = configFiles,
            Mods = localMods,
            PackageIds = packageIds
        };

        File.WriteAllText(
            Path.Combine(repoPath, "pack.json"),
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        );
        File.WriteAllText(
            Path.Combine(repoPath, ".gitignore"),
            ".DS_Store\nThumbs.db\n*.log\n*.tmp\n**/bin/\n**/obj/\ndist/\n"
        );
        File.WriteAllText(
            Path.Combine(repoPath, "README.md"),
            $"# {displayName}\n\n"
            + "PackFrame GitHub modpack repository.\n\n"
            + "```text\n"
            + "pack.json          modpack manifest\n"
            + "config/            RimWorld mod config\n"
            + "mods.zip           local mods (large archives use a GitHub Release asset)\n"
            + "images/            cover images\n"
            + "```\n"
        );
        File.WriteAllText(Path.Combine(imagesPath, ".gitkeep"), string.Empty);
        PackLicenses.Ensure(
            repoPath,
            localMods
                .Select(mod => mod.Name ?? mod.PackageId ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToList()
        );

        return new ModpackRepoExportResult(
            repoPath,
            displayName,
            manifest.Description,
            packageIds,
            workshopIds,
            workshopMods,
            localMods
                .Select(mod => mod.Name ?? mod.PackageId ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToList(),
            missingMods
        );
    }

    public static void SetCollectionInfo(string repoPath, ulong collectionId)
    {
        var path = Path.Combine(repoPath, "pack.json");
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest is null)
        {
            return;
        }

        manifest.WorkshopCollectionId = collectionId.ToString();
        manifest.WorkshopUrl =
            "https://steamcommunity.com/sharedfiles/filedetails/?id=" + collectionId;
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        );
    }
    public static void SetRepoInfo(
        string repoPath,
        string owner,
        string repository,
        string reference
    )
    {
        var path = Path.Combine(repoPath, "pack.json");
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest is null)
        {
            return;
        }

        manifest.Repo = owner + "/" + repository;
        manifest.Ref = reference;
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        );
    }
    public static void SetPackInfo(string repoPath, string name, string description,
        string? version = null, string? changelog = null)
    {
        var path = Path.Combine(repoPath, "pack.json");
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            manifest.Name = name.Trim();
        }
        manifest.Description = description.Trim();
        if (!string.IsNullOrWhiteSpace(version)) manifest.Version = version.Trim();
        if (changelog is not null) manifest.Changelog = changelog.Trim();
        FileTransactions.WriteAtomic(path, temporary => File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        ));
    }
    public static void SetName(string repoPath, string name)
    {
        var path = Path.Combine(repoPath, "pack.json");
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        manifest.Name = name.Trim();
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        );
    }
    public static void SetCollectionUrl(string repoPath, string url)
    {
        var path = Path.Combine(repoPath, "pack.json");
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest is null)
        {
            return;
        }

        manifest.WorkshopUrl = url.Trim();
        var match = Regex.Match(url, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            manifest.WorkshopCollectionId = match.Groups[1].Value;
        }
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                manifest,
                PackFrameJsonContext.Default.ModpackRepoManifest
            )
        );
    }
    internal static void OptimizeCoverForUpload(string repoPath)
    {
        var manifestPath = Path.Combine(repoPath, "pack.json");
        if (!File.Exists(manifestPath)) return;
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), PackFrameJsonContext.Default.ModpackRepoManifest);
        if (string.IsNullOrWhiteSpace(manifest?.Image) || Uri.TryCreate(manifest.Image, UriKind.Absolute, out _)) return;
        var source = Path.GetFullPath(Path.Combine(repoPath, manifest.Image));
        var prefix = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Cover image is outside the exported repository.");
        if (File.Exists(source)) SetCoverImage(repoPath, source);
    }

    public static string? SetCoverImage(string repoPath, string sourcePath)
    {
        var imagesPath = Path.Combine(repoPath, "images");
        Directory.CreateDirectory(imagesPath);
        var encoded = CoverImageEncoder.Encode(sourcePath);
        var name = "cover.jpg";
        var target = Path.Combine(imagesPath, name);
        FileTransactions.WriteAtomic(target, temporary => File.WriteAllBytes(temporary, encoded));

        var path = Path.Combine(repoPath, "pack.json");
        if (File.Exists(path))
        {
            var manifest = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                PackFrameJsonContext.Default.ModpackRepoManifest
            );
            if (manifest is not null)
            {
                manifest.Image = "images/" + name;
                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(
                        manifest,
                        PackFrameJsonContext.Default.ModpackRepoManifest
                    )
                );
            }
        }

        UpdateReadmeCover(repoPath);
        foreach (var old in Directory.EnumerateFiles(imagesPath, "cover.*"))
            if (!Path.GetFullPath(old).Equals(Path.GetFullPath(target), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) File.Delete(old);
        return target;
    }

    public static void UpdateReadmeCover(string repoPath)
    {
        var path = Path.Combine(repoPath, "pack.json");
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            PackFrameJsonContext.Default.ModpackRepoManifest
        );
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Image))
        {
            return;
        }

        var readmePath = Path.Combine(repoPath, "README.md");
        var content = File.Exists(readmePath)
            ? File.ReadAllText(readmePath).TrimStart('\uFEFF')
            : "# " + (manifest.Name ?? "PackFrame") + "\n";
        content = Regex.Replace(
            content,
            @"\A(?:!\[[^\]]*\]\([^)]*\)\s*\n)+",
            string.Empty
        );
        var image = manifest.Image.Replace('\\', '/');
        File.WriteAllText(
            readmePath,
            $"![cover]({image})\n\n{content.TrimStart()}"
        );
    }

    private static List<string> CopyConfig(string source, string target)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.Equals("ModsConfig.xml", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Prefs.xml", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Mod_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(target, name), true);
            result.Add(name);
        }

        return result
            .OrderBy(name => name.Equals("ModsConfig.xml", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ModpackRepoMod> CopyLocalMods(
        ManagedInstance instance,
        string target
    )
    {
        var result = new List<ModpackRepoMod>();
        var source = Path.Combine(instance.DirectoryPath, "Mods");
        if (!Directory.Exists(source))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith("PF_", StringComparison.OrdinalIgnoreCase) ||
                IsWorkshopMod(directory))
            {
                continue;
            }

            var packageId = ModCatalog.ReadPackageId(directory);
            if (string.IsNullOrWhiteSpace(packageId))
            {
                continue;
            }

            var targetDirectory = Path.Combine(target, name);
            CopyDirectory(directory, targetDirectory);
            result.Add(new ModpackRepoMod
            {
                Name = ReadModName(directory) ?? name,
                Path = "mods/" + name,
                PackageId = packageId
            });
        }

        return result;
    }

    private static ModpackRepoMod? CopyLocalMod(ModInfo mod, string target)
    {
        var name = Path.GetFileName(
            mod.Path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            )
        );
        if (string.IsNullOrWhiteSpace(name) ||
            name.StartsWith("PF_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var targetDirectory = Path.Combine(target, name);
        CopyDirectory(mod.Path, targetDirectory);
        return new ModpackRepoMod
        {
            Name = mod.Name,
            Path = "mods/" + name,
            PackageId = mod.PackageId
        };
    }
    private static bool IsWorkshopMod(string directory)
    {
        if (ulong.TryParse(Path.GetFileName(directory), out _))
        {
            return true;
        }

        var publishedPath = Path.Combine(directory, "About", "PublishedFileId.txt");
        if (!File.Exists(publishedPath))
        {
            return false;
        }

        return ulong.TryParse(File.ReadAllText(publishedPath).Trim(), out _);
    }

    private static ulong? TryGetWorkshopId(ModInfo? mod, string packageId)
    {
        if (mod is not null)
        {
            var folder = Path.GetFileName(
                mod.Path.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                )
            );
            if (ulong.TryParse(folder, out var folderId))
            {
                return folderId;
            }

            var publishedPath = Path.Combine(mod.Path, "About", "PublishedFileId.txt");
            if (File.Exists(publishedPath) &&
                ulong.TryParse(File.ReadAllText(publishedPath).Trim(), out var publishedId))
            {
                return publishedId;
            }

            var linkId = ParseWorkshopIdFromUrl(mod.Link);
            if (linkId is not null)
            {
                return linkId;
            }
        }

        return null;
    }

    private static ulong? ParseWorkshopIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = Regex.Match(url, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success || !ulong.TryParse(match.Groups[1].Value, out var id))
        {
            return null;
        }

        return id;
    }

    private static string? ReadModName(string directory)
    {
        try
        {
            var about = Path.Combine(directory, "About", "About.xml");
            if (!File.Exists(about))
            {
                return null;
            }

            var value = XDocument.Load(about).Root?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "name")
                ?.Value.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        }

        foreach (var directory in Directory.EnumerateDirectories(
            source,
            "*",
            SearchOption.TopDirectoryOnly
        ))
        {
            var name = Path.GetFileName(directory);
            if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Cache", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(target, name));
        }
    }

    private static string SanitizePathName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) ? '_' : character
        )).Trim();
    }

    private static string Slugify(string value, string fallback)
    {
        var slug = new string(value
            .ToLowerInvariant()
            .Select(character =>
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '-' || character == '_'
                    ? character
                    : '-')
            .ToArray())
            .Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? fallback.ToLowerInvariant() : slug;
    }
}
