using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PackFrame;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush TextBrush = ThemeService.PrimaryTextBrush;
    private static readonly SolidColorBrush SecondaryBorderBrush = ThemeService.SecondaryBrush;
    private static readonly TextDecorationCollection StrikethroughDecorations = new()
    {
        new TextDecoration { Location = TextDecorationLocation.Strikethrough }
    };
    private static readonly TextDecorationCollection UnderlineDecorations = new()
    {
        new TextDecoration { Location = TextDecorationLocation.Underline }
    };
    private static readonly DropShadowEffect TextOutlineEffect = new()
    {
        Color = Colors.Black,
        BlurRadius = 2.2,
        OffsetX = 0,
        OffsetY = 0,
        Opacity = 1
    };

    private enum ProblemFilterMode
    {
        Off,
        Hide,
        Only
    }

    private sealed class ProblemFilterState
    {
        public ProblemFilterMode Sort;
        public ProblemFilterMode Version;
        public ProblemFilterMode Duplicate;
    }

    private readonly AppConfig _config;
    private readonly TextBlock _gamePathLabel;
    private readonly TextBox _pathBox;
    private readonly Border _pathField;
    private readonly Button _languageButton;
    private readonly Button _autoDetectGameButton;
    private readonly Button _browseButton;
    private readonly Button _paintModeButton;
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _launchButton;
    private readonly Button _saveButton;
    private readonly Button _refreshButton;
    private readonly Button _syncConfigButton;
    private readonly Button _githubLinkButton;
    private readonly Button _discordLinkButton;
    private readonly Button _backgroundButton;
    private readonly Button _checkUpdateButton;
    private bool _checkingUpdate;
    private readonly SelectableTextBlock _qqGroupText;
    private readonly TextBox _inactiveSearchBox;
    private readonly TextBox _activeSearchBox1;
    private readonly TextBox _activeSearchBox2;
    private readonly ComboBox _inactiveSearchField;
    private readonly ComboBox _activeSearchField1;
    private readonly ComboBox _activeSearchField2;
    private readonly Button _inactiveSourceButton;
    private readonly Button _inactiveSortButton;
    private readonly Button _activeSourceButton1;
    private readonly Button _activeSourceButton2;
    private readonly Button _inactiveViewModeButton;
    private readonly Button _activeViewModeButton1;
    private readonly Button _activeViewModeButton2;
    private readonly TextBlock _inactiveHeader;
    private readonly TextBlock _activeHeader1;
    private readonly TextBlock _activeHeader2;
    private readonly TextBlock _inactiveProblemV;
    private readonly TextBlock _inactiveProblemR;
    private readonly TextBlock _activeProblemS1;
    private readonly TextBlock _activeProblemV1;
    private readonly TextBlock _activeProblemR1;
    private readonly TextBlock _activeProblemS2;
    private readonly TextBlock _activeProblemV2;
    private readonly TextBlock _activeProblemR2;
    private readonly Button _activeMissing1;
    private readonly Button _activeMissing2;
    private readonly ComboBox _instanceCombo1;
    private readonly ComboBox _instanceCombo2;
    private readonly Button _instanceActionButton1;
    private readonly Button _instanceActionButton2;
    private readonly Button _modpackButton1;
    private readonly Button _modpackButton2;
    private readonly ListBox _inactiveList;
    private readonly ListBox _activeList1;
    private readonly ListBox _activeList2;
    private readonly Border _activeColumn1;
    private readonly Border _activeColumn2;
    private readonly SelectableTextBlock _nameLabel;
    private readonly SelectableTextBlock _authorLabel;
    private readonly SelectableTextBlock _pathLabel;
    private readonly SelectableTextBlock _packageLabel;
    private readonly SelectableTextBlock _descriptionLabel;
    private readonly SelectableTextBlock _nameValue;
    private readonly SelectableTextBlock _authorValue;
    private readonly SelectableTextBlock _pathValue;
    private readonly SelectableTextBlock _packageValue;
    private readonly SelectableTextBlock _descriptionValue;
    private readonly Image _previewImage;
    private readonly Canvas _dragOverlay;
    private readonly Border _dragGhost;
    private readonly TextBlock _dragGhostText;
    private readonly Border _insertionLine;
    private readonly Border _undoToast;
    private readonly TextBlock _undoToastText;
    private readonly DispatcherTimer _undoToastTimer;
    private readonly DispatcherTimer _problemTooltipTimer;
    private readonly Popup _modBadgePopup;
    private Control? _problemTooltipTarget;
    private bool _problemTooltipContentHovered;

    private const string ScratchFolderName = "__scratch__";
    private List<ModInfo> _allMods = new();
    private Dictionary<string, ModInfo> _modsById = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<ModInfo>> _modsByPackage = new(StringComparer.OrdinalIgnoreCase);
    private List<ManagedInstance> _instances = new();
    private readonly List<string> _scratchActiveIds = new();
    private readonly ObservableCollection<string> _instanceNames1 = new();
    private readonly ObservableCollection<string> _instanceNames2 = new();
    private int _inactiveCount;
    private int _active1Count;
    private int _active2Count;
    private List<MissingModRow> _missingMods1 = new();
    private List<MissingModRow> _missingMods2 = new();
    private int _loadVersion;
    private bool _refreshing;
    private bool _loadingMods;
    private bool _updatingInstances;
    private string? _lastLoadedGamePath;
    private string? _packRoot;
    private string? _gameVersion;
    private RimWorldInstallation? _gameInstallation;
    private Bitmap? _previewBitmap;
    private ListBox? _selectionAnchorList;
    private int _selectionAnchorIndex = -1;
    private ListBox? _pressedMultiList;
    private List<ModInfo>? _pressedMultiMods;
    private ListBox? _pressedSnapshotList;
    private List<ModInfo> _pressedSnapshotMods = new();
    private ListBox? _rangeSelectionList;
    private List<ModInfo>? _rangeSelectionMods;
    private ListBox? _dragSourceList;
    private Point _dragStartPoint;
    private DragPayload? _dragPayload;
    private bool _customDragActive;
    private bool _paintMode;
    private bool _paintEraser;
    private string? _paintColorHex;
    private bool _painting;
    private ListBox? _paintList;
    private int _paintColumn;
    private ModColorData? _paintData;
    private readonly Dictionary<string, string?> _paintBeforeColors =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _paintedIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _autoScrollTimer;
    private ListBox? _autoScrollList;
    private int _autoScrollDirection;
    private ListBox? _lastPressList;
    private ModInfo? _lastPressMod;
    private long _lastPressTicks;
    private int _lastEnableColumn = 1;
    private int _lastInstanceIndex1 = -1;
    private int _lastInstanceIndex2 = -1;
    private readonly HashSet<Control> _instanceContainers1 = new();
    private readonly HashSet<Control> _instanceContainers2 = new();
    private ComboBox? _instanceDragCombo;
    private Control? _instanceDragContainer;
    private int _instanceDragIndex = -1;
    private int _instanceDragBoundary = 2;
    private Point _instanceDragStart;
    private bool _instanceDragActive;
    private bool _instanceDragStarted;
    private TopLevel? _instanceDragTopLevel;
    private ComboBoxItem? _instanceDropTarget;
    private bool _updatingSearch;
    private int _inactiveSortField;
    private bool _inactiveSortAscending = true;
    private readonly ProblemFilterState _inactiveFilter = new();
    private readonly ProblemFilterState _activeFilter1 = new();
    private readonly ProblemFilterState _activeFilter2 = new();
    private readonly ModSearchState _inactiveSearch = new();
    private readonly ModSearchState _activeSearch1 = new();
    private readonly ModSearchState _activeSearch2 = new();
    private ListBoxItem? _hoveredItem;
    private readonly List<UndoEntry> _undoHistory = new();
    private readonly List<UndoEntry> _redoHistory = new();
    private ListBox? _pendingCollapseList;
    private ModInfo? _pendingCollapseMod;
    private int _pendingCollapseIndex = -1;

    public MainWindow()
        : this(AppConfig.Load())
    {
    }

    public MainWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();

        _gamePathLabel = Find<TextBlock>("GamePathLabel");
        _pathBox = Find<TextBox>("PathBox");
        _pathField = Find<Border>("PathField");
        _languageButton = Find<Button>("LanguageButton");
        _autoDetectGameButton = Find<Button>("AutoDetectGameButton");
        _browseButton = Find<Button>("BrowseButton");
        _paintModeButton = Find<Button>("PaintModeButton");
        _undoButton = Find<Button>("UndoButton");
        _redoButton = Find<Button>("RedoButton");
        _launchButton = Find<Button>("LaunchButton");
        _saveButton = Find<Button>("SaveButton");
        _refreshButton = Find<Button>("RefreshButton");
        _syncConfigButton = Find<Button>("SyncConfigButton");
        _githubLinkButton = Find<Button>("GithubLinkButton");
        _discordLinkButton = Find<Button>("DiscordLinkButton");
        _backgroundButton = Find<Button>("BackgroundButton");
        _checkUpdateButton = Find<Button>("CheckUpdateButton");
        _qqGroupText = Find<SelectableTextBlock>("QqGroupText");
        _inactiveSearchBox = Find<TextBox>("InactiveSearchBox");
        _activeSearchBox1 = Find<TextBox>("ActiveSearchBox1");
        _activeSearchBox2 = Find<TextBox>("ActiveSearchBox2");
        _inactiveSearchField = Find<ComboBox>("InactiveSearchField");
        _activeSearchField1 = Find<ComboBox>("ActiveSearchField1");
        _activeSearchField2 = Find<ComboBox>("ActiveSearchField2");
        _inactiveSourceButton = Find<Button>("InactiveSourceButton");
        _inactiveSortButton = Find<Button>("InactiveSortButton");
        _activeSourceButton1 = Find<Button>("ActiveSourceButton1");
        _activeSourceButton2 = Find<Button>("ActiveSourceButton2");
        _inactiveViewModeButton = Find<Button>("InactiveViewModeButton");
        _activeViewModeButton1 = Find<Button>("ActiveViewModeButton1");
        _activeViewModeButton2 = Find<Button>("ActiveViewModeButton2");
        _inactiveHeader = Find<TextBlock>("InactiveHeader");
        _activeHeader1 = Find<TextBlock>("ActiveHeader1");
        _activeHeader2 = Find<TextBlock>("ActiveHeader2");
        _inactiveProblemV = Find<TextBlock>("InactiveProblemV");
        _inactiveProblemR = Find<TextBlock>("InactiveProblemR");
        _activeProblemS1 = Find<TextBlock>("ActiveProblemS1");
        _activeProblemV1 = Find<TextBlock>("ActiveProblemV1");
        _activeProblemR1 = Find<TextBlock>("ActiveProblemR1");
        _activeProblemS2 = Find<TextBlock>("ActiveProblemS2");
        _activeProblemV2 = Find<TextBlock>("ActiveProblemV2");
        _activeProblemR2 = Find<TextBlock>("ActiveProblemR2");
        _activeMissing1 = Find<Button>("ActiveMissing1");
        _activeMissing2 = Find<Button>("ActiveMissing2");
        _instanceCombo1 = Find<ComboBox>("InstanceCombo1");
        _instanceCombo2 = Find<ComboBox>("InstanceCombo2");
        _instanceCombo1.ItemsSource = _instanceNames1;
        _instanceCombo2.ItemsSource = _instanceNames2;
        _instanceActionButton1 = Find<Button>("InstanceActionButton1");
        _instanceActionButton2 = Find<Button>("InstanceActionButton2");
        _modpackButton1 = Find<Button>("ModpackButton1");
        _modpackButton2 = Find<Button>("ModpackButton2");
        _instanceCombo1.ContainerPrepared += (_, args) =>
            RegisterInstanceContainer(
                _instanceCombo1,
                args.Container,
                _instanceContainers1
            );
        _instanceCombo2.ContainerPrepared += (_, args) =>
            RegisterInstanceContainer(
                _instanceCombo2,
                args.Container,
                _instanceContainers2
            );
        _instanceCombo1.DropDownOpened += (_, _) =>
            AttachInstanceContainers(_instanceCombo1, _instanceContainers1);
        _instanceCombo2.DropDownOpened += (_, _) =>
            AttachInstanceContainers(_instanceCombo2, _instanceContainers2);
        _inactiveList = Find<ListBox>("InactiveList");
        _activeList1 = Find<ListBox>("ActiveList1");
        _activeList2 = Find<ListBox>("ActiveList2");
        _activeColumn1 = Find<Border>("ActiveColumn1");
        _activeColumn2 = Find<Border>("ActiveColumn2");
        _nameLabel = Find<SelectableTextBlock>("NameLabel");
        _authorLabel = Find<SelectableTextBlock>("AuthorLabel");
        _pathLabel = Find<SelectableTextBlock>("PathLabel");
        _packageLabel = Find<SelectableTextBlock>("PackageLabel");
        _descriptionLabel = Find<SelectableTextBlock>("DescriptionLabel");
        _nameValue = Find<SelectableTextBlock>("NameValue");
        _authorValue = Find<SelectableTextBlock>("AuthorValue");
        _pathValue = Find<SelectableTextBlock>("PathValue");
        _packageValue = Find<SelectableTextBlock>("PackageValue");
        _descriptionValue = Find<SelectableTextBlock>("DescriptionValue");
        _previewImage = Find<Image>("PreviewImage");
        _dragOverlay = Find<Canvas>("DragOverlay");
        _dragGhost = Find<Border>("DragGhost");
        _dragGhostText = Find<TextBlock>("DragGhostText");
        _insertionLine = Find<Border>("InsertionLine");
        _undoToast = Find<Border>("UndoToast");
        _undoToastText = Find<TextBlock>("UndoToastText");
        _autoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _autoScrollTimer.Tick += (_, _) => OnAutoScrollTick();

        _undoToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _undoToastTimer.Tick += (_, _) =>
        {
            _undoToastTimer.Stop();
            _undoToast.IsVisible = false;
        };

        _problemTooltipTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _modBadgePopup = new Popup
        {
            IsLightDismissEnabled = false,
            Placement = PlacementMode.Top
        };
        _problemTooltipTimer.Tick += (_, _) =>
        {
            _problemTooltipTimer.Stop();
            if (!_problemTooltipContentHovered &&
                _modBadgePopup.Child?.IsKeyboardFocusWithin != true)
            {
                CloseProblemTooltip();
            }
        };
        foreach (var label in new[]
                 {
                     _inactiveProblemV,
                     _inactiveProblemR,
                     _activeProblemS1,
                     _activeProblemV1,
                     _activeProblemR1,
                     _activeProblemS2,
                     _activeProblemV2,
                     _activeProblemR2
                 })
        {
            ToolTip.SetServiceEnabled(label, false);
            ToolTip.SetPlacement(label, PlacementMode.Top);
            ToolTip.SetVerticalOffset(label, 6);
            label.PointerEntered += OnProblemLabelPointerEntered;
            label.PointerExited += OnProblemLabelPointerExited;
        }

        foreach (var list in GetModLists())
        {
            list.Focusable = true;
            list.SelectionMode = SelectionMode.Multiple;
            list.AddHandler(
                InputElement.PointerPressedEvent,
                OnModListPointerPressed,
                RoutingStrategies.Tunnel,
                true
            );

            list.PointerMoved += OnModListPointerMoved;
            list.PointerReleased += OnModListPointerReleased;
            list.PointerExited += OnModListPointerExited;
            list.PointerCaptureLost += (_, _) =>
            {
                if (_painting)
                {
                    FinishPaint();
                }
            };
            list.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnModListPointerWheelChanged,
                RoutingStrategies.Tunnel,
                true
            );
        }

        _activeColumn1.AddHandler(
            InputElement.PointerPressedEvent,
            OnActiveColumnPointerPressed,
            RoutingStrategies.Tunnel,
            true
        );
        _activeColumn2.AddHandler(
            InputElement.PointerPressedEvent,
            OnActiveColumnPointerPressed,
            RoutingStrategies.Tunnel,
            true
        );

        _packRoot = AppPaths.FindPackRoot();
        ApplyLocalization();
        UpdateEnableTargetBorders();
        UpdateUndoButton();

        _pathBox.KeyDown += OnPathKeyDown;
        _pathBox.LostFocus += OnPathLostFocus;
        _pathBox.TextChanged += OnPathTextChanged;
        _languageButton.Click += OnLanguageClick;
        _browseButton.Click += OnBrowseClick;
        _undoButton.Click += OnUndoClick;
        _redoButton.Click += OnRedoClick;
        _inactiveSearchBox.TextChanged += (_, _) =>
            OnSearchTextChanged(_inactiveSearch, _inactiveSearchBox.Text, _inactiveList);
        _activeSearchBox1.TextChanged += (_, _) =>
            OnSearchTextChanged(_activeSearch1, _activeSearchBox1.Text, _activeList1);
        _activeSearchBox2.TextChanged += (_, _) =>
            OnSearchTextChanged(_activeSearch2, _activeSearchBox2.Text, _activeList2);
        _inactiveSearchField.SelectionChanged += (_, _) =>
            OnSearchFieldChanged(_inactiveSearch, _inactiveSearchField, _inactiveList);
        _activeSearchField1.SelectionChanged += (_, _) =>
            OnSearchFieldChanged(_activeSearch1, _activeSearchField1, _activeList1);
        _activeSearchField2.SelectionChanged += (_, _) =>
            OnSearchFieldChanged(_activeSearch2, _activeSearchField2, _activeList2);

        _inactiveSearchField.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnSearchFieldWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _activeSearchField1.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnSearchFieldWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _activeSearchField2.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnSearchFieldWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _inactiveSourceButton.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnSourceWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _activeSourceButton1.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnSourceWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _activeSourceButton2.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnSourceWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _inactiveSortButton.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnSortWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _instanceCombo1.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnInstanceComboWheel,
            RoutingStrategies.Tunnel,
            true
        );
        _instanceCombo2.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnInstanceComboWheel,
            RoutingStrategies.Tunnel,
            true
        );
        AddHandler(
            InputElement.KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            true
        );
        AddHandler(
            InputElement.PointerPressedEvent,
            OnWindowPointerPressed,
            RoutingStrategies.Tunnel,
            true
        );
        AddHandler(
            InputElement.PointerPressedEvent,
            OnScrollBarPointerPressed,
            RoutingStrategies.Tunnel,
            true
        );
        Closing += OnClosing;

        if (!string.IsNullOrWhiteSpace(_config.GamePath))
        {
            _pathBox.Text = _config.GamePath;
            ValidatePath(save: false);
        }

        Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(
                () => _inactiveList.Focus(),
                DispatcherPriority.Loaded
            );
            if (_gameInstallation is null)
            {
                _ = AutoDetectGameAsync();
            }
        };
    }

    private T Find<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"{name} was not found.");
    }

    private static TextBlock CreateConfigTooltip(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        };
    }

    private void ApplyLocalization()
    {
        var language = _config.Language;
        _gamePathLabel.Text = Localization.GamePathLabel(language);
        _autoDetectGameButton.Content = Localization.AutoDetectButton(language);
        _browseButton.Content = Localization.BrowseButton(language);
        UpdatePaintButtonVisual();
        _undoButton.Content = Localization.Undo(language);
        _redoButton.Content = Localization.Redo(language);
        _launchButton.Content = Localization.LaunchGame(language);
        _githubLinkButton.Content = "GitHub";
        _discordLinkButton.Content = "Discord";
        _backgroundButton.Content = OperationText.Choose(
            language,
            "Background",
            "背景",
            "背景",
            "背景"
        );
        _checkUpdateButton.Content = OperationText.Choose(
            language,
            "Check updates",
            "检查更新",
            "檢查更新",
            "更新を確認"
        );
        _qqGroupText.Text = OperationText.Choose(
            language,
            "QQ group: 960717872",
            "交流QQ群：960717872",
            "交流QQ群：960717872",
            "交流QQグループ：960717872"
        );
        _refreshButton.Content = Localization.Refresh(language);
        _syncConfigButton.Content = Localization.SyncConfig(language);
        _modpackButton1.Content = Localization.ModpacksButton(language);
        _modpackButton2.Content = Localization.ModpacksButton(language);
        _nameLabel.Text = Localization.FieldName(language);
        _authorLabel.Text = Localization.FieldAuthor(language);
        _pathLabel.Text = Localization.FieldPath(language);
        _packageLabel.Text = Localization.FieldPackageId(language);
        _descriptionLabel.Text = Localization.FieldDescription(language);
        var searchPlaceholder = Localization.SearchMods(language);
        _inactiveSearchBox.PlaceholderText = searchPlaceholder;
        _activeSearchBox1.PlaceholderText = searchPlaceholder;
        _activeSearchBox2.PlaceholderText = searchPlaceholder;

        var searchFields = new[]
        {
            Localization.SearchByName(language),
            Localization.SearchByAuthor(language),
            Localization.SearchByPath(language),
            Localization.SearchByPackageId(language),
            Localization.SearchByGameVersion(language)
        };
        _updatingSearch = true;
        var field1 = _inactiveSearchField.SelectedIndex;
        var field2 = _activeSearchField1.SelectedIndex;
        var field3 = _activeSearchField2.SelectedIndex;
        _inactiveSearchField.ItemsSource = searchFields;
        _activeSearchField1.ItemsSource = searchFields;
        _activeSearchField2.ItemsSource = searchFields;
        _inactiveSearchField.SelectedIndex = field1 >= 0 ? field1 : 0;
        _activeSearchField1.SelectedIndex = field2 >= 0 ? field2 : 0;
        _activeSearchField2.SelectedIndex = field3 >= 0 ? field3 : 0;
        _updatingSearch = false;
        _inactiveSearch.FieldIndex = _inactiveSearchField.SelectedIndex;
        _activeSearch1.FieldIndex = _activeSearchField1.SelectedIndex;
        _activeSearch2.FieldIndex = _activeSearchField2.SelectedIndex;

        UpdateSourceButton(_inactiveSourceButton, _inactiveSearch, language);
        UpdateSourceButton(_activeSourceButton1, _activeSearch1, language);
        UpdateSourceButton(_activeSourceButton2, _activeSearch2, language);
        UpdateViewModeButton(_inactiveViewModeButton, _inactiveSearch, language);
        UpdateViewModeButton(_activeViewModeButton1, _activeSearch1, language);
        UpdateViewModeButton(_activeViewModeButton2, _activeSearch2, language);
        UpdateSortButton(language);

        UpdateHeaders();
        UpdateLaunchButton();
        UpdateSaveState();
    }

    private void ApplyLanguage(string language)
    {
        _config.Language = language;
        _config.Save();
        ApplyLocalization();
        if (_instances.Count > 0)
        {
            ReloadInstances(null, -1);
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        UndoLastChange();
    }

    private void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        RedoLastChange();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var list = FindModListAt(e.Source);
            _pressedSnapshotList = list;
            _pressedSnapshotMods = list is null
                ? new List<ModInfo>()
                : GetSelectedMods(list);
        }

        if (IsWithinSearchBox(e.Source))
        {
            return;
        }

        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        if (focusManager is null)
        {
            return;
        }

        if (!IsWithinSearchBox(focusManager.GetFocusedElement()))
        {
            return;
        }

        focusManager.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
    }

    private void OnScrollBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source ||
            !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ScrollBar? scrollBar = null;
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Thumb)
            {
                return;
            }

            if (visual is ScrollBar bar)
            {
                scrollBar = bar;
                break;
            }
        }

        if (scrollBar is null || scrollBar.Orientation != Avalonia.Layout.Orientation.Vertical)
        {
            return;
        }

        var thumb = scrollBar.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
        if (thumb is null)
        {
            return;
        }

        var thumbOrigin = thumb.TranslatePoint(new Point(0, 0), scrollBar);
        if (thumbOrigin is null)
        {
            return;
        }

        var thumbTop = thumbOrigin.Value.Y;
        var thumbBottom = thumbTop + thumb.Bounds.Height;
        var point = e.GetPosition(scrollBar);
        if (point.Y >= thumbTop && point.Y <= thumbBottom)
        {
            return;
        }

        var track = scrollBar.GetVisualDescendants().OfType<Track>().FirstOrDefault();
        if (track is null)
        {
            return;
        }

        var thumbHeight = thumb.Bounds.Height;
        var travel = track.Bounds.Height - thumbHeight;
        if (travel <= 0)
        {
            return;
        }

        var trackPoint = e.GetPosition(track);
        var targetTop = Math.Clamp(trackPoint.Y - thumbHeight / 2, 0, travel);
        var value = scrollBar.Minimum +
                    targetTop / travel * (scrollBar.Maximum - scrollBar.Minimum);
        scrollBar.Value = value;
        e.Handled = true;
    }

    private bool IsWithinSearchBox(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (ReferenceEquals(visual, _inactiveSearchBox) ||
                ReferenceEquals(visual, _activeSearchBox1) ||
                ReferenceEquals(visual, _activeSearchBox2))
            {
                return true;
            }

            visual = visual.GetVisualParent();
        }

        return false;
    }
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                      e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!control && (e.Key == Key.Up || e.Key == Key.Down))
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is TextBox || focused is ComboBox)
            {
                return;
            }

            var list = _lastPressList ??
                       GetModLists().FirstOrDefault(item => item.IsKeyboardFocusWithin) ??
                       _selectionAnchorList;
            if (list is not null)
            {
                MoveModSelection(
                    list,
                    e.Key == Key.Up ? -1 : 1,
                    e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                );
                e.Handled = true;
            }

            return;
        }

        if (!control && e.Key == Key.Enter)
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is TextBox || focused is ComboBox || focused is Button)
            {
                return;
            }

            var list = _lastPressList ??
                       GetModLists().FirstOrDefault(item => item.IsKeyboardFocusWithin) ??
                       _selectionAnchorList;
            if (list is not null)
            {
                MoveModsOnDoubleClick(list);
                e.Handled = true;
            }

            return;
        }

        if (!control)
        {
            return;
        }

        if (e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                RedoLastChange();
            }
            else
            {
                UndoLastChange();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            RedoLastChange();
            e.Handled = true;
        }
    }

    private void UpdateUndoButton()
    {
        _undoButton.IsEnabled = _undoHistory.Count > 0;
        _redoButton.IsEnabled = _redoHistory.Count > 0;
    }

    private void ClearUndoHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        UpdateUndoButton();
    }

    private void PushUndo(
        List<InstanceSnapshot> snapshots,
        List<string> modIds,
        int sourceColumn,
        int targetColumn
    )
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        _redoHistory.Clear();
        _undoHistory.Add(new UndoEntry(snapshots, modIds, sourceColumn, targetColumn));
        while (_undoHistory.Count > 10)
        {
            _undoHistory.RemoveAt(0);
        }

        UpdateUndoButton();
    }

    private void PushColorUndo(
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after,
        IReadOnlyList<string> modIds,
        int column
    )
    {
        if (_packRoot is null || modIds.Count == 0 ||
            !ColorMapsDiffer(before, after))
        {
            return;
        }

        _redoHistory.Clear();
        _undoHistory.Add(new UndoEntry(
            new List<InstanceSnapshot>(),
            modIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            column,
            column,
            new Dictionary<string, string?>(
                before,
                StringComparer.OrdinalIgnoreCase
            ),
            new Dictionary<string, string?>(
                after,
                StringComparer.OrdinalIgnoreCase
            )
        ));
        while (_undoHistory.Count > 10)
        {
            _undoHistory.RemoveAt(0);
        }

        UpdateUndoButton();
    }

    private static bool ColorMapsDiffer(
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after
    )
    {
        if (before.Count != after.Count)
        {
            return true;
        }

        foreach (var pair in before)
        {
            if (!after.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Dictionary<string, string?> CaptureColorStates(
        IEnumerable<string> packageIds
    )
    {
        var result = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase
        );
        if (_packRoot is null)
        {
            return result;
        }

        var data = ModColorStore.Load(_packRoot);
        foreach (var packageId in packageIds)
        {
            if (string.IsNullOrWhiteSpace(packageId) ||
                result.ContainsKey(packageId))
            {
                continue;
            }

            result[packageId] = data.ModColors.TryGetValue(packageId, out var color)
                ? color
                : null;
        }

        return result;
    }

    private void UndoLastChange()
    {
        if (_undoHistory.Count == 0 || _packRoot is null)
        {
            return;
        }

        var entry = _undoHistory[^1];
        if (entry.ColorBefore is not null)
        {
            try
            {
                ModColorStore.SetModColors(_packRoot, entry.ColorBefore);
                _undoHistory.RemoveAt(_undoHistory.Count - 1);
                _redoHistory.Add(new UndoEntry(
                    new List<InstanceSnapshot>(),
                    entry.ModIds,
                    entry.SourceColumn,
                    entry.TargetColumn,
                    entry.ColorAfter,
                    entry.ColorBefore
                ));
                while (_redoHistory.Count > 10)
                {
                    _redoHistory.RemoveAt(0);
                }

                UpdateUndoButton();
                RefreshManager(false);
                Dispatcher.UIThread.Post(() =>
                    SelectMovedMods(entry.SourceColumn, entry.ModIds)
                );
            }
            catch
            {
                ShowUndoFailed();
            }

            return;
        }

        if (entry.ModIds.Any(id =>
                !_modsById.TryGetValue(id, out var mod) ||
                !Directory.Exists(mod.Path)))
        {
            ShowUndoFailed();
            return;
        }

        try
        {
            var redoSnapshots = ModListOperations.Restore(entry.Instances, _instances);

            _undoHistory.RemoveAt(_undoHistory.Count - 1);
            _redoHistory.Add(new UndoEntry(
                redoSnapshots,
                entry.ModIds,
                entry.SourceColumn,
                entry.TargetColumn
            ));
            while (_redoHistory.Count > 10)
            {
                _redoHistory.RemoveAt(0);
            }

            UpdateUndoButton();
            ReloadInstances(null, -1);
            Dispatcher.UIThread.Post(() =>
                SelectMovedMods(entry.SourceColumn, entry.ModIds)
            );
        }
        catch
        {
            ShowUndoFailed();
        }
    }

    private void RedoLastChange()
    {
        if (_redoHistory.Count == 0 || _packRoot is null)
        {
            return;
        }

        var entry = _redoHistory[^1];
        if (entry.ColorBefore is not null)
        {
            try
            {
                ModColorStore.SetModColors(_packRoot, entry.ColorBefore);
                _redoHistory.RemoveAt(_redoHistory.Count - 1);
                _undoHistory.Add(new UndoEntry(
                    new List<InstanceSnapshot>(),
                    entry.ModIds,
                    entry.SourceColumn,
                    entry.TargetColumn,
                    entry.ColorAfter,
                    entry.ColorBefore
                ));
                while (_undoHistory.Count > 10)
                {
                    _undoHistory.RemoveAt(0);
                }

                UpdateUndoButton();
                RefreshManager(false);
                Dispatcher.UIThread.Post(() =>
                    SelectMovedMods(entry.TargetColumn, entry.ModIds)
                );
            }
            catch
            {
                ShowRedoFailed();
            }

            return;
        }

        if (entry.ModIds.Any(id =>
                !_modsById.TryGetValue(id, out var mod) ||
                !Directory.Exists(mod.Path)))
        {
            ShowRedoFailed();
            return;
        }

        try
        {
            var undoSnapshots = ModListOperations.Restore(entry.Instances, _instances);

            _redoHistory.RemoveAt(_redoHistory.Count - 1);
            _undoHistory.Add(new UndoEntry(
                undoSnapshots,
                entry.ModIds,
                entry.SourceColumn,
                entry.TargetColumn
            ));
            while (_undoHistory.Count > 10)
            {
                _undoHistory.RemoveAt(0);
            }

            UpdateUndoButton();
            ReloadInstances(null, -1);
            Dispatcher.UIThread.Post(() =>
                SelectMovedMods(entry.TargetColumn, entry.ModIds)
            );
        }
        catch
        {
            ShowRedoFailed();
        }
    }

    private void ShowUndoFailed()
    {
        ShowHistoryFailure(Localization.UndoFailed(_config.Language));
    }

    private void ShowRedoFailed()
    {
        ShowHistoryFailure(Localization.RedoFailed(_config.Language));
    }

    private void ShowToast(string text)
    {
        _undoToastText.Text = text;
        _undoToast.IsVisible = true;
        _undoToastTimer.Stop();
        _undoToastTimer.Start();
    }

    private void ShowHistoryFailure(string text)
    {
        ShowToast(text);
    }


    private sealed record UndoEntry(
        List<InstanceSnapshot> Instances,
        List<string> ModIds,
        int SourceColumn,
        int TargetColumn,
        Dictionary<string, string?>? ColorBefore = null,
        Dictionary<string, string?>? ColorAfter = null
    );
    private void UpdateProblemSummary(
        TextBlock? sortText,
        TextBlock versionText,
        TextBlock duplicateText,
        IReadOnlyList<ModInfo> mods
    )
    {
        var sortMods = mods.Where(mod => mod.SortError).ToList();
        var versionMods = mods.Where(mod => mod.VersionMismatch).ToList();
        var duplicateMods = mods.Where(mod => mod.IsDuplicate).ToList();
        var language = _config.Language;

        if (sortText is not null)
        {
            sortText.Text = $"S: {sortMods.Count}";
            var applyColumn = ReferenceEquals(sortText, _activeProblemS1) ? 1 : 2;
            SetSortProblemTooltip(
                sortText,
                Localization.ProblemSortTitle(language),
                sortMods,
                applyColumn
            );
        }
        versionText.Text = $"V: {versionMods.Count}";
        duplicateText.Text = $"R: {duplicateMods.Count}";
        SetProblemTooltip(
            versionText,
            Localization.ProblemVersionTitle(language),
            versionMods
        );
        SetProblemTooltip(
            duplicateText,
            Localization.ProblemDuplicateTitle(language),
            duplicateMods
        );
    }

    private StackPanel CreateProblemTooltipFrame(string title)
    {
        var content = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(new Border
        {
            Height = 1,
            Background = SecondaryBorderBrush,
            Margin = new Thickness(0, 0, 0, 6)
        });
        return content;
    }

    private void SetSortProblemTooltip(
        TextBlock target,
        string title,
        IReadOnlyList<ModInfo> mods,
        int column
    )
    {
        var content = CreateProblemTooltipFrame(title);
        var orderMods = mods
            .Where(mod => mod.SortOrderAfterNames.Count > 0)
            .ToList();
        var missingMods = mods
            .Where(mod => mod.MissingDependencyNames.Count > 0)
            .ToList();
        var hasSection = false;

        if (column > 0 && mods.Any(mod => mod.SortOrderAfterIds.Count > 0))
        {
            content.Children.Add(new Border
            {
                Height = 1,
                Background = SecondaryBorderBrush,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var apply = new Button
            {
                Content = Localization.ApplyButton(_config.Language),
                Background = SecondaryBorderBrush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 3),
                MinHeight = 26,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            apply.Click += (_, _) =>
            {
                ApplySortSuggestions(column);
                CloseProblemTooltip();
            };
            content.Children.Add(apply);
        }

        if (orderMods.Count > 0)
        {
            AddSortSection(
                content,
                Localization.SortOrderErrorCategory(_config.Language),
                orderMods,
                mod => Localization.SortAfterTooltip(
                    _config.Language,
                    string.Join(", ", mod.SortOrderAfterNames)
                ),
                first: true
            );
            hasSection = true;
        }

        if (missingMods.Count > 0)
        {
            AddSortSection(
                content,
                Localization.MissingDependencyCategory(_config.Language),
                missingMods,
                mod => Localization.MissingDependencies(
                    _config.Language,
                    string.Join(", ", mod.MissingDependencyNames)
                ),
                first: !hasSection
            );
            hasSection = true;
        }

        var conflicts = mods.Where(mod => mod.IncompatibleNames.Count > 0).ToList();
        if (conflicts.Count > 0)
        {
            AddSortSection(content, OperationText.Choose(_config.Language, "Incompatible mods", "不兼容模组", "不相容模組", "非互換 MOD"),
                conflicts, mod => Localization.IncompatibleMods(_config.Language, string.Join(", ", mod.IncompatibleNames)), !hasSection);
            hasSection = true;
        }
        if (!hasSection)
        {
            var names = GetProblemModNames(mods);
            content.Children.Add(new SelectableTextBlock
            {
                Text = names.Count == 0 ? "0" : string.Join(Environment.NewLine, names)
            });
        }

        SetSortProblemTooltipContent(target, content);
    }

    private void AddSortSection(
        StackPanel content,
        string title,
        IReadOnlyList<ModInfo> mods,
        Func<ModInfo, string> detail,
        bool first
    )
    {
        if (!first)
        {
            content.Children.Add(new Border
            {
                Height = 1,
                Background = SecondaryBorderBrush,
                Margin = new Thickness(0, 8, 0, 6)
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = $"{title} ({mods.Count})",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 0, 0, 2)
        });

        foreach (var mod in mods)
        {
            var name = string.IsNullOrWhiteSpace(mod.Name)
                ? mod.PackageId
                : mod.Name;
            content.Children.Add(new SelectableTextBlock
            {
                Text = "• " + name,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            content.Children.Add(new SelectableTextBlock
            {
                Text = detail(mod),
                FontSize = 14,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(14, 0, 0, 0)
            });
        }
    }

    private static List<string> GetProblemModNames(IReadOnlyList<ModInfo> mods)
    {
        return mods
            .Select(mod => string.IsNullOrWhiteSpace(mod.Name)
                ? mod.PackageId
                : mod.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    private void SetProblemTooltip(
        TextBlock target,
        string title,
        IReadOnlyList<ModInfo> mods
    )
    {
        var names = GetProblemModNames(mods);
        var content = CreateProblemTooltipFrame(title);
        if (names.Count == 0)
        {
            content.Children.Add(new SelectableTextBlock { Text = "0" });
            SetProblemTooltipContent(target, content);
            return;
        }

        const double lineHeight = 18;
        var halfHeight = Math.Max(
            180,
            (Bounds.Height > 0 ? Bounds.Height : 600) * 0.5
        );
        var maxRows = Math.Max(1, (int)(halfHeight / lineHeight));
        var columnCount = (int)Math.Ceiling((double)names.Count / maxRows);
        var rows = (int)Math.Ceiling((double)names.Count / columnCount);
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal
        };

        for (var column = 0; column < columnCount; column++)
        {
            if (column > 0)
            {
                panel.Children.Add(new Border
                {
                    Width = 1,
                    Height = rows * lineHeight,
                    Background = SecondaryBorderBrush,
                    Margin = new Thickness(10, 0)
                });
            }

            panel.Children.Add(new SelectableTextBlock
            {
                Text = string.Join(
                    Environment.NewLine,
                    names.Skip(column * rows).Take(rows)
                ),
                FontSize = 14,
                LineHeight = lineHeight,
                TextWrapping = TextWrapping.NoWrap
            });
        }

        content.Children.Add(panel);
        SetProblemTooltipContent(target, content);
    }

    private void AttachProblemTooltipHover(Control target, Control content)
    {
        content.PointerEntered += (_, _) =>
        {
            _problemTooltipContentHovered = true;
            _problemTooltipTimer.Stop();
        };
        content.PointerExited += (_, _) =>
        {
            if (ReferenceEquals(_problemTooltipTarget, target))
            {
                _problemTooltipContentHovered = false;
                StartProblemTooltipCloseTimer();
            }
        };
    }

    private void SetProblemTooltipContent(Control target, StackPanel content)
    {
        AttachProblemTooltipHover(target, content);
        ToolTip.SetTip(target, content);
    }

    private void SetSortProblemTooltipContent(TextBlock target, StackPanel content)
    {
        var popupContent = new Border
        {
            Background = ThemeService.BackgroundBrush,
            BorderBrush = SecondaryBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            Child = new ScrollViewer
            {
                Content = content,
                MaxHeight = 520
            }
        };
        AttachProblemTooltipHover(target, popupContent);
        target.Tag = popupContent;
    }

    private void OnProblemLabelPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not TextBlock target)
        {
            return;
        }

        var tip = ReferenceEquals(target, _activeProblemS1) || ReferenceEquals(target, _activeProblemS2)
            ? target.Tag as Control
            : ToolTip.GetTip(target) as Control;
        if (tip is null)
        {
            return;
        }

        _problemTooltipTimer.Stop();
        if (_problemTooltipTarget is not null &&
            !ReferenceEquals(_problemTooltipTarget, target))
        {
            ToolTip.SetIsOpen(_problemTooltipTarget, false);
        }
        _modBadgePopup.IsOpen = false;

        _problemTooltipTarget = target;
        _problemTooltipContentHovered = false;
        if (ReferenceEquals(target, _activeProblemS1) || ReferenceEquals(target, _activeProblemS2))
        {
            _modBadgePopup.Child = null;
            _modBadgePopup.Child = tip;
            _modBadgePopup.PlacementTarget = target;
            _modBadgePopup.IsOpen = true;
        }
        else
        {
            ToolTip.SetIsOpen(target, true);
        }
    }

    private void OnModBadgePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control target ||
            target.DataContext is not ModInfo mod ||
            string.IsNullOrWhiteSpace(mod.BadgeTooltip))
        {
            return;
        }

        _problemTooltipTimer.Stop();
        if (_problemTooltipTarget is not null &&
            !ReferenceEquals(_problemTooltipTarget, target))
        {
            ToolTip.SetIsOpen(_problemTooltipTarget, false);
        }
        _modBadgePopup.IsOpen = false;

        _problemTooltipTarget = target;
        _problemTooltipContentHovered = false;
        var content = CreateModBadgeTooltip(mod);
        AttachProblemTooltipHover(target, content);
        _modBadgePopup.Child = content;
        _modBadgePopup.PlacementTarget = target;
        _modBadgePopup.IsOpen = true;
    }

    private void OnModBadgePointerExited(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(sender, _problemTooltipTarget))
        {
            StartProblemTooltipCloseTimer();
        }
    }

    private Border CreateModBadgeTooltip(ModInfo mod)
    {
        var content = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 6
        };
        var body = new SelectableTextBlock
        {
            Text = mod.BadgeTooltip,
            FontSize = 14,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Focusable = true,
            SelectionBrush = ThemeService.SelectedBrush,
            SelectionForegroundBrush = ThemeService.PrimaryTextBrush
        };
        body.PointerPressed += (_, _) => body.Focus();
        body.KeyDown += (_, keyEvent) =>
        {
            if (keyEvent.Key == Key.C &&
                (keyEvent.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                 keyEvent.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                body.Copy();
                keyEvent.Handled = true;
            }
        };

        var copyMenu = new ContextMenu();
        var copyItem = new MenuItem
        {
            Header = Localization.CopyButton(_config.Language)
        };
        copyItem.Click += (_, _) => body.Copy();
        copyMenu.Items.Add(copyItem);
        copyMenu.Opened += (_, _) =>
        {
            _problemTooltipContentHovered = true;
            _problemTooltipTimer.Stop();
        };
        copyMenu.Closed += (_, _) => _problemTooltipContentHovered = false;
        body.ContextMenu = copyMenu;
        content.Children.Add(body);

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };
        if (mod.SortError && mod.SortOrderAfterIds.Count > 0)
        {
            var apply = new Button
            {
                Content = Localization.ApplyButton(_config.Language),
                Background = SecondaryBorderBrush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 3),
                MinHeight = 26
            };
            apply.Click += (_, _) =>
            {
                ApplySortSuggestion(mod);
                CloseProblemTooltip();
            };
            actions.Children.Add(apply);
        }

        if (actions.Children.Count > 0)
        {
            content.Children.Add(actions);
        }

        return new Border
        {
            Background = ThemeService.BackgroundBrush,
            BorderBrush = SecondaryBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            Child = content
        };
    }

    private void ApplySortSuggestion(ModInfo mod)
    {
        var column = mod.BadgeColumn;
        if (column < 1)
        {
            return;
        }

        var instance = GetInstanceForColumn(column);
        if (instance is null)
        {
            return;
        }

        var indices = mod.SortOrderAfterIds
            .Select(id => instance.ActivePackageIds.FindIndex(
                activeId => string.Equals(
                    activeId,
                    id,
                    StringComparison.OrdinalIgnoreCase
                )
            ))
            .Where(index => index >= 0)
            .ToList();
        if (indices.Count == 0)
        {
            return;
        }

        MoveMods(
            new DragPayload(column, new List<ModInfo> { mod }),
            column,
            indices.Max() + 1
        );
    }

    private void ApplySortSuggestions(int column)
    {
        var instance = GetInstanceForColumn(column);
        if (instance is null || instance.IsDamaged)
        {
            return;
        }

        var active = ApplyOrderFlags(
            ApplySteamCmdFilter(ResolveActive(instance), instance),
            column
        );
        var order = ModListOperations.ApplySortSuggestions(
            instance.ActivePackageIds,
            active
        );
        if (ActiveIdsEqual(instance.ActivePackageIds, order))
        {
            return;
        }

        var ids = active
            .Where(mod => mod.SortOrderAfterIds.Count > 0)
            .Select(mod => mod.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var snapshots = new List<InstanceSnapshot>
        {
            new(instance.FolderName, instance.ActivePackageIds.ToList())
        };
        try
        {
            InstanceStore.StageActiveMods(instance, order);
            PushUndo(snapshots, ids, column, column);
            RefreshManager(false);
            Dispatcher.UIThread.Post(() => SelectMovedMods(column, ids));
        }
        catch (Exception exception)
        {
            AppLog.Error("apply sort suggestions", exception);
        }
    }

    private void OnProblemLabelPointerExited(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(sender, _problemTooltipTarget))
        {
            StartProblemTooltipCloseTimer();
        }
    }

    private void StartProblemTooltipCloseTimer()
    {
        if (_problemTooltipContentHovered)
        {
            return;
        }

        _problemTooltipTimer.Stop();
        _problemTooltipTimer.Start();
    }

    private void CloseProblemTooltip()
    {
        _problemTooltipTimer.Stop();
        if (_problemTooltipTarget is not null)
        {
            ToolTip.SetIsOpen(_problemTooltipTarget, false);
        }
        _modBadgePopup.IsOpen = false;
        _modBadgePopup.Child = null;

        _problemTooltipTarget = null;
        _problemTooltipContentHovered = false;
    }

    private List<MissingModRow> BuildMissingMods(int instanceIndex)
    {
        var result = new List<MissingModRow>();
        if (instanceIndex < 0 || instanceIndex >= _instances.Count)
        {
            return result;
        }

        var instance = _instances[instanceIndex];
        var mapping = ModFullNameList.Load(instance);
        var history = ModFullNameList.LoadHistory(instance).ToDictionary(
            entry => entry.PackageId,
            StringComparer.OrdinalIgnoreCase
        );
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var activeId in instance.ActivePackageIds)
        {
            var packageId = activeId.Trim();
            if (packageId.Length == 0 ||
                _modsById.ContainsKey(packageId) ||
                !seen.Add(packageId))
            {
                continue;
            }

            mapping.TryGetValue(packageId, out var entry);
            history.TryGetValue(packageId, out var historyEntry);
            var label = entry?.Label;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = historyEntry?.Label;
            }
            if (string.IsNullOrWhiteSpace(label))
            {
                label = packageId;
            }
            var author = entry?.Author;
            if (string.IsNullOrWhiteSpace(author))
            {
                author = historyEntry?.Author;
            }
            if (string.IsNullOrWhiteSpace(author))
            {
                author = "—";
            }
            var link = entry?.Link;
            if (string.IsNullOrWhiteSpace(link))
            {
                link = historyEntry?.Link;
            }
            if (string.IsNullOrWhiteSpace(link))
            {
                link = "—";
            }

            result.Add(new MissingModRow
            {
                Label = label,
                Author = author,
                PackageId = packageId,
                Link = link
            });
        }

        return result;
    }

    private int GetHistoryCount(int instanceIndex)
    {
        if (instanceIndex < 0 || instanceIndex >= _instances.Count)
        {
            return 0;
        }

        return ModFullNameList.LoadHistory(_instances[instanceIndex]).Count;
    }

    private void UpdateMissingButton(Button button, int missingCount, int historyCount)
    {
        button.IsVisible = missingCount > 0 || historyCount > 0;
        if (missingCount > 0)
        {
            button.Content = Localization.MissingModsLabel(
                _config.Language,
                missingCount
            );
            ToolTip.SetTip(button, Localization.MissingModsTooltip(_config.Language));
            return;
        }

        button.Content = Localization.MissingHistoryLabel(
            _config.Language,
            historyCount
        );
        ToolTip.SetTip(button, Localization.MissingHistoryTooltip(_config.Language));
    }

    private async void OnMissing1Click(object? sender, RoutedEventArgs e)
    {
        await ShowMissingModsAsync(0);
    }

    private async void OnMissing2Click(object? sender, RoutedEventArgs e)
    {
        await ShowMissingModsAsync(1);
    }

    private async Task ShowMissingModsAsync(int column)
    {
        var instance = GetSelectedInstance(column);
        var rows = column == 0 ? _missingMods1 : _missingMods2;
        if (instance is null)
        {
            return;
        }

        if (rows.Count == 0)
        {
            if (ModFullNameList.LoadHistory(instance).Count == 0)
            {
                return;
            }

            var historyWindow = new MissingHistoryWindow(instance, _config.Language);
            await historyWindow.ShowDialog(this);
            return;
        }

        var window = new MissingModsWindow(
            instance,
            rows,
            _config.Language
        );
        await window.ShowDialog(this);
    }

    private void UpdateHeaders()
    {
        var language = _config.Language;
        _inactiveHeader.Text = $"{Localization.Inactive(language)} [{_inactiveCount}]";
        _activeHeader1.Text = $"{Localization.Active(language)} [{_active1Count}]";
        _activeHeader2.Text = $"{Localization.Active(language)} [{_active2Count}]";
        UpdateProblemFilterVisuals();
    }

    private void UpdateProblemFilterVisuals()
    {
        _inactiveProblemV.TextDecorations = ProblemDecoration(_inactiveFilter.Version);
        _inactiveProblemR.TextDecorations = ProblemDecoration(_inactiveFilter.Duplicate);
        _activeProblemS1.TextDecorations = ProblemDecoration(_activeFilter1.Sort);
        _activeProblemV1.TextDecorations = ProblemDecoration(_activeFilter1.Version);
        _activeProblemR1.TextDecorations = ProblemDecoration(_activeFilter1.Duplicate);
        _activeProblemS2.TextDecorations = ProblemDecoration(_activeFilter2.Sort);
        _activeProblemV2.TextDecorations = ProblemDecoration(_activeFilter2.Version);
        _activeProblemR2.TextDecorations = ProblemDecoration(_activeFilter2.Duplicate);
    }

    private static TextDecorationCollection? ProblemDecoration(ProblemFilterMode mode)
    {
        return mode switch
        {
            ProblemFilterMode.Hide => StrikethroughDecorations,
            ProblemFilterMode.Only => UnderlineDecorations,
            _ => null
        };
    }

    private static List<ModInfo> ApplyModColors(
        IEnumerable<ModInfo> mods,
        ModColorData data
    )
    {
        var result = new List<ModInfo>();
        foreach (var mod in mods)
        {
            data.ModColors.TryGetValue(mod.PackageId, out var hex);
            var visual = GetColorVisual(hex, data);
            result.Add(mod with
            {
                RowBrush = visual.RowBrush,
                NameEffect = visual.NameEffect,
                ColorLabel = visual.ColorLabel
            });
        }

        return result;
    }

    private static (
        IBrush? RowBrush,
        IEffect? NameEffect,
        string? ColorLabel
    ) GetColorVisual(string? hex, ModColorData data)
    {
        if (string.IsNullOrWhiteSpace(hex) ||
            !Color.TryParse(hex, out var color))
        {
            return (null, null, null);
        }

        var brush = new SolidColorBrush(Color.FromArgb(
            210,
            color.R,
            color.G,
            color.B
        ));
        var luminance = (
            0.299 * color.R +
            0.587 * color.G +
            0.114 * color.B
        ) / 255.0;
        data.ColorLabels.TryGetValue(hex, out var label);
        return (
            brush,
            luminance >= 0.53 ? TextOutlineEffect : null,
            string.IsNullOrWhiteSpace(label) ? null : label
        );
    }

    private static void ApplyColorToInfo(
        ModInfo mod,
        string? hex,
        ModColorData data
    )
    {
        var visual = GetColorVisual(hex, data);
        mod.RowBrush = visual.RowBrush;
        mod.NameEffect = visual.NameEffect;
        mod.ColorLabel = visual.ColorLabel;
    }

    private static List<ModInfo> ApplyProblemFilters(
        IEnumerable<ModInfo> mods,
        ProblemFilterState filter
    )
    {
        if (filter.Sort == ProblemFilterMode.Off &&
            filter.Version == ProblemFilterMode.Off &&
            filter.Duplicate == ProblemFilterMode.Off)
        {
            return mods as List<ModInfo> ?? mods.ToList();
        }

        return mods
            .Where(mod => PassesProblemFilter(filter.Sort, mod.SortError) &&
                          PassesProblemFilter(filter.Version, mod.VersionMismatch) &&
                          PassesProblemFilter(filter.Duplicate, mod.IsDuplicate))
            .ToList();
    }

    private static bool PassesProblemFilter(ProblemFilterMode mode, bool hasProblem)
    {
        return mode switch
        {
            ProblemFilterMode.Hide => !hasProblem,
            ProblemFilterMode.Only => hasProblem,
            _ => true
        };
    }

    private static ProblemFilterMode CycleProblemFilter(ProblemFilterMode mode)
    {
        return mode switch
        {
            ProblemFilterMode.Off => ProblemFilterMode.Hide,
            ProblemFilterMode.Hide => ProblemFilterMode.Only,
            _ => ProblemFilterMode.Off
        };
    }


    private void OnInactiveProblemVClick(object? sender, PointerPressedEventArgs e)
    {
        _inactiveFilter.Version = CycleProblemFilter(_inactiveFilter.Version);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnInactiveProblemRClick(object? sender, PointerPressedEventArgs e)
    {
        _inactiveFilter.Duplicate = CycleProblemFilter(_inactiveFilter.Duplicate);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnActiveProblemS1Click(object? sender, PointerPressedEventArgs e)
    {
        _activeFilter1.Sort = CycleProblemFilter(_activeFilter1.Sort);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnActiveProblemV1Click(object? sender, PointerPressedEventArgs e)
    {
        _activeFilter1.Version = CycleProblemFilter(_activeFilter1.Version);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnActiveProblemR1Click(object? sender, PointerPressedEventArgs e)
    {
        _activeFilter1.Duplicate = CycleProblemFilter(_activeFilter1.Duplicate);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnActiveProblemS2Click(object? sender, PointerPressedEventArgs e)
    {
        _activeFilter2.Sort = CycleProblemFilter(_activeFilter2.Sort);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnActiveProblemV2Click(object? sender, PointerPressedEventArgs e)
    {
        _activeFilter2.Version = CycleProblemFilter(_activeFilter2.Version);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnActiveProblemR2Click(object? sender, PointerPressedEventArgs e)
    {
        _activeFilter2.Duplicate = CycleProblemFilter(_activeFilter2.Duplicate);
        RefreshManager(false);
        e.Handled = true;
    }

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ValidatePath(save: true);
            e.Handled = true;
        }
    }

    private void OnPathLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_pathBox.Text))
        {
            ValidatePath(save: true);
        }
    }

    private void OnPathTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pathBox.Text))
        {
            SetFieldValid(true);
            _lastLoadedGamePath = null;
            _loadVersion++;
            ResetCatalog();
        }
    }

    private async void OnLanguageClick(object? sender, RoutedEventArgs e)
    {
        var languageWindow = new LanguageWindow();
        languageWindow.LanguageSelected += (_, language) =>
        {
            ApplyLanguage(language);
            languageWindow.Close();
        };
        await languageWindow.ShowDialog(this);
    }

    private async void OnAutoDetectGameClick(object? sender, RoutedEventArgs e)
    {
        await AutoDetectGameAsync();
    }

    private async Task AutoDetectGameAsync()
    {
        if (!_autoDetectGameButton.IsEnabled)
        {
            return;
        }

        _autoDetectGameButton.IsEnabled = false;
        try
        {
            var path = await Task.Run(RimWorldLocator.FindGameDirectory);
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowToast(Localization.AutoDetectGameFailed(_config.Language));
                return;
            }

            _pathBox.Text = path;
            ValidatePath(save: true);
            if (!_loadingMods &&
                _allMods.Count == 0 &&
                _gameInstallation is not null)
            {
                _ = LoadModsAsync(_gameInstallation);
            }
            ShowToast(Localization.AutoDetected(_config.Language));
        }
        catch
        {
            ShowToast(Localization.AutoDetectGameFailed(_config.Language));
        }
        finally
        {
            _autoDetectGameButton.IsEnabled = true;
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null)
            {
                return;
            }

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false
            });

            if (folders.Count == 0)
            {
                return;
            }

            var path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            _pathBox.Text = path;
            ValidatePath(save: true);
        }
        catch
        {
        }
    }

    private void ValidatePath(bool save)
    {
        var result = RimWorldLocator.Detect(_pathBox.Text);
        _gameInstallation = result.IsValid ? result : null;
        SetFieldValid(result.IsValid);
        UpdateLaunchButton();

        if (save)
        {
            _config.GamePath = _pathBox.Text ?? string.Empty;
            _config.Save();
        }

        if (!result.IsValid)
        {
            _lastLoadedGamePath = null;
            _loadVersion++;
            ResetCatalog();
            return;
        }

        if (!string.Equals(_lastLoadedGamePath, result.GamePath, StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadModsAsync(result);
        }
    }

    private async Task LoadModsAsync(RimWorldInstallation game)
    {

        var version = ++_loadVersion;
        _loadingMods = true;
        _lastLoadedGamePath = game.GamePath;
        ResetCatalog();
        ClearUndoHistory();
        _refreshButton.Content = Localization.LoadingMods(_config.Language);
        _refreshButton.IsEnabled = false;

        try
        {
            _packRoot = AppPaths.FindPackRoot();
            var mods = await ModCatalog.LoadAsync(game, _packRoot);
            if (version != _loadVersion)
            {
                return;
            }

            var instances = _packRoot is null
                ? new List<ManagedInstance>()
                : InstanceStore.Discover(_packRoot);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _loadVersion)
                {
                    return;
                }

                _allMods = mods;
                _modsByPackage = mods
                    .GroupBy(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.ToList(),
                        StringComparer.OrdinalIgnoreCase
                    );
                _modsById = _modsByPackage.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value[0],
                    StringComparer.OrdinalIgnoreCase
                );
                _instances = BuildInstances(instances);
                _gameVersion = game.Version;
                InitializeInstanceSelectors();
                RefreshManager(true);

            });
        }
        catch (Exception exception)
        {
            AppLog.Error("Load mods and instances: " + game.GamePath, exception);
            if (version == _loadVersion)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (version == _loadVersion)
                    {
                        ResetCatalog();
                        ShowToast(Localization.RefreshFailed(_config.Language));
                    }
                });
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                _loadingMods = false;
                _refreshButton.Content = Localization.Refresh(_config.Language);
                _refreshButton.IsEnabled = true;
            }
        }
    }

    private List<ManagedInstance> BuildInstances(IEnumerable<ManagedInstance> instances)
    {
        var result = new List<ManagedInstance>
        {
            new ManagedInstance
            {
                FolderName = ScratchFolderName,
                DirectoryPath = string.Empty,
                IsScratch = true,
                ActivePackageIds = _scratchActiveIds
            },
            InstanceStore.LoadOfficial(
                _config.OfficialConfigPath,
                Localization.OfficialPath(_config.Language)
            )
        };
        var normalInstances = instances
            .Where(instance => !instance.IsOfficial && !instance.IsScratch)
            .ToList();
        if (_config.ProfileOrder.Count > 0)
        {
            normalInstances = normalInstances
                .OrderBy(instance =>
                {
                    var index = _config.ProfileOrder.FindIndex(folder =>
                        string.Equals(
                            folder,
                            instance.FolderName,
                            StringComparison.OrdinalIgnoreCase
                        ));
                    return index < 0 ? int.MaxValue : index;
                })
                .ToList();
        }

        result.AddRange(normalInstances);
        return result;
    }

    private int FindFirstNormalIndex()
    {
        return _instances.FindIndex(instance =>
            !instance.IsOfficial && !instance.IsScratch);
    }

    private static int FindInstanceFolderIndex(
        IReadOnlyList<string> folders,
        string? folderName
    )
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return -1;
        }

        for (var i = 0; i < folders.Count; i++)
        {
            if (string.Equals(
                folders[i],
                folderName,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return i;
            }
        }

        return -1;
    }

    private void SetInstanceNames(IReadOnlyList<string> names)
    {
        UpdateInstanceNames(_instanceNames1, names);
        UpdateInstanceNames(_instanceNames2, names);
    }

    private static void UpdateInstanceNames(
        ObservableCollection<string> target,
        IReadOnlyList<string> names
    )
    {
        if (target.Count == names.Count &&
            target.SequenceEqual(names, StringComparer.Ordinal))
        {
            return;
        }

        if (target.Count == names.Count)
        {
            for (var i = 0; i < names.Count; i++)
            {
                target[i] = names[i];
            }

            return;
        }

        target.Clear();
        foreach (var name in names)
        {
            target.Add(name);
        }
    }

    private void InitializeInstanceSelectors()
    {
        var names = _instances.Select(instance => instance.DisplayName).ToList();
        var folders = _instances
            .Select(instance => instance.FolderName)
            .ToList();
        var firstNormal = FindFirstNormalIndex();
        var index1 = FindInstanceFolderIndex(folders, _config.ProfileFolder1);
        if (index1 < 0 && firstNormal >= 0)
        {
            index1 = firstNormal;
        }
        else if (index1 < 0)
        {
            index1 = FindInstanceFolderIndex(folders, ScratchFolderName);
        }

        var index2 = FindInstanceFolderIndex(folders, _config.ProfileFolder2);
        if (index2 < 0 && firstNormal >= 0)
        {
            index2 = firstNormal + 1 < _instances.Count
                ? firstNormal + 1
                : index1;
        }

        _updatingInstances = true;
        SetInstanceNames(names);
        _instanceCombo1.SelectedIndex = index1;
        _instanceCombo2.SelectedIndex = index2;
        _updatingInstances = false;
        _lastInstanceIndex1 = _instanceCombo1.SelectedIndex;
        _lastInstanceIndex2 = _instanceCombo2.SelectedIndex;
    }

    private void ResetCatalog()
    {
        foreach (var timer in _searchTimers.Values) timer.Stop();
        _searchSources.Clear();
        _allMods = new List<ModInfo>();
        _modsById = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        _modsByPackage = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
        _instances = new List<ManagedInstance>();
        _updatingInstances = true;
        _instanceNames1.Clear();
        _instanceNames2.Clear();
        _instanceCombo1.SelectedIndex = -1;
        _instanceCombo2.SelectedIndex = -1;
        _updatingInstances = false;
        _lastInstanceIndex1 = -1;
        _lastInstanceIndex2 = -1;
        _refreshing = true;
        _inactiveList.ItemsSource = null;
        _activeList1.ItemsSource = null;
        _activeList2.ItemsSource = null;
        _refreshing = false;
        _inactiveCount = 0;
        _active1Count = 0;
        _active2Count = 0;
        _missingMods1 = new List<MissingModRow>();
        _missingMods2 = new List<MissingModRow>();
        _activeMissing1.IsVisible = false;
        _activeMissing2.IsVisible = false;
        UpdateHeaders();
        ShowDetails(null);
        UpdateLaunchButton();
        UpdateSaveState();
        var empty = new List<ModInfo>();
        UpdateProblemSummary(null, _inactiveProblemV, _inactiveProblemR, empty);
        UpdateProblemSummary(_activeProblemS1, _activeProblemV1, _activeProblemR1, empty);
        UpdateProblemSummary(_activeProblemS2, _activeProblemV2, _activeProblemR2, empty);
    }

    private readonly Dictionary<ListBox, List<ModInfo>> _searchSources = new();
    private readonly Dictionary<ListBox, DispatcherTimer> _searchTimers = new();

    private void RefreshManager(bool selectFirst)
    {
        ToolTip.SetTip(_instanceCombo1, GetSelectedInstance(0)?.LoadError);
        ToolTip.SetTip(_instanceCombo2, GetSelectedInstance(1)?.LoadError);
        foreach (var timer in _searchTimers.Values) timer.Stop();
        _searchSources.Clear();
        if (_allMods.Count == 0)
        {
            ResetCatalog();
            return;
        }

        var index1 = GetInstanceIndex(_instanceCombo1, 0);
        var index2 = GetInstanceIndex(_instanceCombo2, 1);
        var instance1 = GetInstanceAt(index1);
        var instance2 = GetInstanceAt(index2);
        var active1 = ApplyOrderFlags(
            ApplySteamCmdFilter(ResolveActive(instance1), instance1),
            1
        );
        var active2 = ApplyOrderFlags(
            ApplySteamCmdFilter(ResolveActive(instance2), instance2),
            2
        );
        _missingMods1 = BuildMissingMods(index1);
        _missingMods2 = BuildMissingMods(index2);

        var language = _config.Language;
        foreach (var mod in _allMods)
        {
            mod.BadgeTooltip = BuildBadgeTooltip(mod, language, _gameVersion);
        }
        foreach (var mod in active1)
        {
            mod.BadgeTooltip = BuildBadgeTooltip(mod, language, _gameVersion);
        }
        foreach (var mod in active2)
        {
            mod.BadgeTooltip = BuildBadgeTooltip(mod, language, _gameVersion);
        }

        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetIndex = _lastEnableColumn == 1 ? index1 : index2;
        if (targetIndex >= 0 && targetIndex < _instances.Count)
        {
            foreach (var id in _instances[targetIndex].ActivePackageIds)
            {
                activeIds.Add(id);
            }
        }

        var targetInstance = GetInstanceAt(targetIndex);
        var inactiveSource = DeduplicateModsForInstance(
            ApplySteamCmdFilter(
                _allMods.Where(mod => !activeIds.Contains(mod.PackageId)),
                targetInstance
            ),
            targetInstance
        );
        _searchSources[_inactiveList] = inactiveSource.ToList();
        _searchSources[_activeList1] = active1;
        _searchSources[_activeList2] = active2;
        var inactive = PrepareSearchList(_searchSources[_inactiveList], _inactiveSearch);
        inactive = SortInactive(inactive);
        active1 = PrepareSearchList(active1, _activeSearch1);
        active2 = PrepareSearchList(active2, _activeSearch2);
        var inactiveForSummary = inactive;
        var active1ForSummary = active1;
        var active2ForSummary = active2;
        inactive = ApplyProblemFilters(inactive, _inactiveFilter);
        active1 = ApplyProblemFilters(active1, _activeFilter1);
        active2 = ApplyProblemFilters(active2, _activeFilter2);
        var colorData = ModColorStore.Load(_packRoot);
        inactive = ApplyModColors(inactive, colorData);
        active1 = ApplyModColors(active1, colorData);
        active2 = ApplyModColors(active2, colorData);

        _refreshing = true;
        foreach (var list in GetModLists())
        {
            list.SelectedItems!.Clear();
        }
        _inactiveList.ItemsSource = inactive;
        _activeList1.ItemsSource = active1;
        _activeList2.ItemsSource = active2;
        _refreshing = false;

        _inactiveCount = inactive.Count;
        _active1Count = active1.Count;
        _active2Count = active2.Count;
        UpdateHeaders();
        UpdateLaunchButton();
        UpdateSaveState();
        UpdateProblemSummary(
            null,
            _inactiveProblemV,
            _inactiveProblemR,
            inactiveForSummary
        );
        UpdateProblemSummary(
            _activeProblemS1,
            _activeProblemV1,
            _activeProblemR1,
            active1ForSummary
        );
        UpdateProblemSummary(
            _activeProblemS2,
            _activeProblemV2,
            _activeProblemR2,
            active2ForSummary
        );
        UpdateMissingButton(
            _activeMissing1,
            _missingMods1.Count,
            _missingMods1.Count > 0 ? 0 : GetHistoryCount(index1)
        );
        UpdateMissingButton(
            _activeMissing2,
            _missingMods2.Count,
            _missingMods2.Count > 0 ? 0 : GetHistoryCount(index2)
        );

        if (!selectFirst)
        {
            return;
        }

        var first = inactive.FirstOrDefault()
            ?? active1.FirstOrDefault()
            ?? active2.FirstOrDefault();
        if (first is null)
        {
            ShowDetails(null);
            return;
        }

        _refreshing = true;
        if (inactive.Contains(first))
        {
            _inactiveList.SelectedItem = first;
        }
        else if (active1.Contains(first))
        {
            _activeList1.SelectedItem = first;
        }
        else
        {
            _activeList2.SelectedItem = first;
        }
        _refreshing = false;
        ShowDetails(first);
    }

    private void OnSearchTextChanged(ModSearchState state, string? text, ListBox list)
    {
        state.Query = text ?? string.Empty;
        if (_allMods.Count == 0)
        {
            return;
        }

        if (!_searchTimers.TryGetValue(list, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            timer.Tick += (_, _) => { timer.Stop(); RefreshSearch(list); };
            _searchTimers[list] = timer;
        }
        timer.Stop();
        timer.Start();
    }

    private void OnSearchFieldChanged(
        ModSearchState state,
        ComboBox comboBox,
        ListBox list
    )
    {
        if (_updatingSearch)
        {
            return;
        }

        state.FieldIndex = comboBox.SelectedIndex >= 0 ? comboBox.SelectedIndex : 0;
        if (_allMods.Count > 0)
        {
            RefreshSearch(list);
        }
    }

    private void RefreshSearch(ListBox list)
    {
        if (_searchTimers.TryGetValue(list, out var timer)) timer.Stop();
        if (!_searchSources.TryGetValue(list, out var source))
        {
            RefreshManager(false);
            return;
        }
        var inactive = ReferenceEquals(list, _inactiveList);
        var firstColumn = ReferenceEquals(list, _activeList1);
        var state = inactive ? _inactiveSearch : firstColumn ? _activeSearch1 : _activeSearch2;
        var filter = inactive ? _inactiveFilter : firstColumn ? _activeFilter1 : _activeFilter2;
        var matches = PrepareSearchList(source, state);
        if (inactive) matches = SortInactive(matches);
        var visible = ApplyModColors(ApplyProblemFilters(matches, filter), ModColorStore.Load(_packRoot));
        var selectedId = (list.SelectedItem as ModInfo)?.PackageId;
        _refreshing = true;
        // Search becomes the active selection context, as with clicking a different column.
        foreach (var other in GetModLists())
            if (!ReferenceEquals(other, list)) other.SelectedItems!.Clear();
        list.ItemsSource = visible;
        var first = visible.FirstOrDefault(mod => mod.PackageId == selectedId) ?? visible.FirstOrDefault();
        list.SelectedItem = first;
        _selectionAnchorList = list;
        _selectionAnchorIndex = first is null ? -1 : visible.IndexOf(first);
        _lastPressList = list;
        _refreshing = false;
        if (inactive) _inactiveCount = visible.Count;
        else if (firstColumn) _active1Count = visible.Count;
        else _active2Count = visible.Count;
        UpdateHeaders();
        UpdateProblemSummary(
            inactive ? null : firstColumn ? _activeProblemS1 : _activeProblemS2,
            inactive ? _inactiveProblemV : firstColumn ? _activeProblemV1 : _activeProblemV2,
            inactive ? _inactiveProblemR : firstColumn ? _activeProblemR1 : _activeProblemR2, matches);
        ShowDetails(first);
    }

    private void OnInactiveSourceClick(object? sender, RoutedEventArgs e)
    {
        CycleSource(_inactiveSearch, _inactiveSourceButton, _inactiveList);
    }

    private void OnActiveSource1Click(object? sender, RoutedEventArgs e)
    {
        CycleSource(_activeSearch1, _activeSourceButton1, _activeList1);
    }

    private void OnActiveSource2Click(object? sender, RoutedEventArgs e)
    {
        CycleSource(_activeSearch2, _activeSourceButton2, _activeList2);
    }

    private void CycleSource(
        ModSearchState state,
        Button button,
        ListBox list,
        int step = 1
    )
    {
        state.SourceIndex = (state.SourceIndex + step + 3) % 3;
        UpdateSourceButton(button, state, _config.Language);
        if (_allMods.Count > 0)
        {
            RefreshSearch(list);
        }
    }

    private void OnInactiveViewModeClick(object? sender, RoutedEventArgs e)
    {
        ToggleSearchViewMode(
            _inactiveSearch,
            _inactiveViewModeButton,
            _inactiveList
        );
    }

    private void OnActiveViewMode1Click(object? sender, RoutedEventArgs e)
    {
        ToggleSearchViewMode(
            _activeSearch1,
            _activeViewModeButton1,
            _activeList1
        );
    }

    private void OnActiveViewMode2Click(object? sender, RoutedEventArgs e)
    {
        ToggleSearchViewMode(
            _activeSearch2,
            _activeViewModeButton2,
            _activeList2
        );
    }

    private void ToggleSearchViewMode(
        ModSearchState state,
        Button button,
        ListBox list
    )
    {
        var selectedIds = GetSelectedMods(list)
            .Select(mod => mod.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        state.HideUnmatched = !state.HideUnmatched;
        UpdateViewModeButton(button, state, _config.Language);
        if (_allMods.Count == 0)
        {
            return;
        }

        RefreshManager(false);
        RestoreSearchSelection(list, selectedIds);
    }

    private void RestoreSearchSelection(
        ListBox list,
        IReadOnlyList<string> selectedIds
    )
    {
        var items = list.Items.Cast<ModInfo>().ToList();
        var selected = selectedIds
            .Select(id => items.FirstOrDefault(mod => string.Equals(
                mod.PackageId,
                id,
                StringComparison.OrdinalIgnoreCase
            )))
            .Where(mod => mod is not null)
            .Select(mod => mod!)
            .ToList();
        if (selected.Count == 0)
        {
            ShowDetails(null);
            return;
        }

        _refreshing = true;
        foreach (var other in GetModLists())
        {
            other.SelectedItems!.Clear();
        }

        var autoScroll = list.AutoScrollToSelectedItem;
        list.AutoScrollToSelectedItem = false;
        foreach (var mod in selected)
        {
            list.SelectedItems!.Add(mod);
        }
        list.AutoScrollToSelectedItem = autoScroll;
        _selectionAnchorList = list;
        _selectionAnchorIndex = items.IndexOf(selected[0]);
        _refreshing = false;

        Dispatcher.UIThread.Post(
            () => list.ScrollIntoView(selected[0]),
            DispatcherPriority.Loaded
        );
        ShowDetails(selected[0]);
    }

    private void UpdateViewModeButton(
        Button button,
        ModSearchState state,
        string? language
    )
    {
        button.Content = CreateEyeContent(state.HideUnmatched);
        ToolTip.SetTip(
            button,
            state.HideUnmatched
                ? Localization.SearchVisibilityHidden(language)
                : Localization.SearchVisibilityDimmed(language)
        );
    }

    private static Control CreateEyeContent(bool hidden)
    {
        var grid = new Grid
        {
            Width = 16,
            Height = 16
        };
        grid.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(
                "M 1,8 C 3,4 5.5,2 8,2 C 10.5,2 13,4 15,8 " +
                "C 13,12 10.5,14 8,14 C 5.5,14 3,12 1,8 Z"
            ),
            Stroke = TextBrush,
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        });
        grid.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = TextBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        if (hidden)
        {
            grid.Children.Add(new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(1, 15),
                EndPoint = new Point(15, 1),
                Stroke = TextBrush,
                StrokeThickness = 1.5
            });
        }

        return grid;
    }
    private void OnInactiveSortClick(object? sender, RoutedEventArgs e)
    {
        var language = _config.Language;
        var flyout = new MenuFlyout();
        for (var i = 0; i < 6; i++)
        {
            var field = i;
            var header = (i == _inactiveSortField ? "✓ " : "   ") +
                SortFieldName(field, language);
            flyout.Items.Add(CreateMenuItem(header, true, () =>
            {
                _inactiveSortField = field;
                UpdateSortButton(language);
                RefreshInactivePreservingScroll();
            }));
        }

        flyout.Items.Add(new Separator());
        flyout.Items.Add(CreateMenuItem(
            (_inactiveSortAscending ? "✓ " : "   ") + Localization.Ascending(language),
            true,
            () =>
            {
                _inactiveSortAscending = true;
                UpdateSortButton(language);
                RefreshInactivePreservingScroll();
            }
        ));
        flyout.Items.Add(CreateMenuItem(
            (!_inactiveSortAscending ? "✓ " : "   ") + Localization.Descending(language),
            true,
            () =>
            {
                _inactiveSortAscending = false;
                UpdateSortButton(language);
                RefreshInactivePreservingScroll();
            }
        ));
        flyout.ShowAt(_inactiveSortButton);
    }

    private void UpdateSortButton(string? language)
    {
        _inactiveSortButton.Content =
            $"{SortFieldName(_inactiveSortField, language)} " +
            (_inactiveSortAscending ? "↑" : "↓");
    }

    private static string SortFieldName(int field, string? language)
    {
        return field switch
        {
            1 => Localization.SearchByAuthor(language),
            2 => Localization.SearchByPath(language),
            3 => Localization.SearchByPackageId(language),
            4 => Localization.SearchByGameVersion(language),
            5 => Localization.SearchBySubscribedTime(language),
            _ => Localization.SearchByName(language)
        };
    }

    private List<ModInfo> SortInactive(List<ModInfo> mods)
    {
        if (_inactiveSortField == 5)
        {
            var dated = _inactiveSortAscending
                ? mods.OrderBy(mod => mod.SubscribedAt == DateTime.MinValue
                    ? DateTime.MaxValue
                    : mod.SubscribedAt)
                : mods.OrderByDescending(mod => mod.SubscribedAt == DateTime.MinValue
                    ? DateTime.MinValue
                    : mod.SubscribedAt);
            return dated
                .ThenBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        Func<ModInfo, string> selector = _inactiveSortField switch
        {
            1 => mod => mod.Author,
            2 => mod => mod.Path,
            3 => mod => mod.PackageId,
            4 => mod => mod.GameVersions,
            _ => mod => mod.Name
        };

        var ordered = _inactiveSortAscending
            ? mods.OrderBy(selector, StringComparer.CurrentCultureIgnoreCase)
            : mods.OrderByDescending(selector, StringComparer.CurrentCultureIgnoreCase);
        return ordered
            .ThenBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void OnSearchFieldWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
        {
            return;
        }

        var step = e.Delta.Y > 0 ? -1 : 1;
        if (ReferenceEquals(sender, _inactiveSearchField))
        {
            StepSearchField(_inactiveSearchField, _inactiveSearch, _inactiveList, step);
        }
        else if (ReferenceEquals(sender, _activeSearchField1))
        {
            StepSearchField(_activeSearchField1, _activeSearch1, _activeList1, step);
        }
        else if (ReferenceEquals(sender, _activeSearchField2))
        {
            StepSearchField(_activeSearchField2, _activeSearch2, _activeList2, step);
        }

        e.Handled = true;
    }

    private void StepSearchField(
        ComboBox comboBox,
        ModSearchState state,
        ListBox list,
        int step
    )
    {
        var count = comboBox.ItemCount;
        if (count == 0)
        {
            return;
        }

        _updatingSearch = true;
        comboBox.SelectedIndex =
            (comboBox.SelectedIndex + step + count) % count;
        _updatingSearch = false;
        state.FieldIndex = comboBox.SelectedIndex;
        if (_allMods.Count > 0)
        {
            RefreshSearch(list);
        }
    }

    private void OnSourceWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
        {
            return;
        }

        var step = e.Delta.Y > 0 ? -1 : 1;
        if (ReferenceEquals(sender, _inactiveSourceButton))
        {
            CycleSource(_inactiveSearch, _inactiveSourceButton, _inactiveList, step);
        }
        else if (ReferenceEquals(sender, _activeSourceButton1))
        {
            CycleSource(_activeSearch1, _activeSourceButton1, _activeList1, step);
        }
        else if (ReferenceEquals(sender, _activeSourceButton2))
        {
            CycleSource(_activeSearch2, _activeSourceButton2, _activeList2, step);
        }

        e.Handled = true;
    }

    private void AttachInstanceContainers(
        ComboBox combo,
        HashSet<Control> registered
    )
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var container in combo.GetRealizedContainers())
            {
                RegisterInstanceContainer(combo, container, registered);
            }
        }, DispatcherPriority.Loaded);
    }

    private void RegisterInstanceContainer(
        ComboBox combo,
        Control container,
        HashSet<Control> registered
    )
    {
        if (!registered.Add(container))
        {
            return;
        }

        container.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => OnInstanceDragPressed(combo, container, e),
            RoutingStrategies.Tunnel,
            true
        );
        container.AddHandler(
            InputElement.PointerMovedEvent,
            (_, e) => OnInstanceDragMoved(combo, container, e),
            RoutingStrategies.Tunnel,
            true
        );
        container.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, e) => OnInstanceDragReleased(combo, container, e),
            RoutingStrategies.Tunnel,
            true
        );
    }

    private void OnInstanceDragPressed(
        ComboBox combo,
        Control container,
        PointerPressedEventArgs e
    )
    {
        if (!e.GetCurrentPoint(container).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var index = combo.IndexFromContainer(container);
        if (index < 2 || index >= _instances.Count)
        {
            return;
        }

        ResetInstanceDrag();
        _instanceDragCombo = combo;
        _instanceDragContainer = container;
        _instanceDragIndex = index;
        _instanceDragTopLevel = TopLevel.GetTopLevel(container);
        _instanceDragStart = _instanceDragTopLevel is null
            ? e.GetPosition(container)
            : e.GetPosition(_instanceDragTopLevel);
        _instanceDragActive = true;
        _instanceDragStarted = false;
        e.Pointer.Capture(container);
        e.Handled = true;
    }

    private void OnInstanceDragMoved(
        ComboBox combo,
        Control container,
        PointerEventArgs e
    )
    {
        if (!_instanceDragActive ||
            !ReferenceEquals(combo, _instanceDragCombo) ||
            !ReferenceEquals(container, _instanceDragContainer))
        {
            return;
        }

        var position = _instanceDragTopLevel is null
            ? e.GetPosition(container)
            : e.GetPosition(_instanceDragTopLevel);
        if (!_instanceDragStarted)
        {
            if (Math.Abs(position.X - _instanceDragStart.X) < 4 &&
                Math.Abs(position.Y - _instanceDragStart.Y) < 4)
            {
                return;
            }

            _instanceDragStarted = true;
            container.Opacity = 0.65;
            container.ZIndex = 1000;
        }

        container.RenderTransform = new TranslateTransform(
            position.X - _instanceDragStart.X,
            position.Y - _instanceDragStart.Y
        );
        _instanceDragBoundary = GetInstanceDropBoundary(combo, container, e);
        UpdateInstanceDropIndicator(combo, _instanceDragBoundary);
        e.Handled = true;
    }

    private void UpdateInstanceDropIndicator(ComboBox combo, int boundary)
    {
        ClearInstanceDropIndicator();
        var items = combo.GetRealizedContainers()
            .OfType<ComboBoxItem>()
            .Select(item => new
            {
                Container = item,
                Index = combo.IndexFromContainer(item)
            })
            .Where(item => item.Index >= 2 &&
                           item.Index < combo.ItemCount &&
                           !ReferenceEquals(item.Container, _instanceDragContainer))
            .OrderBy(item => item.Index)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        var target = boundary >= combo.ItemCount
            ? items[^1]
            : items.FirstOrDefault(item => item.Index == boundary);
        if (target is null)
        {
            return;
        }

        _instanceDropTarget = target.Container;
        target.Container.BorderBrush = TextBrush;
        target.Container.BorderThickness = boundary >= combo.ItemCount
            ? new Thickness(0, 0, 0, 2)
            : new Thickness(0, 2, 0, 0);
    }

    private void ClearInstanceDropIndicator()
    {
        if (_instanceDropTarget is not null)
        {
            _instanceDropTarget.BorderThickness = default;
            _instanceDropTarget = null;
        }
    }
    private void OnInstanceDragReleased(
        ComboBox combo,
        Control container,
        PointerReleasedEventArgs e
    )
    {
        if (!_instanceDragActive ||
            !ReferenceEquals(combo, _instanceDragCombo) ||
            !ReferenceEquals(container, _instanceDragContainer))
        {
            return;
        }

        var sourceIndex = _instanceDragIndex;
        var started = _instanceDragStarted;
        var boundary = started
            ? _instanceDragBoundary
            : sourceIndex;
        e.Pointer.Capture(null);
        ResetInstanceDrag();
        if (started)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    MoveNormalInstance(sourceIndex, boundary);
                    combo.IsDropDownOpen = true;
                }
                catch
                {
                }
            }, DispatcherPriority.Background);
        }
        else
        {
            combo.SelectedIndex = sourceIndex;
            combo.IsDropDownOpen = false;
        }

        e.Handled = true;
    }

    private int GetInstanceDropBoundary(
        ComboBox combo,
        Control source,
        PointerEventArgs e
    )
    {
        var top = TopLevel.GetTopLevel(source);
        if (top is null)
        {
            return 2;
        }

        var position = e.GetPosition(top);
        var boundary = 2;
        var containers = combo.GetRealizedContainers()
            .OfType<Control>()
            .Select(container => new
            {
                Container = container,
                Index = combo.IndexFromContainer(container),
                Origin = container.TranslatePoint(new Point(0, 0), top)
            })
            .Where(item => item.Index >= 0 &&
                           item.Origin is not null &&
                           !ReferenceEquals(item.Container, _instanceDragContainer))
            .OrderBy(item => item.Index)
            .ToList();
        foreach (var item in containers)
        {
            var origin = item.Origin!.Value;
            var height = item.Container.Bounds.Height;
            if (position.Y < origin.Y)
            {
                return Math.Clamp(item.Index, 2, combo.ItemCount);
            }

            if (position.Y <= origin.Y + height)
            {
                var after = position.Y > origin.Y + height / 2;
                return Math.Clamp(
                    item.Index + (after ? 1 : 0),
                    2,
                    combo.ItemCount
                );
            }

            boundary = Math.Clamp(item.Index + 1, 2, combo.ItemCount);
        }

        return boundary;
    }

    private void MoveNormalInstance(int sourceIndex, int boundary)
    {
        if (sourceIndex < 2 || sourceIndex >= _instances.Count ||
            boundary == sourceIndex || boundary == sourceIndex + 1)
        {
            return;
        }

        var normal = _instances
            .Where(instance => !instance.IsOfficial && !instance.IsScratch)
            .ToList();
        var folder1 = GetSelectedInstanceFolder(0);
        var folder2 = GetSelectedInstanceFolder(1);
        var sourceFolder = _instances[sourceIndex].FolderName;
        var sourceNormal = normal.FindIndex(instance => string.Equals(
            instance.FolderName,
            sourceFolder,
            StringComparison.OrdinalIgnoreCase
        ));
        if (sourceNormal < 0)
        {
            return;
        }

        var insert = boundary - 2;
        if (sourceIndex < boundary)
        {
            insert--;
        }

        var moved = normal[sourceNormal];
        normal.RemoveAt(sourceNormal);
        insert = Math.Clamp(insert, 0, normal.Count);
        normal.Insert(insert, moved);

        var reordered = new List<ManagedInstance>
        {
            _instances[0],
            _instances[1]
        };
        reordered.AddRange(normal);
        _instances = reordered;
        _config.ProfileOrder = normal
            .Select(instance => instance.FolderName)
            .ToList();
        _config.Save();

        var folders = _instances
            .Select(instance => instance.FolderName)
            .ToList();
        var names = _instances
            .Select(instance => instance.DisplayName)
            .ToList();
        var index1 = FindInstanceFolderIndex(folders, folder1);
        var index2 = FindInstanceFolderIndex(folders, folder2);
        _updatingInstances = true;
        SetInstanceNames(names);
        _instanceCombo1.SelectedIndex = index1;
        _instanceCombo2.SelectedIndex = index2;
        _updatingInstances = false;
        _lastInstanceIndex1 = index1;
        _lastInstanceIndex2 = index2;
    }

    private void ResetInstanceDrag()
    {
        if (_instanceDragContainer is not null)
        {
            _instanceDragContainer.Opacity = 1;
            _instanceDragContainer.RenderTransform = null;
            _instanceDragContainer.ZIndex = 0;
        }

        ClearInstanceDropIndicator();
        _instanceDragTopLevel = null;
        _instanceDragCombo = null;
        _instanceDragContainer = null;
        _instanceDragIndex = -1;
        _instanceDragBoundary = 2;
        _instanceDragActive = false;
        _instanceDragStarted = false;
    }

    private void OnInstanceComboWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 ||
            sender is not ComboBox combo ||
            combo.IsDropDownOpen ||
            combo.ItemCount <= 1 ||
            _customDragActive)
        {
            return;
        }

        var step = e.Delta.Y > 0 ? -1 : 1;
        combo.SelectedIndex = (combo.SelectedIndex + step + combo.ItemCount) %
                              combo.ItemCount;
        e.Handled = true;
    }

    private void OnSortWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
        {
            return;
        }

        var step = e.Delta.Y > 0 ? -1 : 1;
        _inactiveSortField = (_inactiveSortField + step + 6) % 6;
        UpdateSortButton(_config.Language);
        RefreshInactivePreservingScroll();
        e.Handled = true;
    }

    private void RefreshInactivePreservingScroll()
    {
        var inactiveOffset = GetListScrollOffset(_inactiveList);
        var activeOffset1 = GetListScrollOffset(_activeList1);
        var activeOffset2 = GetListScrollOffset(_activeList2);
        RefreshManager(false);
        RestoreListScrollOffsets(inactiveOffset, activeOffset1, activeOffset2);
    }

    private void RestoreListScrollOffsets(
        Vector inactiveOffset,
        Vector activeOffset1,
        Vector activeOffset2
    )
    {
        void RestoreOffsets()
        {
            SetListScrollOffset(_inactiveList, inactiveOffset);
            SetListScrollOffset(_activeList1, activeOffset1);
            SetListScrollOffset(_activeList2, activeOffset2);
        }

        Dispatcher.UIThread.Post(RestoreOffsets, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(RestoreOffsets, DispatcherPriority.Background);
    }

    private static Vector GetListScrollOffset(ListBox list)
    {
        return list.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault()
            ?.Offset ?? default;
    }

    private static void SetListScrollOffset(ListBox list, Vector offset)
    {
        var viewer = list.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (viewer is null)
        {
            return;
        }

        var maximum = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        viewer.Offset = new Vector(
            Math.Clamp(offset.X, 0, Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width)),
            Math.Clamp(offset.Y, 0, maximum)
        );
    }

    private void UpdateSourceButton(
        Button button,
        ModSearchState state,
        string? language
    )
    {
        button.Content = state.SourceIndex switch
        {
            1 => Localization.WorkshopMods(language),
            2 => Localization.LocalMods(language),
            _ => Localization.AllSources(language)
        };
    }
    private static string? BuildBadgeTooltip(
        ModInfo mod,
        string? language,
        string? currentVersion
    )
    {
        var parts = new List<string>();
        if (mod.SortError)
        {
            var sortText = string.IsNullOrWhiteSpace(mod.SortAfter)
                ? Localization.BadgeSortError(language)
                : mod.SortAfter;
            parts.Add($"S: {sortText}");
        }
        if (mod.VersionMismatch)
        {
            parts.Add("V: " + Localization.BadgeVersionMismatch(
                language,
                mod.GameVersions,
                currentVersion
            ));
        }
        if (mod.IsDuplicate)
        {
            parts.Add("R: " + Localization.BadgeDuplicate(language));
        }
        if (mod.IsLocal)
        {
            parts.Add("L: " + Localization.BadgeLocal(language));
        }

        return parts.Count > 0 ? string.Join(Environment.NewLine, parts) : null;
    }
    private void RefreshInactiveForTarget()
    {
        var index = _lastEnableColumn == 1
            ? GetInstanceIndex(_instanceCombo1, 0)
            : GetInstanceIndex(_instanceCombo2, 1);
        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (index >= 0 && index < _instances.Count)
        {
            foreach (var id in _instances[index].ActivePackageIds)
            {
                activeIds.Add(id);
            }
        }

        var targetInstance = GetInstanceAt(index);
        var inactiveSource = DeduplicateModsForInstance(
            ApplySteamCmdFilter(
                _allMods.Where(mod => !activeIds.Contains(mod.PackageId)),
                targetInstance
            ),
            targetInstance
        );
        _searchSources[_inactiveList] = inactiveSource.ToList();
        var inactive = PrepareSearchList(_searchSources[_inactiveList], _inactiveSearch);
        inactive = SortInactive(inactive);
        var inactiveForSummary = inactive;
        inactive = ApplyProblemFilters(inactive, _inactiveFilter);
        inactive = ApplyModColors(inactive, ModColorStore.Load(_packRoot));

        _refreshing = true;
        _inactiveList.ItemsSource = inactive;
        _inactiveList.SelectedItem = null;
        _refreshing = false;
        _inactiveCount = inactive.Count;
        UpdateHeaders();
        UpdateLaunchButton();
        UpdateSaveState();
        UpdateProblemSummary(
            null,
            _inactiveProblemV,
            _inactiveProblemR,
            inactiveForSummary
        );
    }
    private int GetInstanceIndex(ComboBox comboBox, int fallback)
    {
        var index = comboBox.SelectedIndex;
        return index >= 0 && index < _instances.Count ? index : -1;
    }

    private List<ModInfo> DeduplicateModsForInstance(
        IEnumerable<ModInfo> mods,
        ManagedInstance? instance
    )
    {
        return mods
            .GroupBy(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                SelectModForInstance(group.Key, instance) ?? group.First()
            )
            .ToList();
    }

    private ManagedInstance? GetInstanceAt(int index)
    {
        return index >= 0 && index < _instances.Count ? _instances[index] : null;
    }

    private static List<ModInfo> ApplySteamCmdFilter(
        IEnumerable<ModInfo> mods,
        ManagedInstance? instance
    )
    {
        var list = mods.ToList();
        if (instance is null || instance.IsOfficial || instance.IsScratch)
        {
            return list.Where(mod => !mod.IsProfileMods).ToList();
        }

        var scoped = list.Where(mod =>
            !mod.IsProfileMods ||
            string.Equals(
                mod.ProfileFolder,
                instance.FolderName,
                StringComparison.OrdinalIgnoreCase
            )
        ).ToList();
        if (instance.IsSteamCmd)
        {
            return scoped.Where(mod =>
                mod.Source == ModSource.Official || mod.IsProfileMods
            ).ToList();
        }

        return scoped;
    }

    private List<ModInfo> ResolveActive(ManagedInstance? instance)
    {
        if (instance is null)
        {
            return new List<ModInfo>();
        }

        var active = new List<ModInfo>();
        foreach (var id in instance.ActivePackageIds)
        {
            var mod = SelectModForInstance(id, instance);
            if (mod is not null)
            {
                active.Add(mod);
            }
        }

        return active;
    }

    private ModInfo? SelectModForInstance(string packageId, ManagedInstance? instance)
    {
        if (!_modsByPackage.TryGetValue(packageId, out var candidates) ||
            candidates.Count == 0)
        {
            return null;
        }

        var official = candidates.FirstOrDefault(mod =>
            mod.Source == ModSource.Official
        );
        if (official is not null)
        {
            return official;
        }

        var workshop = candidates.FirstOrDefault(mod =>
            mod.Source == ModSource.Workshop
        );
        if (workshop is not null)
        {
            return workshop;
        }

        if (instance is null || instance.IsOfficial || instance.IsScratch)
        {
            return candidates.FirstOrDefault(mod => !mod.IsProfileMods) ??
                candidates[0];
        }

        if (instance.IsSteamCmd)
        {
            return candidates.FirstOrDefault(mod =>
                       mod.IsProfileMods &&
                       string.Equals(
                           mod.ProfileFolder,
                           instance.FolderName,
                           StringComparison.OrdinalIgnoreCase
                       )) ??
                   candidates.FirstOrDefault(mod => mod.Source == ModSource.Official) ??
                   candidates[0];
        }

        return candidates.FirstOrDefault(mod =>
                   !mod.IsProfileMods ||
                   string.Equals(
                       mod.ProfileFolder,
                       instance.FolderName,
                       StringComparison.OrdinalIgnoreCase
                   )) ??
               candidates[0];
    }

    private ModInfo? SelectModForExport(string packageId, ManagedInstance? instance)
    {
        if (!_modsByPackage.TryGetValue(packageId, out var candidates) ||
            candidates.Count == 0)
        {
            return null;
        }

        var instanceLocal = candidates.FirstOrDefault(mod =>
            mod.Source == ModSource.Local &&
            mod.IsProfileMods &&
            (instance is null ||
             string.Equals(
                 mod.ProfileFolder,
                 instance.FolderName,
                 StringComparison.OrdinalIgnoreCase
             )));
        if (instanceLocal is not null)
        {
            return instanceLocal;
        }

        var workshop = candidates.FirstOrDefault(mod =>
            mod.Source == ModSource.Workshop
        );
        if (workshop is not null)
        {
            return workshop;
        }

        return candidates.FirstOrDefault(mod =>
            mod.Source == ModSource.Local && !mod.IsProfileMods
        );
    }

    private async void OnInstanceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingInstances || sender is not ComboBox comboBox)
        {
            return;
        }

        var column = ReferenceEquals(comboBox, _instanceCombo1) ? 0 : 1;
        var index = comboBox.SelectedIndex;
        if (index < 0 || index >= _instances.Count)
        {
            return;
        }

        var instance = _instances[index];
        _lastEnableColumn = column + 1;
        UpdateEnableTargetBorders();

        if (instance.IsOfficial && !HasOfficialConfigPath())
        {
            var previous = column == 0 ? _lastInstanceIndex1 : _lastInstanceIndex2;
            await EnsureOfficialConfigAsync(column, previous);
            return;
        }

        SetLastInstanceIndex(column, index);
        SaveProfileSelection();
        RefreshManager(true);
    }

    private void SaveProfileSelection()
    {
        var folder1 = GetSelectedInstanceFolder(0);
        var folder2 = GetSelectedInstanceFolder(1);
        if (string.Equals(
                _config.ProfileFolder1,
                folder1,
                StringComparison.OrdinalIgnoreCase
            ) &&
            string.Equals(
                _config.ProfileFolder2,
                folder2,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return;
        }

        _config.ProfileFolder1 = folder1;
        _config.ProfileFolder2 = folder2;
        _config.Save();
    }

    private bool HasOfficialConfigPath()
    {
        return !string.IsNullOrWhiteSpace(_config.OfficialConfigPath) &&
               Directory.Exists(_config.OfficialConfigPath);
    }

    private void SetLastInstanceIndex(int column, int index)
    {
        if (column == 0)
        {
            _lastInstanceIndex1 = index;
        }
        else
        {
            _lastInstanceIndex2 = index;
        }
    }

    private async Task EnsureOfficialConfigAsync(int column, int previousIndex)
    {
        var language = _config.Language;
        var dialog = new ConfigFolderWindow(
            _config.OfficialConfigPath,
            Localization.ConfigFolderTooltip(language),
            Localization.BrowseButton(language),
            Localization.ConfirmButton(language),
            Localization.CancelButton(language),
            Localization.ConfigFolderWatermark(language),
            Localization.AutoDetectButton(language),
            Localization.AutoDetectConfigFailed(language)
        );
        var path = await dialog.ShowDialog<string?>(this);
        var comboBox = column == 0 ? _instanceCombo1 : _instanceCombo2;

        if (string.IsNullOrWhiteSpace(path))
        {
            var fallback = previousIndex;
            if (fallback < 0 ||
                fallback >= _instances.Count ||
                (_instances[fallback].IsOfficial && !HasOfficialConfigPath()))
            {
                fallback = FindFirstNormalIndex();
            }

            _updatingInstances = true;
            comboBox.SelectedIndex = fallback;
            _updatingInstances = false;
            SetLastInstanceIndex(column, fallback);
            RefreshManager(true);
            return;
        }

        _config.OfficialConfigPath = path;
        _config.Save();
        _instances = BuildInstances(_instances.Where(instance => !instance.IsOfficial));
        var officialIndex = _instances.FindIndex(instance => instance.IsOfficial);
        if (officialIndex < 0)
        {
            officialIndex = 0;
        }

        _updatingInstances = true;
        comboBox.SelectedIndex = officialIndex;
        _updatingInstances = false;
        SetLastInstanceIndex(column, officialIndex);
        ClearUndoHistory();
        RefreshManager(true);
    }

    private async Task ChangeOfficialConfigFolderAsync(int column)
    {
        var selected = GetSelectedInstance(column);
        if (selected?.IsOfficial != true)
        {
            return;
        }

        var language = _config.Language;
        var dialog = new ConfigFolderWindow(
            _config.OfficialConfigPath,
            Localization.ConfigFolderTooltip(language),
            Localization.BrowseButton(language),
            Localization.ConfirmButton(language),
            Localization.CancelButton(language),
            Localization.ConfigFolderWatermark(language),
            Localization.AutoDetectButton(language),
            Localization.AutoDetectConfigFailed(language)
        );
        var path = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _config.OfficialConfigPath = path;
        _config.Save();
        _instances = BuildInstances(_instances.Where(instance => !instance.IsOfficial));
        ClearUndoHistory();
        RefreshManager(true);
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || sender is not ListBox list)
        {
            return;
        }

        var mod = list.SelectedItems!.Cast<ModInfo>().LastOrDefault();
        if (mod is not null)
        {
            ShowDetails(mod);
        }
    }

    private ListBox[] GetModLists()
    {
        return new[] { _inactiveList, _activeList1, _activeList2 };
    }

    private ListBox? GetModListForColumn(int column)
    {
        return column switch
        {
            0 => _inactiveList,
            1 => _activeList1,
            2 => _activeList2,
            _ => null
        };
    }

    private ListBox? FindModListAt(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ListBox list &&
                (ReferenceEquals(list, _inactiveList) ||
                 ReferenceEquals(list, _activeList1) ||
                 ReferenceEquals(list, _activeList2)))
            {
                return list;
            }
        }

        return null;
    }

    private void SelectMovedMods(int targetColumn, IReadOnlyCollection<string> selectedIds)
    {
        var list = GetModListForColumn(targetColumn);
        if (list is null)
        {
            return;
        }

        var selectedSet = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        var items = list.Items.Cast<ModInfo>().ToList();
        var moved = items.Where(mod => selectedSet.Contains(mod.PackageId)).ToList();
        if (moved.Count == 0)
        {
            return;
        }

        _refreshing = true;
        foreach (var other in GetModLists())
        {
            other.SelectedItems!.Clear();
        }

        var selection = list.Selection;
        selection.BeginBatchUpdate();
        try
        {
            foreach (var mod in moved)
            {
                var index = items.IndexOf(mod);
                if (index >= 0)
                {
                    selection.Select(index);
                }
            }
        }
        finally
        {
            selection.EndBatchUpdate();
        }

        _selectionAnchorList = list;
        _selectionAnchorIndex = items.IndexOf(moved[0]);
        _lastPressList = list;
        _refreshing = false;
        list.Focus();
        ShowDetails(moved[0]);
    }

    private int GetListColumn(ListBox list)
    {
        if (ReferenceEquals(list, _inactiveList))
        {
            return 0;
        }
        if (ReferenceEquals(list, _activeList1))
        {
            return 1;
        }
        return 2;
    }

    private static ListBoxItem? FindListItem(ListBox list, object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is ListBoxItem item &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), list))
            {
                return item;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    private static List<ModInfo> GetSelectedMods(ListBox list)
    {
        var selected = new HashSet<ModInfo>(list.SelectedItems!.Cast<ModInfo>());
        return list.Items.Cast<ModInfo>().Where(selected.Contains).ToList();
    }

    private void RestorePressedMultiSelection(ListBox list)
    {
        if (!ReferenceEquals(_pressedMultiList, list) || _pressedMultiMods is null)
        {
            return;
        }

        var autoScroll = list.AutoScrollToSelectedItem;
        list.AutoScrollToSelectedItem = false;
        _refreshing = true;
        try
        {
            list.SelectedItems!.Clear();
            foreach (var mod in _pressedMultiMods)
            {
                if (list.Items.Contains(mod))
                {
                    list.SelectedItems!.Add(mod);
                }
            }
        }
        finally
        {
            _refreshing = false;
            list.AutoScrollToSelectedItem = autoScroll;
        }
    }

    private void OnActiveColumnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border ||
            !e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var column = ReferenceEquals(border, _activeColumn1) ? 1 : 2;
        if (column == 1 && GetSelectedInstance(0)?.IsScratch == true)
        {
            var officialIndex = _instances.FindIndex(instance =>
                instance.IsOfficial &&
                Directory.Exists(instance.ConfigDirectoryPath));
            if (officialIndex < 0)
            {
                var detected = RimWorldLocator.FindConfigDirectory();
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    _config.OfficialConfigPath = detected;
                    _config.Save();
                    _instances = BuildInstances(_instances.Where(instance =>
                        !instance.IsOfficial && !instance.IsScratch));
                    InitializeInstanceSelectors();
                    officialIndex = _instances.FindIndex(instance =>
                        instance.IsOfficial &&
                        Directory.Exists(instance.ConfigDirectoryPath));
                }
            }

            _lastEnableColumn = column;
            UpdateEnableTargetBorders();
            if (officialIndex >= 0)
            {
                _instanceCombo1.SelectedIndex = officialIndex;
            }
            else
            {
                var scratchIndex = _instances.FindIndex(instance => instance.IsScratch);
                if (scratchIndex >= 0)
                {
                    _instanceCombo1.SelectedIndex = scratchIndex;
                }
            }

            return;
        }

        if (_lastEnableColumn == column)
        {
            return;
        }

        _lastEnableColumn = column;
        UpdateEnableTargetBorders();
        if (_allMods.Count > 0)
        {
            RefreshInactiveForTarget();
        }
    }

    private void UpdateEnableTargetBorders()
    {
        _activeColumn1.BorderBrush = _lastEnableColumn == 1
            ? TextBrush
            : SecondaryBorderBrush;
        _activeColumn2.BorderBrush = _lastEnableColumn == 2
            ? TextBrush
            : SecondaryBorderBrush;
        UpdateLaunchButton();
    }

    private void UpdateLaunchButton()
    {
        var instance = GetSelectedInstance(_lastEnableColumn - 1);
        var executable = _gameInstallation?.ExecutablePath;
        var executableExists = !string.IsNullOrWhiteSpace(executable) &&
            (File.Exists(executable) || Directory.Exists(executable));
        var configExists = instance is not null && !instance.IsDamaged &&
            Directory.Exists(instance.ConfigDirectoryPath);
        _launchButton.IsEnabled = executableExists && configExists;
    }

    private void UpdateSaveState()
    {
        var hasPending = InstanceStore.HasPendingMods;
        var hasMissing = _missingMods1.Count > 0 || _missingMods2.Count > 0;
        _saveButton.IsEnabled = hasPending || hasMissing;
        _saveButton.Content = hasPending
            ? Localization.Save(_config.Language) + " *"
            : Localization.Save(_config.Language);
    }
    private void SaveModFullNameLists()
    {
        if (_allMods.Count == 0)
        {
            return;
        }

        var installedIds = new HashSet<string>(
            _allMods.Select(mod => mod.PackageId),
            StringComparer.OrdinalIgnoreCase
        );
        var savedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var column = 0; column < 2; column++)
        {
            var instance = GetSelectedInstance(column);
            if (instance is null || instance.IsDamaged || string.IsNullOrWhiteSpace(instance.DirectoryPath))
            {
                continue;
            }

            var directory = Path.GetFullPath(instance.DirectoryPath);
            if (!savedDirectories.Add(directory))
            {
                continue;
            }

            var missingIds = instance.ActivePackageIds
                .Where(id => !installedIds.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ModFullNameList.Save(instance, _allMods, missingIds);
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        SaveChanges();
    }

    private bool SaveChanges()
    {
        try
        {
            SaveModFullNameLists();
            InstanceStore.SavePendingMods();
            RefreshManager(false);
            ShowToast(Localization.Saved(_config.Language));
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Save mod lists", exception);
            ShowToast(Localization.SaveFailed(_config.Language));
            return false;
        }
    }

    private bool _pendingChangesPromptOpen;
    private bool _closeApproved;
    private CancellationTokenSource? _launchCancellation;

    private async Task<bool> ResolvePendingChangesAsync()
    {
        if (!InstanceStore.HasPendingMods) return true;
        if (_pendingChangesPromptOpen) return false;
        _pendingChangesPromptOpen = true;
        try
        {
            var choice = await new PendingChangesWindow(_config.Language)
                .ShowDialog<PendingChangesChoice>(this);
            if (choice == PendingChangesChoice.Save) return SaveChanges();
            if (choice != PendingChangesChoice.Discard) return false;
            InstanceStore.DiscardPendingMods();
            return true;
        }
        finally { _pendingChangesPromptOpen = false; }
    }

    private async void OnSyncConfigClick(object? sender, RoutedEventArgs e)
    {
        var column = _lastEnableColumn - 1;
        var instance = GetSelectedInstance(column);
        if (instance?.IsDamaged == true) { ShowToast(instance.LoadError!); return; }
        if (instance is null || instance.IsScratch ||
            string.IsNullOrWhiteSpace(instance.ConfigDirectoryPath))
        {
            ShowToast(Localization.SyncConfigNoInstance(_config.Language));
            return;
        }

        if (GameLaunchService.IsRunning())
        {
            ShowToast(Localization.ModpackGameRunning(_config.Language));
            return;
        }

        if (InstanceStore.HasPendingMods)
        {
            ShowToast(Localization.UnsavedChanges(_config.Language));
            return;
        }

        var metadata = instance.Metadata;
        if (metadata is null ||
            string.IsNullOrWhiteSpace(metadata.ConfigBaseUrl) ||
            metadata.ConfigFiles.Count == 0)
        {
            ShowToast(Localization.SyncConfigNoSource(_config.Language));
            return;
        }

        _syncConfigButton.IsEnabled = false;
        try
        {
            await ModpackService.DownloadConfigToAsync(
                metadata.ConfigBaseUrl,
                metadata.ConfigFiles,
                instance.ConfigDirectoryPath
            );
            if (_gameInstallation is not null)
            {
                await LoadModsAsync(_gameInstallation);
            }

            ReloadInstances(instance.FolderName, column);
            ShowToast(Localization.SyncConfigDone(_config.Language));
        }
        catch (Exception exception)
        {
            ShowToast(Localization.SyncConfigFailed(
                _config.Language,
                exception.Message
            ));
        }
        finally
        {
            _syncConfigButton.IsEnabled = true;
        }
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (!_refreshButton.IsEnabled)
        {
            return;
        }

        if (!await ResolvePendingChangesAsync()) return;
        ModColorStore.Invalidate();
        UpdateSaveState();
        var inactiveOffset = GetListScrollOffset(_inactiveList);
        var activeOffset1 = GetListScrollOffset(_activeList1);
        var activeOffset2 = GetListScrollOffset(_activeList2);
        _refreshButton.IsEnabled = false;
        try
        {
            var result = RimWorldLocator.Detect(_pathBox.Text);
            _gameInstallation = result.IsValid ? result : null;
            SetFieldValid(result.IsValid);
            if (!result.IsValid)
            {
                ShowToast(Localization.RefreshFailed(_config.Language));
                return;
            }

            var folder1 = GetSelectedInstanceFolder(0);
            var folder2 = GetSelectedInstanceFolder(1);
            await LoadModsAsync(result);
            if (_instances.Count == 0)
            {
                ShowToast(Localization.RefreshFailed(_config.Language));
                return;
            }

            RestoreInstanceSelection(folder1, folder2);
            RestoreListScrollOffsets(inactiveOffset, activeOffset1, activeOffset2);
            ShowToast(Localization.Refreshed(_config.Language));
        }
        catch (Exception exception)
        {
            AppLog.Error("Refresh mod catalog", exception);
            ShowToast(Localization.RefreshFailed(_config.Language));
        }
        finally
        {
            _refreshButton.IsEnabled = true;
        }
    }

    private void RestoreInstanceSelection(string? folder1, string? folder2)
    {
        var folders = _instances
            .Select(instance => instance.FolderName)
            .ToList();

        var index1 = folder1 is null
            ? -1
            : folders.FindIndex(folder => string.Equals(
                folder,
                folder1,
                StringComparison.OrdinalIgnoreCase
            ));
        if (index1 < 0)
        {
            index1 = _instances.FindIndex(instance =>
                !instance.IsOfficial && !instance.IsScratch);
        }

        var index2 = folder2 is null
            ? -1
            : folders.FindIndex(folder => string.Equals(
                folder,
                folder2,
                StringComparison.OrdinalIgnoreCase
            ));
        if (index2 < 0 && folder2 is not null && index1 >= 0)
        {
            index2 = index1 + 1 < _instances.Count ? index1 + 1 : index1;
        }

        _updatingInstances = true;
        _instanceCombo1.SelectedIndex = index1;
        _instanceCombo2.SelectedIndex = index2;
        _updatingInstances = false;
        _lastInstanceIndex1 = index1;
        _lastInstanceIndex2 = index2;
        RefreshManager(true);
    }
    private void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        var instance = GetSelectedInstance(_lastEnableColumn - 1);
        if (instance?.IsDamaged == true) { ShowToast(instance.LoadError!); return; }
        var executable = _gameInstallation?.ExecutablePath;
        if (instance is null || string.IsNullOrWhiteSpace(executable))
        {
            UpdateLaunchButton();
            return;
        }

        var saveDataFolder = GameLaunchService.GetSaveDataFolder(instance);

        if (string.IsNullOrWhiteSpace(saveDataFolder))
        {
            UpdateLaunchButton();
            return;
        }

        if (InstanceStore.HasPendingMods)
        {
            ShowToast(Localization.UnsavedChanges(_config.Language));
            return;
        }

        if (GameLaunchService.IsRunning())
        {
            ShowToast(Localization.ModpackGameRunning(_config.Language));
            return;
        }

        if (_gameInstallation?.GamePath is { } gamePath)
        {
            try
            {
                ModLinkService.Prepare(gamePath, instance, _packRoot);
            }
            catch (Exception exception)
            {
                AppLog.Error("Prepare game launch: " + gamePath, exception);
                ShowToast(Localization.ModpackLinkPrepareFailed(_config.Language));
                return;
            }
        }

        _launchCancellation?.Cancel();
        _launchCancellation?.Dispose();
        var launchCancellation = new CancellationTokenSource();
        _launchCancellation = launchCancellation;
        try
        {
            _ = GameLaunchService.Start(_gameInstallation!, instance, saveDataFolder,
                launchCancellation.Token);
        }
        catch (Exception exception)
        {
            launchCancellation.Cancel();
            launchCancellation.Dispose();
            _launchCancellation = null;
            AppLog.Error("Launch game: " + executable, exception);
            ShowHistoryFailure(Localization.LaunchFailed(_config.Language));
        }
    }

    private void MoveModSelection(ListBox list, int direction, bool shift)
    {
        if (list.ItemCount == 0)
        {
            return;
        }

        var current = list.SelectedIndex;
        if (current < 0 && ReferenceEquals(_selectionAnchorList, list))
        {
            current = _selectionAnchorIndex;
        }

        var next = current < 0
            ? direction > 0 ? 0 : list.ItemCount - 1
            : Math.Clamp(current + direction, 0, list.ItemCount - 1);
        var nextItem = list.Items[next];
        if (nextItem is null)
        {
            return;
        }

        _refreshing = true;
        foreach (var other in GetModLists())
        {
            if (other != list)
            {
                other.SelectedItems!.Clear();
            }
        }

        if (shift && ReferenceEquals(_selectionAnchorList, list) && _selectionAnchorIndex >= 0)
        {
            var start = Math.Min(_selectionAnchorIndex, next);
            var end = Math.Max(_selectionAnchorIndex, next);
            var selection = list.Selection;
            selection.BeginBatchUpdate();
            try
            {
                list.SelectedItems!.Clear();
                selection.SelectRange(start, end);
            }
            finally
            {
                selection.EndBatchUpdate();
            }
        }
        else
        {
            list.SelectedItems!.Clear();
            list.SelectedItems!.Add(nextItem);
            _selectionAnchorList = list;
            _selectionAnchorIndex = next;
        }

        _lastPressList = list;
        _refreshing = false;
        list.ScrollIntoView(nextItem);
        list.Focus();
        if (nextItem is ModInfo mod)
        {
            ShowDetails(mod);
        }

    }

    private void OnModListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list)
        {
            return;
        }

        _modBadgePopup.IsOpen = false;
        var point = e.GetCurrentPoint(list);
        if (_paintMode && point.Properties.IsLeftButtonPressed)
        {
            var paintItem = FindListItem(list, e.Source);
            if (paintItem?.DataContext is ModInfo paintMod)
            {
                BeginPaint(list, paintMod, e);
            }
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            var contextItem = FindListItem(list, e.Source);
            if (contextItem?.DataContext is ModInfo contextMod)
            {
                var contextIndex = list.IndexFromContainer(contextItem);
                SelectForContextMenu(list, contextMod, contextIndex);
                ShowModContextMenu(list, contextMod);
                e.Handled = true;
            }
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var item = FindListItem(list, e.Source);
        if (item is null || item.DataContext is not ModInfo mod)
        {
            return;
        }

        var index = list.IndexFromContainer(item);
        var selectionBefore = GetSelectedMods(list);
        var rangeMatch = ReferenceEquals(_rangeSelectionList, list) &&
            _rangeSelectionMods is not null &&
            _rangeSelectionMods.Any(selected =>
                string.Equals(selected.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase));
        if (selectionBefore.Count <= 1 && rangeMatch)
        {
            selectionBefore = _rangeSelectionMods!.ToList();
        }
        else if (selectionBefore.Count <= 1 &&
                 ReferenceEquals(_pressedSnapshotList, list) &&
                 _pressedSnapshotMods.Count > 1)
        {
            selectionBefore = _pressedSnapshotMods.ToList();
        }

        if (!rangeMatch)
        {
            _rangeSelectionList = null;
            _rangeSelectionMods = null;
        }

        var pressedWasMulti = selectionBefore.Count > 1 &&
            selectionBefore.Any(selected =>
                string.Equals(selected.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase));
        _pressedMultiList = pressedWasMulti ? list : null;
        _pressedMultiMods = pressedWasMulti ? selectionBefore : null;
        var now = Environment.TickCount64;
        var isDoubleClick = ReferenceEquals(_lastPressList, list) &&
                            ReferenceEquals(_lastPressMod, mod) &&
                            now - _lastPressTicks <= 350 &&
                            !(list.SelectedItems!.Count > 1 &&
                              list.SelectedItems!.Contains(mod));
        _lastPressList = list;
        _lastPressMod = mod;
        _lastPressTicks = now;

        if (isDoubleClick)
        {
            _lastPressList = null;
            _lastPressMod = null;
            _lastPressTicks = 0;
            _pendingCollapseList = null;
            _pendingCollapseMod = null;
            _pendingCollapseIndex = -1;
            SelectForDoubleClick(list, index, mod);
            MoveModsOnDoubleClick(list);
            e.Handled = true;
            return;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        _pendingCollapseList = null;
        _pendingCollapseMod = null;
        _pendingCollapseIndex = -1;

        _refreshing = true;
        foreach (var other in GetModLists())
        {
            if (other != list)
            {
                other.SelectedItems!.Clear();
            }
        }

        if (shift && ReferenceEquals(_selectionAnchorList, list) && _selectionAnchorIndex >= 0)
        {
            var start = Math.Min(_selectionAnchorIndex, index);
            var end = Math.Max(_selectionAnchorIndex, index);
            var selection = list.Selection;
            var autoScroll = list.AutoScrollToSelectedItem;
            list.AutoScrollToSelectedItem = false;
            selection.BeginBatchUpdate();
            try
            {
                if (!ctrl)
                {
                    list.SelectedItems!.Clear();
                }

                for (var rangeIndex = start; rangeIndex <= end; rangeIndex++)
                {
                    if (list.Items[rangeIndex] is ModInfo rangeMod &&
                        !list.SelectedItems!.Contains(rangeMod))
                    {
                        list.SelectedItems!.Add(rangeMod);
                    }
                }
            }
            finally
            {
                selection.EndBatchUpdate();
                list.AutoScrollToSelectedItem = autoScroll;
            }

            _rangeSelectionList = list;
            _rangeSelectionMods = GetSelectedMods(list);
        }
        else if (ctrl)
        {
            if (list.SelectedItems!.Contains(mod))
            {
                list.SelectedItems!.Remove(mod);
            }
            else
            {
                list.SelectedItems!.Add(mod);
            }

            _selectionAnchorList = list;
            _selectionAnchorIndex = index;
        }
        else
        {
            var isSelected = pressedWasMulti || list.SelectedItems!.Contains(mod);
            if (isSelected && list.SelectedItems!.Count > 1)
            {
                _pendingCollapseList = list;
                _pendingCollapseMod = mod;
                _pendingCollapseIndex = index;
            }
            else
            {
                list.SelectedItems!.Clear();
                list.SelectedItems!.Add(mod);
            }

            _selectionAnchorList = list;
            _selectionAnchorIndex = index;
        }

        if (pressedWasMulti && !shift && !ctrl)
        {
            RestorePressedMultiSelection(list);
            Dispatcher.UIThread.Post(() => RestorePressedMultiSelection(list));
        }

        _refreshing = false;
        list.Focus();
        ShowDetails(mod);

        _dragSourceList = list;
        _dragStartPoint = e.GetPosition(list);
        e.Pointer.Capture(list);
        e.Handled = true;
    }

    private void SelectForContextMenu(ListBox list, ModInfo mod, int index)
    {
        if (list.SelectedItems!.Contains(mod))
        {
            _selectionAnchorList = list;
            _selectionAnchorIndex = index;
            ShowDetails(mod);
            return;
        }

        _refreshing = true;
        foreach (var other in GetModLists())
        {
            other.SelectedItems!.Clear();
        }
        list.SelectedItems!.Add(mod);
        _refreshing = false;
        _selectionAnchorList = list;
        _selectionAnchorIndex = index;
        ShowDetails(mod);
    }

    private void ShowModContextMenu(ListBox list, ModInfo mod)
    {
        var language = _config.Language;
        var flyout = new MenuFlyout();
        var steamUrl = GetSteamOpenUrl(mod);
        flyout.Items.Add(CreateMenuItem(
            Localization.OpenInSteam(language),
            steamUrl is not null,
            () => OpenUrl(steamUrl)
        ));
        flyout.Items.Add(CreateMenuItem(
            Localization.OpenInFolder(language),
            Directory.Exists(mod.Path),
            () => InstanceStore.OpenFolder(mod.Path)
        ));
        flyout.Items.Add(new Separator());

        var selectedMods = GetSelectedMods(list);
        if (selectedMods.Count == 0)
        {
            selectedMods.Add(mod);
        }

        var workshopMods = selectedMods
            .Where(selected => selected.Source == ModSource.Workshop)
            .ToList();
        var localMods = selectedMods
            .Where(selected => selected.Source == ModSource.Local)
            .ToList();
        var hasDestructiveItem = false;
        if (workshopMods.Count > 0)
        {
            flyout.Items.Add(CreateMenuItem(
                Localization.UnsubscribeMod(language),
                true,
                () => _ = UnsubscribeWorkshopModsAsync(workshopMods)
            ));
            hasDestructiveItem = true;
        }
        if (localMods.Count > 0)
        {
            flyout.Items.Add(CreateMenuItem(
                Localization.DeleteModFiles(language),
                true,
                () => _ = DeleteLocalModsAsync(localMods)
            ));
            hasDestructiveItem = true;
        }

        if (hasDestructiveItem)
        {
            flyout.Items.Add(new Separator());
        }

        var column = GetListColumn(list);
        flyout.Items.Add(CreateMenuItem(
            Localization.ColorMod(language),
            _packRoot is not null && selectedMods.Count > 0,
            () => _ = ShowModColorAsync(selectedMods, column)
        ));
        flyout.ShowAt(list, true);
    }

    private static string? GetSteamUrl(ModInfo mod)
    {
        var link = mod.Link?.Trim();
        if (!string.IsNullOrWhiteSpace(link) && IsSteamUrl(link))
        {
            return link;
        }

        if (mod.Source == ModSource.Workshop)
        {
            var folder = Path.GetFileName(
                mod.Path.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                )
            );
            if (long.TryParse(folder, out _))
            {
                return $"https://steamcommunity.com/sharedfiles/filedetails/?id={folder}";
            }
        }

        return null;
    }

    private static string? GetSteamOpenUrl(ModInfo mod)
    {
        var workshopId = GetWorkshopId(mod) ??
            ParseWorkshopIdFromUrl(mod.Link);
        if (workshopId is not null)
        {
            return $"steam://url/CommunityFilePage/{workshopId.Value}";
        }

        var link = mod.Link?.Trim();
        if (string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        if (link.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
        {
            return link;
        }

        return IsSteamUrl(link) ? "steam://openurl/" + link : null;
    }

    private static ulong? ParseWorkshopIdFromUrl(string? url)
    {
        var marker = "id=";
        var markerIndex = url?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (markerIndex < 0)
        {
            return null;
        }

        var digits = new string(
            url![(markerIndex + marker.Length)..]
                .TakeWhile(char.IsDigit)
                .ToArray()
        );
        return ulong.TryParse(digits, out var id) ? id : null;
    }

    private static bool IsSteamUrl(string link)
    {
        if (link.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
               (uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("steampowered.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".steampowered.com", StringComparison.OrdinalIgnoreCase));
    }

    private void OnGithubLinkClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/ZXUW/Rimworld-PackFrame");
    }

    private void OnDiscordLinkClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://discord.gg/24Qmy5KHdc");
    }

    private void OnBackgroundClick(object? sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var (key, hex, light) in ThemeService.BackgroundPresets)
        {
            var item = new MenuItem
            {
                Header = BackgroundName(key),
                Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    Background = new SolidColorBrush(Color.Parse(hex)),
                    BorderBrush = SecondaryBorderBrush,
                    BorderThickness = new Thickness(1)
                }
            };
            var selectedHex = hex;
            var selectedLight = light;
            item.Click += (_, _) =>
            {
                ThemeService.Apply(selectedHex, selectedLight);
                _config.BackgroundColor = selectedHex;
                _config.Save();
            };
            flyout.Items.Add(item);
        }
        flyout.ShowAt((Control)sender!);
    }

    private string BackgroundName(string key)
    {
        return key switch
        {
            "rimworld" => "Rimworld",
            "dark" => OperationText.Choose(_config.Language, "Deep blue", "深蓝", "深藍", "ディープブルー"),
            "black" => OperationText.Choose(_config.Language, "Black", "黑色", "黑色", "ブラック"),
            "purple" => OperationText.Choose(_config.Language, "Purple", "暗紫", "暗紫", "パープル"),
            "green" => OperationText.Choose(_config.Language, "Green", "暗绿", "暗綠", "グリーン"),
            "brown" => OperationText.Choose(_config.Language, "Brown", "暗棕", "暗棕", "ブラウン"),
            _ => OperationText.Choose(_config.Language, "Default", "默认", "默認", "デフォルト")
        };
    }

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (_checkingUpdate)
        {
            return;
        }

        _checkingUpdate = true;
        _checkUpdateButton.IsEnabled = false;
        ShowToast(OperationText.Choose(
            _config.Language,
            "Checking for updates...",
            "正在检查更新...",
            "正在檢查更新...",
            "更新を確認中..."
        ));
        try
        {
            var update = await UpdateService.CheckAsync();
            if (update is null || !UpdateService.IsNewer(update.Tag))
            {
                ShowToast(OperationText.Choose(
                    _config.Language,
                    "You are using the latest version.",
                    "当前已是最新版本。",
                    "目前已是最新版本。",
                    "最新バージョンです。"
                ));
                return;
            }

            var message = OperationText.Choose(
                _config.Language,
                "A new version is available: " + update.Tag + "\nOpen the download page?",
                "发现新版本：" + update.Tag + "\n是否打开下载页面？",
                "發現新版本：" + update.Tag + "\n是否開啟下載頁面？",
                "新しいバージョンがあります：" + update.Tag + "\nダウンロードページを開きますか？"
            );
            var confirm = new ConfirmWindow(
                message,
                OperationText.Choose(_config.Language, "Download", "打开下载", "開啟下載", "ダウンロードを開く"),
                Localization.CancelButton(_config.Language)
            );
            if (await confirm.ShowDialog<bool>(this))
            {
                OpenUrl(update.Url);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Check for updates", exception);
            ShowToast(OperationText.Choose(
                _config.Language,
                "Update check failed: " + exception.Message,
                "检查更新失败：" + exception.Message,
                "檢查更新失敗：" + exception.Message,
                "更新確認に失敗しました：" + exception.Message
            ));
        }
        finally
        {
            _checkingUpdate = false;
            _checkUpdateButton.IsEnabled = true;
        }
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static async Task<bool> TryDeleteModFolderAsync(ModInfo mod)
    {
        if (string.IsNullOrWhiteSpace(mod.Path) || !Directory.Exists(mod.Path))
        {
            return false;
        }

        try
        {
            await Task.Run(() => Directory.Delete(mod.Path, true));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task DeleteLocalModsAsync(IReadOnlyList<ModInfo> mods)
    {
        if (mods.Count == 0)
        {
            return;
        }

        var language = _config.Language;
        var message = mods.Count == 1
            ? Localization.DeleteModConfirm(language, mods[0].Name)
            : Localization.DeleteModsConfirm(language, mods.Count);
        var confirm = new ConfirmWindow(
            message,
            Localization.DeleteModFiles(language),
            Localization.CancelButton(language)
        );
        if (!await confirm.ShowDialog<bool>(this))
        {
            return;
        }

        ShowDetails(null);
        var deleted = new List<ModInfo>();
        foreach (var mod in mods)
        {
            if (await TryDeleteModFolderAsync(mod))
            {
                deleted.Add(mod);
            }
        }

        if (deleted.Count > 0)
        {
            RemoveDeletedMods(deleted);
        }

        ShowToast(
            deleted.Count == mods.Count
                ? Localization.ModFilesDeleted(language)
                : Localization.ModDeleteFailed(language)
        );
    }

    private static ulong? GetWorkshopId(ModInfo mod)
    {
        if (mod.Source != ModSource.Workshop)
        {
            return null;
        }

        var folder = Path.GetFileName(
            mod.Path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            )
        );
        if (ulong.TryParse(folder, out var folderId))
        {
            return folderId;
        }

        return ParseWorkshopIdFromUrl(GetSteamUrl(mod));
    }

    private async Task UnsubscribeWorkshopModsAsync(IReadOnlyList<ModInfo> mods)
    {
        var language = _config.Language;
        var gamePath = _gameInstallation?.GamePath;
        var workshopIds = mods
            .Select(GetWorkshopId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (workshopIds.Count == 0 || string.IsNullOrWhiteSpace(gamePath))
        {
            ShowToast(Localization.SteamUnsubscribeFailed(
                language,
                Localization.SteamUnavailable(language)
            ));
            return;
        }

        var result = await Task.Run(() =>
        {
            var success = SteamWorkshopClient.UnsubscribeMany(
                gamePath,
                workshopIds,
                out var error
            );
            return (success, error);
        });

        if (!result.success)
        {
            ShowToast(Localization.SteamUnsubscribeFailed(
                language,
                result.error
            ));
            return;
        }

        ShowDetails(null);
        RemoveDeletedMods(mods);
        ShowToast(workshopIds.Count == 1
            ? Localization.SteamUnsubscribed(language)
            : Localization.SteamUnsubscribedCount(language, workshopIds.Count));
    }

    private void RemoveDeletedMods(IReadOnlyCollection<ModInfo> mods)
    {
        var packageIds = mods
            .Select(mod => mod.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (packageIds.Count == 0)
        {
            return;
        }

        var idSet = new HashSet<string>(
            packageIds,
            StringComparer.OrdinalIgnoreCase
        );
        _allMods.RemoveAll(item => idSet.Contains(item.PackageId));
        foreach (var packageId in packageIds)
        {
            _modsById.Remove(packageId);
        }

        foreach (var instance in _instances)
        {
            var updated = instance.ActivePackageIds
                .Where(id => !idSet.Contains(id))
                .ToList();
            if (!ActiveIdsEqual(instance.ActivePackageIds, updated))
            {
                InstanceStore.StageActiveMods(instance, updated);
            }
        }

        ClearUndoHistory();
        ShowDetails(null);
        RefreshManager(false);
    }

    private async Task ShowModColorAsync(
        IReadOnlyList<ModInfo> mods,
        int column
    )
    {
        if (_packRoot is null || mods.Count == 0)
        {
            return;
        }

        var data = ModColorStore.Load(_packRoot);
        var currentColors = mods
            .Select(mod => data.ModColors.TryGetValue(mod.PackageId, out var hex)
                ? hex
                : null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currentColor = currentColors.Count == 1 ? currentColors[0] : null;
        var picker = new ColorPaletteWindow(
            currentColor,
            data.ColorLabels,
            _config.Language
        );
        var choice = await picker.ShowDialog<ModColorChoice?>(this);
        if (choice is null)
        {
            return;
        }

        ModColorStore.SetLabels(_packRoot, choice.Labels);
        if (choice.Cancelled)
        {
            return;
        }
        var packageIds = mods
            .Select(mod => mod.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var before = CaptureColorStates(packageIds);
        var color = choice.Clear ? null : choice.Hex;
        var after = packageIds.ToDictionary(
            id => id,
            _ => color,
            StringComparer.OrdinalIgnoreCase
        );
        ModColorStore.SetModColors(_packRoot, after);
        PushColorUndo(before, after, packageIds, column);
        RefreshManager(false);
    }

    private static ListBoxItem? FindListItemAtPosition(ListBox list, Point point)
    {
        foreach (var item in list.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var origin = item.TranslatePoint(new Point(0, 0), list);
            if (origin is { } position &&
                new Rect(position, item.Bounds.Size).Contains(point))
            {
                return item;
            }
        }

        return null;
    }

    private void SelectForDoubleClick(ListBox list, int index, ModInfo mod)
    {
        _refreshing = true;
        foreach (var other in GetModLists())
        {
            if (other != list)
            {
                other.SelectedItems!.Clear();
            }
        }

        if (!list.SelectedItems!.Contains(mod))
        {
            list.SelectedItems!.Clear();
            list.SelectedItems!.Add(mod);
        }

        _selectionAnchorList = list;
        _selectionAnchorIndex = index;
        _pendingCollapseList = null;
        _pendingCollapseMod = null;
        _pendingCollapseIndex = -1;
        _refreshing = false;
        list.Focus();
        ShowDetails(mod);
    }

    private void OnModListPointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredItem is not null)
        {
            _hoveredItem.Classes.Remove("hover");
            _hoveredItem = null;
        }

        if (_customDragActive)
        {
            UpdateAutoScroll(e);
        }
    }

    private void UpdateHoveredItem(ListBox list, PointerEventArgs e)
    {
        var item = FindListItem(list, e.Source);
        if (ReferenceEquals(item, _hoveredItem))
        {
            return;
        }

        if (_hoveredItem is not null)
        {
            _hoveredItem.Classes.Remove("hover");
        }

        _hoveredItem = item;
        if (item is not null)
        {
            item.Classes.Add("hover");
        }
    }

    private void OnModListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ListBox)
        {
            return;
        }

        if (_painting)
        {
            FinishPaint();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        var wasDragging = _customDragActive;
        var payload = _dragPayload;
        _pressedMultiList = null;
        _pressedMultiMods = null;
        _rangeSelectionList = null;
        _rangeSelectionMods = null;
        var target = wasDragging ? GetTargetListAt(e) : null;
        var targetIndex = target is null
            ? 0
            : MapDropIndexToActive(target, GetDropIndexAt(target, e.GetPosition(target)));

        StopAutoScroll();
        HideDragGhost();
        _insertionLine.IsVisible = false;
        _insertionLine.Opacity = 1;
        _customDragActive = false;
        _dragPayload = null;
        _dragSourceList = null;
        e.Pointer.Capture(null);

        if (!wasDragging)
        {
            if (_pendingCollapseList is not null && _pendingCollapseMod is not null)
            {
                _refreshing = true;
                _pendingCollapseList.SelectedItems!.Clear();
                _pendingCollapseList.SelectedItems!.Add(_pendingCollapseMod);
                _selectionAnchorList = _pendingCollapseList;
                _selectionAnchorIndex = _pendingCollapseIndex;
                _refreshing = false;
                ShowDetails(_pendingCollapseMod);
                e.Handled = true;
            }

            _pendingCollapseList = null;
            _pendingCollapseMod = null;
            _pendingCollapseIndex = -1;
            return;
        }

        _pendingCollapseList = null;
        _pendingCollapseMod = null;
        _pendingCollapseIndex = -1;
        e.Handled = true;
        if (payload is not null && target is not null)
        {
            var targetColumn = GetListColumn(target);
            var sourceInstance = GetInstanceForColumn(payload.SourceColumn);
            var targetInstance = GetInstanceForColumn(targetColumn);
            var copy = payload.SourceColumn is 1 or 2 &&
                targetColumn is 1 or 2 &&
                sourceInstance is not null &&
                targetInstance is not null &&
                !string.Equals(
                    sourceInstance.DirectoryPath,
                    targetInstance.DirectoryPath,
                    StringComparison.OrdinalIgnoreCase
                );

            if (copy)
            {
                CopyModsToProfile(payload, targetColumn, targetIndex);
            }
            else
            {
                MoveMods(payload, targetColumn, targetIndex);
            }
        }
    }

    private void OnModListPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_customDragActive)
        {
            return;
        }

        var target = GetTargetListAt(e) ?? sender as ListBox;
        if (target is null)
        {
            return;
        }

        var viewer = target
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (viewer is null)
        {
            return;
        }

        var maximum = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        var offset = Math.Clamp(
            viewer.Offset.Y - e.Delta.Y * 80,
            0,
            maximum
        );
        if (Math.Abs(offset - viewer.Offset.Y) >= 0.5)
        {
            viewer.Offset = new Vector(viewer.Offset.X, offset);
            UpdateInsertionLine(e);
        }

        e.Handled = true;
    }
    private void OnModListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBox list)
        {
            return;
        }

        if (_painting)
        {
            PaintAtPointer(e);
            e.Handled = true;
            return;
        }

        UpdateHoveredItem(list, e);

        if (!ReferenceEquals(_dragSourceList, list))
        {
            return;
        }

        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_customDragActive)
        {
            UpdateDragGhost(e);
            UpdateInsertionLine(e);
            UpdateAutoScroll(e);
            e.Handled = true;
            return;
        }

        if (_dragPayload is not null)
        {
            return;
        }

        var position = e.GetPosition(list);
        if (Math.Abs(position.X - _dragStartPoint.X) < 4 &&
            Math.Abs(position.Y - _dragStartPoint.Y) < 4)
        {
            return;
        }

        var mods = GetSelectedMods(list);
        if (mods.Count <= 1 &&
            ReferenceEquals(_pressedMultiList, list) &&
            _pressedMultiMods is { Count: > 1 })
        {
            RestorePressedMultiSelection(list);
            mods = _pressedMultiMods.ToList();
        }

        if (mods.Count == 0)
        {
            return;
        }

        _pendingCollapseList = null;
        _pendingCollapseMod = null;
        _pendingCollapseIndex = -1;
        _dragPayload = new DragPayload(GetListColumn(list), mods);
        _customDragActive = true;
        _dragGhostText.Text = string.Join(
            Environment.NewLine,
            mods.Select(mod => mod.Name)
        );
        _dragGhost.IsVisible = true;
        _insertionLine.Opacity = 1;
        UpdateDragGhost(e);
        UpdateInsertionLine(e);
        UpdateAutoScroll(e);
        e.Handled = true;
    }


    private void UpdateInsertionLine(PointerEventArgs e)
    {
        if (!_customDragActive)
        {
            _insertionLine.IsVisible = false;
            return;
        }

        var target = GetTargetListAt(e);
        if (target is null)
        {
            _insertionLine.IsVisible = false;
            return;
        }

        var position = e.GetPosition(target);
        var index = GetDropIndexAt(target, position);
        var targetTop = target.TranslatePoint(new Point(0, 0), _dragOverlay);
        if (targetTop is null)
        {
            _insertionLine.IsVisible = false;
            return;
        }

        double y;
        if (index < target.ItemCount &&
            target.ContainerFromIndex(index) is ListBoxItem item)
        {
            var itemTop = item.TranslatePoint(new Point(0, 0), _dragOverlay);
            y = itemTop?.Y ?? e.GetPosition(_dragOverlay).Y;
        }
        else
        {
            var last = target.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .Select(container => new
                {
                    Container = container,
                    Top = container.TranslatePoint(new Point(0, 0), _dragOverlay)
                })
                .Where(entry => entry.Top is not null)
                .OrderBy(entry => entry.Top!.Value.Y)
                .LastOrDefault();

            y = last is null
                ? targetTop.Value.Y + 4
                : last.Top!.Value.Y + last.Container.Bounds.Height;
        }

        _insertionLine.Width = Math.Max(0, target.Bounds.Width - 8);
        Canvas.SetLeft(_insertionLine, targetTop.Value.X + 4);
        Canvas.SetTop(_insertionLine, y - 1);
        _insertionLine.IsVisible = true;
    }

    private void HideDragGhost()
    {
        _dragGhost.IsVisible = false;
    }

    private ListBox? GetTargetListAt(PointerEventArgs e)
    {
        foreach (var list in GetModLists())
        {
            var position = e.GetPosition(list);
            if (position.X >= 0 &&
                position.Y >= 0 &&
                position.X <= list.Bounds.Width &&
                position.Y <= list.Bounds.Height)
            {
                return list;
            }
        }

        return null;
    }

    private static int GetDropIndexAt(ListBox target, Point position)
    {
        var index = target.ItemCount;
        var containers = target.GetRealizedContainers()
            .OfType<ListBoxItem>()
            .Select(container => new
            {
                Container = container,
                Top = container.TranslatePoint(new Point(0, 0), target)
            })
            .Where(entry => entry.Top is not null)
            .OrderBy(entry => entry.Top!.Value.Y)
            .ToList();
        var last = containers.LastOrDefault();
        if (last is not null &&
            position.Y >= last.Top!.Value.Y + last.Container.Bounds.Height)
        {
            return target.ItemCount;
        }

        foreach (var entry in containers)
        {
            var containerIndex = target.IndexFromContainer(entry.Container);
            if (containerIndex < 0)
            {
                continue;
            }

            var middle = entry.Top!.Value.Y + entry.Container.Bounds.Height / 2;
            if (position.Y < middle)
            {
                return containerIndex;
            }

            index = containerIndex + 1;
        }

        return index;
    }

    private int MapDropIndexToActive(ListBox target, int displayIndex)
    {
        var column = GetListColumn(target);
        if (column == 0)
        {
            return displayIndex;
        }

        var instance = GetInstanceForColumn(column);
        if (instance is null)
        {
            return displayIndex;
        }

        var active = instance.ActivePackageIds;
        if (displayIndex >= target.ItemCount)
        {
            return active.Count;
        }

        if (displayIndex < 0 || target.Items[displayIndex] is not ModInfo next)
        {
            return Math.Clamp(displayIndex, 0, active.Count);
        }

        var activeIndex = active.FindIndex(id => string.Equals(
            id,
            next.PackageId,
            StringComparison.OrdinalIgnoreCase
        ));
        return activeIndex >= 0
            ? activeIndex
            : Math.Clamp(displayIndex, 0, active.Count);
    }

    private void MoveModsOnDoubleClick(ListBox sourceList)
    {
        var sourceColumn = GetListColumn(sourceList);
        var mods = GetSelectedMods(sourceList);
        if (mods.Count == 0)
        {
            return;
        }

        if (sourceColumn == 0)
        {
            var targetList = GetModListForColumn(_lastEnableColumn);
            if (targetList is null)
            {
                return;
            }

            MoveMods(
                new DragPayload(0, mods),
                _lastEnableColumn,
                targetList.ItemCount
            );
            return;
        }

        MoveMods(new DragPayload(sourceColumn, mods), 0, 0);
    }

    private ManagedInstance? GetInstanceForColumn(int column)
    {
        return column switch
        {
            1 => GetSelectedInstance(0),
            2 => GetSelectedInstance(1),
            _ => null
        };
    }

    private void CopyModsToProfile(
        DragPayload payload,
        int targetColumn,
        int targetIndex
    )
    {
        var selectedIds = payload.Mods
            .Select(mod => mod.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedIds.Count == 0)
        {
            return;
        }

        var targetInstance = GetInstanceForColumn(targetColumn);
        if (targetInstance is null || targetInstance.IsDamaged)
        {
            return;
        }

        var targetOriginal = targetInstance.ActivePackageIds.ToList();
        var targetUpdated = ModListOperations.InsertSelection(targetOriginal, selectedIds, targetIndex);
        if (ActiveIdsEqual(targetOriginal, targetUpdated))
        {
            return;
        }

        try
        {
            InstanceStore.StageActiveMods(targetInstance, targetUpdated);
            PushUndo(
                new List<InstanceSnapshot>
                {
                    new(targetInstance.FolderName, targetOriginal)
                },
                selectedIds,
                payload.SourceColumn,
                targetColumn
            );
            _selectionAnchorList = null;
            _selectionAnchorIndex = -1;
            RefreshManager(false);
            Dispatcher.UIThread.Post(() => SelectMovedMods(targetColumn, selectedIds));
        }
        catch
        {
        }
    }
    private void MoveMods(DragPayload payload, int targetColumn, int targetIndex)
    {
        var selectedIds = payload.Mods
            .Select(mod => mod.PackageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedIds.Count == 0)
        {
            return;
        }

        var selectedSet = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        var sourceInstance = GetInstanceForColumn(payload.SourceColumn);
        var targetInstance = GetInstanceForColumn(targetColumn);
        if (sourceInstance?.IsDamaged == true || targetInstance?.IsDamaged == true) return;
        var changed = false;
        var undoSnapshots = new List<InstanceSnapshot>();

        void CaptureSnapshot(ManagedInstance? instance)
        {
            if (instance is null ||
                undoSnapshots.Any(snapshot =>
                    snapshot.FolderName.Equals(
                        instance.FolderName,
                        StringComparison.OrdinalIgnoreCase
                    )))
            {
                return;
            }

            undoSnapshots.Add(new InstanceSnapshot(
                instance.FolderName,
                instance.ActivePackageIds.ToList()
            ));
        }

        CaptureSnapshot(sourceInstance);
        CaptureSnapshot(targetInstance);

        try
        {
            if (targetColumn == 0)
            {
                if (sourceInstance is null)
                {
                    return;
                }

                var updated = sourceInstance.ActivePackageIds
                    .Where(id => !selectedSet.Contains(id))
                    .ToList();
                if (!ActiveIdsEqual(sourceInstance.ActivePackageIds, updated))
                {
                    InstanceStore.StageActiveMods(sourceInstance, updated);
                    changed = true;
                }
            }
            else
            {
                if (targetInstance is null)
                {
                    return;
                }

                var targetOriginal = targetInstance.ActivePackageIds.ToList();
                var sameInstance = sourceInstance is not null &&
                    string.Equals(
                        sourceInstance.DirectoryPath,
                        targetInstance.DirectoryPath,
                        StringComparison.OrdinalIgnoreCase
                    );

                if (sourceInstance is not null && sameInstance)
                {
                    var updated = ModListOperations.InsertSelection(sourceInstance.ActivePackageIds, selectedIds, targetIndex);
                    if (!ActiveIdsEqual(targetOriginal, updated))
                    {
                        InstanceStore.StageActiveMods(targetInstance, updated);
                        changed = true;
                    }
                }
                else
                {
                    if (sourceInstance is not null)
                    {
                        var sourceUpdated = sourceInstance.ActivePackageIds
                            .Where(id => !selectedSet.Contains(id))
                            .ToList();
                        if (!ActiveIdsEqual(sourceInstance.ActivePackageIds, sourceUpdated))
                        {
                            InstanceStore.StageActiveMods(sourceInstance, sourceUpdated);
                            changed = true;
                        }
                    }

                    var targetUpdated = ModListOperations.InsertSelection(targetOriginal, selectedIds, targetIndex);
                    if (!ActiveIdsEqual(targetOriginal, targetUpdated))
                    {
                        InstanceStore.StageActiveMods(targetInstance, targetUpdated);
                        changed = true;
                    }
                }
            }
        }
        catch
        {
        }

        if (changed)
        {
            PushUndo(undoSnapshots, selectedIds, payload.SourceColumn, targetColumn);
            _selectionAnchorList = null;
            _selectionAnchorIndex = -1;
            RefreshManager(false);
            Dispatcher.UIThread.Post(() => SelectMovedMods(targetColumn, selectedIds));
        }
    }

    private static bool ActiveIdsEqual(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second
    )
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var i = 0; i < first.Count; i++)
        {
            if (!string.Equals(first[i], second[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ModSearchState
    {
        public string Query = string.Empty;
        public int FieldIndex;
        public int SourceIndex;
        public bool HideUnmatched = true;
    }
    private sealed record DragPayload(int SourceColumn, List<ModInfo> Mods);

    private async void OnModpack1Click(object? sender, RoutedEventArgs e)
    {
        await OpenModpacksAsync(0);
    }

    private async void OnModpack2Click(object? sender, RoutedEventArgs e)
    {
        await OpenModpacksAsync(1);
    }

    private async Task OpenModpacksAsync(int column)
    {
        if (_packRoot is null ||
            _gameInstallation is null ||
            !_gameInstallation.IsValid)
        {
            ShowToast(Localization.ModpackNeedsGame(_config.Language));
            return;
        }

        var currentInstance = GetSelectedInstance(column);
        var window = new ModpackWindow(
            _config,
            _gameInstallation,
            _packRoot,
            currentInstance
        );
        var result = await window.ShowDialog<ModpackInstallResult?>(this);
        if (window.ConfigApplied && currentInstance is not null)
        {
            if (_gameInstallation is not null)
            {
                await LoadModsAsync(_gameInstallation);
            }

            ReloadInstances(currentInstance.FolderName, column);
            ShowToast(Localization.SyncConfigDone(_config.Language));
        }

        if (result is null)
        {
            return;
        }

        try
        {
            var created = await Task.Run(() => ModpackInstanceInstaller.Create(_packRoot, _gameVersion, result));
            if (_gameInstallation is not null)
            {
                await LoadModsAsync(_gameInstallation);
            }

            ReloadInstances(created.FolderName, column);
            SaveProfileSelection();
            _lastEnableColumn = column + 1;
            UpdateEnableTargetBorders();
            ClearUndoHistory();
            ShowToast(result.UnresolvedWorkshopCount > 0
                ? Localization.ModpackCreatedPending(
                    _config.Language,
                    result.Name,
                    result.PackageIds.Count,
                    result.UnresolvedWorkshopCount
                )
                : Localization.ModpackCreated(
                    _config.Language,
                    result.Name,
                    result.PackageIds.Count
                ));
        }
        catch (Exception exception)
        {
            AppLog.Error("Create installed modpack instance", exception);
            ShowToast(Localization.ModpackCreateFailed(_config.Language));
        }
    }

    private void OnInstanceActionsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var column = ReferenceEquals(button, _instanceActionButton1) ? 0 : 1;
        var selected = GetSelectedInstance(column);
        var language = _config.Language;
        var hasPackRoot = _packRoot is not null;
        var hasInstance = hasPackRoot && selected is not null;

        var flyout = new MenuFlyout();
        if (selected?.IsDamaged == true)
        {
            flyout.Items.Add(new MenuItem { Header = "⚠ " + selected.LoadError, IsEnabled = false });
            flyout.Items.Add(CreateMenuItem(Localization.OpenInstance(language), true, () => OpenInstance(column)));
            flyout.Items.Add(CreateMenuItem(Localization.ImportInstance(language), hasPackRoot, () => _ = ImportInstancesAsync(column)));
            if (!selected.IsOfficial)
                flyout.Items.Add(CreateMenuItem(Localization.DeleteInstance(language), true, () => _ = DeleteInstanceAsync(column)));
            flyout.ShowAt(button);
            return;
        }
        if (selected is null || selected.IsScratch)
        {
            flyout.Items.Add(CreateMenuItem(
                Localization.NewInstance(language),
                hasPackRoot,
                () => _ = CreateInstanceAsync(column)
            ));
            flyout.Items.Add(CreateMenuItem(
                Localization.ImportInstance(language),
                hasPackRoot,
                () => _ = ImportInstancesAsync(column)
            ));
            flyout.ShowAt(button);
            return;
        }

        if (selected.IsOfficial)
        {
            var hasOfficialPath = Directory.Exists(selected.ConfigDirectoryPath);
            flyout.Items.Add(CreateConfigFolderMenuItem(column));
            flyout.Items.Add(CreateMenuItem(
                Localization.CopyInstance(language),
                hasOfficialPath,
                () => _ = CopyInstanceAsync(column)
            ));
            flyout.Items.Add(CreateMenuItem(
                Localization.ExportInstance(language),
                hasOfficialPath,
                () => _ = ExportInstanceAsync(column)
            ));
            flyout.Items.Add(CreateMenuItem(
                Localization.ImportInstance(language),
                hasPackRoot,
                () => _ = ImportInstancesAsync(column)
            ));
            flyout.Items.Add(CreateMenuItem(
                Localization.OpenInstance(language),
                hasOfficialPath,
                () => OpenInstance(column)
            ));
            flyout.ShowAt(button);
            return;
        }

        flyout.Items.Add(CreateMenuItem(Localization.NewInstance(language), hasPackRoot,
            () => _ = CreateInstanceAsync(column)));
        flyout.Items.Add(CreateMenuItem(Localization.CopyInstance(language), hasInstance,
            () => _ = CopyInstanceAsync(column)));
        flyout.Items.Add(CreateMenuItem(Localization.RenameInstance(language), hasInstance,
            () => _ = RenameInstanceAsync(column)));
        flyout.Items.Add(CreateMenuItem(Localization.DeleteInstance(language), hasInstance,
            () => _ = DeleteInstanceAsync(column)));
        flyout.Items.Add(CreateMenuItem(Localization.ExportInstance(language), hasInstance,
            () => _ = ExportInstanceAsync(column)));
        flyout.Items.Add(CreateMenuItem(Localization.ImportInstance(language), hasPackRoot,
            () => _ = ImportInstancesAsync(column)));
        flyout.Items.Add(CreateMenuItem(Localization.OpenInstance(language), hasInstance,
            () => OpenInstance(column)));

        flyout.ShowAt(button);
    }

    private static MenuItem CreateMenuItem(string header, bool enabled, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled
        };
        item.Click += (_, _) => action();
        return item;
    }

    private MenuItem CreateConfigFolderMenuItem(int column)
    {
        var language = _config.Language;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            MinWidth = 180
        };
        header.Children.Add(new TextBlock
        {
            Text = Localization.ModifyConfigFolder(language),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        var icon = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            BorderBrush = TextBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        icon.Child = new TextBlock
        {
            Text = "!",
            FontSize = 11,
            Foreground = TextBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(
            icon,
            CreateConfigTooltip(Localization.ConfigFolderTooltip(language))
        );
        Grid.SetColumn(icon, 1);
        header.Children.Add(icon);

        var item = new MenuItem
        {
            Header = header
        };
        item.Click += (_, _) => _ = ChangeOfficialConfigFolderAsync(column);
        return item;
    }

    private ManagedInstance? GetSelectedInstance(int column)
    {
        var comboBox = column == 0 ? _instanceCombo1 : _instanceCombo2;
        var index = comboBox.SelectedIndex;
        return index >= 0 && index < _instances.Count ? _instances[index] : null;
    }

    private string? GetSelectedInstanceFolder(int column)
    {
        return GetSelectedInstance(column)?.FolderName;
    }

    private void ReloadInstances(string? selectFolder, int selectColumn)
    {
        if (_packRoot is null)
        {
            return;
        }

        var current1 = GetSelectedInstanceFolder(0);
        var current2 = GetSelectedInstanceFolder(1);
        _instances = BuildInstances(InstanceStore.Discover(_packRoot));
        var folders = _instances.Select(instance => instance.FolderName).ToList();
        var normalFolders = _instances
            .Where(instance => !instance.IsOfficial && !instance.IsScratch)
            .Select(instance => instance.FolderName)
            .ToList();
        var names = _instances.Select(instance => instance.DisplayName).ToList();

        var target1 = current1;
        var target2 = current2;
        if (selectColumn == 0)
        {
            target1 = selectFolder;
        }
        else if (selectColumn == 1)
        {
            target2 = selectFolder;
        }

        if (target1 is not null && !folders.Contains(target1))
        {
            target1 = null;
        }
        if (target2 is not null && !folders.Contains(target2))
        {
            target2 = null;
        }
        if (target1 is null && selectColumn <= 0)
        {
            target1 = normalFolders.FirstOrDefault() ?? folders.FirstOrDefault();
        }
        if (target2 is null && selectColumn == 1)
        {
            target2 = normalFolders.FirstOrDefault() ?? folders.FirstOrDefault();
        }


        var index1 = target1 is null ? -1 : folders.IndexOf(target1);
        var index2 = target2 is null ? -1 : folders.IndexOf(target2);

        _updatingInstances = true;
        SetInstanceNames(names);
        _instanceCombo1.SelectedIndex = index1;
        _instanceCombo2.SelectedIndex = index2;
        _updatingInstances = false;
        _lastInstanceIndex1 = index1;
        _lastInstanceIndex2 = index2;

        RefreshManager(true);
    }

    private async Task CreateInstanceAsync(int column)
    {
        if (_packRoot is null)
        {
            return;
        }

        try
        {
            var source = GetSelectedInstance(column);
            var initialMods = source?.IsScratch == true
                ? source.ActivePackageIds.ToList()
                : null;
            var created = await Task.Run(() =>
                InstanceStore.CreateNew(
                    _packRoot,
                    source,
                    _gameVersion,
                    initialMods
                )
            );
            if (source?.IsScratch == true)
            {
                _scratchActiveIds.Clear();
            }

            ReloadInstances(created.FolderName, column);
            ClearUndoHistory();
        }
        catch
        {
        }
    }

    private async Task CopyInstanceAsync(int column)
    {
        if (_packRoot is null)
        {
            return;
        }

        var source = GetSelectedInstance(column);
        if (source is null)
        {
            return;
        }

        try
        {
            var copied = await Task.Run(() => source.IsOfficial
                ? InstanceStore.CopyOfficial(_packRoot, source)
                : InstanceStore.CopyInstance(_packRoot, source, _gameVersion));
            ReloadInstances(copied.FolderName, column);
            ClearUndoHistory();
        }
        catch
        {
        }
    }

    private async Task RenameInstanceAsync(int column)
    {
        var instance = GetSelectedInstance(column);
        if (instance is null || instance.IsOfficial)
        {
            return;
        }

        var language = _config.Language;
        var dialog = new RenameWindow(
            instance.DisplayName,
            Localization.RenameInstance(language),
            Localization.CancelButton(language)
        );
        var name = await dialog.ShowDialog<string?>(this);
        if (name is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => InstanceStore.RenameInstance(instance, name));
            ReloadInstances(instance.FolderName, column);
        }
        catch
        {
        }
    }

    private async Task DeleteInstanceAsync(int column)
    {
        if (_packRoot is null)
        {
            return;
        }

        var instance = GetSelectedInstance(column);
        if (instance is null || instance.IsOfficial)
        {
            return;
        }

        var language = _config.Language;
        var confirm = new ConfirmWindow(
            Localization.DeleteConfirmMessage(language, instance.FolderName),
            Localization.DeleteButton(language),
            Localization.CancelButton(language)
        );
        var confirmed = await confirm.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        var replacement = GetNextOrPreviousFolder(instance.FolderName);

        try
        {
            await Task.Run(() => InstanceStore.DeleteInstance(instance));
            ReloadInstancesAfterDelete(instance.FolderName, replacement, column);
            ClearUndoHistory();
        }
        catch
        {
        }
    }

    private string? GetNextOrPreviousFolder(string folderName)
    {
        var index = _instances.FindIndex(instance =>
            instance.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase)
        );
        if (index < 0)
        {
            return null;
        }

        for (var i = index + 1; i < _instances.Count; i++)
        {
            if (!_instances[i].IsOfficial)
            {
                return _instances[i].FolderName;
            }
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (!_instances[i].IsOfficial)
            {
                return _instances[i].FolderName;
            }
        }

        return null;
    }

    private void ReloadInstancesAfterDelete(
        string deletedFolder,
        string? replacementFolder,
        int deletedColumn
    )
    {
        if (_packRoot is null)
        {
            return;
        }

        var current1 = GetSelectedInstanceFolder(0);
        var current2 = GetSelectedInstanceFolder(1);
        if (string.Equals(current1, deletedFolder, StringComparison.OrdinalIgnoreCase))
        {
            current1 = replacementFolder;
        }
        if (string.Equals(current2, deletedFolder, StringComparison.OrdinalIgnoreCase))
        {
            current2 = replacementFolder;
        }
        if (deletedColumn == 0)
        {
            current1 = replacementFolder;
        }
        else
        {
            current2 = replacementFolder;
        }

        _instances = BuildInstances(InstanceStore.Discover(_packRoot));
        var folders = _instances.Select(instance => instance.FolderName).ToList();
        var names = _instances.Select(instance => instance.DisplayName).ToList();

        if (current1 is not null && !folders.Contains(current1))
        {
            current1 = null;
        }
        if (current2 is not null && !folders.Contains(current2))
        {
            current2 = null;
        }

        var index1 = current1 is null ? -1 : folders.IndexOf(current1);
        var index2 = current2 is null ? -1 : folders.IndexOf(current2);

        _updatingInstances = true;
        SetInstanceNames(names);
        _instanceCombo1.SelectedIndex = index1;
        _instanceCombo2.SelectedIndex = index2;
        _updatingInstances = false;
        _lastInstanceIndex1 = index1;
        _lastInstanceIndex2 = index2;

        RefreshManager(true);
    }

    private async Task ExportInstanceAsync(int column)
    {
        if (_packRoot is null)
        {
            return;
        }

        var instance = GetSelectedInstance(column);
        if (instance is null)
        {
            return;
        }

        try
        {
            var export = await Task.Run(() =>
                ModpackRepoExporter.Prepare(
                    _packRoot,
                    instance,
                    packageId => SelectModForExport(packageId, instance)
                )
            );
            var window = new ExportCollectionWindow(
                export,
                _gameInstallation?.GamePath,
                _config.Language,
                _config.ModpackIndexUrl,
                instance,
                progress => Task.Run(() =>
                    ModpackRepoExporter.MaterializeLocalMods(
                        export.RepoPath,
                        instance,
                        packageId => SelectModForExport(packageId, instance),
                        progress,
                        _config.Language
                    )),
                _config
            );
            await window.ShowDialog(this);
        }
        catch (Exception exception)
        {
            AppLog.Error("Export instance: " + instance.FolderName, exception);
            ShowToast(Localization.ExportCollectionFailed(
                _config.Language,
                exception.Message
            ));
        }
    }

    private async Task ImportInstancesAsync(int column)
    {
        if (_packRoot is null)
        {
            return;
        }

        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null)
            {
                return;
            }

            var inputFolder = Path.Combine(_packRoot, "Input profile");
            Directory.CreateDirectory(inputFolder);
            var startLocation = await storage.TryGetFolderFromPathAsync(inputFolder);
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Profiles and saves")
                    {
                        Patterns = new[] { "*.xml", "*.rml", "*.json", "*.rws", "*.zip" }
                    }
                }
            });
            if (files.Count == 0)
            {
                return;
            }

            var file = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(file) ||
                !InstanceStore.IsSupportedImportFile(file))
            {
                return;
            }

            var isArchive = Path.GetExtension(file).Equals(
                ".zip",
                StringComparison.OrdinalIgnoreCase
            );
            var target = GetSelectedInstance(column);
            if (target?.IsScratch == true)
            {
                target = null;
            }
            if (target is null)
            {
                var imported = isArchive
                    ? await Task.Run(() => InstanceStore.ImportArchiveNew(_packRoot, file))
                    : await Task.Run(() =>
                        InstanceStore.ImportFileNew(_packRoot, file, _gameVersion)
                    );
                if (imported is null)
                {
                    return;
                }

                ReloadInstances(imported.FolderName, column);
                ClearUndoHistory();
                return;
            }

            var language = _config.Language;
            var canOverwrite = !target.IsOfficial ||
                Directory.Exists(target.ConfigDirectoryPath);
            var modeWindow = new ImportModeWindow(
                Localization.Overwrite(language),
                Localization.NewInstance(language),
                canOverwrite
            );
            var mode = await modeWindow.ShowDialog<ImportMode?>(this);
            if (mode is null)
            {
                return;
            }

            if (mode == ImportMode.New)
            {
                var imported = isArchive
                    ? await Task.Run(() => InstanceStore.ImportArchiveNew(_packRoot, file))
                    : await Task.Run(() =>
                        InstanceStore.ImportFileNew(_packRoot, file, _gameVersion)
                    );
                if (imported is null)
                {
                    return;
                }

                ReloadInstances(imported.FolderName, column);
                ClearUndoHistory();
                return;
            }

            if (target is null)
            {
                return;
            }

            var confirm = new ConfirmWindow(
                isArchive
                    ? Localization.OverwriteArchiveConfirmMessage(language, target.DisplayName)
                    : Localization.OverwriteConfirmMessage(language, target.DisplayName),
                Localization.Overwrite(language),
                Localization.CancelButton(language)
            );
            var confirmed = await confirm.ShowDialog<bool>(this);
            if (!confirmed)
            {
                return;
            }

            var overwritten = target.IsOfficial
                ? await Task.Run(() =>
                    InstanceStore.ImportOfficialOverwrite(target, file, _gameVersion)
                )
                : isArchive
                    ? await Task.Run(() => InstanceStore.ImportArchiveOverwrite(target, file))
                    : await Task.Run(() =>
                        InstanceStore.ImportFileOverwrite(target, file, _gameVersion)
                    );
            if (overwritten)
            {
                ReloadInstances(target.FolderName, column);
                ClearUndoHistory();
            }
        }
        catch
        {
        }
    }

    private void OpenInstance(int column)
    {
        var instance = GetSelectedInstance(column);
        if (instance is null)
        {
            return;
        }

        try
        {
            InstanceStore.OpenInstance(instance);
        }
        catch
        {
        }
    }

    private string? _previewPath;
    private int _previewVersion;
    private bool _windowClosed;

    private async void ShowDetails(ModInfo? mod)
    {
        if (mod is null)
        {
            _nameValue.Text = string.Empty;
            _authorValue.Text = string.Empty;
            _pathValue.Text = string.Empty;
            _packageValue.Text = string.Empty;
            _descriptionValue.Text = string.Empty;
            _previewVersion++;
            _previewPath = null;
            _previewImage.Source = null;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            return;
        }

        _nameValue.Text = mod.Name;
        _authorValue.Text = mod.Author;
        _pathValue.Text = mod.Path;
        _packageValue.Text = mod.PackageId;
        _descriptionValue.Text = mod.Description;

        if (_previewPath == mod.PreviewPath) return;
        _previewPath = mod.PreviewPath;
        var version = ++_previewVersion;
        _previewImage.Source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        if (string.IsNullOrWhiteSpace(mod.PreviewPath))
        {
            return;
        }

        try
        {
            var path = mod.PreviewPath;
            var width = Math.Clamp((int)(_previewImage.Bounds.Width * RenderScaling), 256, 1024);
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, width);
            });
            if (_windowClosed || version != _previewVersion)
            {
                bitmap.Dispose();
                return;
            }
            _previewBitmap = bitmap;
            _previewImage.Source = _previewBitmap;
        }
        catch (Exception exception)
        {
            AppLog.Error("Load preview: " + mod.PreviewPath, exception);
            if (version == _previewVersion) _previewPath = null;
        }
    }

    private void SetFieldValid(bool valid)
    {
        if (valid)
        {
            _pathField.Classes.Remove("invalid");
        }
        else
        {
            _pathField.Classes.Add("invalid");
        }

        _pathBox.Foreground = valid ? TextBrush : Brushes.White;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closeApproved && InstanceStore.HasPendingMods)
        {
            e.Cancel = true;
            if (await ResolvePendingChangesAsync())
            {
                _closeApproved = true;
                Close();
            }
            return;
        }
        _launchCancellation?.Cancel();
        _launchCancellation?.Dispose();
        _launchCancellation = null;
        _problemTooltipTimer.Stop();
        foreach (var timer in _searchTimers.Values) timer.Stop();
        _windowClosed = true;
        _previewVersion++;
        _previewImage.Source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        CloseProblemTooltip();
        _config.GamePath = _pathBox.Text ?? string.Empty;
        _config.ProfileFolder1 = GetSelectedInstanceFolder(0);
        _config.ProfileFolder2 = GetSelectedInstanceFolder(1);
        _config.Save();
    }
}
