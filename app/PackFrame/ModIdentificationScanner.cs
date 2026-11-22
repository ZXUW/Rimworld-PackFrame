using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PackFrame;

public sealed record ModIdentification(
    string PackageId,
    string Name,
    string Author,
    string FolderName,
    bool TranslationNameSignal,
    bool TranslationIdSignal,
    bool LegacyTranslationHeuristic,
    bool HasLanguageFiles,
    bool HasRaceDef,
    bool HasXenotypeDef,
    bool HasHairDef,
    bool HasApparelDef,
    bool HasFurnitureDef,
    bool HasBuildingDef,
    bool HasGeneDef,
    bool HasFactionDef,
    bool HasPatchFiles,
    bool HasAssemblies,
    IReadOnlyList<string> HardDependencies,
    IReadOnlyList<string> LoadAfter,
    IReadOnlyList<string> SameAuthorPackageIds,
    IReadOnlyList<string> TitleBracketTags,
    IReadOnlyList<string> AliasBracketTags,
    IReadOnlyList<string> FolderBracketTags,
    IReadOnlyList<string> Keywords
);

public static class ModIdentificationScanner
{
    private static readonly EnumerationOptions FileOptions = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
    };

    public static IReadOnlyList<ModIdentification> Scan(IEnumerable<ModInfo> mods)
    {
        var unique = mods.Where(mod => !string.IsNullOrWhiteSpace(mod.PackageId))
            .GroupBy(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var authorGroups = unique.Where(mod => !string.IsNullOrWhiteSpace(mod.Author))
            .GroupBy(mod => mod.Author.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(mod => mod.PackageId).ToArray(), StringComparer.OrdinalIgnoreCase);
        return unique.Select(mod => ScanOne(mod, authorGroups)).ToList();
    }

    public static void WriteTsv(string path, IReadOnlyList<ModIdentification> results)
    {
        static string Value(string? value) => (value ?? string.Empty)
            .Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        static string List(IEnumerable<string> values) => string.Join(";", values.Select(Value));
        var lines = new List<string>
        {
            "PackageId\tName\tAuthor\tFolder\tTranslationNameSignal\tTranslationIdSignal\tLegacyTranslationHeuristic\tLanguages\tRaceDef\tXenotypeDef\tHairDef\tApparelDef\tFurnitureDef\tBuildingDef\tGeneDef\tFactionDef\tPatchFiles\tAssemblies\tHardDependencies\tLoadAfter\tSameAuthorPackageIds\tTitleBracketKeywords\tAliasBracketKeywords\tFolderBracketKeywords\tKeywordSignals"
        };
        foreach (var item in results)
            lines.Add(string.Join('\t', new[]
            {
                Value(item.PackageId), Value(item.Name), Value(item.Author), Value(item.FolderName),
                Flag(item.TranslationNameSignal), Flag(item.TranslationIdSignal), Flag(item.LegacyTranslationHeuristic),
                Flag(item.HasLanguageFiles), Flag(item.HasRaceDef), Flag(item.HasXenotypeDef),
                Flag(item.HasHairDef), Flag(item.HasApparelDef), Flag(item.HasFurnitureDef), Flag(item.HasBuildingDef),
                Flag(item.HasGeneDef), Flag(item.HasFactionDef), Flag(item.HasPatchFiles), Flag(item.HasAssemblies)
            }) + "\t" +
                string.Join('\t', new[]
                {
                    List(item.HardDependencies), List(item.LoadAfter), List(item.SameAuthorPackageIds),
                    List(item.TitleBracketTags), List(item.AliasBracketTags), List(item.FolderBracketTags),
                    List(item.Keywords)
                }));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static ModIdentification ScanOne(ModInfo mod, IReadOnlyDictionary<string, string[]> authorGroups)
    {
        var folder = string.IsNullOrWhiteSpace(mod.Path) ? string.Empty :
            Path.GetFileName(mod.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var languageFiles = false;
        var race = false;
        var xenotype = false;
        var hair = false;
        var apparel = false;
        var furniture = false;
        var building = false;
        var gene = false;
        var faction = false;
        var patches = false;
        var assemblies = false;

        if (!string.IsNullOrWhiteSpace(mod.Path) && Directory.Exists(mod.Path))
        {
            var root = mod.Path;
            try
            {
                languageFiles = EnumerateRoots(root).Any(dir => Directory.Exists(Path.Combine(dir, "Languages")));
                patches = EnumerateRoots(root).Any(dir => Directory.Exists(Path.Combine(dir, "Patches")) ||
                    Directory.Exists(Path.Combine(dir, "ModPatches")));
                assemblies = EnumerateRoots(root).Any(dir => Directory.Exists(Path.Combine(dir, "Assemblies")));
                foreach (var defs in EnumerateRoots(root).Select(dir => Path.Combine(dir, "Defs")).Where(Directory.Exists))
                foreach (var file in Directory.EnumerateFiles(defs, "*.xml", FileOptions))
                {
                    try
                    {
                        using var stream = File.OpenRead(file);
                        var document = XDocument.Load(stream, LoadOptions.None);
                        if (document.Root?.Name.LocalName != "Defs") continue;
                        foreach (var def in document.Root.Elements())
                        {
                            if (string.Equals((string?)def.Attribute("Abstract"), "True", StringComparison.OrdinalIgnoreCase) ||
                                def.Element("defName") is null) continue;
                            switch (def.Name.LocalName)
                            {
                                case "AlienRace.ThingDef_AlienRace": race = true; break;
                                case "XenotypeDef": xenotype = true; break;
                                case "HairDef": hair = true; break;
                                case "GeneDef": gene = true; break;
                                case "FactionDef": faction = true; break;
                                case "ThingDef":
                                    var parent = (string?)def.Attribute("ParentName") ?? string.Empty;
                                    if (def.Element("apparel") is not null ||
                                        (string?)def.Element("category") == "Apparel" ||
                                        parent.Contains("Apparel", StringComparison.OrdinalIgnoreCase)) apparel = true;
                                    if (def.Element("building") is not null || (string?)def.Element("category") == "Building") building = true;
                                    if (parent.Contains("Furniture", StringComparison.OrdinalIgnoreCase) ||
                                        def.Element("thingClass")?.Value.Contains("Building", StringComparison.OrdinalIgnoreCase) == true &&
                                        def.Element("building")?.Elements().Any(element => element.Name.LocalName == "isEdifice") == true)
                                        furniture = true;
                                    break;
                            }
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                    {
                        AppLog.Error("Identify mod Defs: " + file, exception);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Identify mod files: " + mod.Path, exception);
            }
        }

        var titleTags = BracketTags(mod.Name);
        var aliasTags = BracketTags(mod.Alias);
        var folderTags = BracketTags(folder);
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in new[] { mod.Name, mod.Alias, folder })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Contains("patch", StringComparison.OrdinalIgnoreCase) || text.Contains("补丁", StringComparison.Ordinal)) keywords.Add("patch-title");
            if (text.Contains("expand", StringComparison.OrdinalIgnoreCase) || text.Contains("addon", StringComparison.OrdinalIgnoreCase) || text.Contains("拓展", StringComparison.Ordinal)) keywords.Add("addon-title");
        }
        if (ModCatalog.HasTranslationNameSignal(mod.Name)) keywords.Add("translation-name");
        if (ModCatalog.HasTranslationIdSignal(mod.PackageId)) keywords.Add("translation-package-id");

        return new ModIdentification(
            mod.PackageId, mod.Name, mod.Author, folder,
            ModCatalog.HasTranslationNameSignal(mod.Name), ModCatalog.HasTranslationIdSignal(mod.PackageId),
            mod.IsTranslationMod, languageFiles,
            race, xenotype, hair, apparel, furniture, building, gene, faction, patches, assemblies,
            mod.ModDependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            mod.LoadAfter.Concat(mod.ForceLoadAfter).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            !string.IsNullOrWhiteSpace(mod.Author) && authorGroups.TryGetValue(mod.Author.Trim(), out var authored)
                ? authored.Where(id => !id.Equals(mod.PackageId, StringComparison.OrdinalIgnoreCase)).ToArray()
                : Array.Empty<string>(),
            titleTags, aliasTags, folderTags, keywords.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IEnumerable<string> EnumerateRoots(string path)
    {
        yield return path;
        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            var name = Path.GetFileName(directory);
            if (name.Equals("Common", StringComparison.OrdinalIgnoreCase) ||
                name.Length >= 3 && char.IsDigit(name[0]) && name.Contains('.'))
                yield return directory;
        }
    }

    private static readonly Regex LeadingBracketRegex = new(
        @"^\s*(?:[\[\【]\s*([^\]\】]+?)\s*[\]\】]\s*)+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BracketVersionTagRegex = new(
        @"^(?:v?\d+\.\d+.*|\d+\.\d+[-~]\d+\.\d+.*|(?:tw|cn|zh|en|jp|kr)?\d+\.\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static IReadOnlyList<string> ExtractAllLeadingBracketTags(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return Array.Empty<string>();
        var match = LeadingBracketRegex.Match(label);
        if (!match.Success) return Array.Empty<string>();
        return match.Groups[1].Captures.Select(capture => capture.Value.Trim())
            .Where(tag => tag.Length is >= 2 and <= 28 && !tag.Contains('/') && !tag.Contains('\\') &&
                          !BracketVersionTagRegex.IsMatch(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private static IReadOnlyList<string> BracketTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return ExtractAllLeadingBracketTags(text);
    }
}
