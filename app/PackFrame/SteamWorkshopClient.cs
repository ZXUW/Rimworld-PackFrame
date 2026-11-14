using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PackFrame;

internal static class SteamWorkshopClient
{
    private const string SubscribeHelperArg = "--packframe-steam-subscribe";
    private const string UnsubscribeHelperArg = "--packframe-steam-unsubscribe";

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
        return RunHelper(gamePath, publishedFileIds, true, out error);
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
        return RunHelper(gamePath, publishedFileIds, false, out error);
    }

    internal static bool TryRunHelper(string[] args)
    {
        if (args.Length == 0 ||
            (args[0] != SubscribeHelperArg && args[0] != UnsubscribeHelperArg))
        {
            return false;
        }

        var subscribe = args[0] == SubscribeHelperArg;
        var resultPath = args.Length >= 3 ? args[2] : string.Empty;
        var ids = args
            .Skip(3)
            .Select(value => ulong.TryParse(value, out var id) ? id : (ulong?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
        string error = string.Empty;
        var success = args.Length >= 2 &&
            SetSubscriptionManyCore(args[1], ids, subscribe, out error);
        try
        {
            if (!string.IsNullOrWhiteSpace(resultPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
                File.WriteAllText(
                    resultPath,
                    success ? "OK" : "ERR:" + (string.IsNullOrWhiteSpace(error)
                        ? "Steam operation failed."
                        : error)
                );
            }
        }
        catch
        {
        }

        Environment.ExitCode = success ? 0 : 1;
        return true;
    }

    public static void RemoveLegacyAppIdFile()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
            if (File.Exists(path) &&
                File.ReadAllText(path).Trim().Equals("294100", StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static bool RunHelper(
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

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "PackFrame executable path was not found.";
            return false;
        }

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "PackFrame",
            "steam-" + Guid.NewGuid().ToString("N")
        );
        var resultPath = Path.Combine(workDirectory, "result.txt");
        try
        {
            Directory.CreateDirectory(workDirectory);
            var info = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(subscribe ? SubscribeHelperArg : UnsubscribeHelperArg);
            info.ArgumentList.Add(gamePath);
            info.ArgumentList.Add(resultPath);
            foreach (var id in publishedFileIds)
            {
                info.ArgumentList.Add(id.ToString());
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                error = "Steam helper process could not be started.";
                return false;
            }

            process.WaitForExit();
            var result = File.Exists(resultPath)
                ? File.ReadAllText(resultPath).Trim()
                : string.Empty;
            if (process.ExitCode == 0 && result == "OK")
            {
                return true;
            }

            error = result.StartsWith("ERR:", StringComparison.Ordinal)
                ? result[4..]
                : "Steam helper process failed.";
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory))
                {
                    Directory.Delete(workDirectory, true);
                }
            }
            catch
            {
            }
        }
    }

    private static bool SetSubscriptionManyCore(
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
            var shutdown = GetDelegate<SteamApiShutdown>(library, "SteamAPI_Shutdown");
            var runCallbacks = GetDelegate<SteamApiRunCallbacks>(
                library,
                "SteamAPI_RunCallbacks"
            );
            var setSubscription = GetDelegate<SteamUgcSetSubscription>(
                library,
                subscribe
                    ? "SteamAPI_ISteamUGC_SubscribeItem"
                    : "SteamAPI_ISteamUGC_UnsubscribeItem"
            );

            if (!SteamNative.TryInit(library, out var initError))
            {
                error = string.IsNullOrWhiteSpace(initError)
                    ? "Steam is not running or the user is not signed in."
                    : initError;
                return false;
            }

            try
            {
                var ugc = SteamNative.GetUgc(library);
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
