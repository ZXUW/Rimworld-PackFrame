using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

// An installed copy may still be waiting for, or downloading, its replacement.
[Flags]
internal enum WorkshopItemState : uint
{
    None = 0,
    Subscribed = 1,
    LegacyItem = 2,
    Installed = 4,
    NeedsUpdate = 8,
    Downloading = 16,
    DownloadPending = 32
}

internal sealed record WorkshopDownloadItem(ulong WorkshopId, WorkshopItemState State,
    ulong BytesDownloaded = 0, ulong BytesTotal = 0, ulong InstalledManifest = 0)
{
    public bool IsDownloading =>
        (State & (WorkshopItemState.Downloading | WorkshopItemState.DownloadPending)) != 0;
    public bool IsQueued =>
        State.HasFlag(WorkshopItemState.DownloadPending) && !State.HasFlag(WorkshopItemState.Downloading);
    public bool IsInstalled => State.HasFlag(WorkshopItemState.Installed) &&
        !NeedsUpdate && !IsDownloading;
    public bool NeedsUpdate => State.HasFlag(WorkshopItemState.NeedsUpdate);
    public bool IsSubscribed => State.HasFlag(WorkshopItemState.Subscribed);
}

// Unknown items are omitted, rather than being reported as missing.
internal sealed record WorkshopDownloadSnapshot(Dictionary<ulong, WorkshopDownloadItem> Items);

internal static partial class SteamWorkshopClient
{
    public static Task<WorkshopDownloadSnapshot?> QueryDownloadStateAsync(string gamePath,
        IReadOnlyList<ulong> ids, CancellationToken cancellationToken = default) =>
        Task.Run(() => SteamWorkshopLocalStateReader.QueryAsync(gamePath, ids, cancellationToken), cancellationToken);
}
