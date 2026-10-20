using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Media;

namespace PackFrame;

public enum ModSource
{
    Local,
    Workshop,
    Official,
    Unknown
}

public sealed record ModInfo(
    string Name,
    string Author,
    string Path,
    string PackageId,
    string Description,
    string? PreviewPath,
    ModSource Source,
    string GameVersions,
    DateTime SubscribedAt
) : INotifyPropertyChanged
{
    private IBrush? _rowBrush;
    private IEffect? _nameEffect;
    private string? _colorLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IBrush? RowBrush
    {
        get => _rowBrush;
        set => SetField(ref _rowBrush, value);
    }

    public IEffect? NameEffect
    {
        get => _nameEffect;
        set => SetField(ref _nameEffect, value);
    }

    public string? ColorLabel
    {
        get => _colorLabel;
        set => SetField(ref _colorLabel, value);
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public bool IsLocal => Source == ModSource.Local;
    public bool IsProfileMods { get; init; }
    public string? ProfileFolder { get; init; }
    public bool SearchMatched { get; set; } = true;
    public double SearchOpacity => SearchMatched ? 1 : 0.22;
    public bool VersionMismatch { get; set; }
    public bool IsDuplicate { get; set; }
    public bool SortError { get; set; }
    public string? SortAfter { get; set; }
    public string? BadgeTooltip { get; set; }
    public List<string> LoadAfter { get; init; } = new();
    public List<string> ForceLoadAfter { get; init; } = new();
    public List<string> ModDependencies { get; init; } = new();
    public List<string> SortOrderAfterNames { get; init; } = new();
    public List<string> SortOrderAfterIds { get; init; } = new();
    public int BadgeColumn { get; init; }
    public List<string> MissingDependencyNames { get; init; } = new();
    public string Link { get; init; } = string.Empty;
    public IBrush? NameBrush { get; init; }

    public IReadOnlyList<string> ActiveBadges
    {
        get
        {
            var badges = new List<string>(4);
            if (SortError)
            {
                badges.Add("S");
            }
            if (VersionMismatch)
            {
                badges.Add("V");
            }
            if (IsDuplicate)
            {
                badges.Add("R");
            }
            if (IsLocal)
            {
                badges.Add("L");
            }
            return badges;
        }
    }
}

public static class AppPaths
{
    public static string? FindPackRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (IsPackRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsPackRoot(string directory)
    {
        if (File.Exists(Path.Combine(directory, "pack.json")))
        {
            return true;
        }

        if (Directory.Exists(Path.Combine(directory, "instance")) ||
            Directory.Exists(Path.Combine(directory, "Input profile")) ||
            Directory.Exists(Path.Combine(directory, "Output profile")))
        {
            return true;
        }

        return Directory.Exists(Path.Combine(directory, "app", "PackFrame"));
    }

    public static IReadOnlyList<string> LoadActivePackageIds(string? packRoot)
    {
        if (string.IsNullOrWhiteSpace(packRoot))
        {
            return Array.Empty<string>();
        }

        var candidates = new[]
        {
            Path.Combine(packRoot, "run", "Config", "ModsConfig.xml"),
            Path.Combine(packRoot, "profile", "ModsConfig.xml")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var document = XDocument.Load(path);
                var activeMods = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "activeMods");
                if (activeMods is null)
                {
                    return Array.Empty<string>();
                }

                return activeMods.Elements()
                    .Where(element => element.Name.LocalName == "li")
                    .Select(element => element.Value.Trim())
                    .Where(id => id.Length > 0)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        return Array.Empty<string>();
    }
}

public static class ModCatalog
{
    private static readonly string[] PreviewNames =
    {
        "Preview.png",
        "Preview.jpg",
        "Preview.jpeg",
        "preview.png",
        "preview.jpg",
        "preview.jpeg"
    };

    public static Task<List<ModInfo>> LoadAsync(RimWorldInstallation game, string? packRoot)
    {
        return Task.Run(() => Load(game, packRoot));
    }

    private static List<ModInfo> Load(RimWorldInstallation game, string? packRoot)
    {
        var roots = new List<(string Path, ModSource Source, bool IsProfileMods, string? ProfileFolder)>();
        if (!string.IsNullOrWhiteSpace(game.GamePath))
        {
            roots.Add((Path.Combine(game.GamePath, "Data"), ModSource.Official, false, null));
        }
        if (!string.IsNullOrWhiteSpace(packRoot))
        {
            var instancesRoot = Path.Combine(packRoot, "instance");
            if (Directory.Exists(instancesRoot))
            {
                foreach (var instanceDirectory in Directory.EnumerateDirectories(
                    instancesRoot,
                    "profile*"
                ))
                {
                    var modsDirectory = Path.Combine(instanceDirectory, "Mods");
                    if (Directory.Exists(modsDirectory))
                    {
                        roots.Add((modsDirectory, ModSource.Local, true, Path.GetFileName(instanceDirectory)));
                    }
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(game.GamePath))
        {
            roots.Add((Path.Combine(game.GamePath, "Mods"), ModSource.Local, false, null));
        }
        if (!string.IsNullOrWhiteSpace(game.WorkshopPath))
        {
            roots.Add((game.WorkshopPath, ModSource.Workshop, false, null));
        }

        var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        var allMods = new List<ModInfo>();

        foreach (var (root, source, isProfileMods, profileFolder) in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (source == ModSource.Local &&
                    Path.GetFileName(directory).StartsWith(
                        "PF_",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var mod = ReadMod(directory, source);
                if (mod is null)
                {
                    continue;
                }

                mod = mod with { IsProfileMods = isProfileMods, ProfileFolder = profileFolder };
                mod.VersionMismatch = IsVersionMismatch(mod.GameVersions, game.Version);
                if (mods.TryGetValue(mod.PackageId, out var existing))
                {
                    allMods.Add(mod);
                    if (isProfileMods && existing.IsProfileMods)
                    {
                        continue;
                    }

                    if (existing.IsProfileMods && mod.Source == ModSource.Workshop)
                    {
                        continue;
                    }

                    existing.IsDuplicate = true;
                    if (mod.Source == ModSource.Local &&
                        existing.Source != ModSource.Local)
                    {
                        mod.IsDuplicate = true;
                        mods[mod.PackageId] = mod;
                    }
                    continue;
                }

                mods[mod.PackageId] = mod;
                allMods.Add(mod);
            }
        }

        return allMods
            .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string? ReadPackageId(string directory)
    {
        var aboutPath = Path.Combine(directory, "About", "About.xml");
        if (!File.Exists(aboutPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(aboutPath);
            var root = document.Root;
            if (root is null)
            {
                return null;
            }

            var packageId = ElementValue(root, "packageId");
            return string.IsNullOrWhiteSpace(packageId) ? null : packageId.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static ModInfo? ReadMod(string directory, ModSource source)
    {
        var aboutPath = Path.Combine(directory, "About", "About.xml");
        if (!File.Exists(aboutPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(aboutPath);
            var root = document.Root;
            if (root is null)
            {
                return null;
            }

            var packageId = ElementValue(root, "packageId");
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            var name = ElementValue(root, "name") ?? Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = packageId;
            }

            var author = ReadAuthor(root);
            var description = ElementValue(root, "description") ?? string.Empty;
            var preview = FindPreview(directory);
            var versions = root.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "supportedVersions")
                ?.Elements()
                .Where(element => element.Name.LocalName == "li")
                .Select(element => element.Value.Trim())
                .Where(value => value.Length > 0)
                .ToList() ?? new List<string>();
            if (versions.Count == 0)
            {
                var singleVersion = ElementValue(root, "gameVersion");
                if (!string.IsNullOrWhiteSpace(singleVersion))
                {
                    versions.Add(singleVersion);
                }
            }

            return new ModInfo(
                name.Trim(),
                author,
                Path.GetFullPath(directory),
                packageId.Trim(),
                description.Trim(),
                preview,
                source,
                string.Join(", ", versions),
                source == ModSource.Workshop
                    ? Directory.GetCreationTime(directory)
                    : DateTime.MinValue
            )
            {
                LoadAfter = ReadIdList(root, "loadAfter"),
                ForceLoadAfter = ReadIdList(root, "forceLoadAfter"),
                ModDependencies = ReadDependencyIds(root),
                Link = ReadLink(root)
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadIdList(XElement root, string name)
    {
        return root.Elements()
            .FirstOrDefault(element => element.Name.LocalName == name)
            ?.Elements()
            .Where(element => element.Name.LocalName == "li")
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .ToList() ?? new List<string>();
    }

    private static List<string> ReadDependencyIds(XElement root)
    {
        var dependencies = root.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "modDependencies");
        if (dependencies is null)
        {
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var item in dependencies.Elements()
            .Where(element => element.Name.LocalName == "li"))
        {
            var packageId = item.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "packageId")
                ?.Value.Trim();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageId = item.Value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(packageId))
            {
                result.Add(packageId);
            }
        }

        return result;
    }

    private static string ReadLink(XElement root)
    {
        foreach (var name in new[] { "steamWorkshopUrl", "url", "downloadUrl" })
        {
            var value = ElementValue(root, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool IsVersionMismatch(string versions, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(versions) ||
            string.IsNullOrWhiteSpace(currentVersion))
        {
            return false;
        }

        var current = MajorMinor(currentVersion);
        if (current is null)
        {
            return false;
        }

        return !versions
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(MajorMinor)
            .Any(version => string.Equals(
                version,
                current,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    private static string? MajorMinor(string version)
    {
        var text = version.Trim();
        var first = text.IndexOf('.');
        if (first <= 0)
        {
            return null;
        }

        var second = text.IndexOf('.', first + 1);
        var minor = second > first + 1
            ? text[(first + 1)..second]
            : text[(first + 1)..];
        minor = new string(minor.TakeWhile(char.IsDigit).ToArray());
        if (minor.Length == 0)
        {
            return null;
        }

        return $"{text[..first]}.{minor}";
    }
    private static string ReadAuthor(XElement root)
    {
        var authors = root.Elements()
            .Where(element => element.Name.LocalName == "author")
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

        if (authors.Count == 0)
        {
            var authorsElement = root.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "authors");
            if (authorsElement is not null)
            {
                authors = authorsElement.Elements()
                    .Where(element => element.Name.LocalName == "li")
                    .Select(element => element.Value.Trim())
                    .Where(value => value.Length > 0)
                    .ToList();
            }
        }

        return string.Join(", ", authors);
    }

    private static string? FindPreview(string directory)
    {
        var aboutDirectory = Path.Combine(directory, "About");
        foreach (var name in PreviewNames)
        {
            var aboutPreview = Path.Combine(aboutDirectory, name);
            if (File.Exists(aboutPreview))
            {
                return aboutPreview;
            }

            var rootPreview = Path.Combine(directory, name);
            if (File.Exists(rootPreview))
            {
                return rootPreview;
            }
        }

        return null;
    }

    private static string? ElementValue(XElement root, string name)
    {
        return root.Elements()
            .FirstOrDefault(element => element.Name.LocalName == name)
            ?.Value
            .Trim();
    }
}





