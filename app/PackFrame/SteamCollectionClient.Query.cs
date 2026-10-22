using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PackFrame;

internal sealed record OwnedCollection(ulong Id, string Title, IReadOnlyList<ulong> Children);

internal sealed partial class SteamCollectionClient
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong GetSteamId(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong QueryUser(IntPtr self, uint account, int list, int type, int sort, uint creator, uint consumer, uint page);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong SendQuery(IntPtr self, ulong query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool QueryOption(IntPtr self, ulong query, [MarshalAs(UnmanagedType.I1)] bool value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool QueryResult(IntPtr self, ulong query, uint index, out UgcDetails details);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool QueryChildren(IntPtr self, ulong query, uint index, [Out] ulong[] children, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ReleaseQuery(IntPtr self, ulong query);

    // Steamworks SDK SteamUGCDetails_t, Windows x64 pack=8, strings are UTF-8 bytes.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct UgcDetails
    {
        public ulong Id;
        public int Result, FileType;
        public uint CreatorApp, ConsumerApp;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 129)] public byte[] Title;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8000)] public byte[] Description;
        public ulong Owner;
        public uint Created, Updated, Added;
        public int Visibility;
        [MarshalAs(UnmanagedType.I1)] public bool Banned;
        [MarshalAs(UnmanagedType.I1)] public bool Accepted;
        [MarshalAs(UnmanagedType.I1)] public bool TagsTruncated;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1025)] public byte[] Tags;
        public ulong File, Preview;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)] public byte[] FileName;
        public int FileSize, PreviewSize;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Url;
        public uint VotesUp, VotesDown;
        public float Score;
        public uint Children;
        public ulong TotalSize;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct QueryCompleted
    {
        public ulong Handle;
        public int Result;
        public uint Returned, Total;
        [MarshalAs(UnmanagedType.I1)] public bool Cached;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Cursor;
    }

    private List<OwnedCollection> QueryOwnedCollections(CancellationToken token)
    {
        var user = IntPtr.Zero;
        foreach (var version in new[] { "v023", "v022", "v021", "v020" })
            if (NativeLibrary.TryGetExport(_library, "SteamAPI_SteamUser_" + version, out var accessor))
            {
                user = Marshal.GetDelegateForFunctionPointer<SteamUgc>(accessor)();
                if (user != IntPtr.Zero) break;
            }
        if (user == IntPtr.Zero) throw new InvalidOperationException("Steam user is unavailable.");
        var owner = GetDelegate<GetSteamId>(_library, "SteamAPI_ISteamUser_GetSteamID")(user);
        var create = GetDelegate<QueryUser>(_library, "SteamAPI_ISteamUGC_CreateQueryUserUGCRequest");
        var send = GetDelegate<SendQuery>(_library, "SteamAPI_ISteamUGC_SendQueryUGCRequest");
        var details = GetDelegate<QueryResult>(_library, "SteamAPI_ISteamUGC_GetQueryUGCResult");
        var children = GetDelegate<QueryChildren>(_library, "SteamAPI_ISteamUGC_GetQueryUGCChildren");
        var release = GetDelegate<ReleaseQuery>(_library, "SteamAPI_ISteamUGC_ReleaseQueryUGCRequest");
        var includeChildren = GetDelegate<QueryOption>(_library, "SteamAPI_ISteamUGC_SetReturnChildren");
        var found = new List<OwnedCollection>();
        uint seen = 0;
        for (uint page = 1; ; page++)
        {
            token.ThrowIfCancellationRequested();
            var query = create(_ugc, unchecked((uint)owner), 0, 3, 0, RimWorldAppId, RimWorldAppId, page);
            if (query == ulong.MaxValue) throw new InvalidOperationException("Collection query failed.");
            try
            {
                if (!includeChildren(_ugc, query, true)) throw new InvalidOperationException("Cannot query collection members.");
                var call = send(_ugc, query);
                if (call == 0) throw new InvalidOperationException("Cannot send collection query.");
                var completed = Wait<QueryCompleted>(call, 3401, token);
                if (completed.Result != ResultOk) throw new InvalidOperationException("Collection query EResult " + completed.Result);
                for (uint i = 0; i < completed.Returned; i++)
                {
                    if (!details(_ugc, query, i, out var item) || item.Result != ResultOk)
                        throw new InvalidOperationException("Cannot read collection details.");
                    if (item.Owner != owner || item.ConsumerApp != RimWorldAppId || item.FileType != CollectionFileType) continue;
                    var members = new ulong[checked((int)item.Children)];
                    if (members.Length > 0 && !children(_ugc, query, i, members, item.Children))
                        throw new InvalidOperationException("Cannot read collection members.");
                    var length = Array.IndexOf(item.Title, (byte)0);
                    found.Add(new(item.Id, Encoding.UTF8.GetString(item.Title, 0, length < 0 ? item.Title.Length : length), members));
                }
                seen += completed.Returned;
                if (seen >= completed.Total) break;
                if (completed.Returned == 0) throw new InvalidOperationException("Collection query returned an incomplete page.");
            }
            finally { release(_ugc, query); }
        }
        return found;
    }

    internal static List<OwnedCollection> SelectCandidates(IEnumerable<OwnedCollection> owned, string title, ulong? bound)
    {
        var list = owned.ToList();
        var boundItem = list.FirstOrDefault(item => item.Id == bound);
        if (boundItem is not null) return new() { boundItem };
        return list.Where(item => item.Title.Trim().Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }
    internal static (List<ulong> Add, List<ulong> Remove) CollectionDelta(IEnumerable<ulong> current, IEnumerable<ulong> desired)
    {
        var old = current.ToHashSet(); var next = desired.ToHashSet();
        return (next.Except(old).ToList(), old.Except(next).ToList());
    }
}
