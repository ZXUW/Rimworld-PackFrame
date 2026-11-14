using System;
using System.Runtime.InteropServices;

namespace PackFrame;

internal static class SteamNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SteamApiInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SteamApiInitFlat(IntPtr errorMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetInterface();

    // RimWorld 1.7 ships a newer Steamworks DLL without SteamAPI_Init or SteamUGC_v016.
    internal static bool TryInit(IntPtr library, out string error)
    {
        error = string.Empty;
        if (NativeLibrary.TryGetExport(library, "SteamAPI_Init", out var export))
        {
            return Marshal.GetDelegateForFunctionPointer<SteamApiInit>(export)();
        }

        if (NativeLibrary.TryGetExport(library, "SteamAPI_InitSafe", out export))
        {
            return Marshal.GetDelegateForFunctionPointer<SteamApiInit>(export)();
        }

        if (NativeLibrary.TryGetExport(library, "SteamAPI_InitFlat", out export))
        {
            var message = Marshal.AllocHGlobal(1024);
            try
            {
                Marshal.WriteByte(message, 0);
                var result = Marshal.GetDelegateForFunctionPointer<SteamApiInitFlat>(export)(message);
                if (result == 1)
                {
                    return true;
                }

                var detail = Marshal.PtrToStringUTF8(message);
                error = string.IsNullOrWhiteSpace(detail)
                    ? $"SteamAPI_InitFlat failed with EResult {result}."
                    : detail;
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(message);
            }
        }

        error = "This steam_api64.dll has no supported SteamAPI_Init export.";
        return false;
    }

    internal static IntPtr GetUgc(IntPtr library)
    {
        for (var version = 21; version >= 13; version--)
        {
            if (!NativeLibrary.TryGetExport(
                    library,
                    "SteamAPI_SteamUGC_v" + version.ToString("D3"),
                    out var export))
            {
                continue;
            }

            var ugc = Marshal.GetDelegateForFunctionPointer<GetInterface>(export)();
            if (ugc != IntPtr.Zero)
            {
                return ugc;
            }
        }

        throw new InvalidOperationException("SteamUGC interface was not available.");
    }
}
