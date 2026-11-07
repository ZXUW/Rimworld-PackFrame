using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

// Reading these files does not register RimWorld as running in Steam. Starting
// a Steam API client to poll item state can suspend the very downloads we watch.
internal static class SteamWorkshopLocalStateReader
{
    internal const int MaximumManifestBytes = 8 * 1024 * 1024;
    internal const int MaximumLogBytes = 512 * 1024;
    private const int MaximumItems = 10000;
    private const int MaximumLibraries = 64;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex ChunkLine = new(@"Downloading \d+ chunks for depot 294100 \((\d+)\)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex FailedItemLine = new(@"(?:Get details for item|Download item) (\d+) (?:failed\s*:|result\s*:)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex ChangedItemLine = new(@"(?:Subscribed to item|added subscribed item|changed cached item) (\d+)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex SubscribedItemLine = new(@"(?:Subscribed to item|added subscribed item) (\d+)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex UnsubscribedItemLine = new(@"(?:Unsubscribed(?: from| to)? item|removed subscribed item|removing subscribed item) (\d+)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex CachedManifestLine = new(@"changed cached item (\d+), new manifest (\d+)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private sealed record Library(string Root, SteamKeyValues Data, DateTime ModifiedUtc);
    private sealed class LogEvidence
    {
        internal bool Downloading;
        internal bool Queued;
        internal readonly HashSet<ulong> Manifests = new();
        internal readonly HashSet<ulong> FailedItems = new();
        internal readonly Dictionary<ulong, LoggedItem> Items = new();
    }
    private sealed class LoggedItem
    {
        internal ulong Manifest;
        internal DateTime ManifestUtc;
        internal bool? Subscribed;
        internal DateTime SubscriptionUtc;
    }

    internal static async Task<WorkshopDownloadSnapshot?> QueryAsync(string gamePath,
        IReadOnlyList<ulong> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = ids.Where(id => id != 0).Distinct().Take(MaximumItems + 1).ToArray();
        if (requested.Length == 0) return new(new());
        if (requested.Length > MaximumItems) return null;
        var workshopRoots = new List<string>();
        var steamRoots = RimWorldLocator.GetSteamRoots();
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(gamePath));
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
                if (current.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    workshopRoots.Add(Path.Combine(current.FullName, "workshop"));
                    if (current.Parent is not null) steamRoots.Add(current.Parent.FullName);
                    break;
                }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException) { }

        foreach (var root in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumLibraries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            workshopRoots.Add(Path.Combine(root, "steamapps", "workshop"));
            var folders = await ReadBoundedAsync(Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                MaximumManifestBytes, false, cancellationToken);
            if (folders is null || !SteamKeyValues.TryParse(folders, out var parsed)) continue;
            var libraries = parsed.Child("libraryfolders");
            if (libraries is null) continue;
            foreach (var entry in libraries.Children.Values.Take(MaximumLibraries))
            {
                var path = entry.Value("path");
                if (!string.IsNullOrWhiteSpace(path))
                    workshopRoots.Add(Path.Combine(path, "steamapps", "workshop"));
            }
        }
        var roots = workshopRoots.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumLibraries).ToArray();
        var logRoots = steamRoots.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumLibraries).ToArray();
        return await QueryFromPathsAsync(roots,
            logRoots.Select(root => Path.Combine(root, "logs", "content_log.txt")).ToArray(),
            logRoots.Select(root => Path.Combine(root, "logs", "workshop_log.txt")).ToArray(),
            SteamStartedUtc(), requested, cancellationToken);
    }

    internal static async Task<WorkshopDownloadSnapshot?> QueryFromPathsAsync(
        IReadOnlyList<string> workshopRoots, IReadOnlyList<string> contentLogs,
        IReadOnlyList<string> workshopLogs, DateTime? steamStartedUtc, IReadOnlyList<ulong> ids,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = ids.Where(id => id != 0).Distinct().Take(MaximumItems + 1).ToArray();
        if (requested.Length == 0) return new(new());
        if (requested.Length > MaximumItems) return null;
        var libraries = new List<Library>();
        foreach (var root in workshopRoots.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumLibraries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = Path.Combine(root, "appworkshop_294100.acf");
            if (!File.Exists(manifest)) continue;
            var text = await ReadBoundedAsync(manifest, MaximumManifestBytes, false, cancellationToken);
            if (text is null || !SteamKeyValues.TryParse(text, out var parsed) ||
                parsed.Child("AppWorkshop") is not { } data || data.Value("appid") != "294100" ||
                data.Child("WorkshopItemsInstalled") is null || data.Child("WorkshopItemDetails") is null)
                return null;
            try { libraries.Add(new(root, data, File.GetLastWriteTimeUtc(manifest))); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        }
        if (libraries.Count == 0) return null;
        libraries.Sort((left, right) => right.ModifiedUtc.CompareTo(left.ModifiedUtc));

        // Steam's content log identifies the manifest being transferred. Its
        // timestamps must belong to the current Steam process, not an old run.
        var evidence = new LogEvidence();
        if (steamStartedUtc is { } started)
        {
            var now = DateTime.UtcNow;
            foreach (var log in contentLogs.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumLibraries))
            {
                var text = await ReadBoundedAsync(log, MaximumLogBytes, true, cancellationToken);
                if (text is not null) ParseContentLog(text, started, now, evidence);
            }
            foreach (var log in workshopLogs.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumLibraries))
            {
                var text = await ReadBoundedAsync(log, MaximumLogBytes, true, cancellationToken);
                if (text is not null) ParseWorkshopLog(text, started, now, evidence);
            }
        }

        var items = new Dictionary<ulong, WorkshopDownloadItem>();
        foreach (var id in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = id.ToString(CultureInfo.InvariantCulture);
            var library = libraries.FirstOrDefault(candidate =>
                candidate.Data.Child("WorkshopItemsInstalled")!.Child(key) is not null ||
                candidate.Data.Child("WorkshopItemDetails")!.Child(key) is not null);
            var hasRecordedItem = library is not null;
            var staging = libraries.Any(candidate => HasStaging(candidate.Root, key));
            library ??= libraries.FirstOrDefault(candidate => HasStaging(candidate.Root, key)) ?? libraries[0];
            var installed = library.Data.Child("WorkshopItemsInstalled")!.Child(key);
            var details = library.Data.Child("WorkshopItemDetails")!.Child(key);
            var installedManifest = Number(installed?.Value("manifest"));
            var expectedManifest = Number(details?.Value("latest_manifest"));
            var subscribed = Number(details?.Value("subscribedby")) != 0;
            evidence.Items.TryGetValue(id, out var logged);
            var freshManifest = logged is { Manifest: not 0 } &&
                (!hasRecordedItem || logged.ManifestUtc > library.ModifiedUtc);
            // Steam can keep a new subscription and its manifest only in
            // memory until the whole Workshop job commits. Its own current
            // session's item-specific events bridge that stale ACF interval.
            if (freshManifest) expectedManifest = logged!.Manifest;
            if (logged?.Subscribed is { } loggedSubscribed &&
                (!hasRecordedItem || logged.SubscriptionUtc > library.ModifiedUtc))
                subscribed = loggedSubscribed;
            var state = subscribed ? WorkshopItemState.Subscribed : WorkshopItemState.None;
            if (logged?.Subscribed == false &&
                (!hasRecordedItem || logged.SubscriptionUtc > library.ModifiedUtc)) continue;
            if (expectedManifest != 0 && installedManifest == expectedManifest &&
                ReadableContent(library.Root, key))
            {
                items[id] = new(id, state | WorkshopItemState.Installed);
                continue;
            }
            if (evidence.FailedItems.Contains(id)) continue;
            var pendingInLog = freshManifest && subscribed && staging && installedManifest != expectedManifest;
            var pendingInManifest = library.Data.Value("NeedsDownload") == "1";
            if (expectedManifest != 0 && subscribed && installedManifest != expectedManifest &&
                (pendingInManifest || pendingInLog) && evidence.Downloading &&
                evidence.Manifests.Contains(expectedManifest))
            {
                items[id] = new(id, state | WorkshopItemState.Downloading |
                    (installedManifest != 0 ? WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate : 0));
                continue;
            }
            // An app-level queued flag alone cannot make an arbitrary missing
            // mod a download. Require a subscribed, uninstalled/outdated item
            // and either a pending ACF plus item staging/current queue, or a
            // fresh item-specific subscription/manifest plus item staging.
            if (expectedManifest != 0 && subscribed && installedManifest != expectedManifest &&
                ((pendingInManifest && (staging || evidence.Queued)) || pendingInLog))
            {
                items[id] = new(id, state | WorkshopItemState.DownloadPending |
                    (installedManifest != 0 ? WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate : 0));
                continue;
            }
            if (!staging && ((!hasRecordedItem && logged?.Subscribed != true && !freshManifest) ||
                (details is not null && details.Value("subscribedby") == "0" && !subscribed && installed is null)))
                items[id] = new(id, WorkshopItemState.None);
            // Omitted IDs are unknown; State.None is reserved for a confirmed
            // absent item. In particular NeedsUpdate is not Downloading.
        }
        return new(items);
    }

    private static ulong Number(string? value) => ulong.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static bool HasStaging(string root, string id) =>
        Directory.Exists(Path.Combine(root, "downloads", "294100", id)) ||
        Directory.Exists(Path.Combine(root, "temp", "294100", id)) ||
        File.Exists(Path.Combine(root, "downloads", $"state_294100_294100_{id}.patch"));

    private static bool ReadableContent(string root, string id)
    {
        try
        {
            using var file = new FileStream(Path.Combine(root, "content", "294100", id, "About", "About.xml"),
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return file.ReadByte() >= 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static DateTime? SteamStartedUtc()
    {
        DateTime? started = null;
        try
        {
            foreach (var process in Process.GetProcessesByName("steam"))
                using (process)
                    try
                    {
                        var candidate = process.StartTime.ToUniversalTime();
                        if (started is null || candidate > started) started = candidate;
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or
                        System.ComponentModel.Win32Exception or NotSupportedException) { }
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or NotSupportedException) { }
        return started;
    }

    private static async Task<string?> ReadBoundedAsync(string path, int maximumBytes, bool tail,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var length = stream.Length;
            if (!tail && length > maximumBytes) return null;
            var offset = tail ? Math.Max(0, length - maximumBytes) : 0;
            if (offset != 0) stream.Seek(offset, SeekOrigin.Begin);
            var bytes = new byte[(int)Math.Min(length, maximumBytes)];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken);
                if (count == 0) break;
                read += count;
            }
            var first = 0;
            if (offset != 0)
            {
                // Discard the partial first line before decoding: the tail
                // boundary may cut through a multibyte UTF-8 character.
                var newline = Array.IndexOf(bytes, (byte)'\n', 0, read);
                first = newline >= 0 ? newline + 1 : read;
            }
            return Utf8.GetString(bytes, first, read - first);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            DecoderFallbackException or ArgumentException or NotSupportedException) { return null; }
    }

    private static bool CurrentLine(string line, DateTime startedUtc, DateTime nowUtc, out DateTime timestampUtc)
    {
        timestampUtc = default;
        if (line.Length < 22 || line[0] != '[' || line[20] != ']' ||
            !DateTime.TryParseExact(line.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var timestamp)) return false;
        timestampUtc = timestamp.ToUniversalTime();
        return timestampUtc >= startedUtc.AddSeconds(-1) && timestampUtc <= nowUtc.AddMinutes(1);
    }

    private static void ParseContentLog(string text, DateTime started, DateTime now, LogEvidence evidence)
    {
        foreach (var line in text.Split('\n'))
        {
            const string workshop = "AppID 294100 Workshop update changed : ";
            var changed = line.IndexOf(workshop, StringComparison.Ordinal);
            if (!CurrentLine(line, started, now, out _))
            {
                // An unreadable later state line cannot leave an earlier
                // Downloading line looking current indefinitely.
                if (changed >= 0 && (line.Length < 22 || line[0] != '[' || line[20] != ']' ||
                    !DateTime.TryParseExact(line.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal, out _)))
                {
                    evidence.Downloading = false;
                    evidence.Manifests.Clear();
                }
                continue;
            }
            if (changed >= 0)
            {
                var state = line[(changed + workshop.Length)..];
                var active = state.Contains("Downloading", StringComparison.Ordinal) &&
                    !state.Contains("Stopping", StringComparison.Ordinal);
                if (!active || !evidence.Downloading) evidence.Manifests.Clear();
                evidence.Downloading = active;
            }
            const string appState = "AppID 294100 state changed : ";
            var stateIndex = line.IndexOf(appState, StringComparison.Ordinal);
            if (stateIndex >= 0)
                evidence.Queued = line[(stateIndex + appState.Length)..].Contains("Update Queued", StringComparison.Ordinal);
            if (line.Contains("AppID 294100 scheduler finished", StringComparison.Ordinal))
            {
                evidence.Downloading = false;
                evidence.Manifests.Clear();
            }
            var chunk = ChunkLine.Match(line);
            if (chunk.Success && evidence.Downloading && Number(chunk.Groups[1].Value) is var manifest && manifest != 0)
                evidence.Manifests.Add(manifest);
        }
    }

    private static void ParseWorkshopLog(string text, DateTime started, DateTime now, LogEvidence evidence)
    {
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("[AppID 294100]", StringComparison.Ordinal) ||
                !CurrentLine(line, started, now, out var timestamp)) continue;
            var failed = FailedItemLine.Match(line);
            if (failed.Success && !line.Contains("result : OK", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("result : No Error", StringComparison.OrdinalIgnoreCase))
                evidence.FailedItems.Add(Number(failed.Groups[1].Value));
            var changed = ChangedItemLine.Match(line);
            if (changed.Success) evidence.FailedItems.Remove(Number(changed.Groups[1].Value));
            var subscribed = SubscribedItemLine.Match(line);
            var unsubscribed = UnsubscribedItemLine.Match(line);
            var subscription = unsubscribed.Success ? unsubscribed : subscribed;
            if (subscription.Success)
            {
                var item = ItemEvidence(evidence, Number(subscription.Groups[1].Value));
                if (timestamp >= item.SubscriptionUtc)
                {
                    item.Subscribed = !unsubscribed.Success;
                    item.SubscriptionUtc = timestamp;
                }
            }
            var manifest = CachedManifestLine.Match(line);
            if (manifest.Success)
            {
                var item = ItemEvidence(evidence, Number(manifest.Groups[1].Value));
                if (timestamp >= item.ManifestUtc)
                {
                    item.Manifest = Number(manifest.Groups[2].Value);
                    item.ManifestUtc = timestamp;
                }
            }
        }
    }

    private static LoggedItem ItemEvidence(LogEvidence evidence, ulong id)
    {
        if (!evidence.Items.TryGetValue(id, out var item)) evidence.Items[id] = item = new();
        return item;
    }
}
