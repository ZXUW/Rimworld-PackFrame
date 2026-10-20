using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;

namespace PackFrame;
internal sealed class SteamCollectionClient
{
    private const uint RimWorldAppId = 294100;
    private const int CollectionFileType = 2;
    private const int PrivateVisibility = 2;
    private const int ResultOk = 1;
    private const int CreateItemCallback = 3403;
    private const int SubmitItemUpdateCallback = 3404;
    private const int AddDependencyCallback = 3412;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SteamApiInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SteamApiShutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SteamApiRunCallbacks();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUgc();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUtils();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong CreateItem(IntPtr self, uint appId, int fileType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong StartItemUpdate(IntPtr self, uint appId, ulong fileId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetItemText(IntPtr self, ulong handle, IntPtr text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetItemVisibility(IntPtr self, ulong handle, int visibility);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetItemPreview(IntPtr self, ulong handle, IntPtr path);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong AddDependency(IntPtr self, ulong parent, ulong child);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong SubmitItemUpdate(IntPtr self, ulong handle, IntPtr changeNote);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool IsApiCallCompleted(
        IntPtr self,
        ulong call,
        [MarshalAs(UnmanagedType.I1)] out bool failed
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetApiCallResult(
        IntPtr self,
        ulong call,
        IntPtr result,
        int size,
        int expectedCallback,
        [MarshalAs(UnmanagedType.I1)] out bool failed
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateItemResult
    {
        public int Result;
        public ulong PublishedFileId;
        [MarshalAs(UnmanagedType.I1)]
        public bool UserNeedsToAcceptWorkshopLegalAgreement;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SubmitItemUpdateResult
    {
        public int Result;
        [MarshalAs(UnmanagedType.I1)]
        public bool UserNeedsToAcceptWorkshopLegalAgreement;
        public ulong PublishedFileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AddDependencyResult
    {
        public int Result;
        public ulong PublishedFileId;
        public ulong ChildPublishedFileId;
    }

    private readonly string _gamePath;
    private readonly IntPtr _library;
    private readonly IntPtr _ugc;
    private readonly IntPtr _utils;
    private readonly SteamApiRunCallbacks _runCallbacks;
    private readonly IsApiCallCompleted _isCallCompleted;
    private readonly GetApiCallResult _getCallResult;

    private SteamCollectionClient(
        string gamePath,
        IntPtr library,
        IntPtr ugc,
        IntPtr utils,
        SteamApiRunCallbacks runCallbacks,
        IsApiCallCompleted isCallCompleted,
        GetApiCallResult getCallResult
    )
    {
        _gamePath = gamePath;
        _library = library;
        _ugc = ugc;
        _utils = utils;
        _runCallbacks = runCallbacks;
        _isCallCompleted = isCallCompleted;
        _getCallResult = getCallResult;
    }

    public static ulong CreatePrivateCollection(
        string gamePath,
        string title,
        string description,
        IReadOnlyList<ulong> workshopIds,
        string? previewPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var libraryPath = Path.Combine(gamePath, "steam_api64.dll");
        if (!File.Exists(libraryPath))
        {
            throw new FileNotFoundException("steam_api64.dll not found", libraryPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureAppId();
        var library = NativeLibrary.Load(libraryPath);
        var init = GetDelegate<SteamApiInit>(library, "SteamAPI_Init");
        var shutdown = GetDelegate<SteamApiShutdown>(library, "SteamAPI_Shutdown");
        var runCallbacks = GetDelegate<SteamApiRunCallbacks>(
            library,
            "SteamAPI_RunCallbacks"
        );
        var getUgc = GetDelegate<SteamUgc>(library, "SteamAPI_SteamUGC_v016");
        var getUtils = GetDelegate<SteamUtils>(library, "SteamAPI_SteamUtils_v010");
        var createItem = GetDelegate<CreateItem>(
            library,
            "SteamAPI_ISteamUGC_CreateItem"
        );
        var startItemUpdate = GetDelegate<StartItemUpdate>(
            library,
            "SteamAPI_ISteamUGC_StartItemUpdate"
        );
        var setTitle = GetDelegate<SetItemText>(
            library,
            "SteamAPI_ISteamUGC_SetItemTitle"
        );
        var setDescription = GetDelegate<SetItemText>(
            library,
            "SteamAPI_ISteamUGC_SetItemDescription"
        );
        var setVisibility = GetDelegate<SetItemVisibility>(
            library,
            "SteamAPI_ISteamUGC_SetItemVisibility"
        );
        var setPreview = GetDelegate<SetItemPreview>(
            library,
            "SteamAPI_ISteamUGC_SetItemPreview"
        );
        var addDependency = GetDelegate<AddDependency>(
            library,
            "SteamAPI_ISteamUGC_AddDependency"
        );
        var submitItemUpdate = GetDelegate<SubmitItemUpdate>(
            library,
            "SteamAPI_ISteamUGC_SubmitItemUpdate"
        );
        var isCallCompleted = GetDelegate<IsApiCallCompleted>(
            library,
            "SteamAPI_ISteamUtils_IsAPICallCompleted"
        );
        var getCallResult = GetDelegate<GetApiCallResult>(
            library,
            "SteamAPI_ISteamUtils_GetAPICallResult"
        );

        if (!init())
        {
            NativeLibrary.Free(library);
            throw new InvalidOperationException(
                "SteamAPI_Init failed. Start Steam and sign in first."
            );
        }

        var ugc = getUgc();
        var utils = getUtils();
        if (ugc == IntPtr.Zero || utils == IntPtr.Zero)
        {
            shutdown();
            NativeLibrary.Free(library);
            throw new InvalidOperationException("Steam UGC/Utils interface was not available.");
        }

        var client = new SteamCollectionClient(
            gamePath,
            library,
            ugc,
            utils,
            runCallbacks,
            isCallCompleted,
            getCallResult
        );

        try
        {
            var createCall = createItem(ugc, RimWorldAppId, CollectionFileType);
            if (createCall == 0)
            {
                throw new InvalidOperationException("CreateItem returned an invalid call.");
            }

            var created = client.Wait<CreateItemResult>(
                createCall,
                CreateItemCallback,
                cancellationToken
            );
            if (created.Result != ResultOk || created.PublishedFileId == 0)
            {
                throw new InvalidOperationException(
                    $"CreateItem failed with EResult {created.Result}."
                );
            }

            var collectionId = created.PublishedFileId;

            var update = startItemUpdate(ugc, RimWorldAppId, collectionId);
            if (update == 0)
            {
                throw new InvalidOperationException("StartItemUpdate returned an invalid handle.");
            }

            SetText(setTitle, ugc, update, title);
            SetText(setDescription, ugc, update, description);
            if (!setVisibility(ugc, update, PrivateVisibility))
            {
                throw new InvalidOperationException("SetItemVisibility(Private) failed.");
            }

            string? temporaryPreview = null;
            if (!string.IsNullOrWhiteSpace(previewPath))
            {
                if (!File.Exists(previewPath))
                {
                    throw new FileNotFoundException("Preview image not found", previewPath);
                }

                var prepared = PreparePreview(previewPath);
                temporaryPreview = prepared.Temporary ? prepared.Path : null;
                var previewPointer = ToUtf8(prepared.Path);
                try
                {
                    if (!setPreview(ugc, update, previewPointer))
                    {
                        throw new InvalidOperationException(
                            "SetItemPreview failed. Steam may require Cloud storage quota for preview images."
                        );
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(previewPointer);
                }
            }


            for (var index = 0; index < workshopIds.Count; index++)
            {
                var child = workshopIds[index];
                var addCall = addDependency(ugc, collectionId, child);
                if (addCall == 0)
                {
                    throw new InvalidOperationException(
                        $"AddDependency returned an invalid call for {child}."
                    );
                }

                var added = client.Wait<AddDependencyResult>(
                    addCall,
                    AddDependencyCallback,
                    cancellationToken
                );
                if (added.Result != ResultOk)
                {
                    throw new InvalidOperationException(
                        $"AddDependency failed for {child} with EResult {added.Result}."
                    );
                }
            }

            var changeNote = ToUtf8("Initial collection from PackFrame");
            ulong submitCall;
            try
            {
                submitCall = submitItemUpdate(ugc, update, changeNote);
            }
            finally
            {
                Marshal.FreeCoTaskMem(changeNote);
            }
            if (submitCall == 0)
            {
                throw new InvalidOperationException(
                    "SubmitItemUpdate returned an invalid call."
                );
            }

            var submitted = client.Wait<SubmitItemUpdateResult>(
                submitCall,
                SubmitItemUpdateCallback,
                cancellationToken
            );
            if (submitted.Result != ResultOk)
            {
                throw new InvalidOperationException(
                    submitted.Result == 25
                        ? "SubmitItemUpdate failed with EResult 25: preview image is larger than 1 MB or Steam Cloud quota is full."
                        : $"SubmitItemUpdate failed with EResult {submitted.Result}."
                );
            }

            if (temporaryPreview is not null)
            {
                try
                {
                    File.Delete(temporaryPreview);
                }
                catch
                {
                }
            }
            return collectionId;
        }
        finally
        {
            try
            {
                shutdown();
            }
            catch
            {
            }

            NativeLibrary.Free(library);
        }
    }

    private static (string Path, bool Temporary) PreparePreview(string source)
    {
        try
        {
            if (new FileInfo(source).Length <= 950_000)
            {
                return (source, false);
            }

            using var bitmap = SKBitmap.Decode(source);
            if (bitmap is null)
            {
                return (source, false);
            }

            foreach (var maximum in new[] { 1024, 768, 512 })
            {
                var scale = Math.Min(
                    1d,
                    maximum / (double)Math.Max(bitmap.Width, bitmap.Height)
                );
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                using var resized = bitmap.Resize(
                    new SKImageInfo(width, height),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                );
                if (resized is null)
                {
                    continue;
                }

                using var image = SKImage.FromBitmap(resized);
                foreach (var quality in new[] { 85, 70 })
                {
                    using var data = image.Encode(
                        SKEncodedImageFormat.Jpeg,
                        quality
                    );
                    if (data is null)
                    {
                        continue;
                    }

                    var temporary = Path.Combine(
                        Path.GetTempPath(),
                        "packframe-steam-cover-"
                        + Guid.NewGuid().ToString("N")
                        + ".jpg"
                    );
                    using (var stream = File.Create(temporary))
                    {
                        data.SaveTo(stream);
                    }

                    if (new FileInfo(temporary).Length <= 950_000)
                    {
                        return (temporary, true);
                    }

                    File.Delete(temporary);
                }
            }
        }
        catch
        {
        }

        return (source, false);
    }
    private static void SetText(
        SetItemText setter,
        IntPtr ugc,
        ulong update,
        string value
    )
    {
        var pointer = Marshal.StringToCoTaskMemUTF8(value ?? string.Empty);
        try
        {
            if (!setter(ugc, update, pointer))
            {
                throw new InvalidOperationException("Steam rejected item text update.");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static IntPtr ToUtf8(string value)
    {
        return Marshal.StringToCoTaskMemUTF8(value ?? string.Empty);
    }

    private T Wait<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(
        ulong call,
        int expectedCallback,
        CancellationToken cancellationToken
    ) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (stopwatch.Elapsed < TimeSpan.FromMinutes(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _runCallbacks();
                if (_isCallCompleted(_utils, call, out var failed))
                {
                    if (failed)
                    {
                        throw new InvalidOperationException(
                            $"Steam API call failed ({call})."
                        );
                    }

                    if (!_getCallResult(
                            _utils,
                            call,
                            buffer,
                            size,
                            expectedCallback,
                            out failed
                        ) || failed)
                    {
                        throw new InvalidOperationException(
                            $"Steam API result could not be read ({call})."
                        );
                    }

                    return Marshal.PtrToStructure<T>(buffer);
                }

                Thread.Sleep(50);
            }

            throw new TimeoutException("Timed out waiting for Steam API call.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static T GetDelegate<T>(IntPtr library, string export) where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(
            NativeLibrary.GetExport(library, export)
        );
    }

    private static void EnsureAppId()
    {
        try
        {
            Environment.SetEnvironmentVariable("SteamAppId", RimWorldAppId.ToString());
            foreach (var directory in new[]
                     {
                         Environment.CurrentDirectory,
                         AppContext.BaseDirectory
                     })
            {
                var path = Path.Combine(directory, "steam_appid.txt");
                if (!File.Exists(path) ||
                    !File.ReadAllText(path).Trim().Equals(
                        RimWorldAppId.ToString(),
                        StringComparison.Ordinal
                    ))
                {
                    File.WriteAllText(path, RimWorldAppId.ToString());
                }
            }
        }
        catch
        {
        }
    }
}


