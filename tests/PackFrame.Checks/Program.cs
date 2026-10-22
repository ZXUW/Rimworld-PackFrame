using PackFrame;
using System.Formats.Tar;
using System.IO.Compression;

var root = Path.Combine(Path.GetTempPath(), "packframe-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var instance = InstanceStore.CreateNew(root, null, "1.6", new[] { "missing.mod" });
    InstanceStore.SetPackBinding(instance, "test-pack", "https://example.com/config",
        new[] { "ModsConfig.xml" }, true);
    InstanceStore.RenameInstance(instance, "Renamed");
    var loaded = InstanceStore.Discover(root).Single();
    Check(loaded.Metadata?.PackId == "test-pack" && loaded.IsSteamCmd &&
        loaded.Metadata.ConfigFiles.SequenceEqual(new[] { "ModsConfig.xml" }), "rename preserves binding");
    var copied = InstanceStore.CopyInstance(root, loaded);
    Check(copied.Metadata?.ConfigBaseUrl == "https://example.com/config" && copied.IsSteamCmd,
        "copy preserves binding");
    var configPath = Path.Combine(loaded.ConfigDirectoryPath, "ModsConfig.xml");
    File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("missing.mod", "Missing.Mod"));
    var beforeRead = File.ReadAllText(configPath);
    InstanceStore.Discover(root);
    Check(File.ReadAllText(configPath) == beforeRead, "discovery does not rewrite configuration");
    var migrationDocument = System.Xml.Linq.XDocument.Load(configPath);
    migrationDocument.Root!.Element("activeMods")!.Add(new System.Xml.Linq.XElement("li", "Ludeon.RimWorld.Biotech"));
    migrationDocument.Root.Add(new System.Xml.Linq.XElement("customField", "PreserveMe"));
    migrationDocument.Save(configPath);
    var beforeMigration = File.ReadAllText(configPath);
    InstanceStore.NormalizeConfigForLaunch(loaded);
    var migrated = System.Xml.Linq.XDocument.Load(configPath);
    Check(migrated.Root!.Element("activeMods")!.Elements("li").Any(item => item.Value == "ludeon.rimworld.biotech") &&
        migrated.Root.Element("customField")!.Value == "PreserveMe", "launch migration normalizes DLC IDs without dropping unknown fields");
    var backups = Directory.GetFiles(loaded.ConfigDirectoryPath, "ModsConfig.xml.before-normalize-*.bak");
    Check(backups.Length == 1 && File.ReadAllText(backups[0]) == beforeMigration, "launch migration preserves original backup");
    var afterMigration = File.ReadAllText(configPath);
    InstanceStore.NormalizeConfigForLaunch(loaded);
    Check(File.ReadAllText(configPath) == afterMigration &&
        Directory.GetFiles(loaded.ConfigDirectoryPath, "ModsConfig.xml.before-normalize-*.bak").Length == 1,
        "normalized config is not rewritten or backed up again");
    InstanceStore.StageActiveMods(loaded, new[] { "missing.mod", "another.mod" });
    InstanceStore.SavePendingMods();
    Check(InstanceStore.Discover(root)[0].ActivePackageIds.SequenceEqual(new[] { "missing.mod", "another.mod" }),
        "save preserves missing packages");

    var target = Path.Combine(root, "target");
    var staging = Path.Combine(root, "staging");
    Directory.CreateDirectory(target);
    Directory.CreateDirectory(staging);
    var original = Path.Combine(target, "original.xml");
    File.WriteAllText(original, "old");
    try
    {
        FileTransactions.WriteAtomic(original, temporary =>
        {
            File.WriteAllText(temporary, "partial");
            throw new IOException("simulated interrupted write");
        });
        throw new Exception("Expected write failure");
    }
    catch (IOException) { }
    Check(File.ReadAllText(original) == "old", "interrupted write preserves original");

    File.WriteAllText(Path.Combine(staging, "a.xml"), "new-a");
    File.WriteAllText(Path.Combine(staging, "b.xml"), "new-b");
    File.WriteAllText(Path.Combine(staging, "c.xml"), "new-c");
    var files = Directory.GetFiles(staging);
    File.WriteAllText(Path.Combine(target, Path.GetFileName(files[0])), "old-a");
    // Force the last replacement to fail, after replacing one file and adding another.
    Directory.CreateDirectory(Path.Combine(target, Path.GetFileName(files[2])));
    try
    {
        FileTransactions.ApplyFiles(staging, target);
        throw new Exception("Expected commit failure");
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
    Check(File.ReadAllText(Path.Combine(target, Path.GetFileName(files[0]))) == "old-a",
        "rollback restores overwritten file");
    Check(!File.Exists(Path.Combine(target, Path.GetFileName(files[1]))),
        "rollback removes newly added file");
    Directory.Delete(Path.Combine(target, Path.GetFileName(files[2])));
    FileTransactions.ApplyFiles(staging, target);
    Check(files.All(file => File.ReadAllText(file) ==
        File.ReadAllText(Path.Combine(target, Path.GetFileName(file)))), "successful multi-file commit");
    var game = Path.Combine(root, "game");
    foreach (var profile in new[] { loaded, copied })
    {
        var about = Path.Combine(profile.DirectoryPath, "Mods", "TestMod", "About");
        Directory.CreateDirectory(about);
        File.WriteAllText(Path.Combine(about, "About.xml"),
            "<ModMetaData><packageId>missing.mod</packageId></ModMetaData>");
    }
    ModLinkService.Prepare(game, loaded, root);
    var link = Path.Combine(game, "Mods", "PF_missing.mod");
    Check(new DirectoryInfo(link).LinkTarget is not null, "create profile mod link");
    File.Delete(Path.Combine(loaded.DirectoryPath, "mod-links.txt"));
    ModLinkService.Recover(game, root);
    Check(new DirectoryInfo(link).LinkTarget is null, "recover legacy orphan link by target and package identity");
    ModLinkService.Prepare(game, loaded, root);
    ModLinkService.Prepare(game, copied, root);
    Check(new DirectoryInfo(link).ResolveLinkTarget(true)!.FullName ==
        Path.Combine(copied.DirectoryPath, "Mods", "TestMod"), "recover old instance link before switching");
    Directory.Delete(link);
    Directory.CreateDirectory(link);
    try
    {
        ModLinkService.Cleanup(game, copied);
        throw new Exception("Expected ownership failure");
    }
    catch (AggregateException) { }
    Check(Directory.Exists(link) && File.Exists(Path.Combine(copied.DirectoryPath, "mod-links.txt")),
        "cleanup preserves ordinary directories and recovery manifest");
    Directory.Delete(link);
    ModLinkService.Cleanup(game, copied);
    ModLinkService.Prepare(game, copied, root);
    Directory.Delete(Path.Combine(copied.DirectoryPath, "Mods", "TestMod"), true);
    ModLinkService.Cleanup(game, copied);
    Check(new DirectoryInfo(link).LinkTarget is null, "cleanup removes broken link");
    var blocked = Path.Combine(game, "Mods", "PF_blocked.mod");
    Directory.CreateDirectory(blocked);
    var blockedAbout = Path.Combine(loaded.DirectoryPath, "Mods", "Blocked", "About");
    Directory.CreateDirectory(blockedAbout);
    File.WriteAllText(Path.Combine(blockedAbout, "About.xml"),
        "<ModMetaData><packageId>blocked.mod</packageId></ModMetaData>");
    loaded.ActivePackageIds = new() { "missing.mod", "blocked.mod" };
    try
    {
        ModLinkService.Prepare(game, loaded, root);
        throw new Exception("Expected occupied path failure");
    }
    catch (IOException) { }
    Check(new DirectoryInfo(link).LinkTarget is null && Directory.Exists(blocked),
        "partial link preparation rolls back without deleting unrelated directory");

    ModColorStore.SetModColors(root, new Dictionary<string, string?> { ["test"] = "#112233" });
    Check(ModColorStore.GetColor(root, "test") == "#112233", "color cache reflects writes");
    File.WriteAllText(ModColorStore.GetPath(root), "{\"modColors\":{\"test\":\"#445566\"}}");
    ModColorStore.Invalidate();
    Check(ModColorStore.GetColor(root, "test") == "#445566", "color refresh loads external changes");
    var colorsCopy = ModColorStore.Load(root);
    colorsCopy.ModColors["test"] = "#FFFFFF";
    Check(ModColorStore.GetColor(root, "test") == "#445566", "caller cannot mutate color cache");
    var colorFile = ModColorStore.GetPath(root);
    File.Move(colorFile, colorFile + ".saved");
    Directory.CreateDirectory(colorFile);
    try
    {
        ModColorStore.SetModColors(root, new Dictionary<string, string?> { ["test"] = "#000000" });
        throw new Exception("Expected color write failure");
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
    Check(ModColorStore.GetColor(root, "test") == "#445566", "failed color save leaves cache unchanged");
    Directory.Delete(colorFile);
    File.Move(colorFile + ".saved", colorFile);
    var originalOrder = new[] { "a", "b", "c", "d", "e" };
    Check(ModListOperations.InsertSelection(originalOrder, new[] { "b", "c" }, 2)
        .SequenceEqual(originalOrder), "drop inside selection preserves order");
    Check(ModListOperations.InsertSelection(originalOrder, new[] { "b", "d" }, 5)
        .SequenceEqual(new[] { "a", "c", "e", "b", "d" }), "noncontiguous selection moves to end in order");
    Check(ModListOperations.InsertSelection(originalOrder, new[] { "d", "e" }, 0)
        .SequenceEqual(new[] { "d", "e", "a", "b", "c" }), "selection moves to beginning");
    Check(ModListOperations.InsertSelection(new[] { "a", "B", "c" }, new[] { "b", "x" }, 3)
        .SequenceEqual(new[] { "a", "c", "b", "x" }), "cross-instance insertion deduplicates case-insensitively");
    Check(ModListOperations.InsertSelection(Array.Empty<string>(), new[] { "a" }, 99)
        .SequenceEqual(new[] { "a" }), "insertion handles empty target and out-of-range index");
    Check(originalOrder.SequenceEqual(new[] { "a", "b", "c", "d", "e" }), "reordering does not mutate original snapshot");

    InstanceStore.StageActiveMods(loaded, new[] { "a", "b" });
    InstanceStore.StageActiveMods(copied, new[] { "c" });
    var beforeMove = new[]
    {
        new InstanceSnapshot(loaded.FolderName, loaded.ActivePackageIds.ToList()),
        new InstanceSnapshot(copied.FolderName, copied.ActivePackageIds.ToList())
    };
    InstanceStore.StageActiveMods(loaded, new[] { "a" });
    InstanceStore.StageActiveMods(copied, ModListOperations.InsertSelection(copied.ActivePackageIds, new[] { "b" }, 0));
    var redo = ModListOperations.Restore(beforeMove, new[] { loaded, copied });
    Check(loaded.ActivePackageIds.SequenceEqual(new[] { "a", "b" }) && copied.ActivePackageIds.SequenceEqual(new[] { "c" }),
        "undo restores both sides of cross-instance move");
    ModListOperations.Restore(redo, new[] { loaded, copied });
    Check(loaded.ActivePackageIds.SequenceEqual(new[] { "a" }) && copied.ActivePackageIds.SequenceEqual(new[] { "b", "c" }),
        "redo restores both sides of cross-instance move");
    try
    {
        ModListOperations.Restore(beforeMove, new[] { loaded });
        throw new Exception("Expected missing instance failure");
    }
    catch (InvalidOperationException) { }
    Check(loaded.ActivePackageIds.SequenceEqual(new[] { "a" }), "missing undo target does not partially restore other instance");

    var saveFolder = Path.Combine(root, "space 中文", "profile1");
    var launch = GameLaunchService.BuildStartInfo("game executable", root, saveFolder, false);
    Check(launch.ArgumentList.SequenceEqual(new[] { "-logfile", Path.Combine(saveFolder, "player.log"), "-savedatafolder=" + saveFolder }) &&
        !launch.UseShellExecute, "launch arguments preserve spaces and Unicode");
    var steamLaunch = GameLaunchService.BuildSteamStartInfo("steam.exe", saveFolder);
    Check(steamLaunch.ArgumentList.SequenceEqual(new[] { "-applaunch", "294100", "-logfile", Path.Combine(saveFolder, "player.log"), "-savedatafolder=" + saveFolder }),
        "steam launch arguments");
    var steamRoot = Path.Combine(root, "SteamTest");
    var steamApps = Path.Combine(steamRoot, "steamapps");
    var steamGame = Path.Combine(steamApps, "common", "RimWorld");
    Directory.CreateDirectory(steamGame);
    File.WriteAllText(Path.Combine(steamRoot, "steam.exe"), string.Empty);
    Check(GameLaunchService.FindSteamExecutable(steamGame, true) is null,
        "Steam locator requires the RimWorld app manifest");
    File.WriteAllText(Path.Combine(steamApps, "appmanifest_294100.acf"), string.Empty);
    Check(GameLaunchService.FindSteamExecutable(steamGame, true) ==
        Path.Combine(steamRoot, "steam.exe"), "Steam locator validates game library layout");
    var macLaunch = GameLaunchService.BuildStartInfo("/Applications/RimWorld.app", root, saveFolder, true);
    Check(macLaunch.FileName == "open" && macLaunch.ArgumentList.Take(5)
        .SequenceEqual(new[] { "-W", "-n", "-a", "/Applications/RimWorld.app", "--args" }), "macOS waits for new application instance");
    var official = new ManagedInstance { FolderName = "official", DirectoryPath = Path.Combine(saveFolder, "Config"), IsOfficial = true };
    Check(GameLaunchService.GetSaveDataFolder(official) == saveFolder &&
        GameLaunchService.GetSaveDataFolder(loaded) == loaded.DirectoryPath, "official and managed profiles use correct save roots");

    var installRoot = Path.Combine(root, "runtime", "install-test");
    var installMods = Path.Combine(installRoot, "Mods");
    var installConfig = Path.Combine(installRoot, "Config");
    Directory.CreateDirectory(Path.Combine(installMods, "Example"));
    Directory.CreateDirectory(installConfig);
    File.WriteAllText(Path.Combine(installMods, "Example", "content.txt"), "mod content");
    File.WriteAllText(Path.Combine(installConfig, "Mod_Test.xml"), "<settings />");
    var installed = ModpackInstanceInstaller.Create(root, "1.6", new ModpackInstallResult(
        "Installed pack", new[] { "example.mod" }, 0, 0, 1, 0, installMods, installConfig,
        "example", "https://example.com/config", new[] { "Mod_Test.xml" }, true));
    Check(installed.Metadata?.PackId == "example" && installed.IsSteamCmd && installed.Metadata.Name == "Installed pack",
        "installation finalization preserves name and source binding");
    Check(File.ReadAllText(Path.Combine(installed.DirectoryPath, "Mods", "Example", "content.txt")) == "mod content" &&
        File.Exists(Path.Combine(installed.ConfigDirectoryPath, "Mod_Test.xml")) && !Directory.Exists(installRoot),
        "installation transfers mod/config files and cleans staging");
    var profileCount = InstanceStore.Discover(root).Count;
    var failedRoot = Path.Combine(root, "runtime", "install-failure");
    var failedMods = Path.Combine(failedRoot, "Mods");
    var failedConfig = Path.Combine(failedRoot, "Config");
    Directory.CreateDirectory(failedMods);
    Directory.CreateDirectory(failedConfig);
    File.WriteAllText(Path.Combine(failedMods, "keep.txt"), "downloaded");
    File.WriteAllText(Path.Combine(failedConfig, "ModsConfig.xml"), "<broken>");
    var failedResult = new ModpackInstallResult("Failure", new[] { "test" }, 0, 0, 1, 0,
        failedMods, failedConfig, null, null, Array.Empty<string>(), false);
    try
    {
        ModpackInstanceInstaller.Create(root, "1.6", failedResult);
        throw new Exception("Expected install failure");
    }
    catch (IOException) { }
    Check(InstanceStore.Discover(root).Count == profileCount && File.Exists(Path.Combine(failedMods, "keep.txt")),
        "failed install publishes no profile and retains downloaded staging");
    Check(!Directory.EnumerateDirectories(Path.Combine(root, "instance"), ".install-*").Any(), "failed install removes temporary profile");
    try
    {
        ModpackInstanceInstaller.Create(root, "1.6", failedResult with { ModsSourcePath = target });
        throw new Exception("Expected invalid staging rejection");
    }
    catch (IOException) { }
    Check(File.Exists(original), "invalid staging cannot delete unrelated parent directory");

    // A bad profile must not prevent cleanup of subsequent profiles or unrelated launches.
    File.WriteAllText(Path.Combine(loaded.DirectoryPath, "mod-links.txt"),
        blocked + "|" + Path.Combine(loaded.DirectoryPath, "Mods", "Blocked"));
    var recoveryErrors = ModLinkService.Recover(game, root);
    Check(recoveryErrors.Count > 0, "recovery aggregates profile failures");
    var otherAbout = Path.Combine(installed.DirectoryPath, "Mods", "Recovery", "About");
    Directory.CreateDirectory(otherAbout);
    File.WriteAllText(Path.Combine(otherAbout, "About.xml"),
        "<ModMetaData><packageId>recovery.mod</packageId></ModMetaData>");
    installed.ActivePackageIds = new() { "recovery.mod" };
    ModLinkService.Prepare(game, installed, root);
    ModLinkService.Recover(game, root);
    Check(new DirectoryInfo(Path.Combine(game, "Mods", "PF_recovery.mod")).LinkTarget is null,
        "recovery continues cleaning other profiles after a bad profile");
    installed.ActivePackageIds = new() { "unrelated.mod" };
    ModLinkService.Prepare(game, installed, root);
    Check(Directory.Exists(blocked), "unrelated profile launches without deleting occupied managed path");
    Directory.CreateDirectory(Path.Combine(blocked, "About"));
    File.WriteAllText(Path.Combine(blocked, "About", "About.xml"),
        "<ModMetaData><packageId>blocked.mod</packageId></ModMetaData>");
    installed.ActivePackageIds = new() { "blocked.mod" };
    try
    {
        ModLinkService.Prepare(game, installed, root);
        throw new Exception("Expected conflicting recovery failure");
    }
    catch (AggregateException) { }
    Check(Directory.Exists(blocked), "affected profile is blocked on unresolved package conflict");

    File.WriteAllText(configPath, "<broken>");
    File.WriteAllText(Path.Combine(copied.DirectoryPath, InstanceStore.MetadataFileName), "{broken");
    var discovered = InstanceStore.Discover(root);
    var damaged = discovered.Single(item => item.FolderName == loaded.FolderName);
    Check(damaged.IsDamaged && discovered.Single(item => item.FolderName == copied.FolderName).IsDamaged &&
        !discovered.Single(item => item.FolderName == installed.FolderName).IsDamaged,
        "damaged XML and JSON profiles are isolated from healthy profiles");
    try { InstanceStore.StageActiveMods(damaged, new[] { "overwrite" }); throw new Exception("Expected damaged edit rejection"); }
    catch (IOException) { }
    InstanceStore.SavePendingMods();
    Check(File.ReadAllText(configPath) == "<broken>", "damaged profile cannot be staged or overwritten by stale pending edits");
    var damagedOfficial = InstanceStore.LoadOfficial(loaded.ConfigDirectoryPath, "Official");
    Check(damagedOfficial.IsDamaged, "damaged official config is isolated");
    try
    {
        ModListOperations.Restore(new[] { new InstanceSnapshot(installed.FolderName, new() { "changed" }),
            new InstanceSnapshot(damaged.FolderName, new() { "changed" }) }, new[] { installed, damaged });
        throw new Exception("Expected damaged undo target rejection");
    }
    catch (IOException) { }
    Check(installed.ActivePackageIds.SequenceEqual(new[] { "blocked.mod" }), "damaged undo target does not partially modify healthy instance");
    Check(File.ReadAllText(AppLog.LogPath).Contains("Read mod list"), "critical error includes operation in log");
    if (!OperatingSystem.IsWindows())
    {
        var archive = Path.Combine(root, "unix.tar.gz");
        using (var output = File.Create(archive))
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        using (var writer = new TarWriter(gzip))
        {
            foreach (var name in new[] { "steamcmd.sh", "linux32/steamcmd" })
            {
                using var content = new MemoryStream(new byte[] { 1, 2, 3 });
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute,
                    DataStream = content
                });
            }
        }
        var extracted = Path.Combine(root, "unix-extracted");
        Directory.CreateDirectory(extracted);
        SteamCmdClient.ExtractUnixArchive(archive, extracted);
        foreach (var name in new[] { "steamcmd.sh", "linux32/steamcmd" })
            Check((File.GetUnixFileMode(Path.Combine(extracted, name)) & UnixFileMode.UserExecute) != 0,
                "SteamCMD tar extraction preserves executable bits: " + name);
    }
    else Console.WriteLine("SKIP: Unix executable-bit check (runs on Linux/macOS CI).");
    Console.WriteLine("All checks passed.");
    var rulesFolder = Path.Combine(root, "rules-mod");
    Directory.CreateDirectory(Path.Combine(rulesFolder, "About"));
    File.WriteAllText(Path.Combine(rulesFolder, "About", "About.xml"),
        "<ModMetaData><packageId>framework</packageId><loadBefore><li>other</li></loadBefore>" +
        "<forceLoadBefore><li>forced</li></forceLoadBefore><incompatibleWith><li>OTHER</li><li>absent</li></incompatibleWith></ModMetaData>");
    var framework = ModCatalog.ReadMod(rulesFolder, ModSource.Local)!;
    var otherMod = framework with { PackageId = "other", LoadBefore = new(), ForceLoadBefore = new(), IncompatibleWith = new() };
    Check(framework.LoadBefore.SequenceEqual(new[] { "other" }) && framework.ForceLoadBefore.SequenceEqual(new[] { "forced" }),
        "mod metadata reads both Before rule types");
    var reverseRules = ModOrderRules.ReverseBefore(new[] { otherMod, framework });
    Check(reverseRules["other"].Contains("framework") && reverseRules["forced"].Contains("framework"),
        "Before declarations become correct After constraints for existing reorder actions");
    var conflictRules = ModOrderRules.Conflicts(new[] { otherMod, framework });
    Check(conflictRules["other"].Contains("framework") && conflictRules["framework"].Count == 1,
        "incompatibilities flag both enabled mods and ignore disabled targets");
    Check(OperationText.Download("en").StartsWith("Downloading") &&
        OperationText.Download("ja").Contains("ローカル") && OperationText.Download("unknown") == OperationText.Download("en"),
        "operation messages respect language and English fallback");
    var packManifest = new Uri("https://raw.githubusercontent.com/ZXUW/UI-1.7-Rimworld-PackFrame/main/pack.json");
    Check(ModpackService.ResolveImage(packManifest, "images/cover.png") ==
        "https://raw.githubusercontent.com/ZXUW/UI-1.7-Rimworld-PackFrame/main/images/cover.png",
        "pack-relative cover image resolves against the pack manifest");
    Check(ModpackService.ResolveImage(packManifest, "https://example.com/cover.png") ==
        "https://example.com/cover.png", "absolute cover image stays absolute");
    await PublishingChecks.Run(root);
    Console.WriteLine("Rule and publishing checks passed.");
}
finally
{
    InstanceStore.DiscardPendingMods();
    Directory.Delete(root, true);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
