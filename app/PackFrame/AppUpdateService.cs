using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

internal sealed record AppUpdateProgress(long BytesReceived, long? TotalBytes, string Phase);
internal sealed record AppUpdateFile(string Path, long Length, string Sha256);
internal sealed record PreparedAppUpdate(string Tag, string StageDirectory, string ProgramDirectory,
    List<AppUpdateFile> Files, string ArchiveSha256);
internal sealed record AppUpdateResult(bool Success, string Tag, string? Error, string? BackupDirectory);
internal sealed record AppUpdatePlan(PreparedAppUpdate Update, int ParentId, long ParentStartedUtcTicks,
    string HandoffDirectory, string CommitToken, string RestartExecutable, string? DotnetHost);

internal sealed class AppUpdateHandoff : IDisposable
{
    private readonly AppUpdatePlan _plan;
    private bool _committed;
    internal AppUpdateHandoff(AppUpdatePlan plan) => _plan = plan;
    // Only the main window's Closed event commits the already-confirmed update.
    public void Commit()
    {
        if (_committed) return;
        File.WriteAllText(Path.Combine(_plan.HandoffDirectory, "commit"), _plan.CommitToken);
        _committed = true;
    }
    public void Cancel()
    {
        if (_committed) return;
        try { File.WriteAllText(Path.Combine(_plan.HandoffDirectory, "cancel"), _plan.CommitToken); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { if (!_committed) Cancel(); }
}

internal static class AppUpdateService
{
    private const long MaximumArchiveBytes = 1024L * 1024 * 1024;
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();
    public static bool CanInstall => OperatingSystem.IsWindows() &&
        RuntimeInformation.ProcessArchitecture == Architecture.X64;

    public static PreparedAppUpdate? FindPrepared(UpdateInfo update, string? programDirectory = null)
    {
        if (update.Asset is null) return null;
        var program = ResolveProgramDirectory(programDirectory);
        var stage = CacheDirectory(program, update.Tag, update.Asset.Sha256);
        try
        {
            var manifest = Path.Combine(stage, "prepared.json");
            if (!File.Exists(manifest) || new FileInfo(manifest).Length > 1024 * 1024) return null;
            var prepared = JsonSerializer.Deserialize(File.ReadAllText(manifest),
                PackFrameJsonContext.Default.PreparedAppUpdate);
            if (prepared is null || prepared.Tag != update.Tag ||
                !string.Equals(prepared.ArchiveSha256, update.Asset.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !SamePath(prepared.ProgramDirectory, program) || !SamePath(prepared.StageDirectory, stage)) return null;
            ValidatePrepared(prepared);
            return prepared;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                        JsonException or ArgumentException or InvalidDataException)
        { return null; }
    }

    public static async Task<PreparedAppUpdate> PrepareAsync(UpdateInfo update,
        IProgress<AppUpdateProgress>? progress = null, CancellationToken cancellationToken = default,
        HttpClient? client = null, string? programDirectory = null)
    {
        var asset = update.Asset ?? throw new InvalidOperationException("This release has no compatible update package.");
        ValidateAsset(asset);
        cancellationToken.ThrowIfCancellationRequested();
        var cached = FindPrepared(update, programDirectory);
        if (cached is not null) return cached;
        var program = ResolveProgramDirectory(programDirectory);
        EnsureNoLinks(program, program);
        var finalStage = CacheDirectory(program, update.Tag, asset.Sha256);
        var temporary = finalStage + ".preparing-" + Guid.NewGuid().ToString("N");
        EnsureNoLinks(temporary, program);
        Directory.CreateDirectory(temporary);
        try
        {
            var archive = Path.Combine(temporary, "package.zip");
            using (var response = await (client ?? Http).GetAsync(asset.Url,
                       HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { Scheme: not "https" })
                    throw new InvalidDataException("The update download redirected to an insecure address.");
                if (response.Content.Headers.ContentLength is { } contentLength && contentLength != asset.Size)
                    throw new InvalidDataException("The update package size does not match the release.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    received += read;
                    if (received > asset.Size || received > MaximumArchiveBytes)
                        throw new InvalidDataException("The update package exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new AppUpdateProgress(received, asset.Size, "download"));
                }
                if (received != asset.Size) throw new InvalidDataException("The update download is incomplete.");
            }
            progress?.Report(new AppUpdateProgress(asset.Size, asset.Size, "verify"));
            if (!string.Equals(Hash(archive), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package checksum does not match GitHub's release checksum.");
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AppUpdateProgress(asset.Size, asset.Size, "extract"));
            var files = await ExtractProgramAsync(archive, Path.Combine(temporary, "payload"), cancellationToken);
            File.Delete(archive);
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = new PreparedAppUpdate(update.Tag, finalStage, program, files, asset.Sha256);
            File.WriteAllText(Path.Combine(temporary, "prepared.json"), JsonSerializer.Serialize(prepared,
                PackFrameJsonContext.Default.PreparedAppUpdate));
            if (Directory.Exists(finalStage))
            {
                var winner = FindPrepared(update, program);
                if (winner is not null) return winner;
                EnsureNoLinks(finalStage, program);
                var invalid = finalStage + ".invalid-" + Guid.NewGuid().ToString("N");
                Directory.Move(finalStage, invalid);
                try { Directory.Move(temporary, finalStage); }
                catch
                {
                    if (!Directory.Exists(finalStage)) Directory.Move(invalid, finalStage);
                    throw;
                }
                finally { DeleteOwnedDirectory(invalid, program); }
            }
            else Directory.Move(temporary, finalStage);
            return prepared;
        }
        finally { DeleteOwnedDirectory(temporary, program); }
    }

    public static AppUpdateHandoff CreateHandoff(PreparedAppUpdate update)
    {
        if (!CanInstall) throw new PlatformNotSupportedException("In-app installation is currently available on Windows x64.");
        ValidatePrepared(update);
        var program = ResolveProgramDirectory(null);
        if (!SamePath(update.ProgramDirectory, program)) throw new InvalidDataException("The update belongs to another installation.");
        var directory = Path.Combine(UpdateRoot(program), "handoff-" + Guid.NewGuid().ToString("N"));
        var helper = Path.Combine(directory, "helper");
        Directory.CreateDirectory(helper);
        AppUpdatePlan? plan = null;
        try
        {
            CopyHelper(program, helper);
            var processPath = Environment.ProcessPath ?? throw new IOException("The application executable could not be located.");
            var dotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? processPath : null;
            var restart = Path.Combine(program, "packframe.exe");
            if (!File.Exists(restart)) throw new IOException("The PackFrame executable could not be located.");
            using var parent = Process.GetCurrentProcess();
            plan = new AppUpdatePlan(update, parent.Id, parent.StartTime.ToUniversalTime().Ticks,
                directory, Guid.NewGuid().ToString("N"), restart, dotnet);
            var planPath = Path.Combine(directory, "plan.json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, PackFrameJsonContext.Default.AppUpdatePlan));
            var start = new ProcessStartInfo
            {
                FileName = dotnet ?? Path.Combine(helper, Path.GetFileName(processPath)),
                WorkingDirectory = helper, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (dotnet is not null) start.ArgumentList.Add(Path.Combine(helper, "packframe.dll"));
            start.ArgumentList.Add("--apply-app-update");
            start.ArgumentList.Add(planPath);
            using var helperProcess = Process.Start(start) ?? throw new IOException("The update helper could not start.");
            WaitForHelperReady(plan, () => helperProcess.HasExited, TimeSpan.FromSeconds(10));
            return new AppUpdateHandoff(plan);
        }
        catch
        {
            if (plan is not null) new AppUpdateHandoff(plan).Cancel();
            DeleteOwnedDirectory(directory, program);
            throw;
        }
    }

    public static AppUpdateResult? TakeLastResult(string? programDirectory = null)
    {
        var path = Path.Combine(UpdateRoot(ResolveProgramDirectory(programDirectory)), "result.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
            var result = JsonSerializer.Deserialize(File.ReadAllText(path), PackFrameJsonContext.Default.AppUpdateResult);
            File.Delete(path);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    public static bool TryRunHelper(string[] args)
    {
        if (args.Length == 0 || args[0] != "--apply-app-update") return false;
        try
        {
            if (!OperatingSystem.IsWindows() || args.Length != 2) throw new InvalidDataException("Invalid update helper arguments.");
            var path = Path.GetFullPath(args[1]);
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Invalid update plan.");
            var plan = JsonSerializer.Deserialize(File.ReadAllText(path), PackFrameJsonContext.Default.AppUpdatePlan)
                ?? throw new InvalidDataException("The update plan is empty.");
            ValidatePlan(plan, path, AppContext.BaseDirectory);
            FileTransactions.WriteAtomic(Path.Combine(plan.HandoffDirectory, "ready"),
                temporary => File.WriteAllText(temporary, plan.CommitToken));
            if (!ApplyWhenReadyAsync(plan, () => ParentExited(plan), () => Restart(plan),
                TimeSpan.FromMinutes(2)).GetAwaiter().GetResult()) Environment.ExitCode = 1;
        }
        catch (Exception exception)
        {
            AppLog.Error("Apply application update", exception);
            Environment.ExitCode = 1;
        }
        return true;
    }

    // Injectable gate/restart keeps the real filesystem transaction testable without
    // launching or closing any application in regression checks.
    internal static async Task<bool> ApplyWhenReadyAsync(AppUpdatePlan plan, Func<bool> parentExited,
        Action restart, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var program = ResolveProgramDirectory(plan.Update.ProgramDirectory);
        EnsureUnder(UpdateRoot(program), plan.HandoffDirectory);
        EnsureNoLinks(plan.HandoffDirectory, program);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasMarker(plan, "cancel")) return false;
            if (HasMarker(plan, "commit") && parentExited()) break;
            if (timer.Elapsed >= timeout) return false;
            await Task.Delay(100, cancellationToken);
        }
        var backup = Path.Combine(plan.HandoffDirectory, "backup");
        try
        {
            ApplyPrepared(plan.Update, backup);
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            TryWriteResult(plan, new AppUpdateResult(false, plan.Update.Tag, message, backup));
            TryWriteHelperLog(plan, exception);
            if (exception is not AggregateException) restart();
            return false;
        }
        TryWriteResult(plan, new AppUpdateResult(true, plan.Update.Tag, null, backup));
        try { restart(); }
        catch (Exception exception)
        {
            TryWriteResult(plan, new AppUpdateResult(false, plan.Update.Tag,
                "The update was installed, but PackFrame could not restart: " + exception.Message, backup));
            TryWriteHelperLog(plan, exception);
            return false;
        }
        return true;
    }

    internal static void WaitForHelperReady(AppUpdatePlan plan, Func<bool> helperExited, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            if (helperExited()) throw new IOException("The update helper exited before it was ready. PackFrame is still running.");
            if (HasMarker(plan, "ready")) return;
            if (timer.Elapsed >= timeout) throw new IOException("The update helper did not become ready. PackFrame is still running.");
            Thread.Sleep(50);
        }
    }

    internal static void ApplyPrepared(PreparedAppUpdate update, string backupDirectory,
        Action<int>? beforeReplace = null)
    {
        ValidatePrepared(update);
        var program = ResolveProgramDirectory(update.ProgramDirectory);
        EnsureUnder(UpdateRoot(program), backupDirectory);
        EnsureNoLinks(backupDirectory, program);
        Directory.CreateDirectory(UpdateRoot(program));
        using var updateLock = new FileStream(Path.Combine(UpdateRoot(program), "update.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.CreateDirectory(backupDirectory);
        var applied = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var file in update.Files)
            {
                var target = PayloadPath(program, file.Path);
                var source = PayloadPath(Path.Combine(update.StageDirectory, "payload"), file.Path);
                EnsureNoLinks(target, program);
                var backup = File.Exists(target) ? PayloadPath(backupDirectory, file.Path) : null;
                if (backup is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: false);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                beforeReplace?.Invoke(applied.Count);
                FileTransactions.WriteAtomic(target, temporary =>
                {
                    File.Copy(source, temporary);
                    if (!string.Equals(Hash(temporary), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The staged update file changed during installation.");
                });
                applied.Add((target, backup));
            }
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            foreach (var item in applied.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Backup is null) File.Delete(item.Target);
                    else FileTransactions.WriteAtomic(item.Target, temporary => File.Copy(item.Backup, temporary));
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (failures.Count > 1)
                throw new AggregateException("Update rollback failed. Restore the files from: " + backupDirectory, failures);
            throw;
        }
    }

    private static async Task<List<AppUpdateFile>> ExtractProgramAsync(string archive, string destination,
        CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 10000) throw new InvalidDataException("The update contains too many files.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            var name = NormalizeRelative(entry.FullName);
            if (!entries.TryAdd(name, entry)) throw new InvalidDataException("The update contains duplicate paths.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The update contains a filesystem link.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes) throw new InvalidDataException("The expanded update is too large.");
        }
        var executables = entries.Keys.Where(IsProgramEntry).ToList();
        if (executables.Count != 1) throw new InvalidDataException("The archive does not contain one recognized PackFrame program.");
        var executable = executables[0];
        var prefix = executable[..^"packframe.exe".Length];
        var files = new List<AppUpdateFile>();
        foreach (var (name, entry) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.EndsWith('/')) continue;
            var relative = name[prefix.Length..];
            if (IsProtected(relative) || !IsProgramPayload(relative)) continue;
            var path = PayloadPath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var input = entry.Open())
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    copied += read;
                    if (copied > entry.Length) throw new InvalidDataException("An update file exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (copied != entry.Length) throw new InvalidDataException("An update file is incomplete.");
            }
            files.Add(new AppUpdateFile(relative, entry.Length, Hash(path)));
        }
        var program = files.SingleOrDefault(file => file.Path.Equals("packframe.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The PackFrame executable is missing.");
        using (var stream = File.OpenRead(PayloadPath(destination, program.Path)))
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new InvalidDataException("The update does not contain a Windows executable.");
        return files;
    }

    private static void ValidatePrepared(PreparedAppUpdate update)
    {
        var program = ResolveProgramDirectory(update.ProgramDirectory);
        EnsureUnder(Path.Combine(UpdateRoot(program), "cache"), update.StageDirectory);
        EnsureNoLinks(update.StageDirectory, program);
        if (update.Files is null || update.Files.Count is < 1 or > 10000 ||
            update.Files.Any(file => file is null || string.IsNullOrWhiteSpace(file.Path) ||
                file.Length < 0 || file.Length > MaximumExpandedBytes ||
                file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) ||
            update.Files.Count(file => file.Path.Equals("packframe.exe", StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidDataException("The prepared update manifest is invalid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in update.Files)
        {
            if (IsProtected(file.Path) || !IsProgramPayload(file.Path) || !names.Add(NormalizeRelative(file.Path)))
                throw new InvalidDataException("The prepared update contains a protected or duplicate path.");
            var source = PayloadPath(Path.Combine(update.StageDirectory, "payload"), file.Path);
            EnsureNoLinks(source, program);
            if (!File.Exists(source) || new FileInfo(source).Length != file.Length ||
                !string.Equals(Hash(source), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The prepared update was changed or is incomplete.");
        }
    }

    private static void ValidatePlan(AppUpdatePlan plan, string path, string helperDirectory)
    {
        var program = ResolveProgramDirectory(plan.Update.ProgramDirectory);
        var handoff = Path.GetFullPath(plan.HandoffDirectory);
        EnsureUnder(UpdateRoot(program), handoff);
        if (!SamePath(Path.Combine(handoff, "plan.json"), path) ||
            !SamePath(Path.Combine(handoff, "helper"), helperDirectory) ||
            !SamePath(plan.RestartExecutable, Path.Combine(program, "packframe.exe")) ||
            plan.CommitToken.Length != 32 || plan.ParentId <= 0)
            throw new InvalidDataException("The update handoff is invalid.");
        EnsureNoLinks(handoff, program);
        ValidatePrepared(plan.Update);
    }

    private static void CopyHelper(string program, string helper)
    {
        foreach (var source in Directory.EnumerateFiles(program))
        {
            if (IsProtected(Path.GetFileName(source)) || !IsProgramPayload(Path.GetFileName(source))) continue;
            EnsureNoLinks(source, program);
            File.Copy(source, Path.Combine(helper, Path.GetFileName(source)));
        }
        var runtimes = Path.Combine(program, "runtimes");
        if (!Directory.Exists(runtimes)) return;
        foreach (var source in Directory.EnumerateFiles(runtimes, "*", SearchOption.AllDirectories))
        {
            EnsureNoLinks(source, program);
            var target = PayloadPath(helper, Path.GetRelativePath(program, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
    }

    private static bool ParentExited(AppUpdatePlan plan)
    {
        try
        {
            using var process = Process.GetProcessById(plan.ParentId);
            return process.HasExited || process.StartTime.ToUniversalTime().Ticks != plan.ParentStartedUtcTicks;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private static void Restart(AppUpdatePlan plan)
    {
        var start = new ProcessStartInfo { FileName = plan.RestartExecutable,
            WorkingDirectory = plan.Update.ProgramDirectory, UseShellExecute = false };
        using var process = Process.Start(start) ?? throw new IOException("PackFrame could not restart. Open it from its installation directory.");
    }

    private static bool HasMarker(AppUpdatePlan plan, string name)
    {
        var path = Path.Combine(plan.HandoffDirectory, name);
        return File.Exists(path) && new FileInfo(path).Length <= 64 && File.ReadAllText(path) == plan.CommitToken;
    }

    private static void WriteResult(string program, AppUpdateResult result)
    {
        var path = Path.Combine(UpdateRoot(program), "result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileTransactions.WriteAtomic(path, temporary => File.WriteAllText(temporary,
            JsonSerializer.Serialize(result, PackFrameJsonContext.Default.AppUpdateResult)));
    }

    private static void TryWriteResult(AppUpdatePlan plan, AppUpdateResult result)
    {
        try { WriteResult(plan.Update.ProgramDirectory, result); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { TryWriteHelperLog(plan, exception); }
    }

    private static void TryWriteHelperLog(AppUpdatePlan plan, Exception exception)
    {
        try { File.AppendAllText(Path.Combine(plan.HandoffDirectory, "update-error.log"), exception + Environment.NewLine); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void ValidateAsset(UpdateAsset asset)
    {
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !IsReleaseDownload(uri) ||
            asset.Size is <= 0 or > MaximumArchiveBytes || string.IsNullOrEmpty(asset.Sha256) || asset.Sha256.Length != 64 ||
            !asset.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("The release update asset is invalid.");
    }

    private static bool IsReleaseDownload(Uri uri) =>
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/ZXUW/Rimworld-PackFrame/releases/download/", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/zxuw/Rimworld-PackFrame/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static bool IsProgramEntry(string name)
    {
        if (!name.EndsWith("packframe.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = name.Split('/');
        if (parts[^1].Equals("packframe.exe", StringComparison.OrdinalIgnoreCase) && parts.Length <= 2) return true;
        const string suffix = "app/PackFrame/bin/Release/net10.0/packframe.exe";
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        return name[..^suffix.Length].Count(character => character == '/') <= 1;
    }

    private static bool IsProtected(string relative)
    {
        var name = NormalizeRelative(relative);
        var first = name.Split('/')[0];
        return first.Equals("packframe.json", StringComparison.OrdinalIgnoreCase) ||
            first.StartsWith("packframe.log", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("packframe.instance.json", StringComparison.OrdinalIgnoreCase) ||
            new[] { "runtime", "instance", "profile", "mods", "saves", "config", "tools", ".git", "input profile", "output profile" }
                .Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsProgramPayload(string relative)
    {
        var name = NormalizeRelative(relative);
        var parts = name.Split('/');
        if (parts.Length > 1 && !parts[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase)) return false;
        return name.Equals("packframe.exe", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
            (parts.Length == 1 && (name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
                                  name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizeRelative(string relative)
    {
        var value = relative.Replace('\\', '/');
        if (value.Length == 0 || value.StartsWith('/') || value.Contains(':') || value.Contains('\0'))
            throw new InvalidDataException("The update contains an absolute or invalid path.");
        var parts = value.TrimEnd('/').Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')))
            throw new InvalidDataException("The update contains an unsafe path.");
        return string.Join('/', parts) + (value.EndsWith('/') ? "/" : "");
    }

    private static string PayloadPath(string root, string relative)
    {
        var normalized = NormalizeRelative(relative);
        var path = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureUnder(root, path);
        return path;
    }

    private static void EnsureUnder(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An update path escapes its expected directory.");
    }
    private static string ResolveProgramDirectory(string? programDirectory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(programDirectory ?? AppContext.BaseDirectory));
        if ((File.Exists(full) || Directory.Exists(full)) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Application updates cannot follow filesystem links: " + full);
        return full;
    }

    // Check links at and inside stopAt; OS ancestor symlinks (e.g. macOS /var -> /private/var) are outside app control.
    private static void EnsureNoLinks(string path, string stopAt)
    {
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stopAt));
        for (var item = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
             item is not null;
             item = Path.GetDirectoryName(item))
        {
            if ((File.Exists(item) || Directory.Exists(item)) &&
                (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Application updates cannot follow filesystem links: " + item);
            if (SamePath(item, boundary)) break;
        }
    }
    private static string UpdateRoot(string program) => Path.Combine(program, "runtime", "app-updates");
    private static string CacheDirectory(string program, string tag, string hash) =>
        Path.Combine(UpdateRoot(program), "cache", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag + "\n" + hash))).ToLowerInvariant());
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static bool SamePath(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
        StringComparison.OrdinalIgnoreCase);
    private static void DeleteOwnedDirectory(string path, string stopAt)
    {
        try
        {
            EnsureNoLinks(path, stopAt);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidDataException) { }
    }
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackFrame-updater");
        return client;
    }
}
