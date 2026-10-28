using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PackFrame;

internal static class SteamWorkshopClient
{
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
    private delegate ulong SteamUgcSetSubscription(IntPtr self, ulong publishedFileId);

    public static bool Subscribe(
        string gamePath,
        ulong publishedFileId,
        out string error
    )
    {
        return SubscribeMany(gamePath, new[] { publishedFileId }, out error);
    }

    public static bool SubscribeMany(
        string gamePath,
        IReadOnlyList<ulong> publishedFileIds,
        out string error
    )
    {
        return SetSubscriptionMany(gamePath, publishedFileIds, true, out error);
    }

    public static bool Unsubscribe(
        string gamePath,
        ulong publishedFileId,
        out string error
    )
    {
        return UnsubscribeMany(
            gamePath,
            new[] { publishedFileId },
            out error
        );
    }

    public static bool UnsubscribeMany(
        string gamePath,
        IReadOnlyList<ulong> publishedFileIds,
        out string error
    )
    {
        return SetSubscriptionMany(gamePath, publishedFileIds, false, out error);
    }

    private static bool SetSubscriptionMany(
        string gamePath,
        IReadOnlyList<ulong> publishedFileIds,
        bool subscribe,
        out string error
    )
    {
        error = string.Empty;
        if (publishedFileIds.Count == 0)
        {
            error = "No Workshop IDs were supplied.";
            return false;
        }
        if (!OperatingSystem.IsWindows())
        {
            error = "Steamworks integration is only available on Windows.";
            return false;
        }

        var libraryPath = FindSteamApi64Dll(gamePath);
        if (libraryPath is null)
        {
            error = "steam_api64.dll was not found in the game folder.";
            return false;
        }

        EnsureAppId();
        var library = IntPtr.Zero;
        try
        {
            library = NativeLibrary.Load(libraryPath);
            var init = GetDelegate<SteamApiInit>(library, "SteamAPI_Init");
            var shutdown = GetDelegate<SteamApiShutdown>(library, "SteamAPI_Shutdown");
            var runCallbacks = GetDelegate<SteamApiRunCallbacks>(
                library,
                "SteamAPI_RunCallbacks"
            );
            var getUgc = GetDelegate<SteamUgc>(library, "SteamAPI_SteamUGC_v016");
            var setSubscription = GetDelegate<SteamUgcSetSubscription>(
                library,
                subscribe
                    ? "SteamAPI_ISteamUGC_SubscribeItem"
                    : "SteamAPI_ISteamUGC_UnsubscribeItem"
            );

            if (!init())
            {
                error = "Steam is not running or the user is not signed in.";
                return false;
            }

            try
            {
                var ugc = getUgc();
                if (ugc == IntPtr.Zero)
                {
                    error = "SteamUGC interface was not available.";
                    return false;
                }

                foreach (var publishedFileId in publishedFileIds)
                {
                    if (setSubscription(ugc, publishedFileId) == 0)
                    {
                        error = subscribe
                            ? "Steam rejected the subscribe request."
                            : "Steam rejected the unsubscribe request.";
                        return false;
                    }

                    runCallbacks();
                    Thread.Sleep(100);
                }

                for (var i = 0; i < 40; i++)
                {
                    runCallbacks();
                    Thread.Sleep(50);
                }

                return true;
            }
            finally
            {
                shutdown();
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
        }
    }

    private static string? FindSteamApi64Dll(string gamePath)
    {
        var candidates = new[]
        {
            Path.Combine(gamePath, "steam_api64.dll"),
            Path.Combine(gamePath, "RimWorldWin64_Data", "Plugins", "x86_64", "steam_api64.dll"),
            Path.Combine(gamePath, "RimWorldWin64_Data", "Plugins", "steam_api64.dll"),
            Path.Combine(gamePath, "RimWorldWin64_Data", "Plugins", "x86", "steam_api64.dll"),
            Path.Combine(gamePath, "RimWorldWin64_Data", "steam_api64.dll"),
            Path.Combine(gamePath, "..", "steam_api64.dll"),
            Path.Combine(gamePath, "..", "RimWorldWin64_Data", "Plugins", "x86_64", "steam_api64.dll")
        };
        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
            }
        }

        try
        {
            return Directory
                .EnumerateFiles(gamePath, "steam_api64.dll", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
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
            Environment.SetEnvironmentVariable("SteamAppId", "294100");
            var path = Path.Combine(Environment.CurrentDirectory, "steam_appid.txt");
            if (!File.Exists(path) ||
                !File.ReadAllText(path).Trim().Equals(
                    "294100",
                    StringComparison.Ordinal
                ))
            {
                File.WriteAllText(path, "294100");
            }
        }
        catch
        {
        }
    }
}
