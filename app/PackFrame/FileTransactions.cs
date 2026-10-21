using System;
using System.Collections.Generic;
using System.IO;

namespace PackFrame;

internal static class FileTransactions
{
    public static void WriteAtomic(string path, Action<string> write)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            write(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Stage all files before calling this; retain backups if rollback itself fails.
    public static void ApplyFiles(string staging, string target)
    {
        Directory.CreateDirectory(target);
        var backup = Path.Combine(target, ".packframe-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var applied = new List<(string Path, string? Backup)>();
        try
        {
            foreach (var source in Directory.EnumerateFiles(staging))
            {
                var destination = Path.Combine(target, Path.GetFileName(source));
                string? old = null;
                if (File.Exists(destination))
                {
                    old = Path.Combine(backup, Path.GetFileName(source));
                    File.Copy(destination, old);
                }
                WriteAtomic(destination, temporary => File.Copy(source, temporary));
                applied.Add((destination, old));
            }
        }
        catch (Exception failure)
        {
            AppLog.Error("Apply configuration files: " + target, failure);
            var errors = new List<Exception> { failure };
            for (var i = applied.Count - 1; i >= 0; i--)
            {
                var entry = applied[i];
                try
                {
                    if (entry.Backup is null) File.Delete(entry.Path);
                    else WriteAtomic(entry.Path, temporary => File.Copy(entry.Backup, temporary));
                }
                catch (Exception rollbackFailure) { errors.Add(rollbackFailure); }
            }
            if (errors.Count > 1)
                throw new AggregateException($"Update and rollback failed. Backups: {backup}", errors);
            TryDeleteDirectory(backup);
            throw;
        }
        TryDeleteDirectory(backup);
    }

    public static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
