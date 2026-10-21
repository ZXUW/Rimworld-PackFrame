using System;
using System.IO;

namespace PackFrame;

internal static class AppLog
{
    private static readonly object Gate = new();
    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "packframe.log");

    public static void Error(string operation, Exception exception)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024)
                    File.Move(LogPath, LogPath + ".1", overwrite: true);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {operation}{Environment.NewLine}{exception}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
