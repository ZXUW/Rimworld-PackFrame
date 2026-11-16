using System;
using System.IO;

namespace PackFrame;

internal static class AppLog
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "packframe.log");

    public static void Cleanup()
    {
        try
        {
            lock (Gate)
            {
                var rotated = LogPath + ".1";
                if (File.Exists(rotated))
                {
                    File.Delete(rotated);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Info(string message) =>
        Write($"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");

    public static void Error(string operation, Exception exception) =>
        Write($"{DateTimeOffset.Now:O} {operation}{Environment.NewLine}{exception}{Environment.NewLine}");

    private static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    File.Move(LogPath, LogPath + ".1", overwrite: true);
                File.AppendAllText(LogPath, text);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
