using PackFrame;

internal static class WorkshopTrackingChecks
{
    public static void Run()
    {
        const ulong first = 11;
        const ulong second = 12;
        var tracking = new WorkshopDownloadTracking();
        Require(!tracking.HasQueried && !tracking.Available && tracking.ReadyToRefresh.Count == 0 &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.CheckingSteam,
            "an unresolved Workshop mod is checking Steam before the first state query");

        tracking.Observe(null);
        Require(tracking.HasQueried && !tracking.Available && tracking.ReadyToRefresh.Count == 0 &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Unknown,
            "unavailable Steam status remains unknown rather than falsely downloading or missing");

        var downloading = Item(first, WorkshopItemState.Subscribed | WorkshopItemState.NeedsUpdate |
            WorkshopItemState.Downloading, 20, 100);
        var queued = Item(second, WorkshopItemState.Subscribed | WorkshopItemState.DownloadPending);
        tracking.Observe(Snapshot(downloading, queued));
        Require(tracking.Available &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Downloading &&
                tracking.Availability(second, foundOnDisk: false) == WorkshopModAvailability.Queued &&
                tracking.ReadyToRefresh.Count == 0,
            "active and queued Workshop items show their Steam download states independently");
        Require(tracking.Item(first)?.BytesDownloaded == 20 && tracking.Item(first)?.BytesTotal == 100 &&
                tracking.Item(99) is null &&
                tracking.Availability(99, foundOnDisk: false) == WorkshopModAvailability.Unknown,
            "download tracking exposes verified progress while absent query entries stay unknown");

        tracking.Observe(Snapshot(downloading with { BytesDownloaded = 100 }, queued));
        Require(tracking.ReadyToRefresh.Count == 0 &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Downloading,
            "100 percent transferred bytes cannot finish a Workshop item whose downloading flag remains set");

        var needsUpdate = Item(first, WorkshopItemState.Subscribed | WorkshopItemState.Installed |
            WorkshopItemState.NeedsUpdate);
        tracking.Observe(Snapshot(needsUpdate, queued));
        Require(tracking.ReadyToRefresh.Count == 0 &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Unknown,
            "an installed item needing an update is neither falsely downloading nor ready for completion refresh");

        var installed = Item(first, WorkshopItemState.Subscribed | WorkshopItemState.Installed, 100, 100);
        tracking.Observe(Snapshot(installed, queued));
        Require(tracking.ReadyToRefresh.SequenceEqual(new[] { first }) &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Completing &&
                tracking.Availability(second, foundOnDisk: false) == WorkshopModAvailability.Queued,
            "Steam's installed transition requests a catalogue refresh while other items may remain queued");

        // The UI can be dragging or scanning: merely reading the pending set or
        // receiving another installed snapshot must not acknowledge a rescan.
        var pending = tracking.ReadyToRefresh.ToArray();
        tracking.Observe(Snapshot(installed, queued));
        tracking.Refreshed(Array.Empty<ulong>(), Array.Empty<ulong>());
        Require(pending.SequenceEqual(new[] { first }) &&
                tracking.ReadyToRefresh.SequenceEqual(new[] { first }) &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Completing,
            "busy UI cycles retain a Workshop completion until the catalogue rescan is explicitly acknowledged");

        tracking.Observe(null);
        Require(tracking.ReadyToRefresh.Count == 0 &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Unknown,
            "a temporary Steam outage does not authorize a refresh from stale installed state");
        tracking.Observe(Snapshot(installed, queued));
        Require(tracking.ReadyToRefresh.SequenceEqual(new[] { first }),
            "pending completion resumes after Steam becomes available again without being lost");

        tracking.Refreshed(new[] { first }, Array.Empty<ulong>());
        tracking.Observe(Snapshot(installed, queued));
        Require(tracking.ReadyToRefresh.SequenceEqual(new[] { first }) &&
                tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Completing,
            "a completed Workshop item unreadable in the first applied scan remains eligible for retry");
        tracking.Refreshed(new[] { first }, new[] { first });
        tracking.Observe(Snapshot(installed, queued));
        Require(tracking.ReadyToRefresh.Count == 0,
            "a later scan that discovers the completed Workshop item acknowledges its pending refresh");

        tracking.Observe(Snapshot(downloading with { BytesDownloaded = 1 }, queued));
        Require(tracking.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Downloading &&
                tracking.ReadyToRefresh.Count == 0,
            "a later Workshop update starts a new download cycle after an earlier completion was acknowledged");
        tracking.Observe(Snapshot(installed, queued));
        Require(tracking.ReadyToRefresh.SequenceEqual(new[] { first }),
            "a subsequent Workshop update receives its own completion refresh");

        var initiallyInstalled = new WorkshopDownloadTracking();
        initiallyInstalled.Observe(Snapshot(installed));
        Require(initiallyInstalled.ReadyToRefresh.SequenceEqual(new[] { first }) &&
                initiallyInstalled.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Completing,
            "an already-installed Workshop item gets one initial rescan when catalogue metadata is absent");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            initiallyInstalled.Refreshed(new[] { first }, Array.Empty<ulong>());
            initiallyInstalled.Observe(Snapshot(installed));
            if (attempt < 3)
                Require(initiallyInstalled.ReadyToRefresh.SequenceEqual(new[] { first }) &&
                        initiallyInstalled.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Completing,
                    $"unreadable Workshop metadata remains pending after applied completion scan {attempt}");
        }
        Require(initiallyInstalled.ReadyToRefresh.Count == 0 &&
                initiallyInstalled.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Missing,
            "persistently unreadable Workshop metadata becomes missing after three applied completion scans");
        initiallyInstalled.Observe(null);
        initiallyInstalled.Observe(Snapshot(installed));
        Require(initiallyInstalled.ReadyToRefresh.Count == 0,
            "a Steam reconnect does not restart exhausted completion scans for the same installed item");

        initiallyInstalled.Observe(Snapshot(needsUpdate));
        initiallyInstalled.Observe(Snapshot(installed));
        Require(initiallyInstalled.ReadyToRefresh.SequenceEqual(new[] { first }),
            "an observed needs-update to installed transition gets a fresh budget even if the download finished between polls");
        initiallyInstalled.Refreshed(new[] { first }, new[] { first });

        initiallyInstalled.Observe(Snapshot(downloading));
        initiallyInstalled.Observe(Snapshot(installed));
        initiallyInstalled.Refreshed(Array.Empty<ulong>(), Array.Empty<ulong>());
        initiallyInstalled.Refreshed(new ulong[] { 99 }, Array.Empty<ulong>());
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            initiallyInstalled.Refreshed(new[] { first }, Array.Empty<ulong>());
            initiallyInstalled.Observe(Snapshot(installed));
        }
        Require(initiallyInstalled.ReadyToRefresh.SequenceEqual(new[] { first }),
            "a new Workshop update resets the scan budget while deferred and unrelated scans consume no attempts");
        initiallyInstalled.Refreshed(new[] { first }, new[] { first });
        initiallyInstalled.Observe(Snapshot(installed));
        Require(initiallyInstalled.ReadyToRefresh.Count == 0,
            "a newly downloaded Workshop item can be acknowledged after its full retry budget is reset");

        var betweenPolls = new WorkshopDownloadTracking();
        var versioned = installed with { InstalledManifest = 1001 };
        betweenPolls.Observe(Snapshot(versioned));
        betweenPolls.Refreshed(new[] { first }, new[] { first });
        betweenPolls.Observe(Snapshot(versioned));
        Require(betweenPolls.ReadyToRefresh.Count == 0,
            "an unchanged installed manifest does not repeatedly refresh an acknowledged item");
        versioned = versioned with { InstalledManifest = 1002 };
        betweenPolls.Observe(Snapshot(versioned));
        Require(betweenPolls.ReadyToRefresh.SequenceEqual(new[] { first }),
            "an update completed between polls refreshes when its installed manifest changes");
        for (var attempt = 0; attempt < WorkshopDownloadTracking.MaximumCompletionScans; attempt++)
        {
            betweenPolls.Refreshed(new[] { first }, Array.Empty<ulong>());
            betweenPolls.Observe(Snapshot(versioned));
        }
        Require(betweenPolls.ReadyToRefresh.Count == 0,
            "an unchanged installed manifest cannot reset an exhausted completion-scan budget");
        betweenPolls.Observe(null);
        betweenPolls.Observe(Snapshot(versioned));
        Require(betweenPolls.ReadyToRefresh.Count == 0,
            "a temporary read failure cannot reset the same manifest's exhausted scan budget");
        betweenPolls.Observe(Snapshot());
        versioned = versioned with { InstalledManifest = 999 };
        betweenPolls.Observe(Snapshot(versioned));
        Require(betweenPolls.ReadyToRefresh.SequenceEqual(new[] { first }),
            "a changed manifest resets retries across omitted snapshots without assuming IDs increase");
        betweenPolls.Refreshed(new[] { first }, new[] { first });
        betweenPolls.Observe(Snapshot(versioned));
        Require(betweenPolls.ReadyToRefresh.Count == 0,
            "a between-polls update is acknowledged once its replacement is discovered");

        var interrupted = new WorkshopDownloadTracking();
        interrupted.Observe(Snapshot(installed));
        interrupted.Observe(Snapshot(downloading));
        interrupted.Refreshed(new[] { first }, Array.Empty<ulong>());
        interrupted.Observe(Snapshot(installed));
        for (var attempt = 1; attempt <= 2; attempt++)
            interrupted.Refreshed(new[] { first }, Array.Empty<ulong>());
        Require(interrupted.ReadyToRefresh.SequenceEqual(new[] { first }),
            "an applied stale scan cannot consume attempts for a download that restarted before acknowledgement");

        var untracked = new WorkshopDownloadTracking();
        untracked.Observe(Snapshot(Item(first, WorkshopItemState.None)));
        Require(untracked.ReadyToRefresh.Count == 0 &&
                untracked.Availability(first, foundOnDisk: false) == WorkshopModAvailability.Missing,
            "a confirmed untracked Workshop item remains genuinely missing");
    }

    private static WorkshopDownloadItem Item(ulong id, WorkshopItemState state,
        ulong downloaded = 0, ulong total = 0) => new(id, state, downloaded, total);

    private static WorkshopDownloadSnapshot Snapshot(params WorkshopDownloadItem[] items) =>
        new(items.ToDictionary(item => item.WorkshopId));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
