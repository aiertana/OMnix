using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkitWrapper;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OMniX.WinUI.Services;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Requests;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Helpers;
using OMniX.Core.Services;
using OMniX.Core.Services.GameBanana;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Models.ViewModels;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels;
using OMniX.WinUI.ViewModels.SubVms;
using OMniX.WinUI.Views.CharacterDetailsPages;
using Serilog;

namespace OMniX.WinUI.ViewModels;

public partial class CharactersViewModel : ObservableRecipient, INavigationAware
{
    private readonly IGameService _gameService;
    private readonly ILogger _logger;
    private readonly INavigationService _navigationService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILanguageLocalizer _localizer;
    private readonly ModDragAndDropService _modDragAndDropService;
    private readonly ModNotificationManager _modNotificationManager;
    private readonly ModCrawlerService _modCrawlerService;
    private readonly ModSettingsService _modSettingsService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly ModPresetHandlerService _modPresetHandlerService;
    private readonly BusyService _busyService;
    private readonly ModRandomizationService _modRandomizationService;
    private readonly IWindowManagerService _windowManagerService;

    public readonly GenshinProcessManager GenshinProcessManager;
    public readonly ThreeDMigtoProcessManager ThreeDMigtoProcessManager;

    public readonly string StartGameIcon;
    public readonly string ShortGameName;
    public NotificationManager NotificationManager { get; }
    public ElevatorService ElevatorService { get; }

    public OverviewDockPanelVM DockPanelVM { get; }

    public SimpleSelectProcessDialogVM SimpleSelectProcessDialogVM { get; } = new();


    private IReadOnlyList<IModdableObject> _characters = new List<IModdableObject>();

    private IReadOnlyList<CharacterGridItemModel> _backendCharacters = new List<CharacterGridItemModel>();

    /// <summary>
    ///     二开：刚新建的角色（内部名 → 创建时间）。
    ///     用来让新角色临时排在列表最前，并在卡片上打「新」角标，过一段时间自动失效。
    ///     只在内存里维护，重启应用就没了。
    /// </summary>
    private static readonly TimeSpan NewCharacterHighlightDuration = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, DateTime> _newlyCreatedCharacters = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<CharacterGridItemModel> SuggestionsBox { get; } = new();
    public ObservableCollection<CharactersViewModels.ModPresetEntryVm> ModPresets { get; } = new();

    /// <summary>
    ///     二开：当前正在用的预设名。空的表示这次运行还没应用过任何预设。
    /// </summary>
    private string? _currentPresetName;

    /// <summary>
    ///     二开：置顶过的预设 → 置顶之前它排在第几位（取消置顶时要挪回去）。
    ///     只在本次运行里有效，重启就没了。
    /// </summary>
    private static readonly Dictionary<string, int> PinnedPresets = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<CharacterGridItemModel> Characters { get; } = new();

    private string _searchText = string.Empty;

    private readonly Dictionary<FilterType, GridFilter> _filters = new();


    public ObservableCollection<GridItemSortingMethod> SortingMethods { get; } = new();

    [ObservableProperty] private GridItemSortingMethod _selectedSortingMethod;
    [ObservableProperty] private bool _sortByDescending;

    [ObservableProperty] private bool _canCheckForUpdates = false;


    [ObservableProperty] private string _categoryPageTitle = string.Empty;
    [ObservableProperty] private string _overviewStatsText = string.Empty;
    [ObservableProperty] private string _modToggleText = string.Empty;
    [ObservableProperty] private string _modEnabledToggleText = string.Empty;
    [ObservableProperty] private string _modNotificationsToggleText = string.Empty;
    [ObservableProperty] private string _searchBoxPlaceHolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ApplyPresetCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    private bool _isNavigating = true;

    public CharactersViewModel(IGameService gameService, ILogger logger, INavigationService navigationService,
        ISkinManagerService skinManagerService, ILocalSettingsService localSettingsService,
        NotificationManager notificationManager, ElevatorService elevatorService,
        GenshinProcessManager genshinProcessManager, ThreeDMigtoProcessManager threeDMigtoProcessManager,
        ModDragAndDropService modDragAndDropService, ModNotificationManager modNotificationManager,
        ModCrawlerService modCrawlerService, ModSettingsService modSettingsService,
        ModUpdateAvailableChecker modUpdateAvailableChecker, ModPresetHandlerService modPresetHandlerService,
        BusyService busyService, ILanguageLocalizer localizer, ModRandomizationService modRandomizationService,
        IWindowManagerService windowManagerService)
    {
        _gameService = gameService;
        _logger = logger.ForContext<CharactersViewModel>();
        _navigationService = navigationService;
        _skinManagerService = skinManagerService;
        _localSettingsService = localSettingsService;
        NotificationManager = notificationManager;
        ElevatorService = elevatorService;
        GenshinProcessManager = genshinProcessManager;
        ThreeDMigtoProcessManager = threeDMigtoProcessManager;
        _modDragAndDropService = modDragAndDropService;
        _modNotificationManager = modNotificationManager;
        _modCrawlerService = modCrawlerService;
        _modSettingsService = modSettingsService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _modPresetHandlerService = modPresetHandlerService;
        _busyService = busyService;
        _localizer = localizer;
        _modRandomizationService = modRandomizationService;
        _windowManagerService = windowManagerService;

        ElevatorService.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ElevatorService.ElevatorStatus))
                RefreshModsInGameCommand.NotifyCanExecuteChanged();
        };

        _modNotificationManager.OnModNotification += (_, _) =>
            App.MainWindow.DispatcherQueue.EnqueueAsync(RefreshNotificationsAsync);

        DockPanelVM = new OverviewDockPanelVM();
        StartGameIcon = _gameService.GameIcon;
        ShortGameName = "Start " + _gameService.GameShortName;

        CanCheckForUpdates = _modUpdateAvailableChecker.IsReady;
        _modUpdateAvailableChecker.OnUpdateCheckerEvent += (_, _) =>
        {
            App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                CanCheckForUpdates = _modUpdateAvailableChecker.IsReady;
                return Task.CompletedTask;
            });
        };

        IsBusy = _busyService.IsPageBusy(this);
    }

    public event EventHandler<ScrollToCharacterArgs>? OnScrollToCharacter;

    private void FilterElementSelected(object? sender, FilterElementSelectedArgs e)
    {
        if (_category.ModCategory != ModCategory.Character)
            return;

        if (e.InternalElementNames.Length == 0)
        {
            _filters.Remove(FilterType.Element);
            ResetContent();
            return;
        }

        _filters[FilterType.Element] = new GridFilter(character =>
        {
            var element = ((ICharacter)character.Character).Element;
            var name = element.InternalName.ToString();

            if (e.InternalElementNames.Contains(name))
                return true;

            // 二开：选了「其他」时，把没有属性的（元素为 None）也筛出来。
            // Element.NoneElement() 是 internal，这里只能用内部名比较。
            return e.InternalElementNames.Contains(
                       OMniX.WinUI.ViewModels.SubVms.OverviewDockPanelVM.OtherElementInternalName) &&
                   name.Equals("None", StringComparison.OrdinalIgnoreCase);
        });
        ResetContent();
    }

    private CharacterGridItemModel NoCharacterFound =>
        new(new Character("None", $"没有找到{_category.DisplayNamePlural}"));

    public void AutoSuggestBox_TextChanged(string text)
    {
        _searchText = text;
        SuggestionsBox.Clear();

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            SuggestionsBox.Clear();
            _filters.Remove(FilterType.Search);
            ResetContent();
            return;
        }

        var suitableItems = _gameService.QueryModdableObjects(text, category: _category, minScore: 120)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(x => new CharacterGridItemModel(x.Key))
            .ToList();


        if (!suitableItems.Any())
        {
            SuggestionsBox.Add(NoCharacterFound);
            _filters.Remove(FilterType.Search);
            ResetContent();
            return;
        }

        suitableItems.ForEach(suggestion => SuggestionsBox.Add(suggestion));

        _filters[FilterType.Search] = new GridFilter(character => SuggestionsBox.Contains(character));
        ResetContent();
    }


    public async Task SuggestionBox_Chosen(CharacterGridItemModel? character)
    {
        if (character == NoCharacterFound || character is null)
            return;


        await CharacterClicked(character);
    }

    private void ResetContent()
    {
        if (_isNavigating) return;

        // 二开：排序期间先把“新增角色”占位卡拿掉，避免参与 IndexOf/Except 的索引计算
        var addCard = Characters.FirstOrDefault(character => character.IsAddCard);
        if (addCard is not null)
            Characters.Remove(addCard);

        var filteredCharacters = FilterCharacters(_backendCharacters);
        var sortedCharacters = SelectedSortingMethod.Sort(filteredCharacters, SortByDescending).ToList();

        // 二开：刚新建的角色临时排到最前。
        // 占位卡由下面的 EnsureAddCardFirst 插到第 0 位，所以新角色实际显示在它后面一格，
        // 也就是用户看到的第一位。
        sortedCharacters = MoveNewCharactersToFront(sortedCharacters);

        var charactersToRemove = Characters.Except(sortedCharacters).ToArray();

        if (Characters.Count == 0)
        {
            foreach (var characterGridItemModel in sortedCharacters)
            {
                Characters.Add(characterGridItemModel);
            }

            EnsureAddCardFirst();
            UpdateOverviewStats();

            return;
        }

        var missingCharacters = sortedCharacters.Except(Characters);

        foreach (var characterGridItemModel in missingCharacters)
        {
            Characters.Add(characterGridItemModel);
        }

        foreach (var characterGridItemModel in sortedCharacters)
        {
            var newIndex = sortedCharacters.IndexOf(characterGridItemModel);
            var oldIndex = Characters.IndexOf(characterGridItemModel);
            //Check if character is already at the right index

            if (newIndex == Characters.IndexOf(characterGridItemModel)) continue;

            if (oldIndex < 0 || oldIndex >= Characters.Count || newIndex < 0 || newIndex >= Characters.Count)
                throw new ArgumentOutOfRangeException();

            Characters.RemoveAt(oldIndex);
            Characters.Insert(newIndex, characterGridItemModel);
        }


        foreach (var characterGridItemModel in charactersToRemove)
        {
            Characters.Remove(characterGridItemModel);
        }


        Debug.Assert(Characters.Distinct().Count() == Characters.Count,
            $"Characters.Distinct().Count(): {Characters.Distinct().Count()} != Characters.Count: {Characters.Count}\n\t" +
            $"Duplicate characters found in character overview");

        // 二开：占位卡固定在第一格
        EnsureAddCardFirst();
        UpdateOverviewStats();
    }

    /// <summary>
    ///     二开：把刚新建的角色挪到列表最前，多个的话按创建时间倒序（最新的在最前）
    /// </summary>
    private List<CharacterGridItemModel> MoveNewCharactersToFront(
        List<CharacterGridItemModel> sortedCharacters)
    {
        CleanupExpiredNewCharacters();

        if (_newlyCreatedCharacters.Count == 0)
            return sortedCharacters;

        var newOnes = sortedCharacters
            .Where(x => _newlyCreatedCharacters.ContainsKey(x.Character.InternalName.ToString()))
            .OrderByDescending(x => _newlyCreatedCharacters[x.Character.InternalName.ToString()])
            .ToArray();

        if (newOnes.Length == 0)
            return sortedCharacters;

        var result = new List<CharacterGridItemModel>(newOnes);
        result.AddRange(sortedCharacters.Where(x => !newOnes.Contains(x)));

        return result;
    }

    /// <summary>
    ///     二开：清掉超过高亮时长的记录，让“新”标记自动失效，角色回到正常排序
    /// </summary>
    private void CleanupExpiredNewCharacters()
    {
        var cutoff = DateTime.UtcNow - NewCharacterHighlightDuration;

        var expired = _newlyCreatedCharacters
            .Where(x => x.Value < cutoff)
            .Select(x => x.Key)
            .ToArray();

        foreach (var internalName in expired)
            _newlyCreatedCharacters.Remove(internalName);
    }

    /// <summary>
    ///     二开：确保“新增角色”卡排在网格第一位
    /// </summary>
    private void EnsureAddCardFirst()
    {
        if (Characters.Count > 0 && Characters[0].IsAddCard)
            return;

        // 占位卡需要借用真实角色做绑定回退；没有角色时不插（空列表里直接去“角色管理”新建）
        var fallback = _backendCharacters.FirstOrDefault()?.Character;
        if (fallback is null)
            return;

        Characters.Insert(0, CharacterGridItemModel.CreateAddCard(fallback));
    }

    /// <summary>
    ///     页头统计文案：当前显示 x / y 个对象，合计 n 个 Mod
    /// </summary>
    private void UpdateOverviewStats()
    {
        var itemName = _category?.DisplayNamePlural ?? "对象";

        // 二开：占位卡不算角色，统计里排除
        var visibleCharacters = Characters.Where(character => !character.IsAddCard).ToArray();
        var totalMods = visibleCharacters.Sum(character => character.Mods.Count);
        var enabledMods = visibleCharacters.Sum(character => character.Mods.Count(mod => mod.IsEnabled));

        OverviewStatsText =
            $"当前显示 {visibleCharacters.Length} / {_backendCharacters.Count} 个{itemName}，合计 {totalMods} 个 Mod · {enabledMods} 个已启用";
    }

    private IEnumerable<CharacterGridItemModel> FilterCharacters(
        IReadOnlyList<CharacterGridItemModel> characters)
    {
        if (!_filters.Any())
        {
            foreach (var characterGridItemModel in characters)
            {
                yield return characterGridItemModel;
            }
        }

        var modsFoundForFilter = new Dictionary<FilterType, IEnumerable<CharacterGridItemModel>>();


        foreach (var filter in _filters)
        {
            modsFoundForFilter.Add(filter.Key, filter.Value.Filter(characters));
        }


        IEnumerable<CharacterGridItemModel>? intersectedMods = null;

        foreach (var kvp in modsFoundForFilter)
        {
            intersectedMods = intersectedMods == null
                ? kvp.Value
                : intersectedMods.Intersect(kvp.Value);
        }


        if (intersectedMods is null)
            yield break;

        var results = intersectedMods.ToList();

        foreach (var characterGridItemModel in results)
        {
            yield return characterGridItemModel;
        }

        // 二开：Others 和漂泊者不参与属性筛选，选了属性也照样显示。
        // Others 是占位卡；漂泊者是主角，属性随时切换，被筛掉不合理。
        foreach (var exempt in characters.Where(IsElementFilterExempt))
        {
            if (!results.Contains(exempt))
                yield return exempt;
        }
    }

    /// <summary>
    ///     二开：不受属性筛选影响的角色。
    /// </summary>
    private static bool IsElementFilterExempt(CharacterGridItemModel item)
    {
        var internalName = item.Character.InternalName.Id;

        return internalName.Contains("Others", StringComparison.OrdinalIgnoreCase) ||
               internalName.Contains("Rover", StringComparison.OrdinalIgnoreCase) ||
               internalName.Contains("Traveler", StringComparison.OrdinalIgnoreCase) ||
               item.Character.DisplayName.Contains("漂泊者", StringComparison.Ordinal);
    }

    private ICategory _category = null!;

    /// <summary>
    ///     二开：回收站。展示清理记录，并提供打开系统回收站的入口。
    /// </summary>
    [RelayCommand]
    private async Task ShowRecycleBinAsync()
    {
        var history = CleanHistoryService.Instance.Load().OrderByDescending(entry => entry.DeletedAt).ToList();

        var panel = new StackPanel { Spacing = 6, MaxWidth = 560 };

        panel.Children.Add(new TextBlock
        {
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Text = "清理掉的文件都进了系统回收站。要还原某个 Mod，点下面按钮打开回收站，右键「还原」即可回到原来的位置。"
        });

        if (history.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                Opacity = 0.8,
                Text = "还没有清理过任何 Mod。"
            });
        }
        else
        {
            var totalMb = history.Sum(entry => entry.SizeBytes) / 1024.0 / 1024.0;

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                FontWeight = FontWeights.SemiBold,
                Text = $"共 {history.Count} 条记录 · 累计 {totalMb:F1} MB"
            });

            foreach (var entry in history.Take(15))
            {
                panel.Children.Add(new TextBlock
                {
                    FontSize = 12,
                    Opacity = 0.85,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Text = $"{entry.TimeText}    {entry.CharacterName} / {entry.ModName}    {entry.SizeText}"
                });
            }

            if (history.Count > 15)
            {
                panel.Children.Add(new TextBlock
                {
                    FontSize = 12,
                    Opacity = 0.6,
                    Text = $"…… 还有 {history.Count - 15} 条"
                });
            }
        }

        var dialog = new ContentDialog
        {
            Title = "回收站",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
            PrimaryButtonText = "打开系统回收站",
            SecondaryButtonText = history.Count > 0 ? "清空记录" : string.Empty,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        }
        else if (result == ContentDialogResult.Secondary)
        {
            CleanHistoryService.Instance.Clear();
            NotificationManager.ShowNotification("已清空记录", "回收站里的文件不受影响",
                TimeSpan.FromSeconds(4));
        }
    }

    private bool _hasModFixerUpdate;

    /// <summary>
    ///     二开：修复器有新版本（按钮上的红点）
    /// </summary>
    public bool HasModFixerUpdate
    {
        get => _hasModFixerUpdate;
        private set => SetProperty(ref _hasModFixerUpdate, value);
    }

    /// <summary>
    ///     二开：后台检查 Wuwa_Mod_Fixer 是否有新版本
    /// </summary>
    private async Task CheckModFixerUpdateAsync()
    {
        try
        {
            var release = await ModFixerService.Instance.GetLatestReleaseAsync();

            if (release is null)
                return;

            HasModFixerUpdate = !ModFixerService.Instance.IsDownloaded(release);
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Could not check mod fixer update");
        }
    }

    /// <summary>
    ///     二开：修复（原「刷新」按钮）。弹窗展示最新版修复器，可下载并直接运行。
    /// </summary>
    [RelayCommand]
    private async Task RepairModsAsync()
    {
        var service = ModFixerService.Instance;
        var release = service.Latest ?? await service.GetLatestReleaseAsync();

        if (release is null)
        {
            var fallback = new ContentDialog
            {
                Title = "无法检查更新",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "连不上 GitHub（可能需要代理）。可以先到项目主页手动下载 Wuwa_Mod_Fixer。"
                },
                PrimaryButtonText = "打开 GitHub 页面",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.MainWindow?.Content?.XamlRoot
            };

            if (await fallback.ShowAsync() == ContentDialogResult.Primary)
            {
                Process.Start(new ProcessStartInfo("https://github.com/Moonholder/Wuwa_Mod_Fixer/releases")
                {
                    UseShellExecute = true
                });
            }

            return;
        }

        var installed = service.IsDownloaded(release);

        var title = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Text = installed ? $"Mod 修复器 {release.Tag}" : $"发现新版本 {release.Tag}"
        };

        var meta = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.7,
            Text = $"{release.Tag}    {release.DateText}    {release.SizeText}"
        };

        var notes = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Text = ModFixerService.FormatNotes(release.Notes)
        };

        var status = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            Text = installed ? "这个版本已经下载过，可以直接运行。" : "点下面按钮下载并运行（下载到本地缓存，不会安装到系统）。"
        };

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Visibility = Visibility.Collapsed
        };

        var actionButton = new Button
        {
            Content = installed ? "运行修复器" : "下载并运行",
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var panel = new StackPanel { Spacing = 10, MaxWidth = 520 };
        panel.Children.Add(title);
        panel.Children.Add(meta);
        panel.Children.Add(new ScrollViewer { Content = notes, MaxHeight = 220 });
        panel.Children.Add(progressBar);
        panel.Children.Add(status);
        panel.Children.Add(actionButton);

        var dialog = new ContentDialog
        {
            Title = installed ? "Mod 修复器" : "发现新版本",
            Content = panel,
            PrimaryButtonText = "查看 GitHub",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        actionButton.Click += async (_, _) =>
        {
            actionButton.IsEnabled = false;

            try
            {
                if (!service.IsDownloaded(release))
                {
                    progressBar.Visibility = Visibility.Visible;
                    status.Text = "正在下载…";

                    var progress = new Progress<(double Percent, long Received, long Total)>(p =>
                    {
                        progressBar.Value = p.Percent;
                        status.Text =
                            $"正在下载 {p.Received / 1024.0 / 1024.0:F1} MB / {p.Total / 1024.0 / 1024.0:F1} MB （{p.Percent:F0}%）";
                    });

                    await service.DownloadAsync(release, progress);
                }

                HasModFixerUpdate = false;
                dialog.Hide();

                service.Launch(service.GetLocalExePath(release));

                NotificationManager.ShowNotification("已启动修复器", release.AssetName, TimeSpan.FromSeconds(4));
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to download mod fixer");
                status.Text = "下载失败：" + e.Message;
                actionButton.IsEnabled = true;
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Process.Start(new ProcessStartInfo("https://github.com/Moonholder/Wuwa_Mod_Fixer/releases")
            {
                UseShellExecute = true
            });
        }
    }

    /// <summary>
    ///     二开：清理同名残留（整理时被跳过的那些重复 Mod）
    /// </summary>
    [RelayCommand]
    private async Task CleanDuplicateModsAsync()
    {
        try
        {
            var modsPath = _skinManagerService.ActiveModsFolderPath;
            var categories = _gameService.GetCategories();
            var characters = _gameService.GetAllModdableObjects()
                .Concat(_gameService.GetAllModdableObjects(GetOnly.Disabled))
                .ToList();

            var duplicates = DuplicateModCleaner.ScanDuplicates(modsPath, categories, characters);

            _logger.Information("Duplicate mod scan: {Count} found | modsPath={ModsPath} | categories={Categories} | characters={Characters}",
                duplicates.Count, modsPath, categories.Count, characters.Count);

            if (duplicates.Count == 0)
            {
                NotificationManager.ShowNotification("没有重复的 Mod", "扫描完成，没有发现同名残留",
                    TimeSpan.FromSeconds(4));
                return;
            }

            var safe = duplicates.Where(pair => pair.Identical).ToList();
            var risky = duplicates.Where(pair => !pair.Identical).ToList();

            var totalMb = duplicates.Sum(pair => pair.SourceSize) / 1024.0 / 1024.0;

            var panel = new StackPanel { Spacing = 6, MaxWidth = 560 };

            panel.Children.Add(new TextBlock
            {
                Text = $"发现 {duplicates.Count} 处同名目录（旧目录总共占 {totalMb:F1} MB）",
                TextWrapping = TextWrapping.Wrap
            });

            if (safe.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"内容完全一致的 {safe.Count} 处，可以安全清理；删除后进回收站，随时能恢复。",
                    Foreground = new SolidColorBrush(Colors.ForestGreen),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (risky.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"内容有差异的 {risky.Count} 处，不会删除，需要你自己对比后再决定。",
                    Foreground = new SolidColorBrush(Colors.Orange),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            foreach (var pair in duplicates.Take(12))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{(pair.Identical ? "可清理" : "有差异")}   {pair.CharacterDisplayName} / " +
                           $"{Path.GetFileName(pair.SourcePath)}   {pair.SourceFileCount} 个文件 · {pair.SizeText}" +
                           (pair.Identical ? string.Empty : $"   ({pair.Note})"),
                    FontSize = 12,
                    Opacity = 0.85,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            if (duplicates.Count > 12)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"…… 还有 {duplicates.Count - 12} 处",
                    FontSize = 12,
                    Opacity = 0.6
                });
            }

            var dialog = new ContentDialog
            {
                Title = "清理同名残留",
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                PrimaryButtonText = safe.Count > 0 ? $"清理这 {safe.Count} 处" : "关闭",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow?.Content?.XamlRoot
            };

            foreach (var pair in duplicates)
            {
                _logger.Information("Duplicate candidate: identical={Identical} files {SourceCount}->{TargetCount} note={Note}\n  src={SourcePath}",
                    pair.Identical, pair.SourceFileCount, pair.TargetFileCount, pair.Note, pair.SourcePath);
            }

            // 没有内容完全一致的可清理项时，别静默返回，要告诉用户为什么
            if (safe.Count == 0)
            {
                NotificationManager.ShowNotification("没有可安全清理的重复项",
                    $"发现 {duplicates.Count} 处同名目录，但内容都有差异，已全部保留，需要你自己对比后再删除",
                    TimeSpan.FromSeconds(10));
                return;
            }

            var dialogResult = await dialog.ShowAsync();

            _logger.Information("Clean dialog result: {Result}", dialogResult);

            if (dialogResult != ContentDialogResult.Primary)
                return;

            var removed = 0;
            var freedBytes = 0L;
            var failures = new List<string>();
            var history = new List<CleanHistoryEntry>();

            foreach (var pair in safe)
            {
                try
                {
                    if (!Directory.Exists(pair.SourcePath))
                        continue;

                    // 删除一律进回收站，误判也能恢复
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(pair.SourcePath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                    freedBytes += pair.SourceSize;
                    removed++;

                    history.Add(new CleanHistoryEntry(DateTimeOffset.Now, pair.CharacterDisplayName,
                        Path.GetFileName(pair.SourcePath), pair.SourcePath, pair.SourceSize));
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(pair.SourcePath) + ": " + ex.Message);
                    _logger.Error(ex, "Could not clean duplicate mod {Path}", pair.SourcePath);
                }
            }

            if (history.Count > 0)
                CleanHistoryService.Instance.Append(history);

            await _skinManagerService.RefreshModsAsync();

            // 同刷新逻辑：清空后全量重建，否则旧卡片不会被替换
            Characters.Clear();
            await OnNavigatedToCoreAsync(_category);

            var message = $"已清理 {removed} 处重复 Mod，回收 {freedBytes / 1024.0 / 1024.0:F1} MB";

            if (failures.Count > 0)
                message += $"，{failures.Count} 处失败";

            if (risky.Count > 0)
                message += $"，{risky.Count} 处有差异已保留";

            NotificationManager.ShowNotification("清理完成", message, TimeSpan.FromSeconds(8));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to clean duplicate mods");
            NotificationManager.ShowNotification("清理失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：刷新角色列表（对齐 QAQM）
    ///     刷新：重新读取目录，并整理可识别的旧分类目录
    ///     刷新并补充：整理后再补齐缺失的角色文件夹
    /// </summary>
    [RelayCommand]
    private async Task RefreshCharactersAsync()
    {
        var confirm = new ContentDialog
        {
            Title = "刷新角色列表",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "刷新：重新读取当前目录，并自动整理可识别的旧管理器结构或错误角色分类名。\n\n" +
                       "刷新并补充：整理后再补充缺失的默认角色文件夹。只会新增或移动可识别的真实 Mod 文件夹；" +
                       "空旧目录会跳过，不会覆盖同名 Mod。"
            },
            PrimaryButtonText = "刷新并补充",
            SecondaryButtonText = "刷新",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        var choice = await confirm.ShowAsync();
        if (choice == ContentDialogResult.None)
            return;

        var bootstrap = choice == ContentDialogResult.Primary;

        try
        {
            var modsPath = _skinManagerService.ActiveModsFolderPath;
            var categories = _gameService.GetCategories();
            // 启用的 + 禁用的都要参与匹配，否则会漏掉停用的角色
            var characters = _gameService.GetAllModdableObjects()
                .Concat(_gameService.GetAllModdableObjects(GetOnly.Disabled))
                .ToList();

            var scan = LegacyCharacterOrganizer.Scan(modsPath, categories, characters);

            if (scan.HasCandidates)
            {
                var preview = BuildLegacyPreviewDialog(scan);

                if (await preview.ShowAsync() != ContentDialogResult.Primary)
                    return;

                var result = LegacyCharacterOrganizer.Migrate(scan);

                var message = result.MovedMods > 0
                    ? $"已整理 {result.MovedMods} 个 Mod"
                    : "没有需要移动的 Mod";

                if (result.SkippedConflicts.Count > 0)
                    message += $"，跳过 {result.SkippedConflicts.Count} 个同名目录";

                if (result.RemovedEmptyFolders.Count > 0)
                    message += $"，清理 {result.RemovedEmptyFolders.Count} 个空目录";

                _logger.Information("Legacy character organize: {Message}", message);
                NotificationManager.ShowNotification("角色目录整理完成", message, TimeSpan.FromSeconds(6));
            }

            await _skinManagerService.RefreshModsAsync();

            if (bootstrap)
            {
                var created = LegacyCharacterOrganizer.EnsureCharacterFolders(modsPath, categories, characters);

                if (created > 0)
                {
                    _logger.Information("Created {Count} missing character folders", created);
                    NotificationManager.ShowNotification("已补充角色文件夹",
                        $"新增 {created} 个缺失的角色目录", TimeSpan.FromSeconds(5));
                }
            }

            // 二开：整理后 Mod 数量和归属都变了，但角色还是同一批对象实例。
            // ResetContent 用 Except 做增量比对，Character 值相等就认为"没变化"，
            // 旧卡片（旧的 Mod 数）会被保留 → 表现就是列表不刷新。
            // 先清空集合，强制它走全量重建分支。
            Characters.Clear();
            await OnNavigatedToCoreAsync(_category);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to refresh character list");
            NotificationManager.ShowNotification("刷新失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：整理前的预览弹窗，列出会把哪些 Mod 搬到哪个角色
    /// </summary>
    private ContentDialog BuildLegacyPreviewDialog(LegacyCharacterScan scan)
    {
        var panel = new StackPanel { Spacing = 6, MaxWidth = 520 };

        panel.Children.Add(new TextBlock
        {
            Text = $"检测到 {scan.FolderCount} 个旧分类目录，里面有 {scan.ModCount} 个真实 Mod 文件夹可以整理。",
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = "只会移动分类目录里的真实 Mod 文件夹；空的旧目录会被清理，不会删除 Mod 内容，也不会覆盖同名目录。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7
        });

        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Text = "准备整理到这些角色：" + string.Join("、", scan.TargetCharacters)
        });

        var skipCount = scan.Moves.Count(move => move.TargetExists);

        if (skipCount > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"其中 {skipCount} 个目标位置已经有同名目录，会被跳过（不会覆盖）",
                FontSize = 12,
                Opacity = 0.75,
                Foreground = new SolidColorBrush(Colors.Orange)
            });
        }

        foreach (var move in scan.Moves.Take(8))
        {
            // 二开：两边都做成可点的链接，直接跳到对应文件夹，省得自己去翻目录找
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, 1, 0, 1)
            };

            line.Children.Add(CreateFolderLink($"{move.SourceFolderName}\\{move.ModName}", move.SourceModPath));

            line.Children.Add(new TextBlock
            {
                Text = "→",
                FontSize = 12,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center
            });

            line.Children.Add(CreateFolderLink(
                $"{move.TargetCategoryName}\\{move.CharacterInternalName}",
                move.TargetModPath,
                "目标位置还没有这个目录"));

            if (move.TargetExists)
            {
                line.Children.Add(new TextBlock
                {
                    Text = "[已存在，跳过]",
                    Margin = new Thickness(4, 0, 0, 0),
                    FontSize = 12,
                    Opacity = 0.75,
                    Foreground = new SolidColorBrush(Colors.Orange),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            panel.Children.Add(line);
        }

        if (scan.Moves.Count > 8)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"…… 还有 {scan.Moves.Count - 8} 个",
                FontSize = 12,
                Opacity = 0.6
            });
        }

        return new ContentDialog
        {
            Title = "发现还没归类好的 Mod",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
            PrimaryButtonText = "开始整理",
            CloseButtonText = "先不处理",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };
    }

    /// <summary>
    ///     二开：弹窗里的文件夹链接。点一下用资源管理器打开；路径不存在就置灰。
    /// </summary>
    private static HyperlinkButton CreateFolderLink(string text, string path, string? disabledHint = null)
    {
        var exists = Directory.Exists(path);

        var link = new HyperlinkButton
        {
            Content = text,
            Padding = new Thickness(0, 1, 0, 1),
            FontSize = 12,
            IsEnabled = exists,
            Tag = path
        };

        ToolTipService.SetToolTip(link, exists ? $"打开：{path}" : disabledHint ?? $"路径不存在：{path}");

        link.Click += (_, _) => OpenInExplorer(path);

        return link;
    }

    /// <summary>
    ///     二开：用资源管理器打开目录。目录不存在时退到它的上级目录。
    /// </summary>
    private static void OpenInExplorer(string path)
    {
        try
        {
            var target = path;

            if (!Directory.Exists(target) && File.Exists(target))
                target = Path.GetDirectoryName(target) ?? target;

            if (!Directory.Exists(target))
                target = Path.GetDirectoryName(target) ?? target;

            if (!Directory.Exists(target))
                return;

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 打不开就算了，别影响对话框
        }
    }

    /// <summary>
    ///     二开：为自定义角色导入头像
    /// </summary>
    [RelayCommand]
    private async Task SetCharacterImageAsync(CharacterGridItemModel? characterModel)
    {
        if (characterModel is null || characterModel.IsAddCard)
            return;

        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };

            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
                picker.FileTypeFilter.Add(extension);

            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            await _gameService.EditCustomCharacterAsync(characterModel.Character.InternalName,
                new EditCustomCharacterRequest { Image = NewValue<Uri?>.Set(new Uri(file.Path)) });

            // 二开：先置空再赋值。
            // 绑定改成 OneWay 之后只有「值发生变化」才会重新加载图片，
            // 如果导入的是同一个文件（Uri 一模一样），直接赋值界面不会有任何反应，
            // 看起来就像自动刷新没了。
            var newImageUri = new Uri(file.Path);

            if (characterModel.ImageUri is not null && characterModel.ImageUri == newImageUri)
            {
                characterModel.ImageUri = ModModel.PlaceholderImagePath;
                await Task.Delay(60);
            }

            characterModel.ImageUri = newImageUri;

            NotificationManager.ShowNotification("已更新角色头像", characterModel.Character.DisplayName,
                TimeSpan.FromSeconds(4));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not set character image for {Character}", characterModel.Character.DisplayName);

            NotificationManager.ShowNotification("设置头像失败",
                characterModel.Character.IsCustomModObject ? e.Message : "只有自定义角色可以修改头像",
                TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：被手动移除的内置角色（内部名列表）
    /// </summary>
    private const string HiddenCharactersKey = "HiddenCharacters";

    private Task<List<string>> ReadHiddenCharactersAsync() =>
        _localSettingsService.ReadOrCreateSettingAsync<List<string>>(HiddenCharactersKey);

    /// <summary>
    ///     二开：删除角色。自定义角色真删；游戏内置角色删不掉（数据来自游戏），改成从列表隐藏。
    /// </summary>
    [RelayCommand]
    private async Task DeleteCharacterAsync(CharacterGridItemModel? characterModel)
    {
        if (characterModel is null || characterModel.IsAddCard)
            return;

        var isCustom = characterModel.Character.IsCustomModObject;

        // 二开：游戏内置角色来自游戏数据，没法从游戏里删掉。
        // 改成「隐藏」——从 OMniX 列表里移除，重启也不会再出现。
        var dialog = new ContentDialog
        {
            Title = "删除角色",
            Content = isCustom
                ? $"确定删除「{characterModel.Character.DisplayName}」吗？\n" +
                  "该角色的配置会被移除，Mod 文件夹会保留。"
                : $"确定把「{characterModel.Character.DisplayName}」从列表里移除吗？\n" +
                  "这是游戏内置角色，无法从游戏本身删除，只会从 OMniX 的列表里隐藏。",
            PrimaryButtonText = isCustom ? "删除" : "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            var internalName = characterModel.Character.InternalName.ToString();

            if (isCustom)
            {
                await _gameService.DeleteCustomCharacterAsync(characterModel.Character.InternalName);
            }
            else
            {
                // 二开：内置角色记入隐藏列表，列表加载时过滤掉
                var hidden = await ReadHiddenCharactersAsync();

                if (!hidden.Contains(internalName))
                    hidden.Add(internalName);

                await _localSettingsService.SaveSettingAsync(HiddenCharactersKey, hidden);
            }

            await _skinManagerService.RefreshModsAsync();

            // 重新加载角色列表（OnNavigatedTo 会按当前排序重建 Characters）
            OnNavigatedTo(_category);

            NotificationManager.ShowNotification(
                isCustom ? "已删除角色" : "已移除角色",
                isCustom
                    ? characterModel.Character.DisplayName
                    : $"{characterModel.Character.DisplayName} 已从列表隐藏",
                TimeSpan.FromSeconds(4));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not delete character {Character}", characterModel.Character.DisplayName);
            NotificationManager.ShowNotification("删除失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：快速新增角色，只填名字（稀有度默认 4 星，其余留空）
    /// </summary>
    /// <summary>
    ///     二开：新建角色的默认头像（OMniX 头像）。
    /// </summary>
    private static Uri? ResolveDefaultAvatarUri()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultCharacterAvatar.png");

        return File.Exists(path) ? new Uri(path) : null;
    }

    public async Task<bool> CreateCharacterAsync(string displayName, string? element = null)
    {
        displayName = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(displayName))
            return false;

        try
        {
            var request = new CreateCharacterRequest
            {
                InternalName = new InternalName(displayName),
                DisplayName = displayName,
                Rarity = 4,
                ModFilesName = displayName.ToLowerInvariant(),
                Element = element,
                // 二开：新建角色默认套上 OMniX 的头像
                Image = ResolveDefaultAvatarUri()
            };

            // 二开：不能包 Task.Run。CreateCharacterAsync 内部会更新角色集合和事件，
            // 在线程池线程跑会让界面绑定跨线程，直接闪退（0xc000027b）
            var createdCharacter = await _gameService.CreateCharacterAsync(request);

            // 二开：记下来，重建列表时用它把新角色置顶并打「新」角标
            _newlyCreatedCharacters[createdCharacter.InternalName.ToString()] = DateTime.UtcNow;

            await _skinManagerService.RefreshModsAsync();

            // 重新走一遍导航逻辑，列表会按当前排序重建
            OnNavigatedTo(_category);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not create character {CharacterName}", displayName);
            NotificationManager.ShowNotification("新增角色失败", e.Message, TimeSpan.FromSeconds(8));
            return false;
        }
    }

    public async void OnNavigatedTo(object parameter)
    {
        // 二开：async void 里抛出的异常会直接终止进程，
        // 新增角色后这里会重新跑一遍，必须兜住（之前“填完名字就闪退”就是这个原因）
        try
        {
            await OnNavigatedToCoreAsync(parameter);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to load characters page");
            NotificationManager.ShowNotification("加载角色列表失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    private async Task OnNavigatedToCoreAsync(object parameter)
    {
        if (parameter is not ICategory category)
        {
            _logger.Error("Invalid parameter type {ParameterType}", parameter?.GetType().FullName);
            category = _gameService.GetCategories().First();
        }

        // 二开：这里会被重复调用（新增角色后刷新），先退订避免处理器叠加
        _busyService.BusyChanged -= OnBusyChangedHandler;
        _busyService.BusyChanged += OnBusyChangedHandler;

        _category = category;
        CategoryPageTitle = category.ModCategory switch
        {
            ModCategory.Character => "角色列表",
            ModCategory.NPC => "NPC 列表",
            ModCategory.Object => "物件列表",
            ModCategory.Weapons => "武器列表",
            _ => $"{category.DisplayNamePlural}"
        };
        ModToggleText = $"只显示有 Mod 的{category.DisplayNamePlural}";
        ModEnabledToggleText = $"Show only {category.DisplayNamePlural} with Enabled Mods";
        ModNotificationsToggleText = $"Show only {category.DisplayNamePlural} with Mod Notifications";
        SearchBoxPlaceHolder = $"Search {category.DisplayNamePlural}...";


        var characters = _gameService.GetModdableObjects(_category);

        var firstType = characters.FirstOrDefault()?.GetType();
        if (firstType is not null && characters.Any(ch => ch.GetType() != firstType))
        {
            // 二开：新建的自定义角色和内置角色类型可能不同，
            // 原来这里直接抛异常 → async void 里没人接 → 进程闪退。
            // 现在只提示并跳过类型不一致的条目，页面照常显示。
            var mixedCount = characters.Count(ch => ch.GetType() != firstType);
            _logger.Information("Skipped {Count} characters with a different type in {Category}",
                mixedCount, category.InternalName);

            characters = characters.Where(ch => ch.GetType() == firstType).ToList();
        }

        var others =
            characters.FirstOrDefault(ch =>
                ch.InternalName.Id.Contains("Others", StringComparison.OrdinalIgnoreCase));
        if (others is not null) // Add to front
        {
            characters.Remove(others);
            characters.Insert(0, others);
        }

        // 二开：Gliders / Weapons 两个伪角色已经从游戏服务里移除，这里不用再挪到末尾


        _characters = characters;

        characters = new List<IModdableObject>(_characters);

        var pinnedCharactersOptions = await ReadCharacterSettings();

        var backendCharacters = new List<CharacterGridItemModel>();
        foreach (var pinedCharacterId in pinnedCharactersOptions.PinedCharacters)
        {
            var character = characters.FirstOrDefault(x => x.InternalNameEquals(pinedCharacterId));
            if (character is not null)
            {
                backendCharacters.Add(new CharacterGridItemModel(character) { IsPinned = true });
                characters.Remove(character);
            }
        }

        foreach (var hiddenCharacterId in pinnedCharactersOptions.HiddenCharacters)
        {
            var character = characters.FirstOrDefault(x => x.InternalNameEquals(hiddenCharacterId));
            if (character is not null)
            {
                backendCharacters.Add(new CharacterGridItemModel(character) { IsHidden = true });
                characters.Remove(character);
            }
        }

        // 二开：过滤掉用户手动移除的内置角色。
        // 这些角色来自游戏数据，删不掉，只能从列表里隐藏（设置项里存着内部名）。
        var hiddenCharacterNames = await ReadHiddenCharactersAsync();

        // Add rest of characters
        foreach (var character in characters)
        {
            if (hiddenCharacterNames.Contains(character.InternalName.ToString()))
                continue;

            backendCharacters.Add(new CharacterGridItemModel(character));
        }

        _backendCharacters = backendCharacters;

        // 二开：给刚新建的角色打标记。列表置顶和卡片上的「新」角标都读这个
        CleanupExpiredNewCharacters();

        foreach (var characterGridItemModel in _backendCharacters)
        {
            var internalName = characterGridItemModel.Character.InternalName.ToString();
            characterGridItemModel.IsNew = _newlyCreatedCharacters.ContainsKey(internalName);
        }

        foreach (var characterGridItemModel in _backendCharacters)
        {
            // 二开：角色库里的角色，磁盘上不一定有对应 Mod 文件夹
            // （刚新建的角色、刚被整理走的旧目录、或者手动删过目录）。
            // 这时 _characterModLists 里根本没有它，GetCharacterModList 会直接抛
            // "Sequence contains no matching element"，导致整个角色页加载失败。
            // 拿不到就当作「没有 Mod」，而不是让整页挂掉。
            var modList = _skinManagerService.GetCharacterModListOrDefault(
                characterGridItemModel.Character.InternalName.ToString());

            var characterModItems = new List<CharacterModItem>();

            if (modList is not null)
            {
                foreach (var skinModEntry in modList.Mods)
                {
                    var modSettings = await skinModEntry.Mod.Settings.TryReadSettingsAsync(true);

                    characterModItems.Add(new CharacterModItem(skinModEntry.Mod.GetDisplayName(),
                        skinModEntry.IsEnabled, modSettings?.DateAdded ?? default));
                }
            }

            characterGridItemModel.SetMods(characterModItems);
        }


        // 二开：读回「常用」排序的记录
        await LoadRecentAccessTimesAsync();

        InitializeSorters();

        if (typeof(ICharacter).IsAssignableFrom(firstType))
        {
            var backendCharactersList = _backendCharacters.Select(x => x.Character).Cast<ICharacter>().ToList();
            var distinctReleaseDates = backendCharactersList
                .Where(ch => ch.ReleaseDate != default)
                .DistinctBy(ch => ch.ReleaseDate)
                .Count();

            if (distinctReleaseDates == 1 &&
                SortingMethods.FirstOrDefault(x => x.SortingMethodType == GridItemSorter.ReleaseDateSortName) is
                { } releaseDateSortingMethod)
            {
                SortingMethods.Remove(releaseDateSortingMethod);
            }

            DockPanelVM.Initialize();
            DockPanelVM.FilterElementSelected += FilterElementSelected;
        }

        await RefreshModPresetsAsync();

        // Add notifications
        await RefreshNotificationsAsync();

        // Character Ids where more than 1 skin is enabled
        await RefreshMultipleModsWarningAsync();

        // ShowOnlyModsCharacters
        var settings =
            await _localSettingsService
                .ReadOrCreateSettingAsync<CharacterOverviewSettings>(CharacterOverviewSettings.GetKey(_category));
        if (settings.ShowOnlyCharactersWithMods)
        {
            ShowOnlyCharactersWithMods = true;
            _filters[FilterType.HasMods] = new GridFilter(characterGridItem =>
                _skinManagerService.GetCharacterModList(characterGridItem.Character).Mods.Any());
        }

        if (settings.ShowOnlyModsWithNotifications)
        {
            ShowOnlyModsWithNotifications = true;
            _filters[FilterType.HasModNotifications] =
                new GridFilter(characterGridItemModel => characterGridItemModel.Notification);
        }

        if (settings.ShowOnlyCharactersWithEnabledMods)
        {
            ShowOnlyWithEnabledMods = true;
            _filters[FilterType.HasEnabledMods] = new GridFilter(characterGridItemModel => characterGridItemModel.Mods.Any(m => m.IsEnabled));
        }


        SortByDescending = settings.SortByDescending;

        var sorter = SortingMethods.FirstOrDefault(x => x.SortingMethodType == settings.SortingMethod);

        SelectedSortingMethod = sorter ?? SortingMethods.First();


        _isNavigating = false;
        ResetContent();

        // 二开：后台检查修复器有没有新版，有就让按钮冒红点（不阻塞页面）
        _ = CheckModFixerUpdateAsync();

        var lastPageType = _navigationService.GetNavigationHistory().SkipLast(1).LastOrDefault();
        if (lastPageType?.PageType == typeof(CharacterDetailsPage) ||
            lastPageType?.PageType == typeof(CharacterDetailsViewModel))
        {
            InternalName? internalName = null;

            if (lastPageType.Parameter is CharacterGridItemModel characterGridModel)
            {
                internalName = characterGridModel.Character.InternalName;
            }
            else if (lastPageType.Parameter is INameable modObject)
            {
                internalName = modObject.InternalName;
            }
            else if (lastPageType.Parameter is string modObjectString)

            {
                internalName = new InternalName(modObjectString);
            }

            if (internalName is null)
                return;

            var characterGridItemModel = FindCharacterByInternalName(internalName);
            if (characterGridItemModel is not null)
            {
                OnScrollToCharacter?.Invoke(this, new ScrollToCharacterArgs(characterGridItemModel));
            }
        }
    }

    private async Task RefreshMultipleModsWarningAsync()
    {
        var charactersWithMultipleMods = _skinManagerService.CharacterModLists
            .Where(x => x.Mods.Count(mod => mod.IsEnabled) > 1);

        var charactersWithMultipleActiveSkins = new HashSet<InternalName>();
        await Task.Run(async () =>
        {
            foreach (var modList in charactersWithMultipleMods)
            {
                if (_gameService.IsMultiMod(modList.Character))
                    continue;

                if (modList.Character is ICharacter { Skins.Count: > 1 } character)
                {
                    var addWarning = false;
                    var subSkinsFound = new List<ICharacterSkin>();
                    foreach (var characterSkinEntry in modList.Mods)
                    {
                        if (!characterSkinEntry.IsEnabled) continue;

                        var subSkin = _modCrawlerService.GetFirstSubSkinRecursive(characterSkinEntry.Mod.FullPath);
                        var modSettingsResult = await _modSettingsService.GetSettingsAsync(characterSkinEntry.Id);


                        var mod = ModModel.FromMod(characterSkinEntry);


                        if (modSettingsResult.IsT0)
                            mod.WithModSettings(modSettingsResult.AsT0);

                        if (!mod.CharacterSkinOverride.IsNullOrEmpty())
                            subSkin = _gameService.GetCharacterByIdentifier(character.InternalName)?.Skins
                                .FirstOrDefault(x => SkinVM.FromSkin(x).InternalNameEquals(mod.CharacterSkinOverride));

                        if (subSkin is null)
                            continue;


                        if (subSkinsFound.All(foundSubSkin =>
                                !subSkin.InternalNameEquals(foundSubSkin)))
                        {
                            subSkinsFound.Add(subSkin);
                            continue;
                        }


                        addWarning = true;
                        break;
                    }

                    if (addWarning || subSkinsFound.Count > 1 && character.Skins.Count == 1)
                        charactersWithMultipleActiveSkins.Add(modList.Character.InternalName);
                }
                else if (modList.Mods.Count(modEntry => modEntry.IsEnabled) >= 2)
                {
                    charactersWithMultipleActiveSkins.Add(modList.Character.InternalName);
                }
            }
        });


        foreach (var characterGridItemModel in _backendCharacters)
        {
            if (charactersWithMultipleActiveSkins.Contains(characterGridItemModel.Character.InternalName))
            {
                if (_gameService.IsMultiMod(characterGridItemModel.Character))
                    continue;

                characterGridItemModel.Warning = true;
            }
            else
            {
                characterGridItemModel.Warning = false;
            }
        }
    }

    private async Task RefreshNotificationsAsync()
    {
        foreach (var character in _characters)
        {
            var characterGridItemModel = FindCharacterByInternalName(character.InternalName);
            if (characterGridItemModel is null) continue;

            var characterMods = _skinManagerService.GetCharacterModList(character).Mods;

            var notifications = new List<ModNotification>();
            foreach (var characterSkinEntry in characterMods)
            {
                var modNotification = await _modNotificationManager.GetNotificationsForModAsync(characterSkinEntry.Id);
                notifications.AddRange(modNotification);
            }

            if (!notifications.Any())
            {
                characterGridItemModel.Notification = false;
                characterGridItemModel.NotificationType = AttentionType.None;
            }

            foreach (var modNotification in notifications)
            {
                if (modNotification.AttentionType == AttentionType.Added ||
                    modNotification.AttentionType == AttentionType.UpdateAvailable)
                {
                    characterGridItemModel.Notification = true;
                    characterGridItemModel.NotificationType = modNotification.AttentionType;
                }
            }
        }
    }

    public void OnNavigatedFrom()
    {
        _busyService.BusyChanged -= OnBusyChangedHandler;
    }

    [RelayCommand]
    private void OpenCharacterManager()
    {
        _navigationService.NavigateTo(typeof(CharacterManagerViewModel).FullName!);
    }

    [RelayCommand]
    private async Task CharacterClicked(CharacterGridItemModel characterModel)
    {
        // 二开：点的是“新增角色”占位卡
        if (characterModel.IsAddCard)
        {
            await PromptCreateCharacterAsync();
            return;
        }

        _navigationService.SetListDataItemForNextConnectedAnimation(characterModel);

        // 二开：记住最近打开，供「常用」排序用
        _ = RecordCharacterOpenedAsync(characterModel.Character.InternalName.ToString());

        _navigationService.NavigateToCharacterDetails(characterModel.Character.InternalName);
    }

    /// <summary>
    ///     二开：「常用」排序的数据来源。
    /// </summary>
    private const string RecentlyOpenedKey = "RecentlyOpenedCharacters";

    /// <summary>
    ///     二开：记录角色最近打开时间。
    /// </summary>
    private async Task RecordCharacterOpenedAsync(string internalName)
    {
        try
        {
            GridItemSorter.RecentAccessTimes[internalName] = DateTime.UtcNow;

            await _localSettingsService.SaveSettingAsync(RecentlyOpenedKey,
                new Dictionary<string, DateTime>(GridItemSorter.RecentAccessTimes));
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Could not record recently opened character {Character}", internalName);
        }
    }

    /// <summary>
    ///     二开：把「常用」排序的记录读回内存。
    /// </summary>
    private async Task LoadRecentAccessTimesAsync()
    {
        try
        {
            var saved = await _localSettingsService
                .ReadOrCreateSettingAsync<Dictionary<string, DateTime>>(RecentlyOpenedKey);

            GridItemSorter.RecentAccessTimes.Clear();

            foreach (var (key, value) in saved)
                GridItemSorter.RecentAccessTimes[key] = value;
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Could not load recently opened characters");
        }
    }

    /// <summary>
    ///     二开：弹出输入框，快速新增一个角色
    /// </summary>
    /// <summary>
    ///     二开：下拉里「不指定」这一项的文案。选它（或者不改）就归到「其他」。
    /// </summary>
    private const string ElementUnspecifiedText = "（不指定 · 默认其他）";

    private const string ElementOtherText = "其他";

    private async Task PromptCreateCharacterAsync()
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "输入角色名字…",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // 二开：所属属性。游戏里有哪些属性就列哪些，另外补一个「其他」，
        // 给非角色（武器、风之翼、Others）或者暂时定不了属性的角色用。
        var elementBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        elementBox.Items.Add(new ElementChoice(ElementUnspecifiedText, null));

        foreach (var element in _gameService.GetElements().Where(e => !e.InternalNameEquals("None")))
            elementBox.Items.Add(new ElementChoice(element.DisplayName, element.InternalName.ToString()));

        elementBox.Items.Add(new ElementChoice(ElementOtherText,
            OMniX.WinUI.ViewModels.SubVms.OverviewDockPanelVM.OtherElementInternalName));

        elementBox.SelectedIndex = 0;

        var panel = new StackPanel
        {
            Width = 340,
            Spacing = 6
        };

        panel.Children.Add(NewFieldLabel("角色名字"));
        panel.Children.Add(nameBox);
        panel.Children.Add(NewFieldLabel("所属属性"));
        panel.Children.Add(elementBox);
        panel.Children.Add(new TextBlock
        {
            Text = "不选择的话，会默认归到「其他」。",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.WrapWholeWords
        });

        var dialog = new ContentDialog
        {
            Title = "新增角色",
            Content = panel,
            PrimaryButtonText = "确认创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var choice = elementBox.SelectedItem as ElementChoice;
        var elementName = choice?.InternalName;

        // 二开：没指定就等于「其他」
        var usedFallback = string.IsNullOrEmpty(elementName);

        if (usedFallback)
            elementName = OMniX.WinUI.ViewModels.SubVms.OverviewDockPanelVM.OtherElementInternalName;

        if (!await CreateCharacterAsync(nameBox.Text, elementName))
            return;

        if (usedFallback)
            NotificationManager.ShowNotification("未指定属性",
                $"已创建「{nameBox.Text}」，默认归类为「其他」。", TimeSpan.FromSeconds(6));
    }

    /// <summary>
    ///     二开：属性下拉里的一项。InternalName 为 null 表示「不指定」。
    /// </summary>
    private sealed class ElementChoice
    {
        public ElementChoice(string displayName, string? internalName)
        {
            DisplayName = displayName;
            InternalName = internalName;
        }

        public string DisplayName { get; }
        public string? InternalName { get; }

        public override string ToString() => DisplayName;
    }

    private static TextBlock NewFieldLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            Opacity = 0.75
        };

    [ObservableProperty] private bool _showOnlyCharactersWithMods = false;

    [RelayCommand]
    private async Task ShowCharactersWithModsAsync()
    {
        if (ShowOnlyCharactersWithMods)
        {
            ShowOnlyCharactersWithMods = false;

            _filters.Remove(FilterType.HasMods);

            ResetContent();
            var settingss = await ReadCharacterSettings();


            settingss.ShowOnlyCharactersWithMods = ShowOnlyCharactersWithMods;

            await SaveCharacterSettings(settingss);

            return;
        }

        _filters[FilterType.HasMods] = new GridFilter(characterGridItem =>
            _skinManagerService.GetCharacterModList(characterGridItem.Character.InternalName).Mods.Any());

        ShowOnlyCharactersWithMods = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        settings.ShowOnlyCharactersWithMods = ShowOnlyCharactersWithMods;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }

    [ObservableProperty] private bool _showOnlyModsWithNotifications;

    [RelayCommand]
    private async Task ShowOnlyCharactersWithModNotificationsAsync()
    {
        if (ShowOnlyModsWithNotifications)
        {
            ShowOnlyModsWithNotifications = false;

            _filters.Remove(FilterType.HasModNotifications);

            ResetContent();
            var settingss = await ReadCharacterSettings();


            settingss.ShowOnlyModsWithNotifications = ShowOnlyModsWithNotifications;

            await SaveCharacterSettings(settingss);

            return;
        }

        _filters[FilterType.HasModNotifications] = new GridFilter(characterGridItem =>
            characterGridItem.Notification);

        ShowOnlyModsWithNotifications = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        settings.ShowOnlyModsWithNotifications = ShowOnlyModsWithNotifications;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }

    [ObservableProperty] private bool _showOnlyWithEnabledMods;

    [RelayCommand]
    private async Task ShowOnlyCharactersWithEnabledMods()
    {
        if (ShowOnlyWithEnabledMods)
        {
            ShowOnlyWithEnabledMods = false;

            _filters.Remove(FilterType.HasEnabledMods);

            ResetContent();

            var settingss = await ReadCharacterSettings();

            settingss.ShowOnlyCharactersWithEnabledMods = ShowOnlyWithEnabledMods;

            await SaveCharacterSettings(settingss);

            return;
        }

        _filters[FilterType.HasEnabledMods] = new GridFilter(characterGridItem => characterGridItem.Mods.Any(m => m.IsEnabled));

        ShowOnlyWithEnabledMods = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        settings.ShowOnlyCharactersWithEnabledMods = ShowOnlyWithEnabledMods;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    [ObservableProperty] private string _pinText = DefaultPinText;

    [ObservableProperty] private string _pinGlyph = DefaultPinGlyph;

    const string DefaultPinGlyph = "\uE718";
    const string DefaultPinText = "Pin To Top";
    const string DefaultUnpinGlyph = "\uE77A";
    const string DefaultUnpinText = "Unpin Character";

    /// <summary>
    ///     二开：右键菜单当前作用的角色。菜单项只传属性名，角色从这里取。
    /// </summary>
    private CharacterGridItemModel? _rightClickedCharacter;

    public void OnRightClickContext(CharacterGridItemModel clickedCharacter)
    {
        _rightClickedCharacter = clickedCharacter;

        ClearNotificationsCommand.NotifyCanExecuteChanged();
        DisableCharacterModsCommand.NotifyCanExecuteChanged();
        if (clickedCharacter.IsPinned)
        {
            PinText = DefaultUnpinText;
            PinGlyph = DefaultUnpinGlyph;
        }
        else
        {
            PinText = DefaultPinText;
            PinGlyph = DefaultPinGlyph;
        }
    }

    /// <summary>
    ///     二开：右键菜单「所属属性」用到的图标。
    /// </summary>
    public ImageSource? GetElementIcon(string internalName)
    {
        var path = OMniX.WinUI.ViewModels.SubVms.OverviewDockPanelVM.ResolveElementIcon(internalName);

        if (path is null || !File.Exists(path))
            return null;

        return new BitmapImage(new Uri(path));
    }

    /// <summary>
    ///     二开：重命名角色。自定义角色改角色配置，内置角色写本地 override（只影响 OMniX 内显示）。
    /// </summary>
    [RelayCommand]
    private async Task RenameCharacterAsync()
    {
        var characterModel = _rightClickedCharacter;

        if (characterModel is null || characterModel.Character is not ICharacter character)
            return;

        var oldName = character.DisplayName;

        var nameBox = new TextBox
        {
            Text = oldName,
            PlaceholderText = "输入新的名字…",
            MinWidth = 300
        };

        var dialog = new ContentDialog
        {
            Title = "重命名角色",
            Content = nameBox,
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var newName = nameBox.Text?.Trim();

        if (string.IsNullOrEmpty(newName) || newName == oldName)
            return;

        try
        {
            if (character.IsCustomModObject)
            {
                await _gameService.EditCustomCharacterAsync(character.InternalName,
                    new EditCustomCharacterRequest
                    {
                        DisplayName = NewValue<string>.Set(newName)
                    });
            }
            else
            {
                await _gameService.SetCharacterOverrideAsync(character,
                    new OverrideCharacterRequest
                    {
                        DisplayName = NewValue<string>.Set(newName)
                    });
            }

            await _skinManagerService.RefreshModsAsync();

            OnNavigatedTo(_category);

            NotificationManager.ShowNotification("已重命名", $"{oldName} → {newName}", TimeSpan.FromSeconds(4));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not rename character {Character}", oldName);
            NotificationManager.ShowNotification("重命名失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：设置角色的所属属性。传「Other」表示其他（无属性）。
    ///     内置角色的属性来自游戏数据，只能改自定义角色。
    /// </summary>
    [RelayCommand]
    private async Task SetCharacterElementAsync(string? elementName)
    {
        var characterModel = _rightClickedCharacter;

        if (characterModel is null)
            return;

        if (characterModel.Character is not ICharacter character)
            return;

        var isOther = elementName.IsNullOrEmpty() ||
                      elementName.Equals("Other", StringComparison.OrdinalIgnoreCase);

        var elementValue = isOther ? string.Empty : elementName!;

        try
        {
            if (character.IsCustomModObject)
            {
                // 自定义角色：直接写进角色配置
                await _gameService.EditCustomCharacterAsync(character.InternalName,
                    new EditCustomCharacterRequest
                    {
                        Element = NewValue<string>.Set(elementValue)
                    });
            }
            else
            {
                // 内置角色 / 非角色：写进本地 override，只影响 OMniX 内的显示和筛选，不动游戏数据
                await _gameService.SetCharacterOverrideAsync(character,
                    new OverrideCharacterRequest
                    {
                        Element = NewValue<string>.Set(elementValue)
                    });
            }

            await _skinManagerService.RefreshModsAsync();

            OnNavigatedTo(_category);

            var label = isOther ? "其他" : character.Element.DisplayName;

            NotificationManager.ShowNotification("已更新属性", $"{character.DisplayName} → {label}",
                TimeSpan.FromSeconds(4));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not set element for character {Character}", character.DisplayName);
            NotificationManager.ShowNotification("修改属性失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    [RelayCommand]
    private async Task PinCharacterAsync(CharacterGridItemModel character)
    {
        if (character.IsPinned)
        {
            character.IsPinned = false;

            ResetContent();

            var settingss = await ReadCharacterSettings();

            var pinedCharacterss = _backendCharacters.Where(ch => ch.IsPinned)
                .Select(ch => ch.Character.InternalName.Id)
                .ToArray();
            settingss.PinedCharacters = pinedCharacterss;
            await SaveCharacterSettings(settingss);
            return;
        }


        character.IsPinned = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        var pinedCharacters = _backendCharacters
            .Where(ch => ch.IsPinned)
            .Select(ch => ch.Character.InternalName.Id)
            .ToArray();

        settings.PinedCharacters = pinedCharacters;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    private bool CanClearNotifications(CharacterGridItemModel? character)
    {
        return character?.Notification ?? false;
    }

    [RelayCommand(CanExecute = nameof(CanClearNotifications))]
    private async Task ClearNotificationsAsync(CharacterGridItemModel character)
    {
        await _modNotificationManager.ClearModNotificationsAsync(character.Character.InternalName);
        await RefreshNotificationsAsync().ConfigureAwait(false);
    }

    private bool CanDisableCharacterMods(CharacterGridItemModel? character) =>
        character is not null && character.Mods.Any(m => m.IsEnabled);

    [RelayCommand(CanExecute = nameof(CanDisableCharacterMods))]
    private async Task DisableCharacterMods(CharacterGridItemModel character)
    {
        var updatedMods = new List<CharacterModItem>();
        try
        {
            await Task.Run(async () =>
            {
                var modList = _skinManagerService.GetCharacterModList(character.Character);
                var mods = modList.Mods.ToArray();
                foreach (var modEntry in mods)
                {
                    if (modList.IsModEnabled(modEntry.Mod))
                    {
                        modList.DisableMod(modEntry.Id);
                    }

                    var modSettings = await modEntry.Mod.Settings.TryReadSettingsAsync(true).ConfigureAwait(false);

                    updatedMods.Add(new CharacterModItem(modEntry.Mod.GetDisplayName(), modEntry.IsEnabled, modSettings?.DateAdded ?? default));
                }
            });
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error disabling mods for character {Character}", character.Character.InternalName);
            NotificationManager.ShowNotification("Error disabling mods", e.Message, TimeSpan.FromSeconds(6));
            return;
        }

        character.SetMods(updatedMods);
        await RefreshMultipleModsWarningAsync();
        NotificationManager.ShowNotification("Mods Disabled", $"Alls mods for {character.Character.DisplayName} have been disabled", null);
    }

    [RelayCommand]
    private async Task OpenCharacterFolderAsync(CharacterGridItemModel? character)
    {
        if (character is null)
            return;
        var modList = _skinManagerService.GetCharacterModList(character.Character);

        var directoryToOpen = new DirectoryInfo(modList.AbsModsFolderPath);
        if (!directoryToOpen.Exists)
        {
            modList.InstantiateCharacterFolder();
            directoryToOpen.Refresh();

            if (!directoryToOpen.Exists)
            {
                var parentDir = directoryToOpen.Parent;

                if (parentDir is null)
                {
                    _logger.Error("Could not find parent directory of {Directory}", directoryToOpen.FullName);
                    return;
                }

                directoryToOpen = parentDir;
            }
        }

        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(directoryToOpen.FullName));
    }

    [RelayCommand]
    private void HideCharacter(CharacterGridItemModel character)
    {
        NotImplemented.Show("Hiding characters is not implemented yet");
    }

    private Task<CharacterOverviewSettings> ReadCharacterSettings() =>
        _localSettingsService
            .ReadOrCreateSettingAsync<CharacterOverviewSettings>(CharacterOverviewSettings.GetKey(_category));

    private Task SaveCharacterSettings(CharacterOverviewSettings settings) =>
        _localSettingsService.SaveSettingAsync(CharacterOverviewSettings.GetKey(_category), settings);


    /// <summary>
    ///     二开：XXMI Launcher 的下载页。
    /// </summary>
    private const string XxmiLauncherUrl = "https://github.com/SpectrumQT/XXMI-Launcher/releases/latest";

    [RelayCommand]
    private async Task Start3DmigotoAsync()
    {
        // 二开：启动器没装 / 路径失效 → 先问是去下载还是自己指定路径，不硬启动
        if (!await EnsureLauncherAvailableAsync())
            return;

        await SimpleSelectProcessDialogVM.InternalStart(ThreeDMigtoProcessManager,
            SimpleSelectProcessDialogVM.StartType.ModelImporter);
    }

    /// <summary>
    ///     二开：检查启动器是否可用。路径为空或文件不存在时弹窗，引导去下载或手动指定。
    /// </summary>
    /// <returns>可用返回 true；不可用（已弹窗处理）返回 false。</returns>
    private async Task<bool> EnsureLauncherAvailableAsync()
    {
        await ThreeDMigtoProcessManager.TryInitialize();

        var path = ThreeDMigtoProcessManager.ProcessPath;

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return true;

        var message = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = string.IsNullOrWhiteSpace(path)
                ? "没有找到 XXMI Launcher（模型导入器）。\n\n" +
                  "· 点「去下载」打开 GitHub 发布页，下载解压后回到「设置 → 启动器文件夹」选中它所在的目录。\n" +
                  "· 已经装好了的，点「自己设置路径」直接去设置页指定。"
                : $"记录里的启动器已经不存在了：\n{path}\n\n" +
                  "· 点「去下载」重新下载一份最新的。\n" +
                  "· 点「自己设置路径」重新指定位置。"
        };

        var dialog = new ContentDialog
        {
            Title = "没有找到启动器",
            Content = message,
            PrimaryButtonText = "去下载",
            SecondaryButtonText = "自己设置路径",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);

        switch (result)
        {
            case ContentDialogResult.Primary:
                await Launcher.LaunchUriAsync(new Uri(XxmiLauncherUrl));
                break;
            case ContentDialogResult.Secondary:
                _navigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
                break;
        }

        return false;
    }


    [RelayCommand]
    private async Task StartGenshinAsync() =>
        await SimpleSelectProcessDialogVM.InternalStart(GenshinProcessManager,
            SimpleSelectProcessDialogVM.StartType.Game);

    private bool CanRefreshModsInGame()
    {
        return ElevatorService.ElevatorStatus == ElevatorStatus.Running;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshModsInGame))]
    private async Task RefreshModsInGameAsync()
    {
        _logger.Debug("Refreshing Mods In Game");
        await ElevatorService.RefreshGenshinMods();
    }

    [ObservableProperty] private bool _isAddingMod = false;

    public async Task ModDroppedOnCharacterAsync(CharacterGridItemModel characterGridItemModel,
        IReadOnlyList<IStorageItem> storageItems)
    {
        if (IsAddingMod)
        {
            _logger.Warning("Already adding mod");
            return;
        }

        var modList =
            _skinManagerService.CharacterModLists.FirstOrDefault(x =>
                x.Character.InternalNameEquals(characterGridItemModel.Character));
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}",
                characterGridItemModel.Character.InternalName);
            return;
        }

        try
        {
            IsAddingMod = true;
            await _modDragAndDropService.AddStorageItemFoldersAsync(modList, storageItems);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error adding mod");
            NotificationManager.ShowNotification("Error adding mod", e.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            IsAddingMod = false;
        }
    }

    public async Task ModUrlDroppedOnCharacterAsync(CharacterGridItemModel characterGridItemModel, Uri uri)
    {
        if (IsAddingMod)
        {
            _logger.Warning("Already adding mod");
            return;
        }

        var modList = _skinManagerService.CharacterModLists.FirstOrDefault(x => x.Character.InternalNameEquals(characterGridItemModel.Character));
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}",
                characterGridItemModel.Character.InternalName);
            return;
        }

        if (!GameBananaUrlHelper.TryGetModIdFromUrl(uri, out _))
        {
            NotificationManager.ShowNotification("Invalid GameBanana mod page link", "", null);
            return;
        }

        try
        {
            IsAddingMod = true;
            await _modDragAndDropService.AddModFromUrlAsync(modList, uri);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error opening mod page window");
            NotificationManager.ShowNotification("Error opening mod page window", e.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            IsAddingMod = false;
        }
    }


    private CharacterGridItemModel? FindCharacterByInternalName(string internalName)
    {
        return _backendCharacters.FirstOrDefault(x =>
            x.Character.InternalNameEquals(internalName));
    }


    [RelayCommand]
    private async Task SortBy(IEnumerable<GridItemSortingMethod> methodTypes)
    {
        if (_isNavigating) return;
        var sortingMethodType = methodTypes.First();

        ResetContent();

        var settings = await ReadCharacterSettings();
        settings.SortingMethod = sortingMethodType.SortingMethodType;
        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    [RelayCommand]
    private async Task InvertSorting()
    {
        ResetContent();

        var settings = await ReadCharacterSettings();
        settings.SortByDescending = SortByDescending;
        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }

    [RelayCommand]
    private void CheckForUpdatesForCharacter(object? characterGridItemModel)
    {
        if (characterGridItemModel is not CharacterGridItemModel character)
            return;

        var modList = _skinManagerService.GetCharacterModList(character.Character);
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}", character.Character.InternalName);
            return;
        }

        var check = ModCheckRequest.ForCharacter(character.Character);

        _modUpdateAvailableChecker.CheckNow(check.WithIgnoreLastChecked());
    }


    private void InitializeSorters()
    {
        // 二开：Gliders / Weapons 两个伪角色已经移除，这里不再引用它们。
        // 原来是 FindCharacterByInternalName(...)!，一旦取不到就是空引用，现在留空列表即可
        var lastCharacters = new List<CharacterGridItemModel>();

        var othersCharacter = _backendCharacters.FirstOrDefault(ch =>
            ch.Character.InternalName.Id.Contains("Others", StringComparison.OrdinalIgnoreCase));

        // 二开：常用（按最近打开）放最前面
        SortingMethods.Add(new GridItemSortingMethod(GridItemSorter.FrequentlyUsed, othersCharacter, lastCharacters));

        var alphabetical = new GridItemSortingMethod(GridItemSorter.Alphabetical, othersCharacter, lastCharacters);
        SortingMethods.Add(alphabetical);

        var byModCount = new GridItemSortingMethod(GridItemSorter.ModCount, othersCharacter, lastCharacters);
        SortingMethods.Add(byModCount);


        var byModRecentlyAdded = new GridItemSortingMethod(GridItemSorter.ModRecentlyAdded, othersCharacter, lastCharacters);
        SortingMethods.Add(byModRecentlyAdded);

        if (_category.ModCategory == ModCategory.Character)
        {
            SortingMethods.Add(new GridItemSortingMethod(GridItemSorter.ReleaseDate, othersCharacter, lastCharacters));
            SortingMethods.Add(new GridItemSortingMethod(GridItemSorter.Rarity, othersCharacter, lastCharacters));
        }

        if (_category.ModCategory == ModCategory.Weapons)
        {
            SortingMethods.Add(new GridItemSortingMethod(GridItemSorter.Rarity, othersCharacter, lastCharacters));
        }
    }


    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ApplyPreset(object? modPresetObject)
    {
        if (modPresetObject is not CharactersViewModels.ModPresetEntryVm modPresetEntryVm)
            return;

        var presetName = modPresetEntryVm.Name;

        using var _ = _busyService.SetPageBusy(this);

        var result = await Task.Run(() => _modPresetHandlerService.ApplyModPresetAsync(presetName));
        await RefreshMultipleModsWarningAsync();
        ResetContent();

        if (result.Notification is not null)
            NotificationManager.ShowNotification(result.Notification);

        // 二开：刚应用的那个算「当前预设」，名字变绿并自动挪到最前。
        // 注意：这里不记进 PinnedPresets —— 置顶的「针」只认手动点的那一下。
        _currentPresetName = presetName;

        await Task.Run(() => _modPresetHandlerService.MovePresetToTopAsync(presetName));
        await RefreshModPresetsAsync();
    }

    /// <summary>
    ///     二开：重新读预设列表。
    ///     排序优先级：正在用的 &gt; 手动置顶过的 &gt; 预设自身顺序。
    /// </summary>
    private async Task RefreshModPresetsAsync()
    {
        var presets = await _modPresetHandlerService.GetModPresetsAsync();

        var ordered = presets
            .OrderByDescending(preset => string.Equals(preset.Name, _currentPresetName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(preset => PinnedPresets.ContainsKey(preset.Name))
            .ThenBy(preset => preset.Index)
            .ToList();

        ModPresets.Clear();

        foreach (var preset in ordered)
        {
            var isActive = string.Equals(preset.Name, _currentPresetName, StringComparison.OrdinalIgnoreCase);

            var isPinned = PinnedPresets.ContainsKey(preset.Name);

            ModPresets.Add(new CharactersViewModels.ModPresetEntryVm(preset.Name, isActive, isPinned,
                ApplyPresetCommand, PinPresetCommand, OverwritePresetCommand, DeletePresetCommand,
                RenamePresetCommand));
        }
    }

    /// <summary>
    ///     二开：把预设挪到最前面。
    /// </summary>
    [RelayCommand]
    private async Task PinPreset(CharactersViewModels.ModPresetEntryVm? presetVm)
    {
        if (presetVm is null)
            return;

        using var _ = _busyService.SetPageBusy(this);

        // 二开：已经钉住了就取消（挪回原位），没钉就挪到最前
        var ok = presetVm.IsPinned
            ? await Task.Run(() => _modPresetHandlerService.UnpinPresetAsync(presetVm.Name,
                PinnedPresets.TryGetValue(presetVm.Name, out var previousIndex) ? previousIndex : 0))
            : await Task.Run(() => _modPresetHandlerService.MovePresetToTopAsync(presetVm.Name));

        if (!ok)
        {
            NotificationManager.ShowNotification(presetVm.IsPinned ? "取消置顶失败" : "置顶失败",
                $"没找到叫「{presetVm.Name}」的预设", TimeSpan.FromSeconds(5));
            return;
        }

        if (presetVm.IsPinned)
        {
            PinnedPresets.Remove(presetVm.Name);
        }
        else
        {
            var currentIndex = ModPresets.IndexOf(presetVm);

            PinnedPresets[presetVm.Name] = currentIndex < 0 ? 0 : currentIndex;
        }

        await RefreshModPresetsAsync();
    }

    /// <summary>
    ///     二开：用当前已启用的 Mod 组合覆盖这个预设。
    /// </summary>
    [RelayCommand]
    private async Task OverwritePreset(CharactersViewModels.ModPresetEntryVm? presetVm)
    {
        if (presetVm is null)
            return;

        using var _ = _busyService.SetPageBusy(this);

        var ok = await Task.Run(() => _modPresetHandlerService.OverwritePresetWithCurrentAsync(presetVm.Name));

        if (ok)
            NotificationManager.ShowNotification("已保存到预设",
                $"「{presetVm.Name}」已更新为当前启用的 Mod 组合", TimeSpan.FromSeconds(6));

        await RefreshModPresetsAsync();
    }

    /// <summary>
    ///     二开：删除预设（先弹窗确认）。
    /// </summary>
    [RelayCommand]
    private async Task DeletePreset(CharactersViewModels.ModPresetEntryVm? presetVm)
    {
        if (presetVm is null)
            return;

        var dialog = new ContentDialog
        {
            Title = "删除预设",
            Content = $"确定要删除「{presetVm.Name}」吗？删除后无法恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _windowManagerService.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
            return;

        using var _ = _busyService.SetPageBusy(this);

        var ok = await Task.Run(() => _modPresetHandlerService.DeletePresetAsync(presetVm.Name));

        if (ok)
        {
            if (string.Equals(_currentPresetName, presetVm.Name, StringComparison.OrdinalIgnoreCase))
                _currentPresetName = null;

            PinnedPresets.Remove(presetVm.Name);

            NotificationManager.ShowNotification("预设已删除",
                $"「{presetVm.Name}」已删除", TimeSpan.FromSeconds(4));
        }

        await RefreshModPresetsAsync();
    }

    /// <summary>
    ///     二开：重命名预设（弹窗输入新名字）。
    /// </summary>
    [RelayCommand]
    private async Task RenamePreset(CharactersViewModels.ModPresetEntryVm? presetVm)
    {
        if (presetVm is null)
            return;

        var input = new TextBox
        {
            Text = presetVm.Name,
            PlaceholderText = "新的预设名称"
        };

        var dialog = new ContentDialog
        {
            Title = "重命名预设",
            Content = input,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await _windowManagerService.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
            return;

        var newName = input.Text.Trim();

        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, presetVm.Name, StringComparison.Ordinal))
            return;

        if (ModPresets.Any(preset => string.Equals(preset.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            NotificationManager.ShowNotification("名字重复",
                $"已经有叫「{newName}」的预设了", TimeSpan.FromSeconds(5));
            return;
        }

        using var _ = _busyService.SetPageBusy(this);

        var ok = await Task.Run(() => _modPresetHandlerService.RenamePresetAsync(presetVm.Name, newName));

        if (ok)
        {
            // 二开：改的是当前在用的 / 置顶过的，状态跟着新名字走
            if (string.Equals(_currentPresetName, presetVm.Name, StringComparison.OrdinalIgnoreCase))
                _currentPresetName = newName;

            if (PinnedPresets.Remove(presetVm.Name, out var pinnedIndex))
                PinnedPresets[newName] = pinnedIndex;

            NotificationManager.ShowNotification("预设已重命名",
                $"「{presetVm.Name}」→「{newName}」", TimeSpan.FromSeconds(5));
        }

        await RefreshModPresetsAsync();
    }


    private void OnBusyChangedHandler(object? sender, BusyChangedEventArgs args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (args.IsKey(this))
                IsBusy = args.IsBusy;
        });
    }

    public sealed class GridItemSortingMethod(
        Sorter<CharacterGridItemModel> sortingMethodType,
        CharacterGridItemModel? firstItem = null,
        ICollection<CharacterGridItemModel>? lastItems = null)
        : SortingMethod<CharacterGridItemModel>(sortingMethodType, firstItem, lastItems)
    {
        protected override void PostSortAction(List<CharacterGridItemModel> sortedList)
        {
            var pinnedCharacters = sortedList.Where(x => x.IsPinned).ToArray();
            if (pinnedCharacters.Length == 0)
                return;
            var pinnedStartIndex = FirstItem is not null ? 1 : 0;
            foreach (var pinnedCharacter in pinnedCharacters)
            {
                if (pinnedCharacter == FirstItem) continue;
                sortedList.Remove(pinnedCharacter);
                sortedList.Insert(pinnedStartIndex, pinnedCharacter);
                pinnedStartIndex++;
            }
        }
    }

    public sealed class GridItemSorter : Sorter<CharacterGridItemModel>
    {
        private GridItemSorter(string sortingMethodType, SortFunc firstSortFunc, AdditionalSortFunc? secondSortFunc = null,
            AdditionalSortFunc? thirdSortFunc = null) : base(sortingMethodType, firstSortFunc, secondSortFunc, thirdSortFunc)
        {
        }

        /// <summary>
        ///     二开：角色最近打开时间（内部名 → 时间）。Sorter 是静态的，所以用静态字段共享。
        /// </summary>
        public static Dictionary<string, DateTime> RecentAccessTimes { get; } = new();

        public const string FrequentlyUsedSortName = "常用";

        /// <summary>
        ///     二开：按最近打开排序，最常点开的排前面。
        /// </summary>
        public static GridItemSorter FrequentlyUsed { get; } =
            new(
                FrequentlyUsedSortName,
                (characters, isDescending) =>
                {
                    DateTime GetLastAccess(CharacterGridItemModel item) =>
                        RecentAccessTimes.GetValueOrDefault(item.Character.InternalName.Id, DateTime.MinValue);

                    return !isDescending
                        ? characters.OrderByDescending(GetLastAccess)
                        : characters.OrderBy(GetLastAccess);
                },
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));

        public const string AlphabeticalSortName = "按名称";

        public static GridItemSorter Alphabetical { get; } =
            new(
                AlphabeticalSortName,
                (characters, isDescending) =>
                    isDescending
                        ? characters.OrderByDescending(x => x.Character.DisplayName)
                        : characters.OrderBy(x => x.Character.DisplayName
                        ));


        public const string ReleaseDateSortName = "发布日期";

        public static GridItemSorter ReleaseDate { get; } =
            new(
                ReleaseDateSortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => ((ICharacter)x.Character).ReleaseDate)
                        : characters.OrderBy(x => ((ICharacter)x.Character).ReleaseDate),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string RaritySortName = "稀有度";

        public static GridItemSorter Rarity { get; } =
            new(
                RaritySortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => ((IRarity)x.Character).Rarity)
                        : characters.OrderBy(x => ((IRarity)x.Character).Rarity),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string ModCountSortName = "Mod 数量";

        public static GridItemSorter ModCount { get; } =
            new(
                ModCountSortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => x.ModCount)
                        : characters.OrderBy(x => x.ModCount),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string ModRecentlyAddedName = "最近添加";

        public static GridItemSorter ModRecentlyAdded { get; } =
            new(
                ModRecentlyAddedName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x =>
                        {
                            var validDates = x.Mods.Where(mod => mod.DateAdded != default).Select(mod => mod.DateAdded)
                                .ToArray();
                            if (validDates.Any())
                                return validDates.Max();
                            else
                                return DateTime.MinValue;
                        })
                        : characters.OrderBy(x =>
                        {
                            var validDates = x.Mods.Where(mod => mod.DateAdded != default).Select(mod => mod.DateAdded)
                                .ToArray();
                            if (validDates.Any())
                                return validDates.Min();
                            else
                                return DateTime.MaxValue;
                        }),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));
    }

    [RelayCommand]
    private Task RandomizeMods() => _modRandomizationService.ShowRandomizeModsDialog();

    /// <summary>
    ///     二开：把当前已启用的 Mod 组合存成一个预设，名字可以自己起。
    ///     默认填「预设1 / 预设2 …」，直接回车也能用。
    /// </summary>
    [RelayCommand]
    private async Task SaveCurrentAsPresetAsync()
    {
        var input = new TextBox
        {
            Text = GetNextPresetName(),
            PlaceholderText = "给这个预设起个名字"
        };

        var dialog = new ContentDialog
        {
            Title = "保存当前为预设",
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "把当前已启用的 Mod 组合存成一个预设，以后从「Mod 预设」里一键切回来。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    input
                }
            }
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result != ContentDialogResult.Primary)
            return;

        var name = input.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            NotificationManager.ShowNotification("名字不能为空", "没保存，重新点一次就行",
                TimeSpan.FromSeconds(4));
            return;
        }

        if (ModPresets.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            NotificationManager.ShowNotification("预设重名", $"已经有一个叫「{name}」的预设了，换个名字",
                TimeSpan.FromSeconds(5));
            return;
        }

        using var _ = _busyService.SetPageBusy(this);

        await Task.Run(() => _modPresetHandlerService.CreatePresetFromCurrentAsync(name));

        // 二开：刚存的就是当前状态，名字变绿并排到最前。「针」同样不自动亮。
        _currentPresetName = name;

        await RefreshModPresetsAsync();

        NotificationManager.ShowNotification("预设已保存",
            $"「{name}」已保存当前启用的 Mod 组合", TimeSpan.FromSeconds(6));
    }

    /// <summary>
    ///     二开：下一个没被占用的「预设N」名字。
    /// </summary>
    private string GetNextPresetName()
    {
        var index = 1;

        while (ModPresets.Any(preset =>
                   string.Equals(preset.Name, $"预设{index}", StringComparison.OrdinalIgnoreCase)))
            index++;

        return $"预设{index}";
    }
}

public sealed class GridFilter
{
    private readonly Func<CharacterGridItemModel, bool> _filter;

    public GridFilter(Func<CharacterGridItemModel, bool> filter)
    {
        _filter = filter;
    }

    public bool Filter(CharacterGridItemModel character)
    {
        return _filter(character);
    }

    public IEnumerable<CharacterGridItemModel> Filter(IEnumerable<CharacterGridItemModel> characters)
    {
        return characters.Where(Filter);
    }
}

public enum FilterType
{
    Element,
    Search,
    HasMods,
    HasModNotifications,
    HasEnabledMods
}

public class ScrollToCharacterArgs : EventArgs
{
    public CharacterGridItemModel Character { get; }

    public ScrollToCharacterArgs(CharacterGridItemModel character)
    {
        Character = character;
    }
}