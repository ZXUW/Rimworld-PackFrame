using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace PackFrame;

internal sealed class ModIdentificationWindow : Window
{
    private readonly IReadOnlyList<ModInfo> _mods;
    private readonly string? _language;
    private readonly TextBlock _summary;
    private readonly TextBox _filter;
    private readonly ListBox _results;
    private List<(ModIdentification Result, string Text)> _rows = new();

    internal ModIdentificationWindow(IReadOnlyList<ModInfo> mods, string? language)
    {
        _mods = mods;
        _language = language;
        Title = T("Mod identification scan", "模组识别扫描", "模組識別掃描", "MOD識別スキャン");
        Width = 1100;
        Height = 760;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;

        _summary = new TextBlock { Text = T("Scanning...", "扫描中……", "掃描中……", "スキャン中…"),
            Foreground = ThemeService.SecondaryTextBrush };
        _filter = new TextBox { PlaceholderText = T("Filter results", "筛选结果", "篩選結果", "結果を絞り込む") };
        _results = new ListBox { MinHeight = 200 };
        _filter.TextChanged += (_, _) => UpdateVisibleRows();

        var close = new Button
        {
            Content = T("Close", "关闭", "關閉", "閉じる"),
            IsCancel = true,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        close.Click += (_, _) => Close();
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16),
            Children =
            {
                _summary,
                new Panel { [Grid.RowProperty] = 1, Children = { _filter } },
                new Panel { [Grid.RowProperty] = 2, Children = { _results } },
                new Panel { [Grid.RowProperty] = 3, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } }
            }
        };
        Opened += (_, _) => _ = RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        try
        {
            var results = await Task.Run(() => ModIdentificationScanner.Scan(_mods));
            if (!IsVisible) return;
            _rows = results.Select(result => (result, Format(result))).ToList();
            var translationCount = results.Count(result => result.TranslationNameSignal ||
                result.TranslationIdSignal || result.LegacyTranslationHeuristic);
            _summary.Text = T(
                $"Scanned {results.Count} unique mods; translation signals: {translationCount}. Results are evidence only, not classifications.",
                $"扫描 {results.Count} 个唯一模组；汉化信号 {translationCount} 个。以下为识别证据，不是分类结果。",
                $"掃描 {results.Count} 個唯一模組；漢化訊號 {translationCount} 個。以下為識別證據，不是分類結果。",
                $"{results.Count} 個のMODをスキャン。翻訳シグナル {translationCount} 件。分類ではなく識別根拠を表示します。");
            UpdateVisibleRows();
        }
        catch (Exception exception)
        {
            AppLog.Error("Run mod identification scan", exception);
            _summary.Text = T("Scan failed. See the log for details.", "扫描失败，详情请查看日志。", "掃描失敗，詳情請查看日誌。", "スキャンに失敗しました。ログを確認してください。");
        }
    }

    private void UpdateVisibleRows()
    {
        var query = _filter.Text?.Trim();
        _results.ItemsSource = string.IsNullOrEmpty(query)
            ? _rows.Select(row => row.Text).ToList()
            : _rows.Where(row => row.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Select(row => row.Text).ToList();
    }

    private string Format(ModIdentification item)
    {
        var facts = new List<string>();
        Add(item.TranslationNameSignal, "名称疑似汉化");
        Add(item.TranslationIdSignal, "PackageId疑似汉化");
        Add(item.LegacyTranslationHeuristic, "旧检测判定翻译包");
        Add(item.HasLanguageFiles, "Languages目录");
        Add(item.HasRaceDef, "种族Def");
        Add(item.HasXenotypeDef, "XenotypeDef");
        Add(item.HasHairDef, "HairDef");
        Add(item.HasApparelDef, "服装Def");
        Add(item.HasFurnitureDef, "家具Def");
        Add(item.HasBuildingDef, "建筑Def");
        Add(item.HasGeneDef, "GeneDef");
        Add(item.HasFactionDef, "FactionDef");
        Add(item.HasPatchFiles, "Patch目录");
        Add(item.HasAssemblies, "程序集");
        var relations = new List<string>();
        AddList(relations, "硬前置", item.HardDependencies);
        AddList(relations, "加载提示", item.LoadAfter);
        AddList(relations, "同作者", item.SameAuthorPackageIds);
        AddList(relations, "标题前缀", item.TitleBracketTags);
        AddList(relations, "别名前缀", item.AliasBracketTags);
        AddList(relations, "文件夹前缀", item.FolderBracketTags);
        AddList(relations, "关键词信号", item.Keywords);
        return $"{item.Name}\nPackageId: {item.PackageId}\n作者: {item.Author}\n识别: {(facts.Count == 0 ? "无明确内容信号" : string.Join(" · ", facts))}\n关系/关键词: {(relations.Count == 0 ? "无" : string.Join(" | ", relations))}";

        void Add(bool value, string label) { if (value) facts.Add(label); }
        static void AddList(List<string> target, string label, IReadOnlyList<string> values)
        {
            if (values.Count > 0) target.Add(label + "=" + string.Join(", ", values));
        }
    }

    private string T(string en, string zh, string tw, string ja) => OperationText.Choose(_language, en, zh, tw, ja);
}
