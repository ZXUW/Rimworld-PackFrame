using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace PackFrame;

internal sealed record PackUpdatePlan(List<string> Conflicts, bool OrderChanged,
    List<string> Added, List<string> Removed);

internal static class PackUpdateService
{
    private const string BackupName = ".packframe-update-backup";
    private const string Journal = "paths.json";

    public static bool Matches(ManagedInstance instance, ModpackDefinition pack) =>
        !string.IsNullOrWhiteSpace(pack.Id) && !string.IsNullOrWhiteSpace(pack.PackSource) &&
        string.Equals(instance.Metadata?.PackId, pack.Id, StringComparison.Ordinal) &&
        string.Equals(instance.Metadata?.PackSource, pack.PackSource, StringComparison.Ordinal);

    public static void BindLegacy(ManagedInstance instance, ModpackDefinition pack)
    {
        instance.EnsureHealthy();
        if (string.IsNullOrWhiteSpace(pack.Id) || string.IsNullOrWhiteSpace(pack.PackSource))
            throw new IOException("Pack identity is missing.");
        var metadata = Clone(instance.Metadata);
        metadata.PackId = pack.Id;
        metadata.PackSource = pack.PackSource;
        // Do not guess ownership or claim a version was installed merely from its name.
        metadata.InstalledRevision = null;
        metadata.InstalledVersion = null;
        metadata.ManagedFiles.Clear();
        metadata.ManagedModFolders.Clear();
        metadata.InstalledOrder.Clear();
        InstanceStore.SaveMetadata(instance, metadata);
    }

    internal static void RecordInstallation(ManagedInstance instance, ModpackInstallResult result)
    {
        var metadata = Clone(instance.Metadata);
        metadata.PackSource = result.PackSource;
        metadata.InstalledRevision = result.Revision;
        metadata.InstalledVersion = result.Version;
        metadata.InstalledOrder = InstanceStore.LoadInstance(instance.DirectoryPath).ActivePackageIds;
        metadata.ManagedFiles = ConfigHashes(result.ConfigSourcePath);
        metadata.ManagedModFolders = ModFolders(result.ModsSourcePath);
        InstanceStore.SaveMetadata(instance, metadata);
    }

    public static PackUpdatePlan Plan(ManagedInstance instance, ModpackInstallResult result)
    {
        ValidateIdentity(instance, result);
        var metadata = instance.Metadata!;
        var incoming = ConfigHashes(result.ConfigSourcePath);
        var conflicts = new List<string>();
        foreach (var key in incoming.Keys.Union(metadata.ManagedFiles.Keys, StringComparer.OrdinalIgnoreCase))
        {
            ValidateManagedKey(key);
            if (key.Equals("Config/ModsConfig.xml", StringComparison.OrdinalIgnoreCase)) continue;
            var current = HashIfExists(SafePath(instance.DirectoryPath, key));
            metadata.ManagedFiles.TryGetValue(key, out var baseline);
            incoming.TryGetValue(key, out var next);
            if (current != baseline && current != next && next != baseline) conflicts.Add(key);
        }
        foreach (var folder in ModFolders(result.ModsSourcePath))
        {
            ValidateManagedKey(folder);
            if (Directory.Exists(SafePath(instance.DirectoryPath, folder)) &&
                !metadata.ManagedModFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                conflicts.Add(folder);
        }
        var nextOrder = NextOrder(instance, result);
        return new(conflicts,
            !instance.ActivePackageIds.SequenceEqual(metadata.InstalledOrder, StringComparer.OrdinalIgnoreCase),
            nextOrder.Except(instance.ActivePackageIds, StringComparer.OrdinalIgnoreCase).ToList(),
            instance.ActivePackageIds.Except(nextOrder, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static void Apply(string packRoot, ManagedInstance instance, ModpackInstallResult result,
        IReadOnlySet<string> keepLocal, bool keepOrder)
    {
        ValidateIdentity(instance, result);
        var staging = ModpackInstanceInstaller.ValidateStaging(packRoot, result.ModsSourcePath, result.ConfigSourcePath);
        if (InstanceStore.HasPendingMods) throw new IOException("Save or discard pending edits before updating.");
        var plan = Plan(instance, result);
        if (keepLocal.Any(key => !plan.Conflicts.Contains(key, StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Update choices no longer match the plan.");
        if (result.UnresolvedWorkshopCount > 0) throw new IOException("Workshop package IDs are unresolved. Finish downloading and retry.");
        var workspace = Path.Combine(instance.DirectoryPath, ".update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var metadata = Clone(instance.Metadata);
            var nextFiles = ConfigHashes(result.ConfigSourcePath);
            var nextMods = ModFolders(result.ModsSourcePath);
            var replacements = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in metadata.ManagedModFolders.Union(nextMods, StringComparer.OrdinalIgnoreCase))
            {
                if (keepLocal.Contains(folder)) continue;
                replacements[folder] = nextMods.Contains(folder, StringComparer.OrdinalIgnoreCase)
                    ? SafePath(result.ModsSourcePath!, folder[5..]) : null;
            }
            foreach (var key in metadata.ManagedFiles.Keys.Union(nextFiles.Keys, StringComparer.OrdinalIgnoreCase))
            {
                if (key.Equals("Config/ModsConfig.xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (keepLocal.Contains(key)) continue;
                var current = HashIfExists(SafePath(instance.DirectoryPath, key));
                metadata.ManagedFiles.TryGetValue(key, out var baseline);
                nextFiles.TryGetValue(key, out var next);
                // Preserve local-only edits when the author has not changed the file.
                if (current != baseline && baseline == next) continue;
                replacements[key] = next is null ? null : SafePath(result.ConfigSourcePath!, key[7..]);
            }
            var order = NextOrder(instance, result);
            if (!keepOrder)
            {
                var draft = InstanceStore.CreateNew(workspace, instance, instance.GameVersion, order);
                replacements["Config/ModsConfig.xml"] = Path.Combine(draft.ConfigDirectoryPath, "ModsConfig.xml");
            }
            metadata.PackSource = result.PackSource;
            metadata.InstalledRevision = result.Revision;
            metadata.InstalledVersion = result.Version;
            metadata.ConfigBaseUrl = result.ConfigBaseUrl;
            metadata.ConfigFiles = result.ConfigFiles.ToList();
            metadata.ManagedFiles = nextFiles;
            metadata.ManagedModFolders = nextMods.Where(folder => !keepLocal.Contains(folder)).ToList();
            metadata.InstalledOrder = order;
            var metadataPath = Path.Combine(workspace, InstanceStore.MetadataFileName);
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, PackFrameJsonContext.Default.InstanceMetadata));
            replacements[InstanceStore.MetadataFileName] = metadataPath; // Version is committed last.
            Commit(instance, replacements);
            Reload(instance);
            if (staging is not null) FileTransactions.TryDeleteDirectory(staging);
        }
        finally { FileTransactions.TryDeleteDirectory(workspace); }
    }

    public static bool CanRestore(ManagedInstance instance) => File.Exists(Path.Combine(instance.DirectoryPath, BackupName, Journal));

    public static void Restore(ManagedInstance instance)
    {
        if (InstanceStore.HasPendingMods) throw new IOException("Save or discard pending edits before restoring.");
        var backup = Path.Combine(instance.DirectoryPath, BackupName);
        var paths = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(backup, Journal)),
            PackFrameJsonContext.Default.DictionaryStringBoolean) ?? throw new IOException("Invalid update backup.");
        var replacements = new Dictionary<string, string?>();
        foreach (var entry in paths)
        {
            ValidateManagedKey(entry.Key);
            replacements[entry.Key] = entry.Value ? SafePath(Path.Combine(backup, "files"), entry.Key) : null;
        }
        Commit(instance, replacements);
        Reload(instance);
    }

    private static void Commit(ManagedInstance instance, Dictionary<string, string?> replacements)
    {
        var root = instance.DirectoryPath;
        var pending = Path.Combine(root, ".update-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        var existing = new Dictionary<string, bool>();
        var applied = new List<string>();
        var preserve = false;
        try
        {
            foreach (var key in replacements.Keys)
            {
                ValidateManagedKey(key);
                var path = SafePath(root, key);
                existing[key] = File.Exists(path) || Directory.Exists(path);
                if (existing[key]) Copy(path, SafePath(Path.Combine(pending, "files"), key));
            }
            File.WriteAllText(Path.Combine(pending, Journal), JsonSerializer.Serialize(existing,
                PackFrameJsonContext.Default.DictionaryStringBoolean));
            foreach (var (key, source) in replacements)
            {
                // Sources are fully prepared before touching the instance, including nested files.
                if (source is not null)
                {
                    var prepared = SafePath(Path.Combine(pending, "incoming"), key);
                    Copy(source, prepared);
                }
            }
            foreach (var (key, source) in replacements)
            {
                var destination = SafePath(root, key);
                // Track deletions that can partially succeed (directories); for files a failed
                // Delete leaves the old file intact and must not trigger a spurious rollback.
                if (Directory.Exists(destination)) applied.Add(key);
                Delete(destination);
                if (!applied.Contains(key)) applied.Add(key);
                if (source is not null) Copy(SafePath(Path.Combine(pending, "incoming"), key), destination);
            }
            // Keep exactly the previous successful state; do not delete its backup until commit succeeds.
            var old = Path.Combine(root, BackupName);
            var retired = old + "-" + Guid.NewGuid().ToString("N");
            if (Directory.Exists(old)) Directory.Move(old, retired);
            try { Directory.Move(pending, old); }
            catch { if (Directory.Exists(retired)) Directory.Move(retired, old); throw; }
            FileTransactions.TryDeleteDirectory(retired);
            FileTransactions.TryDeleteDirectory(Path.Combine(old, "incoming"));
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var key in applied.AsEnumerable().Reverse())
                try
                {
                    var destination = SafePath(root, key);
                    Delete(destination);
                    if (existing[key]) Copy(SafePath(Path.Combine(pending, "files"), key), destination);
                }
                catch (Exception rollback) { errors.Add(rollback); }
            if (errors.Count > 1)
            {
                preserve = true;
                throw new AggregateException("Update rollback failed. Backup: " + pending, errors);
            }
            throw;
        }
        finally { if (!preserve) FileTransactions.TryDeleteDirectory(pending); }
    }

    private static void ValidateIdentity(ManagedInstance instance, ModpackInstallResult result)
    {
        instance.EnsureHealthy();
        if (instance.IsOfficial || instance.IsScratch || instance.Metadata?.PackId != result.Id ||
            instance.Metadata?.PackSource != result.PackSource || string.IsNullOrWhiteSpace(result.Id) ||
            string.IsNullOrWhiteSpace(result.PackSource)) throw new IOException("Pack identity does not match this instance.");
        if (result.Revision is null || result.Revision <= 0 || result.Revision <= instance.Metadata!.InstalledRevision)
            throw new IOException("No newer pack revision is available.");
    }

    private static List<string> NextOrder(ManagedInstance instance, ModpackInstallResult result)
    {
        var path = result.ConfigSourcePath is null ? null : Path.Combine(result.ConfigSourcePath, "ModsConfig.xml");
        if (path is not null && File.Exists(path))
        {
            var official = InstanceStore.LoadOfficial(result.ConfigSourcePath, "staging");
            official.EnsureHealthy();
            return official.ActivePackageIds;
        }
        return result.PackageIds.Select(id => id.ToLowerInvariant()).ToList();
    }

    private static Dictionary<string, string> ConfigHashes(string? root) => root is null || !Directory.Exists(root)
        ? new() : Directory.EnumerateFiles(root).ToDictionary(path => "Config/" + Path.GetFileName(path), Hash,
            StringComparer.OrdinalIgnoreCase);
    private static List<string> ModFolders(string? root) => root is null || !Directory.Exists(root)
        ? new() : Directory.EnumerateDirectories(root).Select(path => "Mods/" + Path.GetFileName(path)).ToList();
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string? HashIfExists(string path) => File.Exists(path) ? Hash(path) : null;
    private static InstanceMetadata Clone(InstanceMetadata? metadata) => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(metadata ?? new(), PackFrameJsonContext.Default.InstanceMetadata),
        PackFrameJsonContext.Default.InstanceMetadata)!;
    private static void Reload(ManagedInstance instance)
    {
        InstanceStore.RemovePending(instance);
        var loaded = InstanceStore.LoadInstance(instance.DirectoryPath);
        instance.Metadata = loaded.Metadata;
        instance.ActivePackageIds = loaded.ActivePackageIds;
        instance.GameVersion = loaded.GameVersion;
        instance.KnownExpansions = loaded.KnownExpansions;
    }
    private static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Unsafe managed path: " + relative);
        for (var current = full; current.Length >= prefix.Length; current = Path.GetDirectoryName(current)!)
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
                throw new IOException("Managed path is a link: " + current);
        return full;
    }
    private static void Copy(string source, string target)
    {
        if (Directory.Exists(source)) ModpackInstanceInstaller.CopyTree(source, target);
        else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true); }
    }
    private static void ValidateManagedKey(string key)
    {
        var parts = key.Split('/');
        if (key == InstanceStore.MetadataFileName) return;
        if (parts.Length != 2 || (parts[0] != "Config" && parts[0] != "Mods") ||
            parts[1] is "" or "." or ".." || parts[1].IndexOfAny(new[] { '\\', ':' }) >= 0)
            throw new IOException("Invalid managed update entry: " + key);
    }
    private static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
    }
}
