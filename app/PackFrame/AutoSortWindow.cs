using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace PackFrame;

internal sealed record AutoSortScanOptions(
    IReadOnlySet<string> ConfirmedFuzzyDeps,
    bool UseCommunityRules,
    IReadOnlyDictionary<string, string> CycleResolutions
);

internal sealed record AutoSortScanSnapshot(
    PrerequisiteScanResult Scan,
    IReadOnlyList<CorePrerequisiteItem> CoreItems,
    CollectedRulesResult RulesResult,
    TopologicalSortPlan SortPlan
);

internal sealed class AutoSortWindow : Window
{
    private readonly string? _language;
    private readonly Func<AutoSortScanOptions, AutoSortScanSnapshot> _rescan;
    private readonly Action<IReadOnlyList<string>> _enableMods;
    private readonly Action<IReadOnlyList<string>> _disableMods;
    private readonly Action<IReadOnlySet<string>> _applyCoreOrder;
    private readonly Action<IReadOnlyList<string>> _applySortedOrder;
    private readonly ContentControl _stepHeaderHost;
    private readonly ScrollViewer _bodyScroll;
    private readonly TextBlock _footerStatus;
    private readonly Button _prevButton;
    private readonly Button _applyAllButton;
    private readonly Button _nextButton;
    private PrerequisiteScanResult _scan = null!;
    private IReadOnlyList<CorePrerequisiteItem> _coreItems = Array.Empty<CorePrerequisiteItem>();
    private CollectedRulesResult _rulesResult = null!;
    private TopologicalSortPlan _sortPlan = null!;
    private readonly HashSet<string> _confirmedFuzzyDeps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rejectedFuzzyDeps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cycleResolutions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string PackageId, CheckBox Box, bool IsFuzzy, string? RawDepId)> _prereqChecks = new();
    private readonly List<(string PackageId, CheckBox Box)> _coreChecks = new();
    private bool _useCommunityRules = true;
    private bool _transExpanded;
    private bool _satisfiedRulesExpanded;
    private bool _showFullOrderInStep4;
    private int _currentStep = 1;

    internal AutoSortWindow(
        Func<AutoSortScanOptions, AutoSortScanSnapshot> rescan,
        Action<IReadOnlyList<string>> enableMods,
        Action<IReadOnlyList<string>> disableMods,
        Action<IReadOnlySet<string>> applyCoreOrder,
        Action<IReadOnlyList<string>> applySortedOrder,
        string? language)
    {
        _language = language;
        _rescan = rescan;
        _enableMods = enableMods;
        _disableMods = disableMods;
        _applyCoreOrder = applyCoreOrder;
        _applySortedOrder = applySortedOrder;

        Title = T("Auto sort", "自动排序", "自動排序", "自動ソート");
        Width = 680;
        Height = 500;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ThemeService.BackgroundBrush;
        Foreground = ThemeService.PrimaryTextBrush;
        FontSize = 14;

        _stepHeaderHost = new ContentControl();

        _footerStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeService.SecondaryTextBrush
        };

        var cancelButton = new Button
        {
            Content = Localization.CancelButton(_language),
            IsCancel = true,
            MinHeight = 32,
            Padding = new Thickness(18, 6),
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        cancelButton.Click += (_, _) => Close();

        _prevButton = new Button
        {
            Content = T("Previous", "上一步", "上一步", "前へ"),
            MinHeight = 32,
            Padding = new Thickness(18, 6),
            IsVisible = false,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        _prevButton.Click += (_, _) =>
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                ReloadAndRebuild(preserveScroll: false);
            }
        };

        _applyAllButton = new Button
        {
            Content = T("Apply selected", "一键应用", "一鍵應用", "一括適用"),
            MinHeight = 32,
            Padding = new Thickness(18, 6),
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        _applyAllButton.Click += (_, _) => OnApplySelectedClick();

        _nextButton = new Button
        {
            Content = T("Next step", "下一步", "下一步", "次へ"),
            IsDefault = true,
            MinHeight = 32,
            Padding = new Thickness(18, 6),
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
        };
        _nextButton.Click += (_, _) =>
        {
            if (_currentStep < 4)
            {
                _currentStep++;
                ReloadAndRebuild(preserveScroll: false);
            }
            else
            {
                if (_sortPlan.MovedRows.Count > 0)
                {
                    _applySortedOrder(_sortPlan.OrderedPackageIds);
                }
                Close();
            }
        };

        var footer = new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    _footerStatus,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { cancelButton, _prevButton, _applyAllButton, _nextButton },
                        [Grid.ColumnProperty] = 1
                    }
                }
            }
        };

        _bodyScroll = new ScrollViewer();
        _bodyScroll.PointerWheelChanged += (_, _) => _scrollRestoreGeneration++;
        var bodyBorder = new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 10),
            Child = _bodyScroll,
            [Grid.RowProperty] = 1
        };

        Content = new Grid
        {
            Margin = new Thickness(14, 12, 14, 12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { _stepHeaderHost, bodyBorder, footer }
        };
        Grid.SetRow(footer, 2);

        ReloadAndRebuild();
    }

    private void OnApplySelectedClick()
    {
        if (_currentStep == 1)
        {
            var checkedItems = _prereqChecks
                .Where(c => c.Box.IsChecked == true)
                .ToList();
            foreach (var item in checkedItems.Where(c => c.IsFuzzy))
            {
                _confirmedFuzzyDeps.Add(item.PackageId);
                if (!string.IsNullOrWhiteSpace(item.RawDepId))
                {
                    _confirmedFuzzyDeps.Add(item.RawDepId!);
                }
            }

            var prereqIds = checkedItems
                .Select(c => c.PackageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (prereqIds.Count > 0)
            {
                _enableMods(prereqIds);
            }
        }
        else if (_currentStep == 2)
        {
            var selectedIds = new HashSet<string>(
                _coreChecks.Where(c => c.Box.IsChecked == true).Select(c => c.PackageId),
                StringComparer.OrdinalIgnoreCase);
            if (selectedIds.Count > 0)
            {
                _applyCoreOrder(selectedIds);
            }
        }
        else if (_currentStep == 4)
        {
            if (_sortPlan.MovedRows.Count > 0)
            {
                _applySortedOrder(_sortPlan.OrderedPackageIds);
            }
        }

        ReloadAndRebuild();
    }

    private int _scrollRestoreGeneration;

    private void ReloadAndRebuild(bool preserveScroll = true)
    {
        var savedOffset = preserveScroll ? _bodyScroll.Offset : default;
        var generation = ++_scrollRestoreGeneration;
        var opts = new AutoSortScanOptions(_confirmedFuzzyDeps, _useCommunityRules, _cycleResolutions);
        var snap = _rescan(opts);
        _scan = snap.Scan;
        _coreItems = snap.CoreItems;
        _rulesResult = snap.RulesResult;
        _sortPlan = snap.SortPlan;
        _prereqChecks.Clear();
        _coreChecks.Clear();
        _stepHeaderHost.Content = BuildStepFlowchart(_currentStep);
        _prevButton.IsVisible = _currentStep > 1;
        _applyAllButton.IsVisible = _currentStep is 1 or 2 or 4;
        _applyAllButton.Content = _currentStep == 4
            ? T("Apply sort", "应用排序", "應用排序", "ソート適用")
            : T("Apply selected", "一键应用", "一鍵應用", "一括適用");
        _nextButton.Content = _currentStep < 4
            ? T("Next step", "下一步", "下一步", "次へ")
            : (_sortPlan.MovedRows.Count > 0
                ? T("Apply & close", "应用并关闭", "應用並關閉", "適用して閉じる")
                : T("Finish", "完成", "完成", "完了"));
        _bodyScroll.Content = _currentStep switch
        {
            1 => BuildStep1Body(),
            2 => BuildStep2Body(),
            3 => BuildStep3Body(),
            _ => BuildStep4Body()
        };
        UpdateFooterStatus();

        void ApplyScroll()
        {
            if (_scrollRestoreGeneration != generation) return;
            var maxY = Math.Max(0, _bodyScroll.Extent.Height - _bodyScroll.Viewport.Height);
            var maxX = Math.Max(0, _bodyScroll.Extent.Width - _bodyScroll.Viewport.Width);
            _bodyScroll.Offset = preserveScroll
                ? new Vector(Math.Clamp(savedOffset.X, 0, maxX), Math.Clamp(savedOffset.Y, 0, maxY))
                : default;
        }

        ApplyScroll();
        Dispatcher.UIThread.Post(ApplyScroll, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(ApplyScroll, DispatcherPriority.Background);
    }

    private Control BuildStepFlowchart(int currentStep)
    {
        var labels = new[]
        {
            T("Prerequisites", "补充前置", "補充前置", "前提補充"),
            T("Core prerequisites", "核心前置", "核心前置", "コア前提"),
            T("Order rules", "收集规则", "收集規則", "ルール収集"),
            T("Apply sort", "应用排序", "應用排序", "ソート適用")
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*,Auto"),
            Margin = new Thickness(8, 0, 8, 0)
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var isCurrent = i + 1 == currentStep;
            var isPassed = i + 1 < currentStep;
            var circle = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = ThemeService.BackgroundBrush,
                Stroke = (isCurrent || isPassed) ? ThemeService.PrimaryTextBrush : ThemeService.SecondaryBrush,
                StrokeThickness = isCurrent ? 2 : 1.5,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = labels[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = isCurrent ? ThemeService.PrimaryTextBrush : ThemeService.MutedTextBrush
            };

            var node = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { circle, text }
            };
            Grid.SetColumn(node, i * 2);
            grid.Children.Add(node);

            if (i < labels.Length - 1)
            {
                var line = new Border
                {
                    Height = 1,
                    Margin = new Thickness(6, 6, 6, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = ThemeService.SecondaryBrush
                };
                Grid.SetColumn(line, i * 2 + 1);
                grid.Children.Add(line);
            }
        }

        return grid;
    }

    private Control BuildStep1Body()
    {
        var panel = new StackPanel { Spacing = 8 };
        var fuzzyPending = _scan.FuzzyMatches
            .Where(item => !_rejectedFuzzyDeps.Contains(item.PackageId))
            .ToList();

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Scanned {_scan.EnabledCount} active mods · {_scan.InstalledMissing.Count} to enable · {fuzzyPending.Count} similar ID to confirm · {_scan.UninstalledMissing.Count} not installed",
                $"已扫描 {_scan.EnabledCount} 个启用模组 · 待启用前置 {_scan.InstalledMissing.Count} 个 · ID 相似待确认 {fuzzyPending.Count} 个 · 未安装 {_scan.UninstalledMissing.Count} 个",
                $"已掃描 {_scan.EnabledCount} 個啟用模組 · 待啟用前置 {_scan.InstalledMissing.Count} 個 · ID 相似待確認 {fuzzyPending.Count} 個 · 未安裝 {_scan.UninstalledMissing.Count} 個",
                $"有効MOD {_scan.EnabledCount} 件 · 前提追加 {_scan.InstalledMissing.Count} 件 · 類似ID確認 {fuzzyPending.Count} 件 · 未導入 {_scan.UninstalledMissing.Count} 件"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.SecondaryTextBrush
        });

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Prerequisites to enable ({_scan.InstalledMissing.Count})",
                $"需补充启用的前置 ({_scan.InstalledMissing.Count})",
                $"需補充啟用的前置 ({_scan.InstalledMissing.Count})",
                $"追加で有効化する前提MOD ({_scan.InstalledMissing.Count})"),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = ThemeService.PrimaryTextBrush
        });

        if (_scan.InstalledMissing.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    "No missing prerequisites found.",
                    "没有找到缺失的前置。",
                    "沒有找到缺失的前置。",
                    "不足している前提MODは見つかりませんでした。"),
                Foreground = ThemeService.MutedTextBrush
            });
        }
        else
        {
            var listStack = new StackPanel { Spacing = 2 };
            foreach (var item in _scan.InstalledMissing)
            {
                listStack.Children.Add(BuildPrerequisiteRow(item, canCheck: true, defaultChecked: true, isFuzzy: false));
            }

            panel.Children.Add(listStack);
        }

        if (fuzzyPending.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    $"Similar packageId / renamed prerequisites — please confirm individually ({fuzzyPending.Count})",
                    $"可能改过名的前置（ID 相似） ({fuzzyPending.Count})",
                    $"可能改過名的前置（ID 相似） ({fuzzyPending.Count})",
                    $"名前が変わった可能性がある前提MOD（ID が似ている） ({fuzzyPending.Count})"),
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = ThemeService.PrimaryTextBrush
            });

            var fuzzyStack = new StackPanel { Spacing = 2 };
            foreach (var item in fuzzyPending)
            {
                fuzzyStack.Children.Add(BuildPrerequisiteRow(item, canCheck: true, defaultChecked: false, isFuzzy: true));
            }

            panel.Children.Add(fuzzyStack);
        }

        if (_scan.UninstalledMissing.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    $"Uninstalled prerequisites ({_scan.UninstalledMissing.Count})",
                    $"未安装的前置 ({_scan.UninstalledMissing.Count})",
                    $"未安裝的前置 ({_scan.UninstalledMissing.Count})",
                    $"未導入の前提MOD ({_scan.UninstalledMissing.Count})"),
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = ThemeService.PrimaryTextBrush
            });

            var uninstalledStack = new StackPanel { Spacing = 2 };
            foreach (var item in _scan.UninstalledMissing)
            {
                uninstalledStack.Children.Add(BuildPrerequisiteRow(item, canCheck: false, defaultChecked: false, isFuzzy: false));
            }

            panel.Children.Add(uninstalledStack);
        }

        if (_scan.IgnoredTranslation.Count > 0)
        {
            var transStack = new StackPanel
            {
                Spacing = 2,
                IsVisible = _transExpanded
            };

            foreach (var item in _scan.IgnoredTranslation)
            {
                transStack.Children.Add(BuildPrerequisiteRow(
                    item,
                    canCheck: item.InstalledMod is not null,
                    defaultChecked: false,
                    isFuzzy: false));
            }

            var toggleText = new TextBlock { Foreground = ThemeService.SecondaryTextBrush };
            void UpdateToggleLabel(bool expanded)
            {
                var arrow = expanded ? "▼" : "▶";
                toggleText.Text = T(
                    $"{arrow} Satisfied bundle localization remaining targets ({_scan.IgnoredTranslation.Count})",
                    $"{arrow} 汉化模组已满足的其他前置 ({_scan.IgnoredTranslation.Count})",
                    $"{arrow} 漢化模組已滿足的其他前置 ({_scan.IgnoredTranslation.Count})",
                    $"{arrow} 充足済みの統合翻訳MODの残り前提 ({_scan.IgnoredTranslation.Count})");
            }
            UpdateToggleLabel(_transExpanded);

            var toggleBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 4),
                MinHeight = 28,
                Margin = new Thickness(0, 6, 0, 0),
                Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
                Content = toggleText
            };
            toggleBtn.Click += (_, _) =>
            {
                _transExpanded = !_transExpanded;
                transStack.IsVisible = _transExpanded;
                UpdateToggleLabel(_transExpanded);
            };

            panel.Children.Add(toggleBtn);
            panel.Children.Add(transStack);
        }

        return panel;
    }

    private Control BuildStep2Body()
    {
        var panel = new StackPanel { Spacing = 8 };
        var needsMoveCount = _coreItems.Count(i => i.NeedsMove);

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Identified {_coreItems.Count} large prerequisite(s) (Harmony / Core / Official DLCs / HugsLib) · {needsMoveCount} out of position",
                $"已识别 {_coreItems.Count} 个大前置（Harmony / Core / 官方 DLC / HugsLib） · 待归位 {needsMoveCount} 个",
                $"已識別 {_coreItems.Count} 個大前置（Harmony / Core / 官方 DLC / HugsLib） · 待歸位 {needsMoveCount} 個",
                $"システム基盤 {_coreItems.Count} 件を検出（Harmony / Core / 公式DLC / HugsLib） · 位置調整 {needsMoveCount} 件"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.SecondaryTextBrush
        });

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Core prerequisite top-tier order ({_coreItems.Count})",
                $"大前置置顶顺序 ({_coreItems.Count})",
                $"大前置置頂順序 ({_coreItems.Count})",
                $"コア前提の最上位配置順 ({_coreItems.Count})"),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = ThemeService.PrimaryTextBrush
        });

        if (_coreItems.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    "No core prerequisites found in active list.",
                    "当前启用列表中未检测到核心前置。",
                    "當前啟用列表中未檢測到核心前置。",
                    "有効リストにコア前提が見つかりません。"),
                Foreground = ThemeService.MutedTextBrush
            });
            return panel;
        }

        var listStack = new StackPanel { Spacing = 2 };
        foreach (var item in _coreItems)
        {
            listStack.Children.Add(BuildCoreRow(item));
        }

        panel.Children.Add(listStack);
        return panel;
    }

    private Control BuildCoreRow(CorePrerequisiteItem item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var checkBox = new CheckBox
        {
            IsChecked = item.NeedsMove,
            IsEnabled = item.NeedsMove,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        if (item.NeedsMove)
        {
            checkBox.IsCheckedChanged += (_, _) => UpdateFooterStatus();
            _coreChecks.Add((item.Mod.PackageId, checkBox));
        }
        grid.Children.Add(checkBox);

        var posText = item.NeedsMove
            ? $"#{item.CurrentIndex + 1} → #{item.TargetIndex + 1}"
            : $"#{item.CurrentIndex + 1}";
        var title = string.IsNullOrWhiteSpace(item.Mod.RowName) ? item.Mod.Name : item.Mod.RowName;
        var mainText = new TextBlock
        {
            Text = $"[{item.TierLabel}] {title} ({posText})  —  {item.Reason}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = item.NeedsMove ? ThemeService.PrimaryTextBrush : ThemeService.MutedTextBrush
        };
        ToolTip.SetTip(mainText, $"[{item.TierLabel}] {title} ({item.Mod.PackageId})\n{posText} · {item.Reason}");
        Grid.SetColumn(mainText, 1);
        grid.Children.Add(mainText);

        if (item.NeedsMove)
        {
            var moveBtn = new Button
            {
                Content = Localization.ApplyButton(_language),
                Padding = new Thickness(12, 2),
                MinHeight = 26,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
            };
            moveBtn.Click += (_, _) =>
            {
                _applyCoreOrder(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.Mod.PackageId });
                ReloadAndRebuild();
            };
            Grid.SetColumn(moveBtn, 2);
            grid.Children.Add(moveBtn);
        }
        else
        {
            var okText = new TextBlock
            {
                Text = T("In place", "已就位", "已就位", "配置済"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeService.MutedTextBrush
            };
            Grid.SetColumn(okText, 2);
            grid.Children.Add(okText);
        }

        return new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 3),
            Child = grid
        };
    }

    private Control BuildPrerequisiteRow(
        PrerequisiteItem item,
        bool canCheck,
        bool defaultChecked,
        bool isFuzzy)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var checkBox = new CheckBox
        {
            IsChecked = canCheck && defaultChecked,
            IsEnabled = canCheck,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        if (canCheck)
        {
            checkBox.IsCheckedChanged += (_, _) => UpdateFooterStatus();
            _prereqChecks.Add((item.PackageId, checkBox, isFuzzy, item.OriginalDepId));
        }
        grid.Children.Add(checkBox);

        var reqPrefix = T("Required by: ", "需求于：", "需求於：", "要求元：");
        var origInfo = isFuzzy && !string.IsNullOrWhiteSpace(item.OriginalDepId)
            ? T($" [Declared: {item.OriginalDepName ?? item.OriginalDepId} ({item.OriginalDepId})]",
                $" [原声明：{item.OriginalDepName ?? item.OriginalDepId} ({item.OriginalDepId})]",
                $" [原聲明：{item.OriginalDepName ?? item.OriginalDepId} ({item.OriginalDepId})]",
                $" [元指定：{item.OriginalDepName ?? item.OriginalDepId} ({item.OriginalDepId})]")
            : string.Empty;

        var mainText = new TextBlock
        {
            Text = $"{item.DisplayName} ({item.PackageId}){origInfo}  —  {reqPrefix}{string.Join("、", item.RequiredBy)}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = item.InstalledMod is not null ? ThemeService.PrimaryTextBrush : ThemeService.MutedTextBrush
        };
        ToolTip.SetTip(
            mainText,
            $"{item.DisplayName} ({item.PackageId}){origInfo}\n{reqPrefix}{string.Join("、", item.RequiredBy)}");
        Grid.SetColumn(mainText, 1);
        grid.Children.Add(mainText);

        if (item.InstalledMod is not null)
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var enableBtn = new Button
            {
                Content = isFuzzy
                    ? T("Use this", "就是这个", "就是這個", "これを採用")
                    : T("Enable", "启用", "啟用", "有効化"),
                Padding = new Thickness(12, 2),
                MinHeight = 26,
                Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
            };
            enableBtn.Click += (_, _) =>
            {
                if (isFuzzy)
                {
                    _confirmedFuzzyDeps.Add(item.PackageId);
                    if (!string.IsNullOrWhiteSpace(item.OriginalDepId))
                    {
                        _confirmedFuzzyDeps.Add(item.OriginalDepId!);
                    }
                }
                _enableMods(new[] { item.PackageId });
                ReloadAndRebuild();
            };
            actions.Children.Add(enableBtn);

            if (isFuzzy)
            {
                var ignoreBtn = new Button
                {
                    Content = T("Not this one", "不是它", "不是它", "これではない"),
                    Padding = new Thickness(10, 2),
                    MinHeight = 26,
                    Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
                };
                ignoreBtn.Click += (_, _) =>
                {
                    _rejectedFuzzyDeps.Add(item.PackageId);
                    ReloadAndRebuild();
                };
                actions.Children.Add(ignoreBtn);
            }

            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
        }
        else
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (item.WorkshopFileId is { } wsId)
            {
                var steamUrl = $"steam://url/CommunityFilePage/{wsId}";
                var webUrl = $"https://steamcommunity.com/sharedfiles/filedetails/?id={wsId}";
                var wsBtn = new Button
                {
                    Content = T("Workshop", "创意工坊", "創意工坊", "ワークショップ"),
                    Padding = new Thickness(10, 2),
                    MinHeight = 26,
                    Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
                };
                ToolTip.SetTip(wsBtn, webUrl);
                wsBtn.Click += (_, _) => OpenSteamOrWebUrl(steamUrl, webUrl);
                actions.Children.Add(wsBtn);
            }
            else if (item.SteamStoreAppId is { } appId)
            {
                var steamUrl = $"steam://store/{appId}";
                var webUrl = $"https://store.steampowered.com/app/{appId}/";
                var storeBtn = new Button
                {
                    Content = T("Steam Store", "Steam 商店", "Steam 商店", "Steam ストア"),
                    Padding = new Thickness(10, 2),
                    MinHeight = 26,
                    Theme = Application.Current?.FindResource("FlatButton") as ControlTheme
                };
                ToolTip.SetTip(storeBtn, webUrl);
                storeBtn.Click += (_, _) => OpenSteamOrWebUrl(steamUrl, webUrl);
                actions.Children.Add(storeBtn);
            }

            actions.Children.Add(new TextBlock
            {
                Text = T("Not installed", "未安装", "未安裝", "未導入"),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeService.MutedTextBrush
            });

            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
        }

        return new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 3),
            Child = grid
        };
    }

    private static void OpenSteamOrWebUrl(string steamUrl, string fallbackWebUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fallbackWebUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
    }

    private Control BuildStep3Body()
    {
        var panel = new StackPanel { Spacing = 8 };
        var r = _rulesResult;

        // Top conflict / cycle resolution bar (shown at the very top whenever rule cycles or incompatibilities exist)
        if (r.CyclePairs.Count > 0 || r.Conflicts.Count > 0)
        {
            var topConflictPanel = new StackPanel { Spacing = 8 };
            topConflictPanel.Children.Add(new TextBlock
            {
                Text = T(
                    $"Rule Conflicts Requiring Choice ({r.CyclePairs.Count} cycle pair(s), {r.Conflicts.Count} incompatible pair(s))",
                    $"存在规则冲突，请选择保留方案（循环依赖 {r.CyclePairs.Count} 组 · 互不兼容 {r.Conflicts.Count} 组）",
                    $"存在規則衝突，請選擇保留方案（循環依賴 {r.CyclePairs.Count} 組 · 互不兼容 {r.Conflicts.Count} 組）",
                    $"ルール競合の選択（循環依存 {r.CyclePairs.Count} 組 · 非互換 {r.Conflicts.Count} 組）"),
                FontWeight = FontWeight.SemiBold,
                Foreground = ThemeService.PrimaryTextBrush
            });

            foreach (var pair in r.CyclePairs)
            {
                topConflictPanel.Children.Add(BuildCycleChoiceCard(pair));
            }

            foreach (var pair in r.Conflicts)
            {
                topConflictPanel.Children.Add(BuildIncompatibleChoiceCard(pair));
            }

            panel.Children.Add(new Border
            {
                Background = ThemeService.BackgroundBrush,
                BorderBrush = ThemeService.PrimaryTextBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(10, 8),
                Child = topConflictPanel
            });
        }

        // Community Rules toggle row
        var communityToggle = new CheckBox
        {
            IsChecked = _useCommunityRules,
            MinHeight = 24,
            Content = new TextBlock
            {
                Text = T(
                    $"Enable RimSort Community Rules Database (CC0, {r.CommunityRuleCount} active rule(s))",
                    $"启用社区排序规则库（{r.CommunityRuleCount} 条有关）",
                    $"啟用社區排序規則庫（{r.CommunityRuleCount} 條有關）",
                    $"コミュニティルールDBを有効化（{r.CommunityRuleCount} 件が該当）"),
                Foreground = ThemeService.PrimaryTextBrush
            }
        };
        communityToggle.IsCheckedChanged += (_, _) =>
        {
            _useCommunityRules = communityToggle.IsChecked == true;
            ReloadAndRebuild();
        };
        panel.Children.Add(communityToggle);

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Collected {r.Rules.Count} order rule(s) across {r.ActiveCount} active mods · {r.ViolatedRules.Count} inverted · {r.CycleRules.Count} cycle conflict(s) · {r.Conflicts.Count} conflict(s)",
                $"已从 {r.ActiveCount} 个启用模组收集 {r.Rules.Count} 条排序规则 · 顺序错误 {r.ViolatedRules.Count} 条 · 循环冲突 {r.CycleRules.Count} 组 · 不兼容 {r.Conflicts.Count} 组",
                $"已從 {r.ActiveCount} 個啟用模組收集 {r.Rules.Count} 條排序規則 · 順序錯誤 {r.ViolatedRules.Count} 條 · 循環衝突 {r.CycleRules.Count} 組 · 不兼容 {r.Conflicts.Count} 組",
                $"有効MOD {r.ActiveCount} 件から {r.Rules.Count} 件の順序ルールを収集 · 順序逆転 {r.ViolatedRules.Count} 件 · 循環競合 {r.CycleRules.Count} 件 · 非互換 {r.Conflicts.Count} 組"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.SecondaryTextBrush
        });

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Sources: modDependencies {r.DependencyRuleCount} · loadAfter {r.LoadAfterRuleCount} · loadBefore {r.LoadBeforeRuleCount} · Community {r.CommunityRuleCount} · Core/DLC baseline {r.ImplicitCoreDlcRuleCount} · Localization pairing {r.ImplicitTranslationRuleCount}" +
                (r.ImplicitRjwRuleCount > 0 ? $" · RJW ordering {r.ImplicitRjwRuleCount}" : string.Empty),
                $"规则来源：前置依赖 {r.DependencyRuleCount} · loadAfter {r.LoadAfterRuleCount} · loadBefore {r.LoadBeforeRuleCount} · 社区规则 {r.CommunityRuleCount} · 内置本体/DLC {r.ImplicitCoreDlcRuleCount} · 内置汉化配对 {r.ImplicitTranslationRuleCount}" +
                (r.ImplicitRjwRuleCount > 0 ? $" · 内置 RJW 置后 {r.ImplicitRjwRuleCount}" : string.Empty),
                $"規則來源：前置依賴 {r.DependencyRuleCount} · loadAfter {r.LoadAfterRuleCount} · loadBefore {r.LoadBeforeRuleCount} · 社區規則 {r.CommunityRuleCount} · 內置本體/DLC {r.ImplicitCoreDlcRuleCount} · 內置漢化配對 {r.ImplicitTranslationRuleCount}" +
                (r.ImplicitRjwRuleCount > 0 ? $" · 內置 RJW 置後 {r.ImplicitRjwRuleCount}" : string.Empty),
                $"内訳：前提依存 {r.DependencyRuleCount} · loadAfter {r.LoadAfterRuleCount} · loadBefore {r.LoadBeforeRuleCount} · コミュニティ {r.CommunityRuleCount} · 内蔵Core/DLC {r.ImplicitCoreDlcRuleCount} · 内蔵翻訳ペア {r.ImplicitTranslationRuleCount}" +
                (r.ImplicitRjwRuleCount > 0 ? $" · 内蔵RJW {r.ImplicitRjwRuleCount}" : string.Empty)),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.MutedTextBrush
        });

        if (r.CycleRules.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    $"Unresolved circular dependencies ({r.CycleRules.Count}, choose the order above or let the next step auto-resolve the lowest-priority one)",
                    $"互相要求对方先加载 ({r.CycleRules.Count}，可在顶部选择先后顺序，或交由下一步应用排序自动解除优先级最低的那条)",
                    $"互相要求對方先載入 ({r.CycleRules.Count}，可在頂部選擇先後順序，或交由下一步應用排序自動解除優先級最低的那條)",
                    $"相互に先読み込みを要求 ({r.CycleRules.Count}、上部で順序を選ぶか次のステップで優先度最低のものを自動解除)"),
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = ThemeService.PrimaryTextBrush
            });

            var cycleStack = new StackPanel { Spacing = 2 };
            foreach (var rule in r.CycleRules)
            {
                cycleStack.Children.Add(BuildRuleRow(rule));
            }
            panel.Children.Add(cycleStack);
        }

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Currently inverted rules to fix in Step 4 ({r.ViolatedRules.Count})",
                $"当前顺序错误、等待调整的排序 ({r.ViolatedRules.Count})",
                $"當前順序錯誤、等待調整的排序 ({r.ViolatedRules.Count})",
                $"現在順序が逆になっているルール ({r.ViolatedRules.Count})"),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = ThemeService.PrimaryTextBrush
        });

        if (r.ViolatedRules.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    "No sorting errors.",
                    "无排序错误。",
                    "無排序錯誤。",
                    "並び順のエラーはありません。"),
                Foreground = ThemeService.MutedTextBrush
            });
        }
        else
        {
            var violatedStack = new StackPanel { Spacing = 2 };
            foreach (var rule in r.ViolatedRules)
            {
                violatedStack.Children.Add(BuildRuleRow(rule));
            }
            panel.Children.Add(violatedStack);
        }

        var satisfiedRules = r.Rules
            .Where(rule => !rule.IsViolated && rule.Kind != SortRuleKind.ImplicitCoreDlc)
            .ToList();
        if (satisfiedRules.Count > 0)
        {
            var previewLimit = 200;
            var satisfiedStack = new StackPanel
            {
                Spacing = 2,
                IsVisible = _satisfiedRulesExpanded
            };
            foreach (var rule in satisfiedRules.Take(previewLimit))
            {
                satisfiedStack.Children.Add(BuildRuleRow(rule));
            }
            if (satisfiedRules.Count > previewLimit)
            {
                satisfiedStack.Children.Add(new TextBlock
                {
                    Text = T(
                        $"... and {satisfiedRules.Count - previewLimit} more satisfied rule(s)",
                        $"…… 其余 {satisfiedRules.Count - previewLimit} 条已满足规则已省略",
                        $"…… 其餘 {satisfiedRules.Count - previewLimit} 條已滿足規則已省略",
                        $"…… 他 {satisfiedRules.Count - previewLimit} 件の充足済みルール"),
                    Margin = new Thickness(4, 2, 0, 0),
                    Foreground = ThemeService.MutedTextBrush
                });
            }

            var toggleText = new TextBlock { Foreground = ThemeService.SecondaryTextBrush };
            void UpdateSatisfiedLabel(bool expanded)
            {
                var arrow = expanded ? "▼" : "▶";
                toggleText.Text = T(
                    $"{arrow} Satisfied mod ordering ({satisfiedRules.Count})",
                    $"{arrow} 已符合的模组排序 ({satisfiedRules.Count})",
                    $"{arrow} 已符合的模組排序 ({satisfiedRules.Count})",
                    $"{arrow} 充足済みのMOD順序 ({satisfiedRules.Count})");
            }
            UpdateSatisfiedLabel(_satisfiedRulesExpanded);

            var toggleBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 4),
                MinHeight = 28,
                Margin = new Thickness(0, 6, 0, 0),
                Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
                Content = toggleText
            };
            toggleBtn.Click += (_, _) =>
            {
                _satisfiedRulesExpanded = !_satisfiedRulesExpanded;
                satisfiedStack.IsVisible = _satisfiedRulesExpanded;
                UpdateSatisfiedLabel(_satisfiedRulesExpanded);
            };

            panel.Children.Add(toggleBtn);
            panel.Children.Add(satisfiedStack);
        }

        return panel;
    }

    private Control BuildCycleChoiceCard(RuleCyclePair pair)
    {
        var pairKey = AutoSortPrerequisites.MakePairKey(pair.FirstMod.PackageId, pair.SecondMod.PackageId);
        _cycleResolutions.TryGetValue(pairKey, out var chosenBeforeId);

        var card = new StackPanel { Spacing = 4 };
        card.Children.Add(new TextBlock
        {
            Text = T(
                $"Circular Dependency: [{pair.ForwardRule.KindLabel}] vs [{pair.ReverseRule.KindLabel}] — choose which mod loads FIRST:",
                $"相互循环依赖：[{pair.ForwardRule.KindLabel}] 与 [{pair.ReverseRule.KindLabel}] 互指 — 请选择谁排在前面：",
                $"相互循環依賴：[{pair.ForwardRule.KindLabel}] 與 [{pair.ReverseRule.KindLabel}] 互指 — 請選擇誰排在前面：",
                $"循環依存：[{pair.ForwardRule.KindLabel}] と [{pair.ReverseRule.KindLabel}] — 先に読み込むMODを選択："),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.SecondaryTextBrush
        });

        card.Children.Add(BuildModMetaLine("A", pair.FirstMod));
        card.Children.Add(BuildModMetaLine("B", pair.SecondMod));

        var btnRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var titleA = ModDisplayTitle(pair.FirstMod);
        var titleB = ModDisplayTitle(pair.SecondMod);
        var aSelected = string.Equals(chosenBeforeId, pair.FirstMod.PackageId, StringComparison.OrdinalIgnoreCase);
        var bSelected = string.Equals(chosenBeforeId, pair.SecondMod.PackageId, StringComparison.OrdinalIgnoreCase);

        var btnA = MakeChoiceButton(
            (aSelected ? "✓ " : "") + T($"{titleA} → {titleB}", $"{titleA} 在前 → {titleB} 在后", $"{titleA} 在前 → {titleB} 在後", $"{titleA} → {titleB}"),
            aSelected);
        btnA.Click += (_, _) =>
        {
            _cycleResolutions[pairKey] = pair.FirstMod.PackageId;
            ReloadAndRebuild();
        };

        var btnB = MakeChoiceButton(
            (bSelected ? "✓ " : "") + T($"{titleB} → {titleA}", $"{titleB} 在前 → {titleA} 在后", $"{titleB} 在前 → {titleA} 在後", $"{titleB} → {titleA}"),
            bSelected);
        btnB.Click += (_, _) =>
        {
            _cycleResolutions[pairKey] = pair.SecondMod.PackageId;
            ReloadAndRebuild();
        };

        btnRow.Children.Add(btnA);
        btnRow.Children.Add(btnB);

        if (aSelected || bSelected)
        {
            var btnReset = MakeChoiceButton(T("Auto", "恢复自动", "恢復自動", "自動に戻す"), false);
            btnReset.Click += (_, _) =>
            {
                _cycleResolutions.Remove(pairKey);
                ReloadAndRebuild();
            };
            btnRow.Children.Add(btnReset);
        }

        card.Children.Add(btnRow);

        return new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(8, 6),
            Child = card
        };
    }

    private Control BuildIncompatibleChoiceCard(RuleConflictPair pair)
    {
        var card = new StackPanel { Spacing = 4 };
        card.Children.Add(new TextBlock
        {
            Text = T(
                $"Incompatible Mods ({pair.SourceLabel}) — choose which mod to KEEP enabled:",
                $"互不兼容冲突（来源：{pair.SourceLabel}）— 请选择要保留启用的模组：",
                $"互不兼容衝突（來源：{pair.SourceLabel}）— 請選擇要保留啟用的模組：",
                $"非互換MOD（{pair.SourceLabel}）— 有効のまま残すMODを選択："),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.SecondaryTextBrush
        });

        card.Children.Add(BuildModMetaLine("A", pair.FirstMod));
        card.Children.Add(BuildModMetaLine("B", pair.SecondMod));

        var btnRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var titleA = ModDisplayTitle(pair.FirstMod);
        var titleB = ModDisplayTitle(pair.SecondMod);

        var keepABtn = MakeChoiceButton(
            T($"Keep {titleA} (Disable B)", $"保留 {titleA}（停用 {titleB}）", $"保留 {titleA}（停用 {titleB}）", $"{titleA} を残す"),
            false);
        keepABtn.Click += (_, _) =>
        {
            _disableMods(new[] { pair.SecondMod.PackageId });
            ReloadAndRebuild();
        };

        var keepBBtn = MakeChoiceButton(
            T($"Keep {titleB} (Disable A)", $"保留 {titleB}（停用 {titleA}）", $"保留 {titleB}（停用 {titleA}）", $"{titleB} を残す"),
            false);
        keepBBtn.Click += (_, _) =>
        {
            _disableMods(new[] { pair.FirstMod.PackageId });
            ReloadAndRebuild();
        };

        btnRow.Children.Add(keepABtn);
        btnRow.Children.Add(keepBBtn);
        card.Children.Add(btnRow);

        return new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(8, 6),
            Child = card
        };
    }

    private Control BuildModMetaLine(string tag, ModInfo mod)
    {
        var title = ModDisplayTitle(mod);
        var author = string.IsNullOrWhiteSpace(mod.Author) ? T("Unknown", "未知", "未知", "不明") : mod.Author;
        var source = mod.Source switch
        {
            ModSource.Workshop => T("Workshop", "创意工坊", "創意工坊", "Workshop"),
            ModSource.Local => T("Local Mods", "本地 Mods", "本地 Mods", "ローカル"),
            ModSource.Official => T("Official", "官方", "官方", "公式"),
            _ => mod.Source.ToString()
        };
        var ver = string.IsNullOrWhiteSpace(mod.GameVersions) ? "-" : mod.GameVersions;
        var text = new TextBlock
        {
            Text = T(
                $"[{tag}] {title}  |  Author: {author}  |  ID: {mod.PackageId}  |  Source: {source}  |  Ver: {ver}",
                $"[{tag}] {title}  |  作者：{author}  |  包名：{mod.PackageId}  |  来源：{source}  |  支持版本：{ver}",
                $"[{tag}] {title}  |  作者：{author}  |  包名：{mod.PackageId}  |  來源：{source}  |  支持版本：{ver}",
                $"[{tag}] {title}  |  作者：{author}  |  ID：{mod.PackageId}  |  ソース：{source}  |  対応Ver：{ver}"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.PrimaryTextBrush
        };
        ToolTip.SetTip(text, $"{title}\nAuthor: {author}\nPackageId: {mod.PackageId}\nPath: {mod.Path}");
        return text;
    }

    private static string ModDisplayTitle(ModInfo mod) =>
        string.IsNullOrWhiteSpace(mod.RowName) ? mod.Name : mod.RowName;

    private static Button MakeChoiceButton(string label, bool highlighted)
    {
        return new Button
        {
            Padding = new Thickness(10, 4),
            MinHeight = 28,
            Margin = new Thickness(0, 0, 8, 4),
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(1),
            BorderBrush = highlighted ? ThemeService.PrimaryTextBrush : ThemeService.SecondaryBrush,
            Theme = Application.Current?.FindResource("FlatButton") as ControlTheme,
            Content = new TextBlock
            {
                Text = label,
                FontWeight = highlighted ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = ThemeService.PrimaryTextBrush
            }
        };
    }

    private Control BuildRuleRow(CollectedSortRule rule)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var mainText = new TextBlock
        {
            Text = $"[{rule.KindLabel}] {rule.BeforeName} (#{rule.BeforeIndex + 1})  →  {rule.AfterName} (#{rule.AfterIndex + 1})",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = rule.IsViolated ? ThemeService.PrimaryTextBrush : ThemeService.MutedTextBrush
        };
        ToolTip.SetTip(
            mainText,
            $"[{rule.KindLabel}]\n{rule.BeforeName} ({rule.BeforeId}, #{rule.BeforeIndex + 1})\n  ↓\n{rule.AfterName} ({rule.AfterId}, #{rule.AfterIndex + 1})");
        grid.Children.Add(mainText);

        var status = new TextBlock
        {
            Text = rule.IsViolated
                ? T("Inverted", "排序错误", "排序錯誤", "逆になっている")
                : T("Satisfied", "已符合", "已符合", "充足済"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = rule.IsViolated ? ThemeService.PrimaryTextBrush : ThemeService.MutedTextBrush
        };
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);

        return new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 3),
            Child = grid
        };
    }

    private Control BuildStep4Body()
    {
        var panel = new StackPanel { Spacing = 8 };
        var plan = _sortPlan;

        panel.Children.Add(new TextBlock
        {
            Text = T(
                $"Sort preview: {plan.MovedRows.Count} mod(s) will move across {plan.Rows.Count} active mods · fixes {plan.FixedRuleCount} inverted rule(s) · {plan.BrokenCycleEdges.Count} cycle conflict(s) resolved",
                $"排序预览：共 {plan.Rows.Count} 个启用模组中有 {plan.MovedRows.Count} 个发生位置调整 · 修复放反了的规则 {plan.FixedRuleCount} 条 · 解除循环冲突 {plan.BrokenCycleEdges.Count} 条",
                $"排序預覽：共 {plan.Rows.Count} 個啟用模組中有 {plan.MovedRows.Count} 個發生位置調整 · 修復放反了的規則 {plan.FixedRuleCount} 條 · 解除循環衝突 {plan.BrokenCycleEdges.Count} 條",
                $"ソートプレビュー：有効MOD {plan.Rows.Count} 件中 {plan.MovedRows.Count} 件が移動 · 逆転解消 {plan.FixedRuleCount} 件 · 循環競合の解除 {plan.BrokenCycleEdges.Count} 件"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.SecondaryTextBrush
        });

        if (plan.BrokenCycleEdges.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    $"Auto-resolved cycle conflicts (lowest priority) ({plan.BrokenCycleEdges.Count})",
                    $"已自动解除的循环冲突（优先级最低） ({plan.BrokenCycleEdges.Count})",
                    $"已自動解除的循環衝突（優先級最低） ({plan.BrokenCycleEdges.Count})",
                    $"自動解除された循環競合（優先度最低） ({plan.BrokenCycleEdges.Count})"),
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = ThemeService.PrimaryTextBrush
            });

            var brokenStack = new StackPanel { Spacing = 2 };
            foreach (var edge in plan.BrokenCycleEdges)
            {
                brokenStack.Children.Add(new Border
                {
                    BorderBrush = ThemeService.SecondaryBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(4, 3),
                    Child = new TextBlock
                    {
                        Text = $"[{edge.KindLabel}] {edge.BeforeName}  ✕→  {edge.AfterName}",
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = ThemeService.MutedTextBrush
                    }
                });
            }
            panel.Children.Add(brokenStack);
        }

        if (plan.MovedRows.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = T(
                    "Current load order is already optimal — no mod positions need to change.",
                    "当前加载顺序已符合全部排序规则，无需调整任何模组位置。",
                    "當前加載順序已符合全部排序規則，無需調整任何模組位置。",
                    "現在の読み込み順はすでに最適であり、変更の必要はありません。"),
                Margin = new Thickness(0, 8, 0, 8),
                Foreground = ThemeService.PrimaryTextBrush
            });
            return panel;
        }

        var fullViewToggle = new CheckBox
        {
            IsChecked = _showFullOrderInStep4,
            MinHeight = 24,
            Content = new TextBlock
            {
                Text = T(
                    $"Show all {plan.Rows.Count} active mods (uncheck to show only {plan.MovedRows.Count} moved mod(s))",
                    $"显示完整新序列（共 {plan.Rows.Count} 项；取消勾选则仅显示发生位置变动的 {plan.MovedRows.Count} 项）",
                    $"顯示完整新序列（共 {plan.Rows.Count} 項；取消勾選則僅顯示發生位置變動的 {plan.MovedRows.Count} 項）",
                    $"全 {plan.Rows.Count} 件を表示（オフで移動のある {plan.MovedRows.Count} 件のみ表示）"),
                Foreground = ThemeService.PrimaryTextBrush
            }
        };
        fullViewToggle.IsCheckedChanged += (_, _) =>
        {
            _showFullOrderInStep4 = fullViewToggle.IsChecked == true;
            _bodyScroll.Content = BuildStep4Body();
        };
        panel.Children.Add(fullViewToggle);

        var rowsToRender = _showFullOrderInStep4 ? plan.Rows : plan.MovedRows;
        var rowStack = new StackPanel { Spacing = 2 };
        foreach (var row in rowsToRender)
        {
            rowStack.Children.Add(BuildPreviewRow(row));
        }
        panel.Children.Add(rowStack);

        return panel;
    }

    private Control BuildPreviewRow(SortPreviewRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var posText = new TextBlock
        {
            Text = row.OldIndex == row.NewIndex
                ? $"#{row.NewIndex + 1}"
                : $"#{row.OldIndex + 1} → #{row.NewIndex + 1}",
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = row.OldIndex == row.NewIndex ? ThemeService.MutedTextBrush : ThemeService.PrimaryTextBrush
        };
        grid.Children.Add(posText);

        var title = ModDisplayTitle(row.Mod);
        var suffix = row.IsBottomPinned ? T(" [Bottom]", " [置底]", " [置底]", " [最下段]") : "";
        var nameText = new TextBlock
        {
            Text = $"{title}{suffix}",
            Margin = new Thickness(8, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = row.OldIndex == row.NewIndex ? ThemeService.MutedTextBrush : ThemeService.PrimaryTextBrush
        };
        ToolTip.SetTip(nameText, $"{title}\nAuthor: {row.Mod.Author}\nPackageId: {row.Mod.PackageId}\n#{row.OldIndex + 1} → #{row.NewIndex + 1}");
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);

        var deltaStr = row.Delta switch
        {
            > 0 => $"↑ {row.Delta}",
            < 0 => $"↓ {-row.Delta}",
            _ => "—"
        };
        var deltaText = new TextBlock
        {
            Text = deltaStr,
            MinWidth = 48,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = row.Delta == 0 ? ThemeService.MutedTextBrush : ThemeService.SecondaryTextBrush
        };
        Grid.SetColumn(deltaText, 2);
        grid.Children.Add(deltaText);

        return new Border
        {
            BorderBrush = ThemeService.SecondaryBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 3),
            Child = grid
        };
    }

    private void UpdateFooterStatus()
    {
        if (_currentStep == 1)
        {
            var selectedPrereqs = _prereqChecks.Count(c => c.Box.IsChecked == true);
            _applyAllButton.IsEnabled = selectedPrereqs > 0;
            _footerStatus.Text = selectedPrereqs > 0
                ? T($"Selected: {selectedPrereqs} prerequisite(s) to enable",
                    $"已勾选：启用前置 {selectedPrereqs} 个",
                    $"已勾選：啟用前置 {selectedPrereqs} 個",
                    $"選択中：前提追加 {selectedPrereqs} 件")
                : T("No pending prerequisites selected",
                    "当前无待启用勾选条目",
                    "當前無待啟用勾選條目",
                    "処理対象の選択はありません");
        }
        else if (_currentStep == 2)
        {
            var selectedCore = _coreChecks.Count(c => c.Box.IsChecked == true);
            _applyAllButton.IsEnabled = selectedCore > 0;
            _footerStatus.Text = selectedCore > 0
                ? T($"Selected: {selectedCore} core prerequisite(s) to move to top",
                    $"已勾选：调整大前置 {selectedCore} 个",
                    $"已勾選：調整大前置 {selectedCore} 個",
                    $"選択中：コア前提の配置調整 {selectedCore} 件")
                : T("All core prerequisites are in their top-tier positions",
                    "大前置无排序错误",
                    "大前置無排序錯誤",
                    "すべてのコア前提は最上位に配置されています");
        }
        else if (_currentStep == 3)
        {
            _applyAllButton.IsEnabled = false;
            _footerStatus.Text = _rulesResult.ViolatedRules.Count > 0
                ? T($"Ready to apply sort: {_rulesResult.ViolatedRules.Count} rule(s) in the wrong order detected",
                    $"规则收集完成：共 {_rulesResult.ViolatedRules.Count} 条排序错误，待下一步应用排序",
                    $"規則收集完成：共 {_rulesResult.ViolatedRules.Count} 條排序錯誤，待下一步應用排序",
                    $"ルール収集完了：{_rulesResult.ViolatedRules.Count} 件の逆転ルールを検出")
                : T("Rule check complete: no rules in the wrong order",
                    "收集规则：当前没有排序错误",
                    "收集規則：當前沒有排序錯誤",
                    "ルール収集完了：逆転ルール 0 件");
        }
        else
        {
            _applyAllButton.IsEnabled = _sortPlan.MovedRows.Count > 0;
            _footerStatus.Text = _sortPlan.MovedRows.Count > 0
                ? T($"Ready: {_sortPlan.MovedRows.Count} mod(s) will be reordered",
                    $"待应用：将调整 {_sortPlan.MovedRows.Count} 个模组的位置",
                    $"待應用：將調整 {_sortPlan.MovedRows.Count} 個模組的位置",
                    $"適用準備完了：{_sortPlan.MovedRows.Count} 件のMOD順序を更新します")
                : T("Load order already matches all rules",
                    "当前排序已完全就位，无需改动",
                    "當前排序已完全就位，無需改動",
                    "現在の順序はすべてのルールを満たしています");
        }
    }

    private string T(string en, string zh, string tw, string ja) =>
        OperationText.Choose(_language, en, zh, tw, ja);
}
