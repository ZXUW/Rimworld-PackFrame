using PackFrame;

internal static class DropPreviewChecks
{
    public static void Run()
    {
        var order = new[] { "core", "framework", "tool", "last" };
        var before = ModListOperations.PreviewInsertion(order, new[] { "tool" }, 0);
        Require(before is { HasChanges: true, AddedCount: 0, ExistingCount: 1, BeforeId: "core", AfterId: null },
            "reorder preview names the non-dragged successor at the beginning");

        var after = ModListOperations.PreviewInsertion(order, new[] { "framework" }, order.Length);
        Require(after is { HasChanges: true, AddedCount: 0, ExistingCount: 1, BeforeId: null, AfterId: "last" },
            "reorder preview names the predecessor at the end");

        var inside = ModListOperations.PreviewInsertion(order, new[] { "framework", "tool" }, 2);
        Require(inside is { HasChanges: false, AddedCount: 0, ExistingCount: 2, BeforeId: "last", AfterId: null },
            "dropping inside a contiguous selection is previewed as a no-op");

        var mixedIds = new[] { "new.mod", "TOOL", "new.mod", " ", string.Empty };
        var mixed = ModListOperations.PreviewInsertion(order, mixedIds, 1);
        var predicted = ModListOperations.InsertSelection(order, new[] { "new.mod", "TOOL" }, 1);
        Require(mixed is { HasChanges: true, AddedCount: 1, ExistingCount: 1, BeforeId: "framework", AfterId: null } &&
                predicted.SequenceEqual(new[] { "core", "new.mod", "TOOL", "framework", "last" }),
            "mixed add and reorder preview matches deduplicated insertion with case aliases");

        var aliases = ModListOperations.PreviewInsertion(order, new[] { "CORE", "core" }, 0);
        Require(aliases is { HasChanges: false, AddedCount: 0, ExistingCount: 1, BeforeId: "framework", AfterId: null },
            "case-only aliases do not advertise a changed load order");

        var noncontiguous = ModListOperations.PreviewInsertion(order, new[] { "core", "tool" }, 2);
        Require(noncontiguous is { HasChanges: true, BeforeId: "last", AfterId: null } &&
                noncontiguous.BeforeId is not ("core" or "tool"),
            "noncontiguous reorder preview never anchors against a dragged mod");

        var onlySelection = ModListOperations.PreviewInsertion(new[] { "core" }, new[] { "core" }, 1);
        Require(onlySelection is { HasChanges: false, BeforeId: null, AfterId: null },
            "a list containing only the dragged selection has no unrelated drop anchor");

        var empty = ModListOperations.PreviewInsertion(Array.Empty<string>(), new[] { "new.mod" }, 10);
        var noSelection = ModListOperations.PreviewInsertion(order, new[] { " ", string.Empty }, -1);
        Require(empty is { HasChanges: true, AddedCount: 1, ExistingCount: 0, BeforeId: null, AfterId: null } &&
                noSelection is { HasChanges: false, AddedCount: 0, ExistingCount: 0, BeforeId: null, AfterId: null } &&
                order.SequenceEqual(new[] { "core", "framework", "tool", "last" }) &&
                mixedIds.SequenceEqual(new[] { "new.mod", "TOOL", "new.mod", " ", string.Empty }),
            "drop previews handle empty targets and selections without mutating their inputs");

        var profileMod = new ModInfo("RJW", "", "x", "rjw.mod", "", null, ModSource.Local, "1.6", default)
            { IsProfileMods = true, ProfileFolder = "profile1" };
        var globalMod = new ModInfo("Global", "", "y", "global.mod", "", null, ModSource.Local, "1.6", default);
        var officialMod = new ModInfo("Core", "", "z", "core", "", null, ModSource.Official, "1.6", default);
        var owner = new ManagedInstance { FolderName = "profile1", DirectoryPath = "a" };
        var other = new ManagedInstance { FolderName = "profile2", DirectoryPath = "b" };
        var official = new ManagedInstance
            { FolderName = InstanceStore.OfficialFolderName, DirectoryPath = "c", IsOfficial = true };
        var steamCmd = new ManagedInstance
            { FolderName = "profile3", DirectoryPath = "d", Metadata = new InstanceMetadata { SteamCmd = true } };
        Require(ModVisibility.IsVisible(profileMod, owner) &&
                !ModVisibility.IsVisible(profileMod, other) &&
                !ModVisibility.IsVisible(profileMod, official),
            "instance-local mod stays inside its own instance");
        Require(ModVisibility.IsVisible(globalMod, owner) &&
                ModVisibility.IsVisible(globalMod, official) &&
                !ModVisibility.IsVisible(globalMod, steamCmd),
            "game-wide mods reach normal and official instances but not SteamCMD instances");
        Require(ModVisibility.IsVisible(officialMod, steamCmd),
            "SteamCMD instances still see official game data");
    }

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
