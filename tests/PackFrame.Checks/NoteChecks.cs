using PackFrame;

internal static class NoteChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "packframe-note-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "instance"));
        try
        {
            ModNoteStore.Invalidate();
            ModNoteStore.Set(root, "a.mod", "  别名  ", "分类一");
            ModNoteStore.Set(root, "b.mod", null, "分类二");
            var note = ModNoteStore.Get(root, "A.MOD");
            Require(note?.Alias == "别名" && note.Category == "分类一",
                "mod notes keep alias and category case-insensitively");
            Require(ModNoteStore.Categories(root).SequenceEqual(new[] { "分类一", "分类二" }) ||
                    ModNoteStore.Categories(root).Count == 2,
                "mod note categories are deduplicated");

            ModNoteStore.SetCategory(root, new[] { "a.mod", "c.mod" }, "批量");
            Require(ModNoteStore.Get(root, "a.mod")?.Category == "批量" &&
                    ModNoteStore.Get(root, "a.mod")?.Alias == "别名",
                "batch category keeps the alias");
            ModNoteStore.SetCategory(root, new[] { "a.mod", "b.mod", "c.mod" }, null);
            Require(ModNoteStore.Get(root, "a.mod") is { Category: null, Alias: "别名" } &&
                    ModNoteStore.Get(root, "b.mod") is null &&
                    ModNoteStore.Get(root, "c.mod") is null,
                "clearing categories keeps aliases and drops empty notes");

            ModNoteStore.Set(root, "a.mod", "别名", "分类");
            Require(File.Exists(Path.Combine(root, "instance", ModNoteStore.FileName)),
                "mod notes are stored in the instance folder");

            var langOnlyDir = Path.Combine(root, "lang-only");
            Directory.CreateDirectory(Path.Combine(langOnlyDir, "1.6", "Languages"));
            Require(ModCatalog.DetectTranslationMod(langOnlyDir, "Some Pack", "author.somepack"),
                "languages-only folder is detected as translation mod");

            var contentDir = Path.Combine(root, "content-mod");
            Directory.CreateDirectory(Path.Combine(contentDir, "1.6", "Languages"));
            Directory.CreateDirectory(Path.Combine(contentDir, "1.6", "Defs"));
            Require(!ModCatalog.DetectTranslationMod(contentDir, "Some Content", "author.content"),
                "mod with Defs and neutral name is not marked as translation mod");

            var harmony = new ModInfo("Harmony", "A", "", "brrainz.harmony", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var framework = new ModInfo("Framework", "A", "", "test.framework", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                ModDependencies = new List<string> { "brrainz.harmony" }
            };
            var featureMod = new ModInfo("Feature Mod", "A", "", "test.feature", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                ModDependencies = new List<string> { "test.framework", "missing.uninstalled" }
            };
            var enabledTarget = new ModInfo("Big Mod 1", "A", "", "other.bigmod1", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var megaZhMod = new ModInfo("大合集汉化", "A", "", "test.megazh", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                IsTranslationMod = true,
                ModDependencies = new List<string> { "other.bigmod1", "other.bigmod2", "test.framework" }
            };
            var singleZhMod = new ModInfo("单项汉化", "A", "", "test.singlezh", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                IsTranslationMod = true,
                ModDependencies = new List<string> { "author.oldtarget" },
                ModDependencyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["author.oldtarget"] = "Renamed Target"
                }
            };
            var renamedTarget = new ModInfo("Renamed Target (Continued)", "B", "", "mlie.renamedtarget", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var installedMap = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [harmony.PackageId] = harmony,
                [framework.PackageId] = framework,
                [featureMod.PackageId] = featureMod,
                [enabledTarget.PackageId] = enabledTarget,
                [megaZhMod.PackageId] = megaZhMod,
                [singleZhMod.PackageId] = singleZhMod,
                [renamedTarget.PackageId] = renamedTarget
            };
            var allInstalled = installedMap.Values.ToList();
            var scan = AutoSortPrerequisites.Scan(
                new[] { featureMod, enabledTarget, megaZhMod, singleZhMod },
                id => installedMap.TryGetValue(id, out var found) ? found : null,
                allInstalled);
            Require(scan.InstalledMissing.Select(i => i.PackageId).SequenceEqual(
                    new[] { "test.framework", "brrainz.harmony" }, StringComparer.OrdinalIgnoreCase),
                "normal and transitive installed prerequisites are detected");
            Require(scan.FuzzyMatches.Select(i => i.PackageId).SequenceEqual(
                    new[] { "mlie.renamedtarget" }, StringComparer.OrdinalIgnoreCase),
                "fuzzy renamed prerequisites are separated for manual confirmation");
            Require(scan.UninstalledMissing.Count == 1 && scan.UninstalledMissing[0].PackageId == "missing.uninstalled",
                "uninstalled normal prerequisites are reported");
            Require(scan.IgnoredTranslation.Select(i => i.PackageId).SequenceEqual(
                    new[] { "other.bigmod2" }, StringComparer.OrdinalIgnoreCase),
                "satisfied 1-to-many translation skips remaining targets");

            var inserted = AutoSortPrerequisites.InsertPrerequisites(
                new[] { "test.feature", "test.megazh" },
                new[] { "test.framework", "brrainz.harmony" },
                new[] { featureMod, megaZhMod },
                id => installedMap.TryGetValue(id, out var found) ? found : null);
            Require(inserted.SequenceEqual(
                    new[] { "brrainz.harmony", "test.framework", "test.feature", "test.megazh" },
                    StringComparer.OrdinalIgnoreCase),
                "prerequisites are inserted in dependency order before dependents");

            var core = new ModInfo("Core", "Ludeon", "", "ludeon.rimworld", "", null, ModSource.Official, "1.6", DateTime.MinValue);
            var royalty = new ModInfo("Royalty", "Ludeon", "", "ludeon.rimworld.royalty", "", null, ModSource.Official, "1.6", DateTime.MinValue);
            var biotech = new ModInfo("Biotech", "Ludeon", "", "ludeon.rimworld.biotech", "", null, ModSource.Official, "1.6", DateTime.MinValue);
            var hugsLib = new ModInfo("HugsLib", "UnlimitedHugs", "", "unlimitedhugs.hugslib", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var secondConsumer = new ModInfo("Consumer 2", "A", "", "test.consumer2", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                ModDependencies = new List<string> { "test.framework" }
            };
            var scrambled = new List<ModInfo> { harmony, biotech, royalty, featureMod, secondConsumer, framework, hugsLib, core };
            var coreScan = AutoSortPrerequisites.ScanCorePrerequisites(scrambled, "zh");
            Require(coreScan.Select(c => c.Mod.PackageId).SequenceEqual(
                    new[] { "brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.biotech", "unlimitedhugs.hugslib" },
                    StringComparer.OrdinalIgnoreCase),
                "core prerequisites order Harmony -> Core -> canonical DLCs -> HugsLib only (Plan A)");
            var coreApplied = AutoSortPrerequisites.ApplyCorePrerequisitesOrder(
                scrambled.Select(m => m.PackageId).ToList(),
                scrambled,
                new HashSet<string>(coreScan.Where(c => c.NeedsMove).Select(c => c.Mod.PackageId), StringComparer.OrdinalIgnoreCase),
                "zh");
            Require(coreApplied.SequenceEqual(
                    new[] { "brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.biotech", "unlimitedhugs.hugslib", "test.feature", "test.consumer2", "test.framework" },
                    StringComparer.OrdinalIgnoreCase),
                "applying core prerequisites moves only system base tier without pulling general frameworks");

            var loadingProgress = new ModInfo("Loading Progress", "ilyvion", "", "ilyvion.loadingprogress", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                LoadBefore = new List<string> { "ludeon.rimworld" }
            };
            var loadingProgressZh = new ModInfo("Loading Progress (简中汉化)", "Z", "", "ilyvion.loadingprogress.zh", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                IsTranslationMod = true,
                ModDependencies = new List<string> { "brrainz.harmony", "ilyvion.loadingprogress" },
                LoadAfter = new List<string> { "brrainz.harmony", "ilyvion.loadingprogress" }
            };
            var fasterLoading = new ModInfo("Faster Game Loading - Continued", "T", "", "taranchuk.fastergameloading", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                LoadAfter = new List<string> { "brrainz.harmony", "ilyvion.loadingprogress" },
                LoadBefore = new List<string> { "ludeon.rimworld" }
            };
            var preCoreList = new List<ModInfo> { harmony, loadingProgress, loadingProgressZh, fasterLoading, core };
            var preCoreScan = AutoSortPrerequisites.ScanCorePrerequisites(preCoreList, "zh");
            Require(preCoreScan.All(c => !c.NeedsMove) &&
                    preCoreScan.Select(c => c.Mod.PackageId).SequenceEqual(
                        new[] { "brrainz.harmony", "ilyvion.loadingprogress", "ilyvion.loadingprogress.zh", "taranchuk.fastergameloading", "ludeon.rimworld" },
                        StringComparer.OrdinalIgnoreCase),
                "Pre-Core translation stays glued right after its Pre-Core parent without swapping with Faster Game Loading");

            // Step 3: CollectSortRules (explicit + Plan B1 implicit + inverted + cycle detection)
            var orphanZh = new ModInfo("Feature 简体中文", "Z", "", "rwzh.chinesepack.test.feature", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                IsTranslationMod = true
            };
            var cycleA = new ModInfo("Cycle A", "A", "", "test.cyclea", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                LoadAfter = new List<string> { "test.cycleb" }
            };
            var cycleB = new ModInfo("Cycle B", "B", "", "test.cycleb", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                LoadAfter = new List<string> { "test.cyclea" },
                IncompatibleWith = new List<string> { "test.consumer2" }
            };
            var step3List = new List<ModInfo> { harmony, core, biotech, royalty, orphanZh, featureMod, framework, cycleA, cycleB, secondConsumer };
            var rulesRes = AutoSortPrerequisites.CollectSortRules(step3List, "zh");
            Require(rulesRes.ImplicitTranslationRuleCount == 1 &&
                    rulesRes.Rules.Any(r =>
                        r.Kind == SortRuleKind.ImplicitTranslation &&
                        string.Equals(r.BeforeId, "test.feature", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.AfterId, "rwzh.chinesepack.test.feature", StringComparison.OrdinalIgnoreCase) &&
                        r.IsViolated),
                "implicit translation parent rule inferred and marked inverted when translation precedes parent");
            Require(rulesRes.ViolatedRules.Any(r =>
                        string.Equals(r.BeforeId, "ludeon.rimworld.royalty", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.AfterId, "ludeon.rimworld.biotech", StringComparison.OrdinalIgnoreCase)) &&
                    rulesRes.ViolatedRules.Any(r =>
                        string.Equals(r.BeforeId, "test.framework", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.AfterId, "test.feature", StringComparison.OrdinalIgnoreCase)),
                "inverted canonical DLC and explicit dependency rules detected");
            Require(rulesRes.CycleRules.Count == 2 &&
                    rulesRes.CyclePairs.Count == 1 &&
                    rulesRes.CycleRules.All(r => r.BeforeId.StartsWith("test.cycle", StringComparison.OrdinalIgnoreCase)),
                "cycle edges isolated to strongly connected cycle participants");
            Require(rulesRes.Conflicts.Count == 1,
                "incompatible active mod pairs collected");

            // Step 3b: Bundled CommunityRulesCache + user cycle resolution override
            var bundledDb = CommunityRulesCache.Shared.LoadLocal();
            Require(bundledDb.Rules.Count > 500 &&
                    bundledDb.Rules.TryGetValue("krkr.rocketman", out var rmRule) &&
                    rmRule.LoadBottom,
                "bundled RimSort CommunityRules.json snapshot loads with 500+ mod rules");

            var customCommunity = CommunityRulesCache.ParseSnapshot(
                """{"timestamp":100,"rules":{"test.consumer2":{"loadAfter":{"test.feature":{"name":["Feature"]}}}}}""");
            var cycleChoice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AutoSortPrerequisites.MakePairKey("test.cyclea", "test.cycleb")] = "test.cycleb"
            };
            var resolvedRes = AutoSortPrerequisites.CollectSortRules(
                step3List, "zh", customCommunity, useCommunityRules: true, cycleChoice);
            Require(resolvedRes.CyclePairs.Count == 1 &&
                    resolvedRes.CycleRules.Count == 0 &&
                    resolvedRes.CommunityRuleCount == 1 &&
                    resolvedRes.Rules.Any(r =>
                        string.Equals(r.BeforeId, "test.cycleb", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.AfterId, "test.cyclea", StringComparison.OrdinalIgnoreCase) &&
                        r.IsViolated),
                "cycle resolution override breaks mutual cycle and preserves top selector pair while merging community rules");

            // Step 4: Apply sort (BuildTopologicalOrder) — honors cycle choice, glues translation beneath parent, pins loadBottom last
            var bottomCommunity = CommunityRulesCache.ParseSnapshot(
                """{"timestamp":101,"rules":{"test.consumer2":{"loadAfter":{"test.feature":{"name":["Feature"]}}},"test.cyclea":{"loadBottom":{"value":true}}}}""");
            var step4Rules = AutoSortPrerequisites.CollectSortRules(
                step3List, "zh", bottomCommunity, useCommunityRules: true, cycleChoice);
            var rawWithMissing = new List<string> { "brrainz.harmony", "missing.ghost.mod" }
                .Concat(step3List.Skip(1).Select(m => m.PackageId))
                .ToList();
            var plan = AutoSortPrerequisites.BuildTopologicalOrder(rawWithMissing, step3List, step4Rules);
            var sorted = plan.OrderedPackageIds.Where(id => !string.Equals(id, "missing.ghost.mod", StringComparison.OrdinalIgnoreCase)).ToList();
            int Pos(string id) => sorted.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

            Require(plan.OrderedPackageIds[1] == "missing.ghost.mod" &&
                    Pos("brrainz.harmony") < Pos("ludeon.rimworld") &&
                    Pos("ludeon.rimworld") < Pos("ludeon.rimworld.royalty") &&
                    Pos("ludeon.rimworld.royalty") < Pos("ludeon.rimworld.biotech") &&
                    Pos("test.framework") < Pos("test.feature") &&
                    Pos("rwzh.chinesepack.test.feature") == Pos("test.feature") + 1 &&
                    Pos("test.feature") < Pos("test.consumer2") &&
                    Pos("test.cycleb") < Pos("test.cyclea") &&
                    Pos("test.cyclea") == sorted.Count - 1,
                "BuildTopologicalOrder sorts DAG stably, glues translation immediately after parent, honors cycle choice and loadBottom, and preserves missing slots");

            // Unresolved cycle auto-breaking check
            var unresolvedPlan = AutoSortPrerequisites.BuildTopologicalOrder(
                step3List.Select(m => m.PackageId).ToList(), step3List, rulesRes);
            Require(unresolvedPlan.BrokenCycleEdges.Count == 1 &&
                    unresolvedPlan.Rows.Count == step3List.Count,
                "BuildTopologicalOrder automatically breaks weakest cycle edge when user leaves cycle on Auto");

            // Trimmed CommunityRules array format + WorkshopMapCache + About.xml URL priority + Workshop ID resolution
            var slimCommunity = CommunityRulesCache.ParseSnapshot(
                """{"timestamp":200,"rules":{"test.consumer2":{"loadAfter":["test.feature"],"loadBottom":true}}}""");
            Require(slimCommunity.Rules.TryGetValue("test.consumer2", out var slimRule) &&
                    slimRule.LoadAfter.Contains("test.feature", StringComparer.OrdinalIgnoreCase) &&
                    slimRule.LoadBottom,
                "CommunityRulesCache parses trimmed string-array and boolean format");

            var bundledWs = WorkshopMapCache.Shared.LoadLocal();
            Require(bundledWs.Mods.Count > 10_000 &&
                    WorkshopMapCache.Shared.TryGetWorkshopId("brrainz.harmony") == 2009463077UL &&
                    WorkshopMapCache.Shared.TryGetWorkshopId("unlimitedhugs.hugslib") == 818773962UL &&
                    WorkshopMapCache.Shared.TryGetWorkshopId("oskarpotocki.vanillafactionsexpanded.core_steam") == 2023507013UL &&
                    WorkshopMapCache.TryGetOfficialAppId("ludeon.rimworld.biotech") == 1826140U,
                "WorkshopMapCache resolves canonical Workshop IDs, suffix stems, and official DLC AppIDs");

            Require(ModpackService.ParseWorkshopId("steam://url/CommunityFilePage/2009463077") == 2009463077UL &&
                    ModpackService.ParseWorkshopId("https://steamcommunity.com/workshop/filedetails/?id=818773962") == 818773962UL &&
                    ModpackService.ParseWorkshopId("https://github.com/pardeike/HarmonyRimWorld/releases/latest") is null,
                "ParseWorkshopId handles CommunityFilePage/, filedetails/?id=, and rejects non-Workshop URLs");

            var urlTestModDir = Path.Combine(root, "Mods", "UrlPriorityMod");
            Directory.CreateDirectory(Path.Combine(urlTestModDir, "About"));
            File.WriteAllText(Path.Combine(urlTestModDir, "About", "About.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <ModMetaData>
                  <name>URL Priority Test</name>
                  <packageId>test.urlpriority</packageId>
                  <supportedVersions><li>1.6</li></supportedVersions>
                  <modDependencies>
                    <li>
                      <packageId>custom.explicit.dep</packageId>
                      <displayName>Explicit Dep</displayName>
                      <downloadUrl>https://github.com/example/ExplicitDep/releases/latest</downloadUrl>
                      <steamWorkshopUrl>steam://url/CommunityFilePage/3123456789</steamWorkshopUrl>
                    </li>
                    <li>
                      <packageId>brrainz.harmony</packageId>
                      <displayName>Harmony</displayName>
                      <downloadUrl>https://github.com/pardeike/HarmonyRimWorld/releases/latest</downloadUrl>
                    </li>
                    <li>
                      <packageId>totally.unknown.localmod</packageId>
                      <displayName>Unknown Local Mod</displayName>
                    </li>
                    <li>
                      <packageId>ludeon.rimworld.odyssey</packageId>
                      <displayName>Odyssey</displayName>
                    </li>
                  </modDependencies>
                </ModMetaData>
                """);
            var parsedCatalog = ModCatalog.LoadAsync(
                new RimWorldInstallation(true, root, null, null, "1.6", null, false, null),
                null).GetAwaiter().GetResult();
            Require(parsedCatalog.Count == 1, "ModCatalog parses URL Priority Test mod");
            var wsScan = AutoSortPrerequisites.Scan(parsedCatalog, _ => null, parsedCatalog);
            var byPkg = wsScan.UninstalledMissing.ToDictionary(x => x.PackageId, StringComparer.OrdinalIgnoreCase);
            Require(byPkg["custom.explicit.dep"].WorkshopFileId == 3123456789UL &&
                    byPkg["brrainz.harmony"].WorkshopFileId == 2009463077UL &&
                    byPkg["totally.unknown.localmod"].WorkshopFileId is null &&
                    byPkg["totally.unknown.localmod"].SteamStoreAppId is null &&
                    byPkg["ludeon.rimworld.odyssey"].SteamStoreAppId == 3022790U &&
                    byPkg["ludeon.rimworld.odyssey"].WorkshopFileId is null,
                "Scan prioritizes About.xml steamWorkshopUrl over GitHub downloadUrl, falls back to WorkshopMapCache, resolves DLC AppID, and leaves unknown mods buttonless");

            // Implicit RJW rule check (standalone "rjw" in Name, PackageId, or Folder name; ignores substrings like "marjwana")
            var rjwAddonById = new ModInfo("Events Addon", "A", @"C:\Mods\12345", "c0ffee.rjw.events", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var rjwAddonByName = new ModInfo("[RJW] Animations", "A", @"C:\Mods\23456", "author.animpack", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var rjwAddonByFolder = new ModInfo("Custom Addon", "A", @"C:\Mods\RJW_Extra_Content\", "author.custompack", "", null, ModSource.Local, "1.6", DateTime.MinValue);
            var falsePositiveSubstr = new ModInfo("Marjwana Plants", "A", @"C:\Mods\marjwanamod", "author.marjwanaplants", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var rjwCore = new ModInfo("RimJobWorld", "A", @"C:\Mods\rim.job.world", "rim.job.world", "", null, ModSource.Local, "1.6", DateTime.MinValue);
            var rjwList = new List<ModInfo> { core, rjwAddonById, rjwAddonByName, rjwAddonByFolder, falsePositiveSubstr, rjwCore };
            var rjwRules = AutoSortPrerequisites.CollectSortRules(rjwList, "zh", useCommunityRules: true);
            var rjwPlan = AutoSortPrerequisites.BuildTopologicalOrder(rjwList.Select(m => m.PackageId).ToList(), rjwList, rjwRules);
            int RjwPos(string id) => rjwPlan.OrderedPackageIds.ToList().FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            Require(rjwRules.ImplicitRjwRuleCount == 3 &&
                    !AutoSortPrerequisites.HasStandaloneRjwToken(falsePositiveSubstr) &&
                    RjwPos("author.marjwanaplants") < RjwPos("rim.job.world") &&
                    RjwPos("rim.job.world") < RjwPos("c0ffee.rjw.events") &&
                    RjwPos("rim.job.world") < RjwPos("author.animpack") &&
                    RjwPos("rim.job.world") < RjwPos("author.custompack"),
                "Implicit RJW rule places standalone 'rjw' mods (name/packageId/folder) after rim.job.world while ignoring partial substrings");
        }
        finally
        {
            ModNoteStore.Invalidate();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
