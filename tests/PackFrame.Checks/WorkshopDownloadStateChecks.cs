using PackFrame;

internal static class WorkshopDownloadStateChecks
{
    public static async Task Run()
    {
        var downloading = new WorkshopDownloadItem(11, WorkshopItemState.Subscribed |
            WorkshopItemState.NeedsUpdate | WorkshopItemState.Downloading, 25, 100);
        Require(downloading.IsDownloading && !downloading.IsQueued && !downloading.IsInstalled &&
                downloading.IsSubscribed && downloading.NeedsUpdate,
            "Steam's downloading flag identifies a real incomplete Workshop download");
        var queued = downloading with { State = WorkshopItemState.Subscribed | WorkshopItemState.DownloadPending };
        Require(queued.IsDownloading && queued.IsQueued && !queued.IsInstalled,
            "a pending Steam Workshop download remains queued rather than missing or installed");
        var updating = downloading with { State = WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate |
            WorkshopItemState.Downloading };
        Require(updating.IsDownloading && !updating.IsInstalled,
            "existing installed files do not make an in-progress Workshop update complete");
        Require(!(updating with { BytesDownloaded = 100, BytesTotal = 100 }).IsInstalled,
            "reported download byte completion alone cannot authorize a Workshop completion refresh");
        var stale = downloading with { State = WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate };
        Require(!stale.IsDownloading && !stale.IsInstalled,
            "a needed update alone is not falsely labeled as actively downloading or complete");
        var completed = downloading with { State = WorkshopItemState.Subscribed | WorkshopItemState.Installed };
        Require(completed.IsInstalled && !completed.IsDownloading && !completed.IsQueued,
            "Workshop completion requires Steam's installed state without pending update flags");
        var unknown = downloading with { State = WorkshopItemState.None };
        Require(!unknown.IsDownloading && !unknown.IsInstalled,
            "an untracked Workshop item is not mislabeled as downloading");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await SteamWorkshopClient.QueryDownloadStateAsync("unused", new ulong[] { 11 }, cancelled.Token);
            throw new Exception("FAIL: cancelled Workshop status query did not cancel");
        }
        catch (OperationCanceledException)
        {
            Require(true, "cancelled Workshop queries stop before reading Steam download records");
        }
        var empty = await SteamWorkshopClient.QueryDownloadStateAsync("unused", Array.Empty<ulong>());
        Require(empty?.Items.Count == 0, "an empty Workshop query requires no Steam files or client session");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
