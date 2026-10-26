using PackFrame;

internal static class UpdateChecks
{
    public static void Run(string root)
    {
        InstanceStore.DiscardPendingMods();
        var packRoot = Path.Combine(root, "update-tests");
        Directory.CreateDirectory(packRoot);
        var export = Path.Combine(packRoot, "export");
        Directory.CreateDirectory(export);
        File.WriteAllText(Path.Combine(export, "pack.json"), "{\"id\":\"fixed-id\",\"revision\":123,\"version\":\"old\"}");
        ModpackRepoExporter.SetPackInfo(export, "New name", "Description", "1.4.0", "New content");
        using (var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(export, "pack.json"))))
            Require(saved.RootElement.GetProperty("version").GetString() == "1.4.0" &&
                saved.RootElement.GetProperty("changelog").GetString() == "New content" &&
                saved.RootElement.GetProperty("revision").GetInt64() == 123 && saved.RootElement.GetProperty("id").GetString() == "fixed-id",
                "export form saves version/changelog without changing revision or identity");
        ModpackInstallResult Download(long revision, string value)
        {
            var staging = Path.Combine(packRoot, "runtime", "install-" + Guid.NewGuid().ToString("N"));
            var mods = Path.Combine(staging, "Mods");
            var config = Path.Combine(staging, "Config");
            Directory.CreateDirectory(Path.Combine(mods, "Managed")); Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(mods, "Managed", "version.txt"), value);
            File.WriteAllText(Path.Combine(config, "Mod_Test.xml"), "<value>" + value + "</value>");
            return new("Pack", new[] { "mod." + revision }, 0, 0, 1, 0, mods, config,
                "stable-id", "https://example.com/config", new[] { "Mod_Test.xml" }, false,
                "https://example.com/index.json", revision, revision.ToString());
        }
        var instance = ModpackInstanceInstaller.Create(packRoot, "1.6", Download(1, "one"));
        InstanceStore.RenameInstance(instance, "My name");
        File.WriteAllText(Path.Combine(instance.DirectoryPath, "Saves", "save.rws"), "SAVE");
        Directory.CreateDirectory(Path.Combine(instance.DirectoryPath, "Mods", "Extra"));
        File.WriteAllText(Path.Combine(instance.DirectoryPath, "Mods", "Extra", "keep.txt"), "EXTRA");
        File.WriteAllText(Path.Combine(instance.ConfigDirectoryPath, "Mod_Test.xml"), "<value>user</value>");
        var update = Download(2, "two");
        var plan = PackUpdateService.Plan(instance, update);
        Require(plan.Conflicts.Contains("Config/Mod_Test.xml"), "update detects both-author-and-user config conflict");
        PackUpdateService.Apply(packRoot, instance, update, new HashSet<string> { "Config/Mod_Test.xml" }, false);
        Require(instance.Metadata!.InstalledRevision == 2 && instance.Metadata.Name == "My name" &&
            File.ReadAllText(Path.Combine(instance.ConfigDirectoryPath, "Mod_Test.xml")).Contains("user"), "update commits revision and preserves chosen local config/name");
        Require(File.ReadAllText(Path.Combine(instance.DirectoryPath, "Saves", "save.rws")) == "SAVE" &&
            File.Exists(Path.Combine(instance.DirectoryPath, "Mods", "Extra", "keep.txt")), "update preserves saves and extra mods");
        Require(File.ReadAllText(Path.Combine(instance.DirectoryPath, "Mods", "Managed", "version.txt")) == "two", "managed mod directory replaced");
        PackUpdateService.Restore(instance);
        Require(instance.Metadata!.InstalledRevision == 1 && instance.ActivePackageIds.SequenceEqual(new[] { "mod.1" }) &&
            File.ReadAllText(Path.Combine(instance.DirectoryPath, "Mods", "Managed", "version.txt")) == "one", "restore recovers old version order and managed mod");
        try { PackUpdateService.Plan(instance, Download(3, "three") with { PackSource = "https://other/index.json" }); throw new Exception("identity accepted"); }
        catch (IOException) { }
        Require(instance.Metadata.InstalledRevision == 1, "different index cannot update same-ID instance");
        var broken = Download(4, "four");
        var conflictDirectory = Path.Combine(instance.ConfigDirectoryPath, "Mod_Test.xml");
        File.Delete(conflictDirectory); Directory.CreateDirectory(conflictDirectory);
        // File and directory collisions must either apply transactionally or leave the old revision intact.
        File.WriteAllText(Path.Combine(broken.ConfigSourcePath!, "ModsConfig.xml"), "<broken>");
        try { PackUpdateService.Apply(packRoot, instance, broken, new HashSet<string>(), false); throw new Exception("invalid config accepted"); }
        catch (IOException) { }
        Require(instance.Metadata.InstalledRevision == 1, "invalid staged order never advances installed revision");
        Directory.Delete(conflictDirectory);
        File.WriteAllText(conflictDirectory, "<value>one</value>");
        var removed = Download(5, "five");
        File.Delete(Path.Combine(removed.ConfigSourcePath!, "Mod_Test.xml"));
        Directory.Delete(Path.Combine(removed.ModsSourcePath!, "Managed"), true);
        PackUpdateService.Apply(packRoot, instance, removed, new HashSet<string>(), false);
        Require(!File.Exists(conflictDirectory) && !Directory.Exists(Path.Combine(instance.DirectoryPath, "Mods", "Managed")) &&
            Directory.Exists(Path.Combine(instance.DirectoryPath, "Mods", "Extra")), "author deletions remove only owned content");
        PackUpdateService.Restore(instance);
        Require(File.Exists(conflictDirectory) && Directory.Exists(Path.Combine(instance.DirectoryPath, "Mods", "Managed")),
            "restore recovers author-deleted files and directories");
        if (OperatingSystem.IsWindows())
        {
            var failing = Download(6, "six");
            using (var locked = new FileStream(Path.Combine(instance.DirectoryPath, InstanceStore.MetadataFileName),
                FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { PackUpdateService.Apply(packRoot, instance, failing, new HashSet<string>(), false); throw new Exception("Expected locked metadata failure"); }
                catch (IOException) { }
                catch (AggregateException) { }
            }
            Require(File.ReadAllText(Path.Combine(instance.DirectoryPath, "Mods", "Managed", "version.txt")) == "one" &&
                InstanceStore.LoadInstance(instance.DirectoryPath).Metadata!.InstalledRevision == 1,
                "commit failure restores already replaced mod files and leaves revision unchanged");
        }
        var unowned = InstanceStore.CreateNew(packRoot, null, "1.6", new[] { "user.mod" });
        PackUpdateService.BindLegacy(unowned, new ModpackDefinition { Id = "stable-id", PackSource = "https://example.com/index.json" });
        Require(unowned.Metadata!.InstalledRevision is null && unowned.Metadata.ManagedFiles.Count == 0,
            "legacy linking does not claim version or file ownership");
    }
    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
