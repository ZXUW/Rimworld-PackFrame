using System.Text.Json;
using PackFrame;

internal static class WorkshopBindingChecks
{
    public static void Run(string root)
    {
        Require(new[]
                {
                    "https://github.com/example/mod/releases/latest", "https://example.com/mod.zip",
                    @"C:\Mods\KnownLocalMod", "/home/player/Mods/KnownLocalMod", @"\\server\share\KnownLocalMod",
                    "file:///C:/Mods/KnownLocalMod", "file:///home/player/Mods/KnownLocalMod"
                }.All(WorkshopModReferences.HasNonWorkshopSource),
            "explicit remote and absolute local sources remain distinguishable from pending Workshop targets");
        Require(new string?[]
                {
                    null, "", " ", "—", "relative/mod", @"C:relative\mod",
                    "https://steamcommunity.com/sharedfiles/filedetails/?id=101",
                    "https://www.steamcommunity.com/sharedfiles/filedetails/?id=101",
                    "steam://url/CommunityFilePage/101", "https://store.steampowered.com/app/294100",
                    "not-a-link", "file:relative/mod"
                }.All(link => !WorkshopModReferences.HasNonWorkshopSource(link)),
            "empty, ambiguous, relative and Steam sources cannot be mistaken for an explicit non-Workshop source");
        var references = WorkshopModReferences.ForTargets(new ulong[] { 101, 102, 103, 101, 0 },
            new Dictionary<ulong, WorkshopModReference>
            {
                [101] = new(101, " Known.Workshop ", "Known mod"),
                [999] = new(999, "unrequested.mod", "Not requested")
            },
            new Dictionary<ulong, string> { [101] = "disk-alias", [103] = " Discovered.Workshop " });
        Require(references.Select(reference => reference.WorkshopId).SequenceEqual(new ulong[] { 101, 102, 103 }) &&
                references[0].PackageId == "known.workshop" && references[0].Name == "Known mod" &&
                references[1].PackageId is null && references[2].PackageId == "discovered.workshop",
            "Workshop references retain explicit identities, discovered identities and unresolved collection targets");

        var packRoot = Path.Combine(root, "workshop-bindings");
        var result = new ModpackInstallResult("Waiting pack", new[] { "known.workshop", "local.only" },
            3, 0, 1, 1, null, null, "waiting-pack", "https://example.com/config", Array.Empty<string>(), false,
            PackSource: "https://example.com/index.json", Revision: 1, WorkshopMods: references);
        var created = ModpackInstanceInstaller.Create(packRoot, "1.6", result);
        var loaded = InstanceStore.Discover(packRoot).Single();
        Require(loaded.Metadata?.WorkshopMods.Select(reference => reference.WorkshopId)
                    .SequenceEqual(new ulong[] { 101, 102, 103 }) == true &&
                loaded.Metadata.WorkshopMods[1].PackageId is null &&
                loaded.ActivePackageIds.SequenceEqual(new[] { "known.workshop", "local.only" }),
            "installation persists pending Workshop targets without inventing package IDs or changing the declared order");

        var configPath = Path.Combine(loaded.ConfigDirectoryPath, "ModsConfig.xml");
        var configBeforeCompletion = File.ReadAllText(configPath);
        var metadataPathBeforeCompletion = Path.Combine(loaded.DirectoryPath, InstanceStore.MetadataFileName);
        var newerMetadata = JsonSerializer.Deserialize(File.ReadAllText(metadataPathBeforeCompletion),
            PackFrameJsonContext.Default.InstanceMetadata)!;
        newerMetadata.Name = "Renamed after the scan started";
        File.WriteAllText(metadataPathBeforeCompletion,
            JsonSerializer.Serialize(newerMetadata, PackFrameJsonContext.Default.InstanceMetadata));
        InstanceStore.StageActiveMods(loaded, new[] { "local.only", "known.workshop", "pending.extra" });
        var pendingOrder = loaded.ActivePackageIds.ToArray();
        var resolved = WorkshopModReferences.ResolveInstalled(loaded, new[]
        {
            Mod(Path.Combine(root, "workshop-content", "102"), "downloaded.workshop", ModSource.Workshop),
            Mod(Path.Combine(root, "local-content", "102"), "unrelated.local", ModSource.Local),
            Mod(Path.Combine(root, "workshop-content", "101"), "wrong.alias", ModSource.Workshop)
        });
        var afterCompletion = InstanceStore.LoadInstance(loaded.DirectoryPath);
        Require(resolved && afterCompletion.Metadata!.WorkshopMods.Single(reference => reference.WorkshopId == 102)
                    .PackageId == "downloaded.workshop" &&
                afterCompletion.Metadata.WorkshopMods.Single(reference => reference.WorkshopId == 101)
                    .PackageId == "known.workshop" &&
                afterCompletion.Metadata.Name == "Renamed after the scan started" &&
                afterCompletion.Metadata.PackId == "waiting-pack",
            "a completed Workshop scan persists unknown package bindings across restart while preserving explicit identities and newer metadata");
        Require(File.ReadAllText(configPath) == configBeforeCompletion && InstanceStore.HasPendingMods &&
                loaded.ActivePackageIds.SequenceEqual(pendingOrder),
            "persisting completed Workshop bindings preserves the saved order and pending list edits");
        InstanceStore.RemovePending(loaded);
        loaded = InstanceStore.LoadInstance(loaded.DirectoryPath);

        InstanceStore.RenameInstance(loaded, "Renamed waiting pack");
        var copied = InstanceStore.CopyInstance(packRoot, loaded);
        loaded.Metadata!.WorkshopMods.Clear();
        Require(copied.Metadata?.WorkshopMods.Count == 3 &&
                InstanceStore.LoadInstance(loaded.DirectoryPath).Metadata?.WorkshopMods.Count == 3,
            "rename and copy retain independent persisted Workshop references");
        loaded = InstanceStore.LoadInstance(loaded.DirectoryPath);

        var replacement = result with
        {
            Revision = 2,
            WorkshopMods = new[] { new WorkshopModReference(104, "known.workshop", "Replacement target") }
        };
        PackUpdateService.Apply(packRoot, loaded, replacement, new HashSet<string>(), keepOrder: false);
        Require(loaded.Metadata?.WorkshopMods.Single().WorkshopId == 104,
            "pack updates replace the recorded Workshop targets with the incoming pack's targets");
        PackUpdateService.Restore(loaded);
        Require(loaded.Metadata?.WorkshopMods.Select(reference => reference.WorkshopId)
                    .SequenceEqual(new ulong[] { 101, 102, 103 }) == true,
            "pack rollback restores the prior Workshop references together with instance metadata");
        PackUpdateService.RecordInstallation(loaded, result with { WorkshopMods = null });
        Require(loaded.Metadata?.WorkshopMods.Count == 3,
            "legacy installation results without Workshop metadata do not erase recorded targets");

        var legacy = InstanceStore.CreateNew(Path.Combine(root, "legacy-workshop-bindings"), null, "1.6",
            new[] { "legacy.mod", "legacy.history", "local.only" });
        InstanceStore.SaveMetadata(legacy, new InstanceMetadata
        {
            WorkshopMods = new List<WorkshopModReference> { new(201, null, "Recorded name"), new(202) }
        });
        var labels = new ModFullNameDocument
        {
            Mods = new List<ModFullNameEntry>
            {
                new() { PackageId = "legacy.mod", Label = "Legacy label", Link = "https://steamcommunity.com/sharedfiles/filedetails/?id=201" },
                new() { PackageId = "local.only", Label = "Local", Link = "https://example.com/sharedfiles/filedetails/?id=777" },
                new() { PackageId = "inactive.mod", Label = "Inactive", Link = "https://steamcommunity.com/sharedfiles/filedetails/?id=999" }
            },
            MissingHistory = new List<MissingHistoryEntry>
            {
                new() { PackageId = "legacy.history", Label = "History label", Link = "steam://url/CommunityFilePage/203" }
            }
        };
        File.WriteAllText(ModFullNameList.GetPath(legacy),
            JsonSerializer.Serialize(labels, PackFrameJsonContext.Default.ModFullNameDocument));
        var metadataPath = Path.Combine(legacy.DirectoryPath, InstanceStore.MetadataFileName);
        var metadataBefore = File.ReadAllText(metadataPath);
        var recovered = WorkshopModReferences.ForInstance(legacy);
        Require(recovered.Single(reference => reference.WorkshopId == 201) is
                    { PackageId: "legacy.mod", Name: "Recorded name" } &&
                recovered.Any(reference => reference is { WorkshopId: 202, PackageId: null }) &&
                recovered.Any(reference => reference is { WorkshopId: 203, PackageId: "legacy.history" }) &&
                recovered.Count == 3,
            "legacy Workshop links recover active package bindings while unrelated links and inactive labels are ignored");
        Require(File.ReadAllText(metadataPath) == metadataBefore && legacy.Metadata!.WorkshopMods[0].PackageId is null,
            "looking up Workshop references does not mutate saved metadata or in-memory source records");
        Require(!WorkshopModReferences.ResolveInstalled(legacy, new[]
                {
                    Mod(Path.Combine(root, "workshop-one", "202"), "ambiguous.one", ModSource.Workshop),
                    Mod(Path.Combine(root, "workshop-two", "202"), "ambiguous.two", ModSource.Workshop),
                    Mod(Path.Combine(root, "local", "201"), "local.alias", ModSource.Local)
                }) && File.ReadAllText(metadataPath) == metadataBefore,
            "ambiguous Workshop packages and local-only matches cannot invent a completed package binding");
        var changedTargets = JsonSerializer.Deserialize(metadataBefore, PackFrameJsonContext.Default.InstanceMetadata)!;
        changedTargets.WorkshopMods = new List<WorkshopModReference> { new(204) };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(changedTargets, PackFrameJsonContext.Default.InstanceMetadata));
        var changedTargetsJson = File.ReadAllText(metadataPath);
        Require(!WorkshopModReferences.ResolveInstalled(legacy,
                    new[] { Mod(Path.Combine(root, "workshop", "201"), "finished.legacy", ModSource.Workshop) }) &&
                File.ReadAllText(metadataPath) == changedTargetsJson,
            "a completion scan cannot reintroduce Workshop targets removed by a newer metadata update");

        var waitingLabels = InstanceStore.CreateNew(Path.Combine(root, "pending-workshop-labels"), null, "1.6",
            new[] { "pending.legacy", "history.pending", "really.missing" });
        var pendingLabels = new ModFullNameDocument
        {
            Mods = new List<ModFullNameEntry>
            {
                new() { PackageId = "pending.legacy", Label = "Saved Workshop name", Author = "Saved author",
                    Link = "https://steamcommunity.com/sharedfiles/filedetails/?id=601" },
                new() { PackageId = "really.missing", Label = "Genuinely missing mod", Link = "https://example.com/mod.zip" }
            },
            MissingHistory = new List<MissingHistoryEntry>
            {
                new() { PackageId = "history.pending", Label = "Historical Workshop name", Author = "Historical author",
                    Link = "https://steamcommunity.com/sharedfiles/filedetails/?id=602", LastMissingAt = "2020-01-01 10:00" }
            }
        };
        File.WriteAllText(ModFullNameList.GetPath(waitingLabels),
            JsonSerializer.Serialize(pendingLabels, PackFrameJsonContext.Default.ModFullNameDocument));
        ModFullNameList.Save(waitingLabels, Array.Empty<ModInfo>(),
            new[] { "pending.legacy", "history.pending", "really.missing" },
            new[] { "PENDING.LEGACY", "history.pending" });
        var savedLabels = ModFullNameList.Load(waitingLabels);
        var savedHistory = ModFullNameList.LoadHistory(waitingLabels).ToDictionary(entry => entry.PackageId,
            StringComparer.OrdinalIgnoreCase);
        var waitingReferences = WorkshopModReferences.ForInstance(waitingLabels);
        Require(savedLabels["pending.legacy"] is { Label: "Saved Workshop name", Author: "Saved author" } &&
                waitingReferences.Any(reference => reference is { WorkshopId: 601, PackageId: "pending.legacy", Name: "Saved Workshop name" }) &&
                waitingReferences.Any(reference => reference is { WorkshopId: 602, PackageId: "history.pending", Name: "Historical Workshop name" }),
            "saving during a Workshop download preserves prior names and item links for continued legacy tracking");
        Require(!savedHistory.ContainsKey("pending.legacy") &&
                savedHistory["history.pending"].LastMissingAt == "2020-01-01 10:00" &&
                savedHistory["really.missing"].Label == "Genuinely missing mod",
            "pending downloads neither create nor update missing history while genuine missing items still do");
        var completedMod = Mod(Path.Combine(root, "workshop", "601"), "pending.legacy", ModSource.Workshop) with
        {
            Name = "Installed Workshop name", Author = "Installed author",
            Link = "https://steamcommunity.com/sharedfiles/filedetails/?id=601&installed=1"
        };
        ModFullNameList.Save(waitingLabels, new[] { completedMod }, new[] { "really.missing" },
            new[] { "pending.legacy", "history.pending" });
        Require(ModFullNameList.Load(waitingLabels)["pending.legacy"] is
                    { Label: "Installed Workshop name", Author: "Installed author" } &&
                ModFullNameList.Load(waitingLabels)["pending.legacy"].Link == completedMod.Link &&
                !ModFullNameList.LoadHistory(waitingLabels).Any(entry => entry.PackageId == "pending.legacy"),
            "completed installed rows replace preserved pending labels with current metadata without missing-history pollution");
    }

    private static ModInfo Mod(string path, string packageId, ModSource source) =>
        new("Downloaded mod", "Fixture", path, packageId, string.Empty, null, source, "1.6", DateTime.MinValue);

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
