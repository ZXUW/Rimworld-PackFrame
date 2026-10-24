using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PackFrame;

public partial class ModpackWindow
{
    private bool _committingUpdate;
    private string T(string en, string zh, string hant, string ja) => OperationText.Choose(_language, en, zh, hant, ja);
    private void RefreshUpdateControls()
    {
        var valid = _currentInstance is { IsOfficial: false, IsScratch: false, IsDamaged: false };
        var match = valid && _selectedPack is not null && PackUpdateService.Matches(_currentInstance!, _selectedPack);
        var check = Find<Button>("CheckUpdateButton");
        check.Content = T("Check updates", "检查更新", "檢查更新", "更新を確認");
        check.IsVisible = match; check.IsEnabled = !_busy;
        var bind = Find<Button>("BindPackButton");
        bind.Content = T("Link this pack", "关联此合集", "關聯此合集", "このパックに関連付け");
        bind.IsVisible = valid && !match && _selectedPack?.Id is not null;
        bind.IsEnabled = !_busy && string.IsNullOrWhiteSpace(_currentInstance?.Metadata?.PackSource);
        var restore = Find<Button>("RestoreUpdateButton");
        restore.Content = T("Restore previous update", "恢复上次更新", "還原上次更新", "前回の更新を復元");
        restore.IsVisible = valid && PackUpdateService.CanRestore(_currentInstance!); restore.IsEnabled = !_busy;
        Find<TextBlock>("UpdateVersionText").Text = match
            ? T("Installed: ", "已安装：", "已安裝：", "インストール済み: ") +
                (_currentInstance!.Metadata?.InstalledVersion ?? _currentInstance.Metadata?.InstalledRevision?.ToString() ?? "?")
            : string.Empty;
    }

    private bool CanChangeInstance()
    {
        if (_busy || _currentInstance is null || _currentInstance.IsDamaged) return false;
        if (GameLaunchService.IsRunning() || InstanceStore.HasPendingMods)
        {
            Find<TextBlock>("StatusText").Text = InstanceStore.HasPendingMods
                ? Localization.UnsavedChanges(_language) : Localization.ModpackGameRunning(_language);
            return false;
        }
        return true;
    }

    private async void OnBindPackClick(object? sender, RoutedEventArgs e)
    {
        if (!CanChangeInstance() || _selectedPack is null) return;
        var confirm = new ConfirmWindow(T("Link this instance? Existing files remain unowned; conflicts will require a choice.",
            "关联当前实例？现有文件不会自动归属合集，更新时有冲突会询问。", "關聯目前實例？現有檔案不會自動歸屬合集，衝突時會詢問。",
            "このインスタンスを関連付けますか？既存ファイルの競合は確認されます。"),
            T("Link", "关联", "關聯", "関連付け"), Localization.CancelButton(_language));
        if (!await confirm.ShowDialog<bool>(this)) return;
        try { PackUpdateService.BindLegacy(_currentInstance!, _selectedPack); ConfigApplied = true; RefreshUpdateControls(); }
        catch (Exception exception) { Find<TextBlock>("StatusText").Text = exception.Message; }
    }

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (!CanChangeInstance() || _selectedItem is null || _indexUri is null || _game is null || _packRoot is null) return;
        _busy = true;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        SetControls(false);
        string? staging = null;
        try
        {
            Find<TextBlock>("StatusText").Text = T("Checking updates...", "正在检查更新...", "正在檢查更新...", "更新を確認中...");
            var pack = await ModpackService.LoadPackAsync(_selectedItem.Entry, _indexUri, cancellation.Token);
            if (!PackUpdateService.Matches(_currentInstance!, pack)) throw new IOException("Pack identity changed.");
            if (pack.Revision is null || pack.Revision <= 0)
            { Find<TextBlock>("StatusText").Text = T("No update version provided.", "此合集尚未提供更新版本。", "此合集尚未提供更新版本。", "更新バージョンがありません。"); return; }
            if (pack.Revision <= _currentInstance!.Metadata!.InstalledRevision)
            { Find<TextBlock>("StatusText").Text = T("Up to date, or local revision is newer.", "已是最新版本，或本地版本较新。", "已是最新版本，或本機版本較新。", "最新、またはローカル版の方が新しいです。"); return; }
            var prompt = $"{pack.Name}: {pack.Version ?? pack.Revision.ToString()}\n{pack.Changelog}\n" +
                T("Download and review changes?", "下载更新并查看变更？", "下載更新並查看變更？", "更新をダウンロードして確認しますか？");
            if (!await new ConfirmWindow(prompt, T("Download", "下载", "下載", "ダウンロード"), Localization.CancelButton(_language)).ShowDialog<bool>(this)) return;
            staging = Path.Combine(_packRoot, "runtime", "install-" + Guid.NewGuid().ToString("N"));
            var result = await ModpackService.InstallAsync(pack, _game, _packRoot, Path.Combine(staging, "Mods"),
                useSteamCmd: _currentInstance.IsSteamCmd,
                status: text => Dispatcher.UIThread.Post(() => { if (!_closed) Find<TextBlock>("StatusText").Text = text; }),
                cancellationToken: cancellation.Token, language: _language);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_currentInstance.IsSteamCmd && (result.UnresolvedWorkshopCount > 0 || pack.Mods.Any(mod => ModpackService.ParseWorkshopId(mod.WorkshopId ?? mod.WorkshopUrl) is { } id &&
                !File.Exists(Path.Combine(_game.WorkshopPath ?? "", id.ToString(), "About", "About.xml"))))
                )
                throw new IOException(T("Waiting for Steam downloads. Retry after Steam finishes.", "等待 Steam 下载完成后重试。", "等待 Steam 下載完成後重試。", "Steam のダウンロード完了後に再試行してください。"));
            var plan = await Task.Run(() => PackUpdateService.Plan(_currentInstance, result));
            var choices = await new UpdateChoicesWindow(plan, _language).ShowDialog<UpdateChoices?>(this);
            if (choices is null) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (GameLaunchService.IsRunning() || InstanceStore.HasPendingMods) throw new IOException("Game is running or unsaved changes exist.");
            Find<TextBlock>("StatusText").Text = T("Applying update...", "正在应用更新...", "正在套用更新...", "更新を適用中...");
            Find<Button>("CancelOperationButton").IsVisible = false;
            _committingUpdate = true;
            await Task.Run(() => PackUpdateService.Apply(_packRoot, _currentInstance, result, choices.KeepLocal, choices.KeepOrder));
            ConfigApplied = true;
            if (!_closed) Find<TextBlock>("StatusText").Text = T("Update complete.", "更新完成。", "更新完成。", "更新完了。");
        }
        catch (OperationCanceledException) { if (!_closed) Find<TextBlock>("StatusText").Text = Localization.OperationCancelled(_language); }
        catch (Exception exception) { AppLog.Error("Update instance", exception); if (!_closed) Find<TextBlock>("StatusText").Text = exception.Message; }
        finally
        {
            if (staging is not null) FileTransactions.TryDeleteDirectory(staging);
            _committingUpdate = false;
            _operationCancellation = null; _busy = false;
            if (!_closed) SetControls(true);
        }
    }

    private async void OnRestoreUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (!CanChangeInstance()) return;
        if (!await new ConfirmWindow(T("Restore files changed by the last update? Saves and external Steam mods are not restored.",
            "恢复上次更新涉及的文件？不恢复存档或外部 Steam 模组。", "還原上次更新涉及的檔案？不還原存檔或外部 Steam 模組。",
            "前回の更新ファイルを復元しますか？セーブと外部 Steam MOD は対象外です。"),
            T("Restore", "恢复", "還原", "復元"), Localization.CancelButton(_language)).ShowDialog<bool>(this)) return;
        _busy = true; _committingUpdate = true; SetControls(false);
        try { await Task.Run(() => PackUpdateService.Restore(_currentInstance!)); ConfigApplied = true; }
        catch (Exception exception) { AppLog.Error("Restore update", exception); Find<TextBlock>("StatusText").Text = exception.Message; }
        finally { _committingUpdate = false; _busy = false; if (!_closed) SetControls(true); }
    }
}
