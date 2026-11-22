using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackFrame;

public sealed class ModNote
{
    public string? Alias { get; set; }
    public string? Category { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IgnoreHardPrerequisites { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? IgnoredPrerequisites { get; set; }

    internal bool IsEmpty => Alias is null && Category is null && !IgnoreHardPrerequisites && IgnoredPrerequisites is not { Count: > 0 };
}

public sealed class ModNoteData
{
    public Dictionary<string, ModNote> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CategoryOrder { get; set; } = new();
    public List<string> ModOrder { get; set; } = new();
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
                pair => new ModNote
                {
                    Alias = pair.Value.Alias, Category = pair.Value.Category,
                    IgnoreHardPrerequisites = pair.Value.IgnoreHardPrerequisites,
                    IgnoredPrerequisites = pair.Value.IgnoredPrerequisites?.ToList()
                },
                StringComparer.OrdinalIgnoreCase),
            CategoryOrder = data.CategoryOrder.ToList(),
            ModOrder = data.ModOrder.ToList()
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
            if (loaded is null) return _cachedData = result;
            if (loaded.Notes is not null)
            {
                foreach (var pair in loaded.Notes)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) continue;
                    result.Notes[pair.Key] = new ModNote
                    {
                        Alias = string.IsNullOrWhiteSpace(pair.Value.Alias) ? null : pair.Value.Alias,
                        Category = string.IsNullOrWhiteSpace(pair.Value.Category) ? null : pair.Value.Category,
                        IgnoreHardPrerequisites = pair.Value.IgnoreHardPrerequisites,
                        IgnoredPrerequisites = pair.Value.IgnoredPrerequisites
                    };
                }
            }
            if (loaded.CategoryOrder is not null)
            {
                result.CategoryOrder = loaded.CategoryOrder
                    .Select(CategoryTree.NormalizePath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            if (loaded.ModOrder is not null)
            {
                result.ModOrder = loaded.ModOrder
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
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

    internal static void ToggleIgnoredPrerequisite(string packRoot, ModInfo mod, string prerequisiteId)
    {
        if (!mod.ModDependencies.Contains(prerequisiteId, StringComparer.OrdinalIgnoreCase)) return;
        var data = Load(packRoot);
        var note = data.Notes.GetValueOrDefault(mod.PackageId) ?? new ModNote();
        var ignored = new HashSet<string>(note.IgnoreHardPrerequisites ? mod.ModDependencies :
            note.IgnoredPrerequisites ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (!ignored.Remove(prerequisiteId)) ignored.Add(prerequisiteId);
        note.IgnoreHardPrerequisites = false;
        note.IgnoredPrerequisites = ignored.Count == 0 ? null : ignored.ToList();
        if (note.IsEmpty) data.Notes.Remove(mod.PackageId);
        else data.Notes[mod.PackageId] = note;
        Write(packRoot, data);
    }

    public static IReadOnlyList<string> Categories(string? packRoot) =>
        CategoryTree.ExpandAllPaths(
            LoadCached(packRoot).Notes.Values
                .Select(note => note.Category)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!));

    public static int SetCategories(string? packRoot, IReadOnlyDictionary<string, string?> categoriesByPackage)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || categoriesByPackage.Count == 0) return 0;
        var data = Load(packRoot);
        var changedCount = 0;
        foreach (var (id, rawCategory) in categoriesByPackage)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var normalized = CategoryTree.NormalizePath(rawCategory);
            data.Notes.TryGetValue(id, out var existing);
            var currentCat = CategoryTree.NormalizePath(existing?.Category);
            if (string.Equals(currentCat, normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            var note = existing ?? new ModNote();
            note.Category = normalized;
            if (note.IsEmpty) data.Notes.Remove(id);
            else data.Notes[id] = note;
            changedCount++;
        }

        if (changedCount > 0) Write(packRoot, data);
        return changedCount;
    }

    public static int ClearAllCategories(string? packRoot)
    {
        if (string.IsNullOrWhiteSpace(packRoot)) return 0;
        var data = Load(packRoot);
        var cleared = 0;
        foreach (var id in data.Notes.Keys.ToList())
        {
            var note = data.Notes[id];
            if (string.IsNullOrWhiteSpace(note.Category)) continue;
            note.Category = null;
            if (note.IsEmpty) data.Notes.Remove(id);
            cleared++;
        }

        if (cleared > 0) Write(packRoot, data);
        return cleared;
    }

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
        note.Category = CategoryTree.NormalizePath(category);
        if (note.IsEmpty)
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
        var normalized = CategoryTree.NormalizePath(category);
        var changed = false;
        foreach (var id in packageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var note = data.Notes.TryGetValue(id, out var existing) ? existing : new ModNote();
            note.Category = normalized;
            if (note.IsEmpty) data.Notes.Remove(id);
            else data.Notes[id] = note;
            changed = true;
        }

        if (changed) Write(packRoot, data);
    }

    public static void ReorderSiblingCategories(
        string? packRoot,
        IReadOnlyList<string> currentSiblingKeys,
        string draggedKey,
        int insertIndex)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || currentSiblingKeys.Count <= 1) return;
        var list = currentSiblingKeys.ToList();
        var oldIdx = list.FindIndex(k => string.Equals(k, draggedKey, StringComparison.OrdinalIgnoreCase));
        if (oldIdx < 0) return;
        if (insertIndex > oldIdx) insertIndex--;
        insertIndex = Math.Clamp(insertIndex, 0, list.Count - 1);
        if (insertIndex == oldIdx) return;

        var item = list[oldIdx];
        list.RemoveAt(oldIdx);
        list.Insert(insertIndex, item);

        var data = Load(packRoot);
        var siblingSet = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        data.CategoryOrder.RemoveAll(siblingSet.Contains);
        data.CategoryOrder.AddRange(list);
        Write(packRoot, data);
    }

    public static void MoveAndReorderModsInCategory(
        string? packRoot,
        IReadOnlyList<string> currentGroupIds,
        IReadOnlyList<string> draggedIds,
        string? targetCategory,
        int insertIndex)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || draggedIds.Count == 0) return;
        var data = Load(packRoot);
        var normalized = CategoryTree.NormalizePath(targetCategory);
        foreach (var id in draggedIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var note = data.Notes.TryGetValue(id, out var existing) ? existing : new ModNote();
            note.Category = normalized;
            if (note.IsEmpty) data.Notes.Remove(id);
            else data.Notes[id] = note;
        }

        var updatedOrder = ModListOperations.InsertSelection(currentGroupIds, draggedIds, insertIndex);
        var groupSet = new HashSet<string>(updatedOrder, StringComparer.OrdinalIgnoreCase);
        data.ModOrder.RemoveAll(groupSet.Contains);
        data.ModOrder.AddRange(updatedOrder);
        Write(packRoot, data);
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
