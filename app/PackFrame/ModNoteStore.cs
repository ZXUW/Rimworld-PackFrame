using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PackFrame;

public sealed class ModNote
{
    public string? Alias { get; set; }
    public string? Category { get; set; }
}

public sealed class ModNoteData
{
    public Dictionary<string, ModNote> Notes { get; set; } = new();
}

public static class ModNoteStore
{
    public const string FileName = "ModNotes.json";
    private static string? _cachedRoot;
    private static ModNoteData? _cachedData;

    public static string GetPath(string packRoot) =>
        Path.Combine(packRoot, InstanceStore.InstancesFolderName, FileName);

    public static void Invalidate() => _cachedData = null;

    public static ModNoteData Load(string? packRoot)
    {
        var data = LoadCached(packRoot);
        return new ModNoteData
        {
            Notes = data.Notes.ToDictionary(
                pair => pair.Key,
                pair => new ModNote { Alias = pair.Value.Alias, Category = pair.Value.Category },
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static ModNoteData LoadCached(string? packRoot)
    {
        if (_cachedData is not null && _cachedRoot == packRoot) return _cachedData;
        _cachedRoot = packRoot;
        var result = new ModNoteData();
        if (string.IsNullOrWhiteSpace(packRoot)) return _cachedData = result;
        var path = GetPath(packRoot);
        if (!File.Exists(path)) return _cachedData = result;
        try
        {
            var loaded = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                PackFrameJsonContext.Default.ModNoteData);
            if (loaded?.Notes is null) return _cachedData = result;
            foreach (var pair in loaded.Notes)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) continue;
                result.Notes[pair.Key] = new ModNote
                {
                    Alias = string.IsNullOrWhiteSpace(pair.Value.Alias) ? null : pair.Value.Alias,
                    Category = string.IsNullOrWhiteSpace(pair.Value.Category) ? null : pair.Value.Category
                };
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Read mod notes: " + path, exception);
        }

        return _cachedData = result;
    }

    public static ModNote? Get(string? packRoot, string packageId) =>
        LoadCached(packRoot).Notes.TryGetValue(packageId, out var note) ? note : null;

    public static IReadOnlyList<string> Categories(string? packRoot) =>
        LoadCached(packRoot).Notes.Values
            .Select(note => note.Category)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static void Set(
        string? packRoot,
        string packageId,
        string? alias,
        string? category)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || string.IsNullOrWhiteSpace(packageId)) return;
        var data = Load(packRoot);
        var note = data.Notes.TryGetValue(packageId, out var current) ? current : new ModNote();
        note.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        note.Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (note.Alias is null && note.Category is null)
            data.Notes.Remove(packageId);
        else
            data.Notes[packageId] = note;
        Write(packRoot, data);
    }

    public static void SetCategory(
        string? packRoot,
        IEnumerable<string> packageIds,
        string? category)
    {
        if (string.IsNullOrWhiteSpace(packRoot)) return;
        var data = Load(packRoot);
        var changed = false;
        foreach (var id in packageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var note = data.Notes.TryGetValue(id, out var existing) ? existing : new ModNote();
            note.Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            if (note.Alias is null && note.Category is null) data.Notes.Remove(id);
            else data.Notes[id] = note;
            changed = true;
        }

        if (changed) Write(packRoot, data);
    }

    private static void Write(string packRoot, ModNoteData data)
    {
        var path = GetPath(packRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileTransactions.WriteAtomic(path, temporary => File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(data, PackFrameJsonContext.Default.ModNoteData)));
        _cachedRoot = packRoot;
        _cachedData = data;
    }
}
