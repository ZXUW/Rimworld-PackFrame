using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
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
    DateTime LastModifiedAt
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

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    public Avalonia.Media.Imaging.Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasThumbnail)));
        }
    }

    public bool HasThumbnail => _thumbnail is not null;

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
    private string? _alias;
    private string? _category;
    private bool _showOriginalName;
    private bool _categoryHovered;

    public string? Alias
    {
        get => _alias;
        set
        {
            if (_alias == value) return;
            _alias = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alias)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAlias)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameOpacity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AliasOpacity)));
        }
    }

    public string? Category
    {
        get => _category;
        set
        {
            if (_category == value) return;
            _category = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryBadgeText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCategory)));
        }
    }

    public bool CategoryHovered
    {
        set
        {
            if (_categoryHovered == value) return;
            _categoryHovered = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryBadgeText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryBadgeOpacity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameColumnSpan)));
        }
    }

    public bool ShowOriginalName
    {
        set
        {
            if (_showOriginalName == value) return;
            _showOriginalName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameOpacity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AliasOpacity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryBadgeOpacity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameColumnSpan)));
        }
    }

    public bool HasAlias => !string.IsNullOrWhiteSpace(_alias);
    public double AliasOpacity => _showOriginalName ? 0 : 1;
    public double NameOpacity =>
        _showOriginalName || string.IsNullOrWhiteSpace(_alias) ? 1 : 0;
    public double CategoryBadgeOpacity => _showOriginalName && !_categoryHovered ? 0.14 : 1;
    public int NameColumnSpan => _showOriginalName && !_categoryHovered ? 2 : 1;

    public string RowName =>
        _showOriginalName || string.IsNullOrWhiteSpace(_alias) ? Name : _alias!;
    public string CategoryText => _category ?? string.Empty;
    public string CategoryBadgeText => _categoryHovered ? CategoryText : FormatCompactCategory(_category);
    public bool ShowCategoryBadge { get; init; } = true;

    internal static string FormatCompactCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;
        var parts = category.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return string.Empty;

        var full = string.Join("/", parts);
        if (parts.Length == 2 && VisualWidth(full) <= 16)
        {
            return full;
        }

        var label = parts.Length >= 3 ? parts[1] : parts[^1];
        if ((label is "内容拓展" or "非内容拓展") && parts.Length > 2)
        {
            label = parts[2];
        }
        if (VisualWidth(label) <= 16) return label;
        return TruncateVisual(label, 14).TrimEnd() + "…";
    }

    private static int VisualWidth(string s)
    {
        var w = 0;
        foreach (var ch in s) w += ch > 0x7F ? 2 : 1;
        return w;
    }

    private static string TruncateVisual(string s, int maxWidth)
    {
        var w = 0;
        for (var i = 0; i < s.Length; i++)
        {
            w += s[i] > 0x7F ? 2 : 1;
            if (w > maxWidth) return s[..i];
        }
        return s;
    }
    public bool HasCategory => ShowCategoryBadge && !string.IsNullOrWhiteSpace(_category);
    public Thickness TreeIndent { get; init; }
    public IReadOnlyList<CategoryFrameLayer>? CategoryFrames { get; init; }
    public Thickness CategoryRowPadding => CategoryFrames is null ? default : new Thickness(0, 3, 0, 3);

    public int? LoadOrder { get; init; }
    public double LoadOrderGutterWidth { get; init; }
    public bool HasLoadOrder => LoadOrder is > 0;
    public string LoadOrderText => LoadOrder is > 0
        ? LoadOrder.Value.ToString(CultureInfo.InvariantCulture)
        : string.Empty;
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
    public List<string> LoadBefore { get; init; } = new();
    public List<string> ForceLoadBefore { get; init; } = new();
    public List<string> IncompatibleWith { get; init; } = new();
    public List<string> ModDependencies { get; init; } = new();
    internal IReadOnlySet<string>? IgnoredPrerequisites { get; set; }
    internal bool IsPrerequisiteIgnored(string id) => IgnoredPrerequisites?.Contains(id) == true;
    internal IEnumerable<string> RequiredDependenciesForSort => IgnoredPrerequisites is null
        ? ModDependencies : ModDependencies.Where(id => !IsPrerequisiteIgnored(id));
    public Dictionary<string, string> ModDependencyNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ModDependencyUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsTranslationMod { get; init; }
    public List<string> SortOrderAfterNames { get; init; } = new();
    public List<string> SortOrderAfterIds { get; init; } = new();
    public int BadgeColumn { get; init; }
    public List<string> MissingDependencyNames { get; init; } = new();
    public List<string> IncompatibleNames { get; init; } = new();
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

        var entries = new List<(string Directory, ModSource Source, bool IsProfileMods, string? ProfileFolder)>();
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

                entries.Add((directory, source, isProfileMods, profileFolder));
            }
        }

        var loaded = new ModInfo?[entries.Count];
        Parallel.For(0, entries.Count, index =>
        {
            var entry = entries[index];
            loaded[index] = ReadMod(entry.Directory, entry.Source, game.Version);
        });

        var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        var allMods = new List<ModInfo>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var mod = loaded[index];
            if (mod is null)
            {
                continue;
            }

            mod = mod with
            {
                IsProfileMods = entry.IsProfileMods,
                ProfileFolder = entry.ProfileFolder
            };
            mod.VersionMismatch = IsVersionMismatch(mod.GameVersions, game.Version);
            if (mods.TryGetValue(mod.PackageId, out var existing))
            {
                allMods.Add(mod);
                if (entry.IsProfileMods && existing.IsProfileMods)
                {
                    continue;
                }

                existing.IsDuplicate = true;
                if (SourceRank(mod.Source) < SourceRank(existing.Source))
                {
                    mod.IsDuplicate = true;
                    mods[mod.PackageId] = mod;
                }
                continue;
            }

            mods[mod.PackageId] = mod;
            allMods.Add(mod);
        }

        return allMods
            .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static int SourceRank(ModSource source) => source switch
    {
        ModSource.Official => 0,
        ModSource.Workshop => 1,
        ModSource.Local => 2,
        _ => 3
    };

    // Description is read when the mod is selected instead of during the scan.
    public static string ReadDescription(string directory)
    {
        var aboutPath = Path.Combine(directory, "About", "About.xml");
        if (!File.Exists(aboutPath)) return string.Empty;
        try
        {
            var document = XDocument.Load(aboutPath);
            return (ElementValue(document.Root!, "description") ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
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

    internal static ModInfo? ReadMod(
        string directory,
        ModSource source,
        string? currentVersion = null
    )
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
            // Loaded on demand when the mod is selected; 2000+ descriptions in memory is wasted.
            var description = string.Empty;
            // Resolved on demand in the details pane; scanning thousands of mods must not hit the disk for previews.
            var preview = (string?)null;
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

            var (dependencyIds, dependencyNames, dependencyUrls) = ReadDependencies(root, currentVersion);
            var trimmedName = name.Trim();
            var trimmedId = packageId.Trim();
            return new ModInfo(
                trimmedName,
                author,
                Path.GetFullPath(directory),
                trimmedId,
                description.Trim(),
                preview,
                source,
                string.Join(", ", versions),
                source == ModSource.Workshop
                    ? Directory.GetLastWriteTime(directory)
                    : DateTime.MinValue
            )
            {
                LoadAfter = ReadIdList(root, "loadAfter"),
                ForceLoadAfter = ReadIdList(root, "forceLoadAfter"),
                LoadBefore = ReadIdList(root, "loadBefore"),
                ForceLoadBefore = ReadIdList(root, "forceLoadBefore"),
                IncompatibleWith = ReadIdList(root, "incompatibleWith"),
                ModDependencies = dependencyIds,
                ModDependencyNames = dependencyNames,
                ModDependencyUrls = dependencyUrls,
                IsTranslationMod = DetectTranslationMod(directory, trimmedName, trimmedId),
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

    private static readonly string[] TranslationNameTokens =
    {
        "汉化", "漢化", "中文化", "翻译", "翻譯", "简中", "繁中", "简体", "繁体",
        "translation", "localisation", "localization"
    };

    private static readonly string[] TranslationIdTokens =
    {
        "translation", "hanhua", ".chs", ".cht", "_chs", "_cht", ".zh", "_zh"
    };

    internal static bool DetectTranslationMod(string directory, string name, string packageId)
    {
        if (HasTranslationNameSignal(name) || HasTranslationIdSignal(packageId))
        {
            return true;
        }

        if (!Directory.Exists(directory)) return false;
        try
        {
            // Fast path: a mod that ships real content at its root is never translation-only,
            // so the common case costs one stat instead of enumerating every subfolder.
            if (HasContentDirectory(directory)) return false;

            var dirs = new List<string> { directory };
            dirs.AddRange(Directory.EnumerateDirectories(directory));
            var hasLanguages = dirs.Any(d => Directory.Exists(Path.Combine(d, "Languages")));
            if (!hasLanguages) return false;
            var hasContent = dirs.Any(HasContentDirectory);
            return !hasContent;
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasTranslationNameSignal(string? name) =>
        !string.IsNullOrWhiteSpace(name) && TranslationNameTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));

    internal static bool HasTranslationIdSignal(string? packageId) =>
        !string.IsNullOrWhiteSpace(packageId) && TranslationIdTokens.Any(t => packageId.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static bool HasContentDirectory(string directory) =>
        Directory.Exists(Path.Combine(directory, "Defs")) ||
        Directory.Exists(Path.Combine(directory, "Assemblies")) ||
        Directory.Exists(Path.Combine(directory, "Textures")) ||
        Directory.Exists(Path.Combine(directory, "Sounds"));

    private static (List<string> Ids, Dictionary<string, string> Names, Dictionary<string, string> Urls) ReadDependencies(
        XElement root,
        string? currentVersion)
    {
        var result = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddDependencyIds(
            root.Elements().FirstOrDefault(element => element.Name.LocalName == "modDependencies"),
            result,
            names,
            urls
        );

        var byVersion = root.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "modDependenciesByVersion");
        if (byVersion is not null)
        {
            var current = MajorMinor(currentVersion ?? string.Empty);
            if (current is not null)
            {
                var matchingVersion = byVersion.Elements().FirstOrDefault(element =>
                {
                    var name = element.Name.LocalName;
                    return name.Equals("v" + current, StringComparison.OrdinalIgnoreCase) ||
                        name.Equals(current, StringComparison.OrdinalIgnoreCase);
                });
                AddDependencyIds(matchingVersion, result, names, urls);
            }
        }

        return (result
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(), names, urls);
    }

    private static void AddDependencyIds(
        XElement? container,
        List<string> result,
        Dictionary<string, string> names,
        Dictionary<string, string> urls)
    {
        if (container is null)
        {
            return;
        }

        foreach (var item in container.Elements()
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
                var displayName = item.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "displayName")
                    ?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    names[packageId] = displayName;
                }

                var url = ReadBestDependencyUrl(item);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    urls[packageId] = url;
                }
            }
        }
    }

    private static string? ReadBestDependencyUrl(XElement item)
    {
        string? firstNonEmpty = null;
        foreach (var tag in new[] { "steamWorkshopUrl", "url", "downloadUrl" })
        {
            foreach (var element in item.Elements().Where(e => e.Name.LocalName == tag))
            {
                var val = element.Value.Trim();
                if (string.IsNullOrWhiteSpace(val)) continue;
                firstNonEmpty ??= val;
                if (ModpackService.ParseWorkshopId(val) is not null)
                {
                    return val;
                }
            }
        }

        return firstNonEmpty;
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

    internal static string? FindPreview(string directory)
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





