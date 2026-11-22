using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackFrame;

internal sealed record CategoryTemplateEntry(
    string Name,
    DateTime? GeneratedAt,
    int Count,
    bool IsBuiltIn,
    string? FilePath,
    ModCategoryDocument Document);

internal static class CategoryTemplateStore
{
    private const string FolderName = "ModCategoryTemplates";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string Folder(string packRoot) =>
        Path.Combine(packRoot, InstanceStore.InstancesFolderName, FolderName);

    internal static IReadOnlyList<CategoryTemplateEntry> LoadAll(string packRoot)
    {
        var result = new List<CategoryTemplateEntry>();
        var builtIn = ModCategoryDatabase.LoadCommunityTemplate(out var builtInDate);
        result.Add(new CategoryTemplateEntry(
            "PackFrame",
            builtInDate,
            builtIn is null ? 0 : ModCategoryDatabase.CountEntries(builtIn),
            true,
            null,
            builtIn ?? new ModCategoryDocument()));

        var folder = Folder(packRoot);
        if (!Directory.Exists(folder))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.json")
                     .OrderByDescending(File.GetLastWriteTime))
        {
            try
            {
                var document = ModCategoryDatabase.Parse(File.ReadAllText(file));
                if (document is null) continue;
                var count = ModCategoryDatabase.CountEntries(document);
                if (count == 0) continue;
                var name = string.IsNullOrWhiteSpace(document.Title)
                    ? Path.GetFileNameWithoutExtension(file)
                    : document.Title!;
                var generated = ModCategoryDatabase.ParseGenerated(document.Generated) ??
                    File.GetLastWriteTime(file);
                result.Add(new CategoryTemplateEntry(name, generated, count, false, file, document));
            }
            catch (Exception exception)
            {
                AppLog.Error("Read category template: " + file, exception);
            }
        }

        return result;
    }

    internal static string PathForName(string packRoot, string name) =>
        Path.Combine(Folder(packRoot), SanitizeFileName(name) + ".json");

    internal static void SaveDocument(string path, ModCategoryDocument document, string title)
    {
        document.Version = 3;
        document.Title = title;
        if (string.IsNullOrWhiteSpace(document.Generated))
        {
            document.Generated = DateTimeOffset.Now.ToString("O");
        }

        var json = JsonSerializer.Serialize(document, WriteOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileTransactions.WriteAtomic(path, temporary =>
            File.WriteAllText(temporary, json, new UTF8Encoding(false)));
    }

    internal static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal static string SanitizeFileName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) trimmed = "Template";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        if (result.Length == 0) result = "Template";
        return result.Length > 80 ? result[..80] : result;
    }
}
