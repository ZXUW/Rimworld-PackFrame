using System.Globalization;
using System.Text;
using PackFrame;

internal static class WorkshopLocalStateChecks
{
    public static async Task Run()
    {
        using var fixture = new Fixture();
        fixture.Manifest(fixture.Root, true, new Item(11, 0, 101), new Item(12, 0, 102));
        fixture.Stage(11);
        fixture.ContentLog("AppID 294100 state changed : Fully Installed,Update Queued,Update Running,",
            "AppID 294100 Workshop update changed : Running Update,Downloading,Staging,",
            "Downloading 20 chunks for depot 294100 (101)");
        var snapshot = await fixture.Query(11, 12, 99);
        Require(snapshot!.Items[11].IsDownloading && !snapshot.Items[11].IsQueued &&
                snapshot.Items[12].IsQueued && snapshot.Items[99].State == WorkshopItemState.None,
            "exact manifest transfer evidence identifies the active item and preserves other queued and absent items");
        Require(snapshot.Items.Values.All(item => item.BytesDownloaded == 0 && item.BytesTotal == 0),
            "passive status never invents a percentage from staged or preallocated file sizes");

        fixture.Content(11);
        snapshot = await fixture.Query(11);
        Require(!snapshot!.Items[11].IsInstalled,
            "an About.xml file appearing before Steam commits its manifest is not a completed download");
        fixture.ContentLog("AppID 294100 state changed : Fully Installed,Update Queued,",
            "AppID 294100 Workshop update changed : None");
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsQueued,
            "a paused or stopped transfer remains pending and is not reported as actively transferring");
        snapshot = await fixture.QueryWithSteamStart(null, 11);
        Require(snapshot!.Items[11].IsQueued,
            "staging plus Steam's pending flag can describe waiting work while a stopped client cannot prove an active transfer");

        fixture.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,",
            "Downloading 20 chunks for depot 294100 (101)");
        snapshot = await fixture.QueryWithSteamStart(DateTime.UtcNow, 11);
        Require(snapshot!.Items[11].IsQueued,
            "transfer messages from an earlier Steam process cannot report downloading after a client restart");
        fixture.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,",
            "Downloading 20 chunks for depot 294100 (999)");
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsQueued,
            "another Workshop item's transfer does not turn every pending item into an active download");

        fixture.Manifest(fixture.Root, false, new Item(11, 100, 101));
        fixture.ContentLog();
        snapshot = await fixture.Query(11);
        Require(snapshot is not null && !snapshot.Items.ContainsKey(11),
            "an installed but outdated manifest and old staging files alone remain unknown");
        fixture.Manifest(fixture.Root, false, new Item(11, 101, 101));
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsInstalled && snapshot.Items[11].InstalledManifest == 101,
            "matching committed and latest manifests plus readable installed content authorize completion");
        fixture.Manifest(fixture.Root, false, new Item(13, 103, 103));
        snapshot = await fixture.Query(13);
        Require(snapshot is not null && !snapshot.Items.ContainsKey(13),
            "matching manifests without readable committed content cannot report completion");
        fixture.Manifest(fixture.Root, true, new Item(13, 103, 103));
        fixture.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,",
            "Downloading 20 chunks for depot 294100 (103)");
        snapshot = await fixture.Query(13);
        Require(snapshot is not null && !snapshot.Items.ContainsKey(13),
            "an old active log cannot label already committed but unreadable content as downloading");
        fixture.Manifest(fixture.Root, false, new Item(13, 0, 103));
        snapshot = await fixture.Query(13);
        Require(snapshot is not null && !snapshot.Items.ContainsKey(13),
            "an old active log cannot report downloading after Steam clears its pending-download flag");

        fixture.Manifest(fixture.Root, true, new Item(11, 101, 101), new Item(12, 0, 102));
        fixture.ContentLog("AppID 294100 state changed : Fully Installed,Update Queued,Update Running,",
            "AppID 294100 Workshop update changed : Running Update,Downloading,Staging,",
            "Downloading 20 chunks for depot 294100 (101)",
            "Downloading 20 chunks for depot 294100 (102)");
        snapshot = await fixture.Query(11, 12);
        Require(snapshot!.Items[11].IsInstalled && snapshot.Items[12].IsDownloading && !snapshot.Items[12].IsInstalled,
            "one committed item completes while another item in the same Workshop job keeps downloading");

        fixture.Manifest(fixture.Root, true, new Item(11, 0, 101), new Item(12, 0, 102));
        snapshot = await fixture.Query(11, 12);
        Require(!snapshot!.Items[11].IsQueued && !snapshot.Items[12].IsQueued,
            "a batch's exact per-manifest transfer messages can identify multiple active items");
        fixture.WorkshopLog("[AppID 294100] Get details for item 11 failed : Access Denied");
        snapshot = await fixture.Query(11, 12);
        Require(!snapshot!.Items.ContainsKey(11) && snapshot.Items[12].IsDownloading,
            "an item-specific failure prevents a false downloading label without hiding unrelated transfers");
        fixture.WorkshopLog("[AppID 294100] Get details for item 11 failed : Access Denied",
            "[AppID 294100] Detected workshop change : added subscribed item 11");
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsDownloading,
            "a later subscription change allows new evidence after a previous item failure");

        fixture.WorkshopLog();
        fixture.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,",
            "Downloading 20 chunks for depot 294100 (101)",
            "AppID 294100 scheduler finished : staying in schedule (result Suspended, state 0x200c)");
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsQueued,
            "a later suspended scheduler event clears active transfer evidence");
        fixture.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,");
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsQueued,
            "a rotated log tail without an exact manifest message cannot fabricate an active item");
        File.WriteAllText(fixture.ContentLogPath, "[bad time] AppID 294100 Workshop update changed : Running Update,Downloading,\nDownloading 20 chunks for depot 294100 (101)");
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsQueued,
            "malformed log timestamps cannot create active transfer evidence");
        fixture.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,",
            "Downloading 20 chunks for depot 294100 (101)");
        File.AppendAllText(fixture.ContentLogPath, "[bad time] AppID 294100 Workshop update changed : None\n");
        snapshot = await fixture.Query(11);
        Require(snapshot!.Items[11].IsQueued,
            "an unreadable later state cannot keep an earlier transfer labeled active");

        fixture.Manifest(fixture.Root, false, new Item(12, 0, 102, false));
        snapshot = await fixture.Query(11, 12, 99);
        Require(snapshot is not null && !snapshot.Items.ContainsKey(11) &&
                snapshot.Items[12].State == WorkshopItemState.None && snapshot.Items[99].State == WorkshopItemState.None,
            "orphaned staging stays unknown while an explicitly unsubscribed or absent item is missing");

        var other = Path.Combine(fixture.DirectoryPath, "other-library", "steamapps", "workshop");
        fixture.Manifest(other, false, new Item(11, 100, 100));
        fixture.Content(11, other);
        File.SetLastWriteTimeUtc(Path.Combine(other, "appworkshop_294100.acf"), DateTime.UtcNow.AddHours(-1));
        fixture.Manifest(fixture.Root, true, new Item(11, 0, 101));
        fixture.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,",
            "Downloading 20 chunks for depot 294100 (101)");
        snapshot = await SteamWorkshopLocalStateReader.QueryFromPathsAsync(new[] { other, fixture.Root },
            new[] { fixture.ContentLogPath }, new[] { fixture.WorkshopLogPath }, fixture.StartedUtc, new ulong[] { 11 });
        Require(snapshot!.Items[11].IsDownloading && !snapshot.Items[11].IsInstalled,
            "a stale copy in another Steam library does not mask the current library's unfinished download");

        using (var fresh = new Fixture())
        {
            fresh.Manifest(fresh.Root, false);
            File.SetLastWriteTimeUtc(Path.Combine(fresh.Root, "appworkshop_294100.acf"), DateTime.UtcNow.AddMinutes(-2));
            fresh.Stage(21);
            fresh.Stage(22);
            fresh.WorkshopLog("[AppID 294100] Subscribed to item 21 @ fixture",
                "[AppID 294100] Detected workshop change : added subscribed item 21",
                "[AppID 294100] Detected workshop change (latest from server) : changed cached item 21, new manifest 201");
            fresh.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,Staging,",
                "Downloading 20 chunks for depot 294100 (201)");
            var pending = await fresh.Query(21, 22);
            Require(pending!.Items[21].IsDownloading && !pending.Items[21].IsQueued &&
                    !pending.Items.ContainsKey(22),
                "fresh subscription and exact manifest transfer logs bridge an ACF that has not saved the new item yet");
            fresh.ContentLog("AppID 294100 Workshop update changed : None");
            pending = await fresh.Query(21);
            Require(pending!.Items[21].IsQueued,
                "a stopped transfer with fresh item-specific subscription, manifest and staging evidence remains queued");
            fresh.WorkshopLog("[AppID 294100] Subscribed to item 21 @ fixture",
                "[AppID 294100] Detected workshop change (latest from server) : changed cached item 21, new manifest 201",
                "[AppID 294100] Unsubscribed from item 21 @ fixture");
            pending = await fresh.Query(21);
            Require(pending is not null && !pending.Items.ContainsKey(21),
                "a later unsubscribe disables the unsaved-ACF download fallback even when staging files remain");

            fresh.Manifest(fresh.Root, false, new Item(21, 201, 201));
            fresh.Content(21);
            File.SetLastWriteTimeUtc(Path.Combine(fresh.Root, "appworkshop_294100.acf"), DateTime.UtcNow.AddMinutes(-2));
            fresh.WorkshopLog("[AppID 294100] Detected workshop change (latest from server) : changed cached item 21, new manifest 202");
            fresh.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,Staging,",
                "Downloading 20 chunks for depot 294100 (202)");
            pending = await fresh.Query(21);
            Require(pending!.Items[21].IsDownloading && !pending.Items[21].IsInstalled && pending.Items[21].NeedsUpdate,
                "a newer logged manifest prevents an old committed ACF copy from masking an active update");
            fresh.WorkshopLog("[AppID 294100] Detected workshop change (latest from server) : changed cached item 21, new manifest 202",
                "[AppID 294100] Get details for item 21 failed : Access Denied");
            pending = await fresh.Query(21);
            Require(pending is not null && !pending.Items.ContainsKey(21),
                "an item-specific failure disables a fresh logged download until a later retry supplies evidence");
            fresh.WorkshopLog("[AppID 294100] Detected workshop change (latest from server) : changed cached item 21, new manifest 202");
            fresh.Manifest(fresh.Root, false, new Item(21, 202, 202));
            pending = await fresh.Query(21);
            Require(pending!.Items[21].IsInstalled && !pending.Items[21].IsDownloading,
                "completion still requires Steam to persist the new installed/latest manifests and readable content");
        }

        using (var sameSecond = new Fixture())
        {
            var second = DateTime.UtcNow.AddMinutes(-1);
            second = second.AddTicks(-(second.Ticks % TimeSpan.TicksPerSecond));
            var manifestPath = Path.Combine(sameSecond.Root, "appworkshop_294100.acf");
            sameSecond.Manifest(sameSecond.Root, false, new Item(31, 301, 301));
            sameSecond.Content(31);
            sameSecond.Stage(31);
            File.SetLastWriteTimeUtc(manifestPath, second.AddMilliseconds(750));
            sameSecond.WorkshopLogAt(second,
                "[AppID 294100] Detected workshop change : changed cached item 31, new manifest 302");
            sameSecond.ContentLog("AppID 294100 Workshop update changed : Running Update,Downloading,",
                "Downloading 20 chunks for depot 294100 (302)");
            var pending = await sameSecond.Query(31);
            Require(pending!.Items[31].IsDownloading && !pending.Items[31].IsInstalled &&
                    pending.Items[31].InstalledManifest == 301,
                "whole-second log evidence can supersede a fractional ACF timestamp in the same second");

            File.SetLastWriteTimeUtc(manifestPath, second.AddSeconds(1).AddMilliseconds(250));
            pending = await sameSecond.Query(31);
            Require(pending!.Items[31].IsInstalled && pending.Items[31].InstalledManifest == 301,
                "a log from an earlier second cannot override a later committed manifest");

            sameSecond.WorkshopLogAt(second, "[AppID 294100] Unsubscribed from item 31 @ fixture");
            File.SetLastWriteTimeUtc(manifestPath, second.AddMilliseconds(750));
            pending = await sameSecond.Query(31);
            Require(pending is not null && !pending.Items.ContainsKey(31),
                "a same-second unsubscribe cannot be hidden by fractional ACF timestamp precision");

            sameSecond.Manifest(sameSecond.Root, false, new Item(31, 301, 301, false));
            File.SetLastWriteTimeUtc(manifestPath, second.AddMilliseconds(750));
            sameSecond.WorkshopLogAt(second,
                "[AppID 294100] Subscribed to item 31 @ fixture",
                "[AppID 294100] Detected workshop change : changed cached item 31, new manifest 302");
            pending = await sameSecond.Query(31);
            Require(pending!.Items[31].IsSubscribed && pending.Items[31].IsDownloading,
                "a same-second resubscription supplies pending evidence despite an old unsubscribed ACF");

            sameSecond.Manifest(sameSecond.Root, true, new Item(31, 301, 302));
            File.SetLastWriteTimeUtc(manifestPath, second.AddMilliseconds(750));
            sameSecond.WorkshopLogAt(second,
                "[AppID 294100] Detected workshop change : changed cached item 31, new manifest 301");
            pending = await sameSecond.Query(31);
            Require(pending!.Items[31].IsDownloading && !pending.Items[31].IsInstalled,
                "an older same-second log cannot erase a newer pending ACF target or report false completion");

            sameSecond.Manifest(sameSecond.Root, true, new Item(31, 301, 303));
            File.SetLastWriteTimeUtc(manifestPath, second.AddMilliseconds(750));
            sameSecond.WorkshopLogAt(second,
                "[AppID 294100] Detected workshop change : changed cached item 31, new manifest 302");
            pending = await sameSecond.Query(31);
            Require(pending is not null && !pending.Items.ContainsKey(31),
                "conflicting replacement manifests in the same second remain unknown instead of guessing their order");

            sameSecond.Manifest(sameSecond.Root, false, new Item(31, 302, 302));
            File.SetLastWriteTimeUtc(manifestPath, second.AddMilliseconds(750));
            pending = await sameSecond.Query(31);
            Require(pending!.Items[31].IsInstalled && pending.Items[31].InstalledManifest == 302,
                "matching committed and logged manifests complete even within the same second");
        }

        using (var busy = new FileStream(Path.Combine(fixture.Root, "appworkshop_294100.acf"),
                   FileMode.Open, FileAccess.Read, FileShare.None))
            Require(await fixture.Query(11) is null,
                "a busy Steam manifest stays unavailable instead of reporting missing or interrupting its writer");

        File.WriteAllText(Path.Combine(fixture.Root, "appworkshop_294100.acf"), "\"AppWorkshop\" { \"appid\" \"294100\"");
        Require(await fixture.Query(11) is null,
            "a partially replaced or malformed ACF remains unavailable until Steam finishes writing it");
        File.WriteAllText(Path.Combine(fixture.Root, "appworkshop_294100.acf"), new string('x', SteamWorkshopLocalStateReader.MaximumManifestBytes + 1));
        Require(await fixture.Query(11) is null,
            "oversized Steam metadata is rejected within the reader's memory bound");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await SteamWorkshopLocalStateReader.QueryFromPathsAsync(new[] { fixture.Root }, Array.Empty<string>(),
                Array.Empty<string>(), null, new ulong[] { 11 }, cancelled.Token);
            throw new Exception("FAIL: cancelled passive Workshop query continued");
        }
        catch (OperationCanceledException) { Require(true, "passive Workshop reads honor cancellation before accessing files"); }
    }

    private sealed record Item(ulong Id, ulong Installed, ulong Latest, bool Subscribed = true);
    private sealed class Fixture : IDisposable
    {
        internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "PackFrame-local-workshop-" + Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(DirectoryPath, "library", "steamapps", "workshop");
        internal string ContentLogPath => Path.Combine(DirectoryPath, "content_log.txt");
        internal string WorkshopLogPath => Path.Combine(DirectoryPath, "workshop_log.txt");
        internal DateTime StartedUtc { get; } = DateTime.UtcNow.AddMinutes(-5);

        internal Fixture() { Directory.CreateDirectory(Root); }
        internal void Manifest(string root, bool needsDownload, params Item[] items)
        {
            Directory.CreateDirectory(root);
            var text = new StringBuilder("\"AppWorkshop\" { \"appid\" \"294100\" \"NeedsDownload\" \"")
                .Append(needsDownload ? "1" : "0").Append("\" \"WorkshopItemsInstalled\" {");
            foreach (var item in items.Where(item => item.Installed != 0))
                text.Append(CultureInfo.InvariantCulture, $" \"{item.Id}\" {{ \"manifest\" \"{item.Installed}\" }}");
            text.Append(" } \"WorkshopItemDetails\" {");
            foreach (var item in items)
                text.Append(CultureInfo.InvariantCulture, $" \"{item.Id}\" {{ \"manifest\" \"{item.Latest}\" \"latest_manifest\" \"{item.Latest}\" \"subscribedby\" \"{(item.Subscribed ? 123 : 0)}\" }}");
            text.Append(" } }");
            File.WriteAllText(Path.Combine(root, "appworkshop_294100.acf"), text.ToString());
        }
        internal void Content(ulong id, string? root = null)
        {
            var about = Path.Combine(root ?? Root, "content", "294100", id.ToString(CultureInfo.InvariantCulture), "About");
            Directory.CreateDirectory(about);
            File.WriteAllText(Path.Combine(about, "About.xml"), "<ModMetaData><name>Fixture</name></ModMetaData>");
        }
        internal void Stage(ulong id) => Directory.CreateDirectory(Path.Combine(Root, "downloads", "294100", id.ToString(CultureInfo.InvariantCulture)));
        internal void ContentLog(params string[] lines) => WriteLog(ContentLogPath, lines);
        internal void WorkshopLog(params string[] lines) => WriteLog(WorkshopLogPath, lines);
        internal void WorkshopLogAt(DateTime timestampUtc, params string[] lines)
        {
            var timestamp = timestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllLines(WorkshopLogPath, lines.Select(line => $"[{timestamp}] {line}"));
        }
        private static void WriteLog(string path, string[] lines)
        {
            var timestamp = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllLines(path, lines.Select(line => $"[{timestamp}] {line}"));
        }
        internal Task<WorkshopDownloadSnapshot?> Query(params ulong[] ids) => QueryWithSteamStart(StartedUtc, ids);
        internal Task<WorkshopDownloadSnapshot?> QueryWithSteamStart(DateTime? started, params ulong[] ids) =>
            SteamWorkshopLocalStateReader.QueryFromPathsAsync(new[] { Root }, new[] { ContentLogPath },
                new[] { WorkshopLogPath }, started, ids);
        public void Dispose()
        {
            var path = Path.GetFullPath(DirectoryPath);
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(path) == parent && Path.GetFileName(path).StartsWith("PackFrame-local-workshop-", StringComparison.Ordinal))
                Directory.Delete(path, true);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
