using System.IO.Compression;
using System.Net;
using System.Text;
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
            ModNoteStore.Set(root, "b.mod", null, "分类一");
            ModNoteStore.Set(root, "d.mod", null, "  大类 / 子类  ");
            var note = ModNoteStore.Get(root, "A.MOD");
            Require(note?.Alias == "别名" && note.Category == "分类一",
                "mod notes keep alias and category case-insensitively");
            var allCats = ModNoteStore.Categories(root);
            Require(allCats.Count == 3 &&
                    allCats.Contains("分类一") &&
                    allCats.Contains("大类") &&
                    allCats.Contains("大类/子类"),
                "mod note categories are deduplicated and expand slash hierarchies");

            var categoryTable = ModCategoryDatabase.Parse(
                "{\"version\":1,\"workshop\":{\"123\":\"种族/A\"},\"packageId\":{\"p.b\":\"系列/B\"},\"name\":{\"[Tag] Name C\":\"美化/C\"}}");
            var flatTable = ModCategoryDatabase.Parse("{\"p.flat\":\"系列/F\"}");
            Require(categoryTable is not null &&
                    categoryTable.Workshop["123"] == "种族/A" &&
                    categoryTable.PackageId["p.b"] == "系列/B" &&
                    categoryTable.Name.Count == 1 &&
                    ModCategoryDatabase.NormalizeName("[Tag] Name   C") == "Name C" &&
                    flatTable is not null && flatTable.PackageId["p.flat"] == "系列/F",
                "category table parses workshop/packageId/name sections and flat files");

            var tableFile = Path.Combine(root, "Assets", "ModCategories.json");
            Directory.CreateDirectory(Path.GetDirectoryName(tableFile)!);
            File.WriteAllText(tableFile, "{\"workshop\":{\"1\":\"种族/文件\"}}");
            var originalTablePath = ModCategoryDatabase.TablePath;
            try
            {
                ModCategoryDatabase.TablePath = tableFile;
                var fromDisk = ModCategoryDatabase.ReadBaseTable();
                ModCategoryDatabase.TablePath = Path.Combine(root, "Assets", "absent.json");
                var fromNowhere = ModCategoryDatabase.ReadBaseTable();
                Require(fromDisk is not null && fromDisk.Workshop["1"] == "种族/文件" &&
                        fromNowhere is null,
                    "the category table is read from its file only, with no copy baked into the exe");
            }
            finally
            {
                ModCategoryDatabase.TablePath = originalTablePath;
            }

            var metadataTable = ModCategoryDatabase.Parse(
                "{\"version\":3,\"generated\":\"2026-11-22T15:43:18+08:00\",\"title\":\"Mine\",\"packageId\":{\"p.meta\":\"内容拓展/Meta\"}}");            Require(metadataTable is { Version: 3, Title: "Mine" } &&
                    metadataTable.Generated == "2026-11-22T15:43:18+08:00" &&
                    metadataTable.PackageId.Count == 1 &&
                    metadataTable.PackageId["p.meta"] == "内容拓展/Meta" &&
                    !metadataTable.PackageId.ContainsKey("generated") &&
                    !metadataTable.PackageId.ContainsKey("title"),
                "category table treats generated/title as metadata, not package ids");

            var resolveTable = ModCategoryDatabase.Parse(
                "{\"workshop\":{\"123\":\"种族/W\"},\"packageId\":{\"p.b\":\"系列/P\"},\"name\":{\"Name C\":\"美化/N\"}}");
            var normalizedNames = ModCategoryDatabase.BuildNormalizedNames(resolveTable!.Name);
            var byWorkshop = new ModInfo("Other", "A", "", "p.b", "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
            {
                Link = "https://steamcommunity.com/sharedfiles/filedetails/?id=123"
            };
            var byPackage = new ModInfo("Other", "A", "", "p.b", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var byName = new ModInfo("Name C", "A", "", "p.other", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var byNormalized = new ModInfo("[Tag] Name C", "A", "", "p.other2", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var byNothing = new ModInfo("None", "A", "", "p.none", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            Require(ModCategoryDatabase.Resolve(byWorkshop, resolveTable, normalizedNames) == "种族/W" &&
                    ModCategoryDatabase.Resolve(byPackage, resolveTable, normalizedNames) == "系列/P" &&
                    ModCategoryDatabase.Resolve(byName, resolveTable, normalizedNames) == "美化/N" &&
                    ModCategoryDatabase.Resolve(byNormalized, resolveTable, normalizedNames) == "美化/N" &&
                    ModCategoryDatabase.Resolve(byNothing, resolveTable, normalizedNames) is null,
                "category resolve uses workshop, then packageId, then exact/normalized name");

            var templatePath = Path.Combine(root, "template.json");
            CategoryTemplateStore.SaveDocument(templatePath, new ModCategoryDocument
            {
                PackageId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["p.saved"] = "内容拓展/Saved"
                }
            }, "Mine");
            var savedTemplate = ModCategoryDatabase.Parse(File.ReadAllText(templatePath));
            Require(savedTemplate is { Version: 3, Title: "Mine" } &&
                    savedTemplate.PackageId.Count == 1 &&
                    savedTemplate.PackageId["p.saved"] == "内容拓展/Saved" &&
                    // '/' is the one character no platform allows in a file name.
                    CategoryTemplateStore.SanitizeFileName("a/b") == "a_b",
                "saved template round-trips and sanitizes file names");

            // Check custom folder & mod drag-reorder persistence in CategoryTree
            var customCatOrder = new[] { "种族/鼠族", "种族/米莉拉" };
            var customModOrder = new[] { "Valeries.miliraexpansion", "Ancot.MiliraRace" };
            var reorderedRows = CategoryTree.BuildRows(
                new[]
                {
                    new ModInfo("Milira Race", "A", "", "Ancot.MiliraRace", "", null, ModSource.Workshop, "1.6", DateTime.MinValue) { Category = "种族/米莉拉" },
                    new ModInfo("Milira Expansion", "A", "", "Valeries.miliraexpansion", "", null, ModSource.Workshop, "1.6", DateTime.MinValue) { Category = "种族/米莉拉" },
                    new ModInfo("NewRatkinPlus", "A", "", "Solaris.RatkinRaceMod", "", null, ModSource.Workshop, "1.6", DateTime.MinValue) { Category = "种族/鼠族" }
                },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                autoExpandAll: false,
                "未分类",
                customCatOrder,
                customModOrder);
            var subHeaders = reorderedRows.OfType<CategoryHeaderRow>().Where(h => h.Depth == 1).Select(h => h.PathKey).ToList();
            var visibleIds = reorderedRows.OfType<ModInfo>().Select(m => m.PackageId).ToList();
            Require(subHeaders.SequenceEqual(new[] { "种族/鼠族", "种族/米莉拉" }) &&
                    visibleIds.SequenceEqual(new[] { "Solaris.RatkinRaceMod", "Valeries.miliraexpansion", "Ancot.MiliraRace" }),
                "CategoryTree honors user drag-reordered folder and mod sequences");

            var miliraBase = new ModInfo("Milira Race", "A", "", "Ancot.MiliraRace", "", null, ModSource.Workshop, "1.6", DateTime.MinValue) { Category = "种族/米莉拉" };
            var miliraExp = new ModInfo("Milira Expansion", "A", "", "Valeries.miliraexpansion", "", null, ModSource.Workshop, "1.6", DateTime.MinValue) { Category = "种族/米莉拉" };
            var ratkinBase = new ModInfo("NewRatkinPlus", "A", "", "Solaris.RatkinRaceMod", "", null, ModSource.Workshop, "1.6", DateTime.MinValue) { Category = "种族/鼠族" };
            var plainMod = new ModInfo("Plain Mod", "A", "", "plain.mod", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var compactLong = ModInfo.FormatCompactCategory("系列/Sized Apparel for RJW");
            Require(miliraBase.HasCategory &&
                    ModInfo.FormatCompactCategory("种族/Monolyn") == "种族/Monolyn" &&
                    compactLong.StartsWith("Sized Apparel") &&
                    !compactLong.Contains("…/") &&
                    ModInfo.FormatCompactCategory("内容拓展/性能优化/Faster Game Loading") == "性能优化" &&
                    ModInfo.FormatCompactCategory("非内容拓展/操作便利/Find Trade") == "操作便利" &&
                    ModInfo.FormatCompactCategory("内容拓展/种族/NewRatkin/Sub") == "种族" &&
                    ModInfo.FormatCompactCategory("Combat Extended/内容拓展/Yuran") == "Yuran",
                "ModInfo shows the middle category level without …/ prefixes");
            var hoverRow = miliraBase with { Alias = "米", Category = "系列/Sized Apparel for RJW" };
            hoverRow.ShowOriginalName = true;
            Require(hoverRow.CategoryBadgeOpacity < 0.2 && hoverRow.NameColumnSpan == 2 &&
                    hoverRow.NameOpacity == 1 && hoverRow.AliasOpacity == 0,
                "hovering a mod name fades the category badge and gives the original name both columns");
            hoverRow.CategoryHovered = true;
            Require(hoverRow.CategoryBadgeOpacity == 1 && hoverRow.NameColumnSpan == 1 &&
                    hoverRow.CategoryBadgeText == hoverRow.CategoryText,
                "hovering the category side reveals the complete category in place");
            Require(!MainWindow.IsCategoryRegion(300, 199) &&
                    MainWindow.IsCategoryRegion(300, 200) &&
                    MainWindow.IsCategoryRegion(300, 300) &&
                    !MainWindow.IsCategoryRegion(0, 0),
                "the right third of a row is the category region, the left two thirds show the name");
            hoverRow.ShowOriginalName = false;
            hoverRow.CategoryHovered = false;
            Require(hoverRow.CategoryBadgeOpacity == 1 && hoverRow.NameColumnSpan == 1 &&
                    hoverRow.AliasOpacity == 1 && hoverRow.CategoryBadgeText == compactLong,
                "moving away restores the alias and compact category badge");

            var slotGrid = new Avalonia.Controls.Grid
            {
                ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("*,8,*,8,*")
            };
            var childA = new Avalonia.Controls.Border { Width = 520, Height = 40 };
            var childB = new Avalonia.Controls.Border { Width = 120, Height = 40 };
            var childC = new Avalonia.Controls.Border { Width = 340, Height = 40 };
            var slotA = new FixedStarSlot { Child = childA };
            var slotB = new FixedStarSlot { Child = childB };
            var slotC = new FixedStarSlot { Child = childC };
            Avalonia.Controls.Grid.SetColumn(slotA, 0);
            Avalonia.Controls.Grid.SetColumn(slotB, 2);
            Avalonia.Controls.Grid.SetColumn(slotC, 4);
            slotGrid.Children.AddRange(new Avalonia.Controls.Control[] { slotA, slotB, slotC });
            slotGrid.Measure(new Avalonia.Size(968, 400));
            slotGrid.Arrange(new Avalonia.Rect(0, 0, 968, 400));
            var initialWidths = slotGrid.ColumnDefinitions.Select(c => c.ActualWidth).ToArray();
            childC.Width = 220;
            slotGrid.Measure(new Avalonia.Size(968, 400));
            slotGrid.Arrange(new Avalonia.Rect(0, 0, 968, 400));
            Require(slotGrid.ColumnDefinitions.Select(c => c.ActualWidth).SequenceEqual(initialWidths) &&
                    slotA.DesiredSize.Width == 0 && slotC.DesiredSize.Width == 0,
                "FixedStarSlot isolates Star pane widths from child desired-width changes");

            var map = CategoryMap.Build(new[] { "种族/米莉拉", "种族/鼠族", "种族", "系列", "系列/A" });
            var mapLeaves = map.Children.Select(child => child.Leaf).ToList();
            // Order follows the current culture's collation, so compare against the
            // same comparer instead of a hard-coded sequence.
            Require(mapLeaves.SequenceEqual(mapLeaves.OrderBy(leaf => leaf, StringComparer.CurrentCultureIgnoreCase)) &&
                    map.Count == 5 &&
                    map.Children.First(child => child.Leaf == "种族").Count == 3,
                "category map builds a sorted tree and counts every path in each subtree");

            ModInfo WithCategory(string name, string id, string? category) =>
                new(name, "A", "", id, "", null, ModSource.Workshop, "1.6", DateTime.MinValue)
                {
                    Category = category
                };
            var modMap = CategoryMap.Build(
                new[] { "种族", "种族/米莉拉", "系列" },
                new[]
                {
                    WithCategory("Milira", "milira", "种族/米莉拉"),
                    WithCategory("Base", "base", "种族"),
                    WithCategory("Series", "series", "系列")
                });
            var raceNode = modMap.Children.First(child => child.Leaf == "种族");
            var miliraNode = raceNode.Children.First(child => child.Leaf == "米莉拉");
            var collected = CategoryMap.CollectMods(raceNode).ToList();
            Require(modMap.TotalMods == 3 && raceNode.TotalMods == 2 &&
                    raceNode.Mods.SequenceEqual(new[] { "Base" }) &&
                    miliraNode.TotalMods == 1 &&
                    collected.Select(entry => entry.Direct).SequenceEqual(new[] { true, false }),
                "category map counts direct and subtree mods and lists subfolder mods after the direct ones");

            var tableMap = CategoryMap.Build(
                new[] { "种族/米莉拉" },
                new[] { ("Milira", (string?)"种族/米莉拉"), ("Ratkin", (string?)"种族") });
            Require(tableMap.TotalMods == 2 &&
                    tableMap.Children.Single().Mods.Count == 1,
                "a category table can be mapped from name/category pairs alone");

            var bandRoot = CategoryMap.Build(
                Enumerable.Range(1, 9).Select(index => "组/A" + index).ToArray());
            var deepRoot = CategoryMap.Build(
                Enumerable.Range(1, 9).Select(index => "组/A" + index)
                    .Concat(new[] { "组/A1/子" }).ToArray());
            var openChild = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "组", "组/A1" };
            Require(CategoryMap.UsesBand(bandRoot.Children[0]) &&
                    CategoryMap.UsesBand(deepRoot.Children[0]),
                "a big level stays a drum even when one of its folders is open");

            var drumLayout = CategoryMap.Layout(deepRoot, openChild, _ => 100, new Dictionary<string, double>());
            var scrolledLayout = CategoryMap.Layout(
                deepRoot, openChild, _ => 100, new Dictionary<string, double> { ["组"] = 2 });
            Require(drumLayout.Placements.Any(item => item.Node.Path == "组/A1/子") &&
                    !scrolledLayout.Placements.Any(item => item.Node.Path == "组/A1/子") &&
                    scrolledLayout.Ghosts.Count > 0,
                "the centred drum item opens its subtree, and it collapses to a fading outline when scrolled away");

            var borrowRoot = CategoryMap.Build(
                new[] { "族/A1/甲", "族/B", "族/C1/丙" }
                    .Concat(Enumerable.Range(1, 6).Select(index => "族/D" + index)).ToArray());
            var borrowExpanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "", "族", "族/A1", "族/C1" };
            var centreLeaf = new Dictionary<string, double> { ["族"] = 1 };
            var bothOpen = CategoryMap.Layout(
                borrowRoot, borrowExpanded, _ => 100, centreLeaf, null,
                new Dictionary<string, long> { ["族/A1"] = 1, ["族/C1"] = 2 });
            var pickedOne = CategoryMap.Layout(
                borrowRoot, borrowExpanded, _ => 100, centreLeaf, "族/A1",
                new Dictionary<string, long> { ["族/A1"] = 1, ["族/C1"] = 2 });
            var reopenedOne = CategoryMap.Layout(
                borrowRoot, borrowExpanded, _ => 100, centreLeaf, null,
                new Dictionary<string, long> { ["族/A1"] = 2, ["族/C1"] = 1 });
            var onlyUp = CategoryMap.Layout(
                borrowRoot,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "族", "族/A1" },
                _ => 100, centreLeaf);
            Require(bothOpen.Placements.Any(item => item.Node.Path == "族/C1/丙") &&
                    !bothOpen.Placements.Any(item => item.Node.Path == "族/A1/甲") &&
                    pickedOne.Placements.Any(item => item.Node.Path == "族/A1/甲") &&
                    reopenedOne.Placements.Any(item => item.Node.Path == "族/A1/甲") &&
                    onlyUp.Placements.Any(item => item.Node.Path == "族/A1/甲"),
                "a drum stopped on a leaf lends its subtree to an expanded neighbour, picking the selected one or the one opened last");

            var expandedMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "组" };
            var bandLayout = CategoryMap.Layout(bandRoot, expandedMap, _ => 100, new Dictionary<string, double>());
            var shelf = bandLayout.Placements.Where(item => item.BandItem).ToList();
            var rotated = CategoryMap.Layout(
                bandRoot, expandedMap, _ => 100, new Dictionary<string, double> { ["组"] = 8 });
            var rotatedShelf = rotated.Placements.Where(item => item.BandItem).ToList();
            var widest = shelf.OrderByDescending(item => item.Bounds.Width).First();
            var narrowest = shelf.OrderBy(item => item.Bounds.Width).First();
            var zone = bandLayout.Zones.Single();
            var gapPoint = new Avalonia.Point(
                zone.Bounds.Center.X,
                (shelf.First().Bounds.Bottom + shelf.Last().Bounds.Top) / 2);
            Require(shelf.Count is > 0 and <= CategoryMap.BandVisible &&
                    widest.Bounds.Width > narrowest.Bounds.Width &&
                    rotatedShelf.Select(item => item.Node.Leaf).First() != shelf.Select(item => item.Node.Leaf).First() &&
                    zone.Bounds.Contains(gapPoint),
                $"drum tapers towards its ends, rotates, and swallows the wheel between items (count={shelf.Count}, gap={gapPoint})");
            var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "种族/鼠族" };
            var treeRows = CategoryTree.BuildRows(new[] { miliraBase, miliraExp, ratkinBase, plainMod }, collapsed, autoExpandAll: false, "未分类");
            var headers = treeRows.OfType<CategoryHeaderRow>().ToList();
            Require(headers.Any(h => h.PathKey == "种族" && h.Depth == 0 && h.TotalModCount == 3 && h.IsExpanded) &&
                    headers.Any(h => h.PathKey == "种族/米莉拉" && h.Depth == 1 && h.TotalModCount == 2 && h.IsExpanded) &&
                    headers.Any(h => h.PathKey == "种族/鼠族" && h.Depth == 1 && h.TotalModCount == 1 && !h.IsExpanded) &&
                    headers.Any(h => h.PathKey == CategoryTree.UncategorizedKey && h.TotalModCount == 1) &&
                    treeRows.OfType<ModInfo>().All(m => !m.HasCategory) &&
                    treeRows.OfType<ModInfo>().Select(m => m.PackageId).SequenceEqual(new[] { "Valeries.miliraexpansion", "Ancot.MiliraRace", "plain.mod" }),
                "CategoryTree builds multi-level headers with rolled-up counts, hides collapsed subtrees, and suppresses duplicate row badges inside tree view");

            var frameRows = CategoryTree.BuildRows(new[] { miliraBase, ratkinBase, plainMod with { Category = "种族" } },
                collapsed, autoExpandAll: false, "未分类");
            Require(frameRows.OfType<CategoryHeaderRow>().Single(h => h.PathKey == "种族").CategoryFrames
                        .SequenceEqual(new[] { new CategoryFrameLayer(0, true, false) }) &&
                    frameRows.OfType<ModInfo>().Single(m => m.PackageId == miliraBase.PackageId).CategoryFrames!
                        .SequenceEqual(new[] { new CategoryFrameLayer(0, false, false), new CategoryFrameLayer(1, false, true) }) &&
                    frameRows.OfType<CategoryHeaderRow>().Single(h => h.PathKey == "种族/鼠族").CategoryFrames
                        .SequenceEqual(new[] { new CategoryFrameLayer(0, false, false), new CategoryFrameLayer(1, true, true) }) &&
                    frameRows.OfType<ModInfo>().Single(m => m.PackageId == plainMod.PackageId).CategoryFrames!
                        .SequenceEqual(new[] { new CategoryFrameLayer(0, false, true) }),
                "expanded frames end at the last owned row and collapsed subfolders retain their own frame");

            ModNoteStore.SetCategory(root, new[] { "a.mod", "c.mod" }, "批量");
            Require(ModNoteStore.Get(root, "a.mod")?.Category == "批量" &&
                    ModNoteStore.Get(root, "a.mod")?.Alias == "别名",
                "batch category keeps the alias");
            ModNoteStore.SetCategory(root, new[] { "a.mod", "b.mod", "c.mod", "d.mod" }, null);
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

            ModNoteStore.ToggleIgnoredPrerequisite(root, featureMod, framework.PackageId.ToUpperInvariant());
            ModNoteStore.Set(root, featureMod.PackageId, null, "试验分类");
            ModNoteStore.ClearAllCategories(root);
            ModNoteStore.Invalidate();
            var ignoredFeature = featureMod with
            {
                IgnoredPrerequisites = ModNoteStore.Get(root, "TEST.FEATURE")!.IgnoredPrerequisites!.ToHashSet(StringComparer.OrdinalIgnoreCase),
                LoadAfter = new() { enabledTarget.PackageId }
            };
            ModInfo? ResolveInstalled(string id) => installedMap.GetValueOrDefault(id);
            var ignoredScan = AutoSortPrerequisites.Scan(new[] { ignoredFeature }, ResolveInstalled, allInstalled);
            Require(ignoredFeature.IsPrerequisiteIgnored(framework.PackageId) && ignoredFeature.ModDependencies.Count == 2 &&
                    ignoredScan.InstalledMissing.Count == 0 && ignoredScan.UninstalledMissing.Count == 1 &&
                    ignoredScan.UninstalledMissing[0].PackageId == "missing.uninstalled",
                "ignoring one prerequisite survives category edits and reload without hiding other requirements");

            var maskedFramework = framework with { IgnoredPrerequisites = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { harmony.PackageId } };
            var transitiveScan = AutoSortPrerequisites.Scan(new[] { featureMod },
                id => id == maskedFramework.PackageId ? maskedFramework : ResolveInstalled(id), allInstalled);
            Require(transitiveScan.InstalledMissing.Select(item => item.PackageId).SequenceEqual(new[] { framework.PackageId }),
                "ignoring a prerequisite's own requirements does not remove other mods' dependency on it");

            var maskedActive = new[] { ignoredFeature, framework, enabledTarget };
            var maskedRules = AutoSortPrerequisites.CollectSortRules(maskedActive, "zh", useCommunityRules: false);
            Require(!maskedRules.Rules.Any(rule => rule.AfterId == ignoredFeature.PackageId && rule.Kind == SortRuleKind.Dependency) &&
                    maskedRules.Rules.Any(rule => rule.AfterId == ignoredFeature.PackageId && rule.BeforeId == enabledTarget.PackageId &&
                        rule.Kind == SortRuleKind.LoadAfter) &&
                    AutoSortPrerequisites.InsertPrerequisites(new[] { featureMod.PackageId }, new[] { framework.PackageId },
                        new[] { ignoredFeature }, ResolveInstalled, allInstalled).SequenceEqual(new[] { featureMod.PackageId, framework.PackageId }),
                "ignored hard dependencies no longer drive sorting or prerequisite insertion, but explicit load order still does");

            ModNoteStore.ToggleIgnoredPrerequisite(root, featureMod, framework.PackageId);
            ModNoteStore.Invalidate();
            Require(ModNoteStore.Get(root, featureMod.PackageId) is null &&
                    AutoSortPrerequisites.Scan(new[] { featureMod }, ResolveInstalled, allInstalled).InstalledMissing.Count == 2,
                "restoring hard prerequisites removes an otherwise empty note and resumes normal scanning");

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

            // Implicit RJW rule check (standalone "rjw" in Name, Alias/RowName, PackageId, or Folder name; ignores substrings like "marjwana")
            var rjwAddonById = new ModInfo("Events Addon", "A", @"C:\Mods\12345", "c0ffee.rjw.events", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var rjwAddonByName = new ModInfo("[RJW] Animations", "A", @"C:\Mods\23456", "author.animpack", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var rjwAddonByFolder = new ModInfo("Custom Addon", "A", @"C:\Mods\RJW_Extra_Content\", "author.custompack", "", null, ModSource.Local, "1.6", DateTime.MinValue);
            var rjwAddonByAlias = new ModInfo("Mysterious Submod", "A", @"C:\Mods\99999", "author.mysterious", "", null, ModSource.Workshop, "1.6", DateTime.MinValue) { Alias = "[RJW] 汉化备注" };
            var falsePositiveSubstr = new ModInfo("Marjwana Plants", "A", @"C:\Mods\marjwanamod", "author.marjwanaplants", "", null, ModSource.Workshop, "1.6", DateTime.MinValue);
            var rjwCore = new ModInfo("RimJobWorld", "A", @"C:\Mods\rim.job.world", "rim.job.world", "", null, ModSource.Local, "1.6", DateTime.MinValue);
            var rjwList = new List<ModInfo> { core, rjwAddonById, rjwAddonByName, rjwAddonByFolder, rjwAddonByAlias, falsePositiveSubstr, rjwCore };
            var rjwRules = AutoSortPrerequisites.CollectSortRules(rjwList, "zh", useCommunityRules: true);
            var rjwPlan = AutoSortPrerequisites.BuildTopologicalOrder(rjwList.Select(m => m.PackageId).ToList(), rjwList, rjwRules);
            int RjwPos(string id) => rjwPlan.OrderedPackageIds.ToList().FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            Require(rjwRules.ImplicitRjwRuleCount == 4 &&
                    !AutoSortPrerequisites.HasStandaloneRjwToken(falsePositiveSubstr) &&
                    RjwPos("author.marjwanaplants") < RjwPos("rim.job.world") &&
                    RjwPos("rim.job.world") < RjwPos("c0ffee.rjw.events") &&
                    RjwPos("rim.job.world") < RjwPos("author.animpack") &&
                    RjwPos("rim.job.world") < RjwPos("author.custompack") &&
                    RjwPos("rim.job.world") < RjwPos("author.mysterious"),
                "Implicit RJW rule places standalone 'rjw' mods (name/alias/packageId/folder) after rim.job.world while ignoring partial substrings");

            // Gitee-first + overseas fallback + stale mirror non-interception checks
            var rulesCacheDir = Path.Combine(root, "rules-cache");
            var requestedUrls = new List<string>();
            using var mirrorHandler = new MirrorTestHandler((req, _) =>
            {
                var u = req.RequestUri!.AbsoluteUri;
                requestedUrls.Add(u);
                if (u == CommunityRulesCache.GiteeMirrorRulesUrl)
                {
                    // Stale Gitee mirror (timestamp 100 < bundled 500) must NOT block GitHub check
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"timestamp":100,"rules":{"stale.mod":{"loadBottom":true}}}""", Encoding.UTF8, "application/json")
                    });
                }
                if (u == CommunityRulesCache.GitHubRulesUrl)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"timestamp":900,"rules":{"fresh.mod":{"loadBottom":true}}}""", Encoding.UTF8, "application/json")
                    });
                }
                if (u == WorkshopMapCache.GiteeMirrorMapUrl)
                {
                    // Overseas user cannot reach Gitee (timeout/403) -> falls back to GitHub
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                }
                if (u == WorkshopMapCache.GitHubMapUrl)
                {
                    using var gzOut = new MemoryStream();
                    using (var gz = new GZipStream(gzOut, CompressionLevel.Fastest, leaveOpen: true))
                    {
                        gz.Write("""{"version":900,"mods":{"overseas.mod":123456}}"""u8);
                    }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(gzOut.ToArray())
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });
            using var mirrorClient = new HttpClient(mirrorHandler);
            var testRulesCache = new CommunityRulesCache(
                mirrorClient,
                rulesCacheDir,
                """{"timestamp":500,"rules":{"bundled.mod":{"loadBottom":true}}}""");
            var refreshedRules = testRulesCache.RefreshAsync().GetAwaiter().GetResult();
            Require(requestedUrls.SequenceEqual(new[] { CommunityRulesCache.GiteeMirrorRulesUrl, CommunityRulesCache.GitHubRulesUrl }) &&
                    refreshedRules.Timestamp == 900 &&
                    refreshedRules.Rules.ContainsKey("fresh.mod"),
                "CommunityRulesCache tries Gitee first and falls through stale Gitee mirror to newer GitHub snapshot");

            requestedUrls.Clear();
            byte[] bundledMapGz;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gz.Write("""{"version":500,"mods":{"bundled.mod":111}}"""u8);
                }
                bundledMapGz = ms.ToArray();
            }
            var testMapCache = new WorkshopMapCache(mirrorClient, rulesCacheDir, bundledMapGz);
            var refreshedMap = testMapCache.RefreshAsync().GetAwaiter().GetResult();
            Require(requestedUrls.SequenceEqual(new[] { WorkshopMapCache.GiteeMirrorMapUrl, WorkshopMapCache.GitHubMapUrl }) &&
                    refreshedMap.Version == 900 &&
                    testMapCache.TryGetWorkshopId("overseas.mod") == 123456UL,
                "WorkshopMapCache tries Gitee first and falls back cleanly to GitHub when Gitee is unreachable");
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

    private sealed class MirrorTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
