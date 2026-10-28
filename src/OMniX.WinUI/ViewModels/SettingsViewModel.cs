using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErrorOr;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;
using OMniX.Core.Services;
using OMniX.Core.Services.GameBanana;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.AppManagement.Updating;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.Validators.PreConfigured;
using OMniX.WinUI.ViewModels.SettingsViewModels;
using OMniX.WinUI.ViewModels.SubVms;
using OMniX.WinUI.Views.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;

namespace OMniX.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableRecipient, INavigationAware
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILogger _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly INavigationViewService _navigationViewService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly ILanguageLocalizer _localizer;
    private readonly AutoUpdaterService _autoUpdaterService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly LifeCycleService _lifeCycleService;
    private readonly INavigationService _navigationService;
    private readonly GameLaunchResolver _launchResolver;
    private readonly GameKernelService _kernel;
    private readonly GameDataRootService _dataRoot;
    private readonly PluginCatalogService _pluginCatalog;
    private readonly ModArchiveRepository _modArchiveRepository;

    /// <summary>二开：搬插件目录时要顺手更新它记住的部署包路径</summary>
    private readonly Dlss5PluginService _dlss5;


    private readonly NotificationManager _notificationManager;
    private readonly UpdateChecker _updateChecker;
    public ElevatorService ElevatorService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetGenshinExePathCommand))]
    public GenshinProcessManager _genshinProcessManager;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Reset3DmigotoPathCommand))]
    public ThreeDMigtoProcessManager _threeDMigtoProcessManager;


    [ObservableProperty] private ElementTheme _elementTheme;

    [ObservableProperty] private string _versionDescription;

    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private bool _showNewVersionAvailable = false;

    /// <summary>二开：正在检查更新（按钮旁边转圈用）</summary>
    [ObservableProperty] private bool _isCheckingUpdate;

    /// <summary>二开：检查完给一句话，「已经是最新版本」/「检查失败…」</summary>
    [ObservableProperty] private string _updateCheckMessage = string.Empty;

    /// <summary>二开：这次更新属于「补丁」还是「新版本」</summary>
    [ObservableProperty] private string _updateKindText = string.Empty;

    /// <summary>二开：正在导入本地更新包</summary>
    [ObservableProperty] private bool _isImportingUpdate;

    /// <summary>二开：导入到本地更新包后的结果提示</summary>
    [ObservableProperty] private string _importMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IgnoreNewVersionCommand))]
    private bool _CanIgnoreUpdate = false;

    [ObservableProperty] private ObservableCollection<string> _languages = new();
    [ObservableProperty] private string _selectedLanguage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _games = new(EnabledGames.All.Select(game => game.ToString()));

    [ObservableProperty] private string _selectedGame = string.Empty;

    [ObservableProperty] private string _modCheckerStatus = ModUpdateAvailableChecker.RunningState.Waiting.ToString();

    [ObservableProperty] private bool _isModUpdateCheckerEnabled = false;

    [ObservableProperty] private DateTime? _nextModCheckTime = null;

    [ObservableProperty] private bool _characterAsSkinsCheckbox = false;

    [ObservableProperty] private int _maxCacheLimit;

    [ObservableProperty] private Uri _archiveCacheFolderPath;

    [ObservableProperty] private bool _persistWindowSize = false;

    [ObservableProperty] private bool _persistWindowPosition = false;

    private Dictionary<string, string> _nameToLangCode = new();

    public PathPicker PathToGIMIFolderPicker { get; }
    public PathPicker PathToModsFolderPicker { get; }

    /// <summary>二开：插件（XXMI、DLSS5 部署包、Mod 修复器）放哪个文件夹</summary>
    public PathPicker PathToPluginsFolderPicker { get; }

    [ObservableProperty] private bool _legacyCharacterDetails;

    private readonly ThemeStyleService _themeStyleService;
    private readonly UiSoundService _uiSoundService;
    private readonly PaletteService _paletteService;

    /// <summary>界面风格：标准版（带动效）/ 简易版（静态玻璃）</summary>
    public bool IsStandardStyle => _themeStyleService.Current == OmniXThemeStyle.Standard;

    public bool IsSimpleStyle => !IsStandardStyle;

    /// <summary>两套配色，互斥，给 RadioButton 的 IsChecked 用</summary>
    public bool IsWarmPalette => _paletteService.Current == OmniXPalette.Warm;

    public bool IsBluePalette => _paletteService.Current == OmniXPalette.Blue;

    /// <summary>
    ///     二开：交互音效开关。简易版会自动静音（UiSoundService 里判断），
    ///     这里只管用户自己想不想听。
    /// </summary>
    public bool SoundEnabled
    {
        get => _uiSoundService.Enabled;
        set
        {
            if (_uiSoundService.Enabled == value)
                return;

            // 保存完会回调 EnabledChanged，再统一刷新界面
            _ = _uiSoundService.SetEnabledAsync(value);

            if (value)
                _uiSoundService.PlayTap();   // 开的时候试听一声
        }
    }


    private static bool _showElevatorStartDialog = true;

    private ModManagerOptions? _modManagerOptions = null!;

    [ObservableProperty] private string _modCacheSizeGB = string.Empty;

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService, ILocalSettingsService localSettingsService,
        ElevatorService elevatorService, ILogger logger, NotificationManager notificationManager,
        INavigationViewService navigationViewService, IWindowManagerService windowManagerService,
        ISkinManagerService skinManagerService, UpdateChecker updateChecker,
        GenshinProcessManager genshinProcessManager, ThreeDMigtoProcessManager threeDMigtoProcessManager,
        IGameService gameService, AutoUpdaterService autoUpdaterService, ILanguageLocalizer localizer,
        SelectedGameService selectedGameService, ModUpdateAvailableChecker modUpdateAvailableChecker,
        LifeCycleService lifeCycleService, INavigationService navigationService,
        ModArchiveRepository modArchiveRepository, ThemeStyleService themeStyleService,
        UiSoundService uiSoundService, PaletteService paletteService, Dlss5PluginService dlss5,
        GameLaunchResolver launchResolver, GameKernelService kernel, GameDataRootService dataRoot,
        PluginCatalogService pluginCatalog)
    {
        _uiSoundService = uiSoundService;
        _themeStyleService = themeStyleService;
        _paletteService = paletteService;

        // 音效开关改动后，让绑定把 ToggleSwitch 拉回一致状态
        _uiSoundService.EnabledChanged += (_, _) => OnPropertyChanged(nameof(SoundEnabled));
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        ElevatorService = elevatorService;
        _notificationManager = notificationManager;
        _navigationViewService = navigationViewService;
        _windowManagerService = windowManagerService;
        _skinManagerService = skinManagerService;
        _updateChecker = updateChecker;
        _gameService = gameService;
        _autoUpdaterService = autoUpdaterService;
        _localizer = localizer;
        _selectedGameService = selectedGameService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _lifeCycleService = lifeCycleService;
        _navigationService = navigationService;
        _modArchiveRepository = modArchiveRepository;
        _dlss5 = dlss5;
        _launchResolver = launchResolver;
        _kernel = kernel;
        _dataRoot = dataRoot;
        _pluginCatalog = pluginCatalog;
        GenshinProcessManager = genshinProcessManager;
        ThreeDMigtoProcessManager = threeDMigtoProcessManager;
        _logger = logger.ForContext<SettingsViewModel>();
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();

        _updateChecker.NewVersionAvailable += UpdateCheckerOnNewVersionAvailable;

        if (_updateChecker.LatestRetrievedVersion is not null &&
            _updateChecker.LatestRetrievedVersion != _updateChecker.CurrentVersion)
        {
            LatestVersion = VersionFormatter(_updateChecker.LatestRetrievedVersion);
            ShowNewVersionAvailable = true;
            if (_updateChecker.LatestRetrievedVersion != _updateChecker.IgnoredVersion)
                CanIgnoreUpdate = true;
        }

        ArchiveCacheFolderPath = _modArchiveRepository.ArchiveDirectory;

        _modManagerOptions = localSettingsService.ReadSetting<ModManagerOptions>(ModManagerOptions.Section);
        PathToGIMIFolderPicker = new PathPicker();
        PathToModsFolderPicker = new PathPicker(ModsFolderValidator.Validators);
        PathToPluginsFolderPicker = new PathPicker();

        CharacterAsSkinsCheckbox = _modManagerOptions?.CharacterSkinsAsCharacters ?? false;

        PathToGIMIFolderPicker.Path = _modManagerOptions?.GimiRootFolderPath;
        PathToModsFolderPicker.Path = _modManagerOptions?.ModsFolderPath;

        // 二开：插件目录。没配置过就把默认位置显示出来（%LOCALAPPDATA%\OMniX\Plugins），
        // 让用户看得见现在到底在用哪儿，想挪到别的盘直接改这里。
        PathToPluginsFolderPicker.Path = string.IsNullOrWhiteSpace(_modManagerOptions?.PluginsFolderPath)
            ? PluginDependencyService.DefaultPluginsRoot
            : _modManagerOptions!.PluginsFolderPath;

        // 顺手同步给运行时 —— 这一页看完用户可能马上去插件页下载，
        // 那会儿再读就晚了。
        PluginDependencyService.SetPluginsRoot(_modManagerOptions?.PluginsFolderPath);


        PathToGIMIFolderPicker.IsValidChanged += (sender, args) => SaveSettingsCommand.NotifyCanExecuteChanged();
        PathToModsFolderPicker.IsValidChanged +=
            (sender, args) => SaveSettingsCommand.NotifyCanExecuteChanged();


        PathToGIMIFolderPicker.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PathPicker.Path))
                SaveSettingsCommand.NotifyCanExecuteChanged();
        };

        PathToModsFolderPicker.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PathPicker.Path))
                SaveSettingsCommand.NotifyCanExecuteChanged();
        };

        // 二开：改插件目录同样要重算「能不能保存」，不然按钮不会从灰变亮。
        PathToPluginsFolderPicker.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PathPicker.Path))
                SaveSettingsCommand.NotifyCanExecuteChanged();
        };

        // 「路径重置」两个按钮能不能点，看的是管理器里记着的东西。
        // ProcessStatus / ProcessPath 都是在管理器内部改的，属性本身不会被重新赋值，
        // 所以 [NotifyCanExecuteChangedFor] 那条走不到 —— 得盯着它的 PropertyChanged 手动通知，
        // 不然初始化完成后按钮还一直是灰的。
        GenshinProcessManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(GenshinProcessManager.ProcessStatus)
                or nameof(GenshinProcessManager.ProcessPath))
                ResetGenshinExePathCommand.NotifyCanExecuteChanged();
        };

        ThreeDMigtoProcessManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ThreeDMigtoProcessManager.ProcessStatus)
                or nameof(ThreeDMigtoProcessManager.ProcessPath))
                Reset3DmigotoPathCommand.NotifyCanExecuteChanged();
        };

        ElevatorService.CheckStatus();

        MaxCacheLimit = localSettingsService.ReadSetting<ModArchiveSettings>(ModArchiveSettings.Key)
            ?.MaxLocalArchiveCacheSizeGb ?? new ModArchiveSettings().MaxLocalArchiveCacheSizeGb;
        SetCacheString(MaxCacheLimit);

        var cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
        cultures = cultures.Append(new CultureInfo("zh-cn")).ToArray();


        var supportedCultures = _localizer.AvailableLanguages.Select(l => l.LanguageCode).ToArray();

        foreach (var culture in cultures)
        {
            if (!supportedCultures.Contains(culture.Name.ToLower())) continue;

            Languages.Add(culture.NativeName);
            _nameToLangCode.Add(culture.NativeName, culture.Name.ToLower());

            if (_localizer.CurrentLanguage.Equals(culture))
                SelectedLanguage = culture.NativeName;
        }

        ModCheckerStatus = _localizer.GetLocalizedStringOrDefault(_modUpdateAvailableChecker.Status.ToString(),
            _modUpdateAvailableChecker.Status.ToString());
        NextModCheckTime = _modUpdateAvailableChecker.NextRunAt;
        _modUpdateAvailableChecker.OnUpdateCheckerEvent += (sender, args) =>
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ModCheckerStatus = _localizer.GetLocalizedStringOrDefault(_modUpdateAvailableChecker.Status.ToString(),
                    _modUpdateAvailableChecker.Status.ToString());
                NextModCheckTime = args.NextRunAt;
            });
        };
    }


    [RelayCommand]
    private async Task SwitchThemeAsync(ElementTheme param)
    {
        if (ElementTheme != param)
        {
            var result = await _windowManagerService.ShowDialogAsync(new ContentDialog()
            {
                Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ThemeRestartTitle", "需要重启"),
                Content = new TextBlock()
                {
                    Text =
                        _localizer.GetLocalizedStringOrDefault("/SettingsUi/ThemeRestartText",
                            "换主题后要重启应用才会生效，不然界面可能会错乱。" +
                            "这是主题处理没做到位，推荐用深色模式。\n\n" +
                            "给你添麻烦了。"),
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ThemeRestartPrimary", "重启"),
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消"),
                DefaultButton = ContentDialogButton.Primary
            });

            if (result != ContentDialogResult.Primary) return;

            ElementTheme = param;
            await _themeSelectorService.SetThemeAsync(param);
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/RestartingTitle", "正在重启…"),
                _localizer.GetLocalizedStringOrDefault("/SettingsUi/RestartingBody", "应用马上重启。"),
                null);
            await RestartAppAsync();
        }
    }

    /// <summary>
    ///     二开：切界面风格。不动配色，所以不用重启，流动层和动效立刻跟着变。
    /// </summary>
    [RelayCommand]
    private async Task SwitchStyleAsync(string? param)
    {
        var style = string.Equals(param, nameof(OmniXThemeStyle.Simple), StringComparison.OrdinalIgnoreCase)
            ? OmniXThemeStyle.Simple
            : OmniXThemeStyle.Standard;

        await _themeStyleService.SetAsync(style);

        OnPropertyChanged(nameof(IsStandardStyle));
        OnPropertyChanged(nameof(IsSimpleStyle));

        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/StyleSwitchedTitle", "界面风格已切换"),
            style == OmniXThemeStyle.Standard
                ? _localizer.GetLocalizedStringOrDefault("/SettingsUi/StyleSwitchedStandard", "标准版：流动光斑、悬停抬起、按压反馈")
                : _localizer.GetLocalizedStringOrDefault("/SettingsUi/StyleSwitchedSimple", "简易版：静态玻璃，不带动画"),
            TimeSpan.FromSeconds(5));
    }

    /// <summary>
    ///     二开：切配色。改的是已经解析出来的画刷实例，不用重启，整界面立刻换色。
    /// </summary>
    [RelayCommand]
    private async Task SwitchPaletteAsync(string? param)
    {
        var palette = param switch
        {
            nameof(OmniXPalette.Blue) => OmniXPalette.Blue,
            _ => OmniXPalette.Warm
        };

        await _paletteService.SetAsync(palette);

        OnPropertyChanged(nameof(IsWarmPalette));
        OnPropertyChanged(nameof(IsBluePalette));

        // 换配色也响一声，和点按钮的手感保持一致
        _uiSoundService.PlayTap();

        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/PaletteSwitchedTitle", "配色已切换"),
            palette switch
            {
                OmniXPalette.Blue => _localizer.GetLocalizedStringOrDefault("/SettingsUi/PaletteSwitchedBlue", "冷调蓝：冷白底 + 蓝色主色"),
                _ => _localizer.GetLocalizedStringOrDefault("/SettingsUi/PaletteSwitchedWarm", "暖白珊瑚：1.0 版那套")
            },
            TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private async Task WindowSizePositionToggle(string? type)
    {
        if (type != "size" && type != "position") return;

        var windowSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);

        if (type == "size")
        {
            PersistWindowSize = !PersistWindowSize;
            windowSettings.PersistWindowSize = PersistWindowSize;
        }
        else
        {
            PersistWindowPosition = !PersistWindowPosition;
            windowSettings.PersistWindowPosition = PersistWindowPosition;
        }

        await _localSettingsService.SaveSettingAsync(ScreenSizeSettings.Key, windowSettings).ConfigureAwait(false);
    }

    private static string GetVersionDescription()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version!;

        return
            $"{"AppDisplayName".GetLocalized()} - {VersionFormatter(version)}";
    }


    /// <summary>当前生效的插件目录：配置过就用配置的，没有就是默认位置。</summary>
    private string EffectivePluginsRoot =>
        string.IsNullOrWhiteSpace(_modManagerOptions?.PluginsFolderPath)
            ? PluginDependencyService.DefaultPluginsRoot
            : _modManagerOptions!.PluginsFolderPath!;

    private bool ValidFolderSettings()
    {
        // 二开：插件目录也算「有改动」。原来这里只看 GIMI / Mods 两个，
        // 只改插件目录时后面那组判断全是 false，按钮就一直灰着 ——
        // 界面上看得出来路径已经改了，却怎么点都点不动。
        var pluginsPath = string.IsNullOrWhiteSpace(PathToPluginsFolderPicker.Path)
            ? PluginDependencyService.DefaultPluginsRoot
            : PathToPluginsFolderPicker.Path!;

        var pluginsChanged = !string.Equals(pluginsPath, EffectivePluginsRoot, StringComparison.OrdinalIgnoreCase);

        return PathToGIMIFolderPicker.IsValid && PathToModsFolderPicker.IsValid &&
               PathToGIMIFolderPicker.Path != PathToModsFolderPicker.Path &&
               (pluginsChanged ||
                PathToGIMIFolderPicker.Path != _modManagerOptions?.GimiRootFolderPath ||
                PathToModsFolderPicker.Path != _modManagerOptions?.ModsFolderPath);
    }


    [RelayCommand(CanExecute = nameof(ValidFolderSettings))]
    private async Task SaveSettings()
    {
        var dialog = new ContentDialog();
        dialog.XamlRoot = App.MainWindow.Content.XamlRoot;
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/SavePathsTitle", "保存文件夹路径？");
        dialog.CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消");
        dialog.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Save", "保存");
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = _localizer.GetLocalizedStringOrDefault("/SettingsUi/SavePathsContent", "要保存新的文件夹路径吗？保存后应用会重启。");

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var modManagerOptions = await _localSettingsService.ReadSettingAsync<ModManagerOptions>(
                ModManagerOptions.Section) ?? new ModManagerOptions();

            // 二开：插件目录。存成默认位置的话就记 null ——
            // 这样以后默认位置万一变了（比如换到别的盘），用户没主动选过就跟着走。
            var newPluginsPath =
                string.Equals(PathToPluginsFolderPicker.Path, PluginDependencyService.DefaultPluginsRoot,
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : PathToPluginsFolderPicker.Path;

            var targetPluginsRoot = string.IsNullOrWhiteSpace(newPluginsPath)
                ? PluginDependencyService.DefaultPluginsRoot
                : newPluginsPath!;

            // 二开：换目录时先把旧的搬过去，再存配置。
            // 先搬后存 —— 复制失败（比如目标盘满了、没权限）就当没改过，
            // 不会出现「配置指到空目录、插件还留在旧地方」那种半截状态。
            if (!await MigratePluginsAsync(EffectivePluginsRoot, targetPluginsRoot))
            {
                _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateFailedTitle", "插件没搬成功，路径没改"),
                    _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateFailedBody", "可以先把旧目录手动复制过去，或者换个目标文件夹再试"), TimeSpan.FromSeconds(6));
                return;
            }

            modManagerOptions.GimiRootFolderPath = PathToGIMIFolderPicker.Path;
            modManagerOptions.ModsFolderPath = PathToModsFolderPicker.Path;
            modManagerOptions.PluginsFolderPath = newPluginsPath;

            await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section,
                modManagerOptions);
            _logger.Information("Saved startup settings: {@ModManagerOptions}", modManagerOptions);
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/SavedRestartingTitle", "设置已保存，正在重启…"), "", TimeSpan.FromSeconds(2));


            await RestartAppAsync();
        }
    }

    /// <summary>
    ///     二开：插件目录从旧位置搬到新位置。
    ///     返回 false 表示这次保存不该继续（复制失败）。
    ///     返回 true 的其他情况都继续保存：目录没变、旧目录本来就是空的、用户说不用搬。
    /// </summary>
    private async Task<bool> MigratePluginsAsync(string oldRoot, string newRoot)
    {
        if (string.IsNullOrWhiteSpace(oldRoot) || string.IsNullOrWhiteSpace(newRoot))
            return true;

        if (string.Equals(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Directory.Exists(oldRoot) || Directory.GetFileSystemEntries(oldRoot).Length == 0)
            return true;

        // 新目录如果就在旧目录里面，递归复制等于自己拷自己，跳过
        var oldFull = Path.GetFullPath(oldRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var newFull = Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (newFull.StartsWith(oldFull, StringComparison.OrdinalIgnoreCase))
            return true;

        double sizeMb = 0;

        try
        {
            sizeMb = new DirectoryInfo(oldRoot)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length) / 1024d / 1024d;
        }
        catch (Exception)
        {
            // 统计不出来就按未知大小提示，不影响搬运
        }

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateTitle", "把已有插件搬过去？"),
            Content = string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateContent",
                    "插件目录要从\n{0}\n换成\n{1}\n\n" +
                    "里面现在大约 {2:0.#} MB（XXMI、DLSS5 部署包、Mod 修复器）。\n" +
                    "要复制一份过去吗？旧目录里的东西我不删 —— 新位置用着没问题你自己删掉就行。"),
                oldRoot, newRoot, sizeMb),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigratePrimary", "搬过去"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateClose", "不用，留旧的"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return true;

        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/MigratingTitle", "正在搬插件…"),
            _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigratingBody", "文件多的话要等一会儿"), TimeSpan.FromSeconds(3));

        try
        {
            await Task.Run(() => CopyDirectory(oldRoot, newRoot));

            // DLSS5 部署包路径单独记了一份配置，搬完得跟着改 ——
            // 不然它还指着旧位置，旧目录一删就失效。
            var kit = _dlss5.ResolveKitDirectory();

            if (!string.IsNullOrWhiteSpace(kit) && kit.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase))
            {
                var newKit = Path.Combine(newRoot, Path.GetRelativePath(oldRoot, kit));

                if (Directory.Exists(newKit))
                    await _dlss5.SavePathsAsync(_dlss5.ResolveGameExePath(), newKit);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "搬插件目录失败");
            return false;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }

    [RelayCommand]
    private async Task BrowseGimiFolderAsync()
    {
        await PathToGIMIFolderPicker.BrowseFolderPathAsync(App.MainWindow);
        if (PathToGIMIFolderPicker.PathHasValue &&
            !PathToModsFolderPicker.PathHasValue)
            PathToModsFolderPicker.Path = Path.Combine(PathToGIMIFolderPicker.Path!, "Mods");
    }


    [RelayCommand]
    private async Task BrowseModsFolderAsync()
    {
        await PathToModsFolderPicker.BrowseFolderPathAsync(App.MainWindow);
    }

    /// <summary>二开：选插件文件夹（选完还要点「保存路径」才生效，和上面两个一样要重启）</summary>
    [RelayCommand]
    private async Task BrowsePluginsFolderAsync()
    {
        await PathToPluginsFolderPicker.BrowseFolderPathAsync(App.MainWindow);
    }

    [RelayCommand]
    private async Task ReorganizeModsAsync()
    {
        var result = await _windowManagerService.ShowDialogAsync(new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ReorganizeTitle", "重新整理 Mod？"),
            Content = new TextBlock()
            {
                Text =
                    _localizer.GetLocalizedStringOrDefault("/SettingsUi/ReorganizeContent",
                        "要把 Mods 文件夹重新整理一遍吗？\n" +
                        "程序会把直接躺在 Mods 文件夹和 Others 文件夹里的 Mod，按角色分到各自的文件夹里。\n\n" +
                        "实在对不上角色的 Mod 会被放进「Others」文件夹；本来就在「Others」里的不会动。"),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ReorganizePrimary", "开始整理"),
            DefaultButton = ContentDialogButton.Primary,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消"),
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        });

        if (result == ContentDialogResult.Primary)
        {
            _navigationViewService.IsEnabled = false;

            try
            {
                var movedModsCount = await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync()); // Mods folder

                movedModsCount += await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync(
                        _gameService.GetCharacterByIdentifier(_gameService.OtherCharacterInternalName)!
                            .InternalName)); // Others folder

                await _skinManagerService.RefreshModsAsync();

                if (movedModsCount == -1)
                    _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ReorgFailedTitle", "整理 Mod 失败"),
                        _localizer.GetLocalizedStringOrDefault("/SettingsUi/ReorgFailedBody", "详细原因见日志。"), TimeSpan.FromSeconds(5));

                else
                    _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ReorgDoneTitle", "整理完成"),
                        string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ReorgDoneBody", "已把 {0} 个 Mod 移进角色文件夹"), movedModsCount),
                        TimeSpan.FromSeconds(5));
            }
            finally
            {
                _navigationViewService.IsEnabled = true;
            }
        }
    }


    private async Task RestartAppAsync(int delay = 2)
    {
        _navigationViewService.IsEnabled = false;

        await Task.Delay(TimeSpan.FromSeconds(delay));

        await _lifeCycleService.RestartAsync(notifyOnError: true);
    }

    private bool CanStartElevator()
    {
        return ElevatorService.ElevatorStatus == ElevatorStatus.NotRunning;
    }

    [RelayCommand(CanExecute = nameof(CanStartElevator))]
    private async Task StartElevator()
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = _localizer.GetLocalizedStringOrDefault("/Settings/StartElevatorDialogText") ??
                   "Press Start to launch the Elevator. The Elevator is an elevated (admin) process that is used for communication with the Genshin game process.\n\n" +
                   "While the Elevator is active, you can press F10 within this App to refresh active mods in Genshin.\n\n" +
                   "Enabling and disabling mods will also automatically refresh active mods in Genshin " +
                   "The Elevator process should automatically close when this program is closed.\n\n" +
                   "After pressing Start, a User Account Control (UAC) prompt will appear to confirm the elevation.\n\n" +
                   "(This requires that Genshin and that 3Dmigoto is running, when pressing F10\n\n" +
                   "Check the FAQ on the OMniX github to download it separately as it gets flagged as malware.",
            Margin = new Thickness(0, 0, 0, 12),
            IsTextSelectionEnabled = true
        };


        var doNotShowAgainCheckBox = new CheckBox
        {
            Content = _localizer.GetLocalizedStringOrDefault("/Settings/StartElevatorDialogDontShowContent") ??
                      "Don't Show this Again",
            IsChecked = false
        };

        var stackPanel = new StackPanel
        {
            Children =
            {
                text,
                doNotShowAgainCheckBox
            }
        };


        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/Settings/StartElevatorDialogTitle") ??
                    "Start Elevator Process?",
            Content = stackPanel,
            DefaultButton = ContentDialogButton.Primary,
            CloseButtonText = "Cancel",
            PrimaryButtonText = "Start",
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var start = true;

        if (_showElevatorStartDialog)
        {
            var result = await dialog.ShowAsync();
            start = result == ContentDialogResult.Primary;
            if (start)
                _showElevatorStartDialog = !doNotShowAgainCheckBox.IsChecked == true;
        }

        if (start && ElevatorService.ElevatorStatus == ElevatorStatus.NotRunning)
            try
            {
                ElevatorService.StartElevator();
            }
            catch (Win32Exception e)
            {
                _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ElevatorFailedTitle", "无法启动 Elevator"), e.Message, TimeSpan.FromSeconds(10));
                _showElevatorStartDialog = true;
            }
    }

    /// <summary>
    ///     路径重置能不能点：只要"确实记过东西"就允许。
    ///
    ///     原来判断的是 ProcessStatus != NotInitialized。问题是没手动指定过 exe 的人
    ///     （路径靠「XXMI 文件夹」自动推导的那批），状态一直是 NotInitialized，
    ///     按钮就永远是灰的、点不动 —— 而他们恰恰是最需要清一次让程序重新找的人。
    /// </summary>
    private bool CanResetGenshinExePath()
    {
        return !string.IsNullOrWhiteSpace(GenshinProcessManager.ProcessPath) || HasLauncherFolder();
    }

    /// <summary>是否记过「XXMI 文件夹」。有它就能重新推导启动程序 / 游戏程序。</summary>
    private bool HasLauncherFolder()
    {
        return !string.IsNullOrWhiteSpace(_modManagerOptions?.GimiRootFolderPath);
    }

    [RelayCommand(CanExecute = nameof(CanResetGenshinExePath))]
    private async Task ResetGenshinExePath()
    {
        var hadPath = !string.IsNullOrWhiteSpace(GenshinProcessManager.ProcessPath);

        await GenshinProcessManager.ResetProcessOptions();

        // 清完必须给一句话。原来这个命令静默跑完，界面上一点变化都没有，
        // 用户点了不知道发生了什么，反馈过来就是"这个功能无效"。
        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/GenshinPathResetTitle", "已重置游戏程序路径"),
            hadPath
                ? _localizer.GetLocalizedStringOrDefault("/SettingsUi/GenshinPathResetHad", "记下的游戏程序已清掉，下次启动游戏时会重新定位，找不到会让你重新指定")
                : _localizer.GetLocalizedStringOrDefault("/SettingsUi/GenshinPathResetNone", "当前没有记下的游戏程序路径，下次启动游戏时会重新定位"),
            TimeSpan.FromSeconds(7));
    }

    private bool CanReset3DmigotoPath()
    {
        return !string.IsNullOrWhiteSpace(ThreeDMigtoProcessManager.ProcessPath) || HasLauncherFolder();
    }

    [RelayCommand(CanExecute = nameof(CanReset3DmigotoPath))]
    private async Task Reset3DmigotoPath()
    {
        var hadPath = !string.IsNullOrWhiteSpace(ThreeDMigtoProcessManager.ProcessPath);

        await ThreeDMigtoProcessManager.ResetProcessOptions();

        // 启动器 exe 本来就是从「XXMI 文件夹」里推导出来的，
        // 记录清掉之后，下次会按那个文件夹重新识别一遍。
        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/LoaderPathResetTitle", "已重置启动器路径"),
            hadPath
                ? _localizer.GetLocalizedStringOrDefault("/SettingsUi/LoaderPathResetHad", "记下的启动程序已清掉，下次会按「XXMI 文件夹」重新识别")
                : _localizer.GetLocalizedStringOrDefault("/SettingsUi/LoaderPathResetNone", "当前没有记下的启动程序，下次会按「XXMI 文件夹」重新识别"),
            TimeSpan.FromSeconds(7));
    }

    private void UpdateCheckerOnNewVersionAvailable(object? sender, UpdateChecker.NewVersionEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Version == new Version())
            {
                CanIgnoreUpdate = _updateChecker.LatestRetrievedVersion != _updateChecker.IgnoredVersion;
                return;
            }

            LatestVersion = VersionFormatter(e.Version);
        });
    }

    private static string VersionFormatter(Version version)
    {
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    [RelayCommand(CanExecute = nameof(CanIgnoreUpdate))]
    private async Task IgnoreNewVersion()
    {
        await _updateChecker.IgnoreCurrentVersionAsync();
    }

    /// <summary>
    ///     二开：新的导出面板 —— 按角色 / 全量选，zip 或文件夹，支持暂停与取消。
    /// </summary>
    [RelayCommand]
    private async Task OpenModExportPanelAsync()
    {
        await _windowManagerService.ShowDialogAsync(new ModExportDialog());
    }

    [ObservableProperty] private bool _exportingMods = false;
    [ObservableProperty] private int _exportProgress = 0;
    [ObservableProperty] private string _exportProgressText = string.Empty;
    [ObservableProperty] private string? _currentModName;

    [RelayCommand]
    private async Task ExportMods(ContentDialog contentDialog)
    {
        var dialog = new ContentDialog()
        {
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ExportPrimary", "导出"),
            IsPrimaryButtonEnabled = true,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ExportTitle", "导出 Mod");

        dialog.ContentTemplate = contentDialog.ContentTemplate;

        var model = new ExportModsDialogModel(_gameService.GetAllModdableObjects());
        dialog.DataContext = model;
        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result != ContentDialogResult.Primary)
            return;

        var folderPicker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null)
            return;

        ExportingMods = true;
        _navigationViewService.IsEnabled = false;

        var charactersToExport =
            model.CharacterModsToBackup.Where(modList => modList.IsChecked).Select(ch => ch.Character);
        var modsList = new List<ICharacterModList>();
        foreach (var character in charactersToExport)
            modsList.Add(_skinManagerService.GetCharacterModList(character.InternalName));

        try
        {
            _skinManagerService.ModExportProgress += HandleProgressEvent;
            await Task.Run(() =>
            {
                _skinManagerService.ExportMods(modsList, folder.Path,
                    removeLocalOmniXSettings: model.RemoveOmniXSettings, zip: false,
                    keepCharacterFolderStructure: model.KeepFolderStructure, setModStatus: model.SetModStatus);
            });
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ExportDoneTitle", "Mod 已导出"),
                string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ExportDoneBody", "Mod 已导出 to {0}"), folder.Path),
                TimeSpan.FromSeconds(5));
        }
        catch (Exception e)
        {
            _logger.Error(e, "导出 Mod 失败");
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ExportFailedTitle", "导出 Mod 失败"), e.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            _skinManagerService.ModExportProgress -= HandleProgressEvent;
            ExportingMods = false;
            _navigationViewService.IsEnabled = true;
        }
    }

    private void HandleProgressEvent(object? sender, ExportProgress args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            ExportProgress = args.Progress;
            ExportProgressText = args.Operation;
            CurrentModName = args.ModName;
        });
    }


    [RelayCommand]
    private async Task SelectLanguage(string selectedLanguageName)
    {
        if (_nameToLangCode.TryGetValue(selectedLanguageName, out var langCode))
        {
            if (langCode == _localizer.CurrentLanguage.LanguageCode)
                return;

            var restartDialog = new ContentDialog()
            {
                Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ThemeRestartTitle", "需要重启"),
                Content = new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("/Settings/ChangeLanguageDialogText",
                        defaultValue:
                        "切换语言需要重启应用，这样才能按选好的语言重新加载。\n\n" +
                        "要切换语言吗？"),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    IsTextSelectionEnabled = true
                },
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/LangSwitchPrimary", "切换语言并重启"),
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消"),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await _windowManagerService.ShowDialogAsync(restartDialog);

            var currentLanguage = _localizer.CurrentLanguage.LanguageName;
            if (result != ContentDialogResult.Primary)
            {
                SelectedLanguage = currentLanguage;
                return;
            }

            await _localizer.SetLanguageAsync(langCode);

            var appSettings = await _localSettingsService.ReadOrCreateSettingAsync<AppSettings>(AppSettings.Key);
            appSettings.Language = langCode;
            await _localSettingsService.SaveSettingAsync(AppSettings.Key, appSettings);
            currentLanguage = _localizer.CurrentLanguage.LanguageName;
            SelectedLanguage = currentLanguage;

            await RestartAppAsync();
        }
    }

    /// <summary>
    ///     二开：设置页「检查更新」—— 点一下就查 GitHub。
    ///     有新版本时顺便判断是补丁（只动修订号）还是新版本（次版本号变了）。
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (IsCheckingUpdate)
            return;

        IsCheckingUpdate = true;
        UpdateCheckMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/UpdateChecking", "正在检查…");

        try
        {
            var (latest, hasUpdate) = await _updateChecker.CheckNowAsync();

            if (latest is null)
            {
                UpdateCheckMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/UpdateFailGitHub", "检查失败：连不上 GitHub");
                return;
            }

            LatestVersion = VersionFormatter(latest);

            if (!hasUpdate)
            {
                ShowNewVersionAvailable = false;
                UpdateCheckMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/UpdateAlreadyLatest", "已经是最新版本");
                return;
            }

            var current = _updateChecker.CurrentVersion;

            UpdateKindText = latest.Major != current.Major || latest.Minor != current.Minor
                ? _localizer.GetLocalizedStringOrDefault("/SettingsUi/UpdateKindMajor", "新版本")
                : _localizer.GetLocalizedStringOrDefault("/SettingsUi/UpdateKindPatch", "补丁");

            ShowNewVersionAvailable = true;
            CanIgnoreUpdate = latest != _updateChecker.IgnoredVersion;
            UpdateCheckMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/UpdateFoundPrefix", "发现") + UpdateKindText + " " + VersionFormatter(latest);
        }
        catch (Exception e)
        {
            _logger.Error(e, "检查更新失败");
            UpdateCheckMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/UpdateFailPrefix", "检查失败：") + e.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>
    ///     二开：设置页「导入本地更新包」。
    ///     网络下不动的时候，用户可以自己去 Release 页面把 zip 下回来，
    ///     在这儿选文件就能装 —— 自动认出是完整包还是增量补丁、是哪个版本。
    /// </summary>
    [RelayCommand]
    private async Task ImportLocalUpdateAsync()
    {
        if (IsImportingUpdate)
            return;

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            CommitButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportCommit", "用这个包更新")
        };
        picker.FileTypeFilter.Add(".zip");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        IsImportingUpdate = true;
        ImportMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportIdentifying", "正在识别…");

        try
        {
            var info = InspectLocalZip(file.Path);

            if (info is null)
            {
                ImportMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportNotPackage", "这不像 OMniX 更新包（包里得有 OMniX 文件夹）");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportInstallTitle", "安装本地更新包"),
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportInstallPrimary", "安装并重启"),
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text =
                    string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportIdentified",
                        "识别结果：OMniX {0} · {1}"),
                        info.Value.Version,
                        info.Value.IsPatch
                            ? _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportPatch", "增量补丁")
                            : _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportFull", "完整包")),
                TextWrapping = TextWrapping.WrapWholeWords,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text =
                    string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportFiles",
                        "包含 {0} 个文件，会覆盖到当前程序目录。\n" +
                        "OMniX 目录里其它文件一个都不会删。装完 OMniX 会自己重新打开。"),
                        info.Value.FileCount),
                TextWrapping = TextWrapping.WrapWholeWords
            });

            dialog.Content = panel;

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                ImportMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportCanceled", "已取消");
                return;
            }

            // 先复制到临时目录再交给更新器：原文件还在下载目录里，
            // 万一被用户挪走或清理，更新器装到一半就断了
            // 二开：暂存目录也从 %TEMP% 挪到程序根目录。
            // 作者的原话是「与 OMniX 相关的统一放在安装根目录，不然每次都找不到路径」——
            // 放在程序目录下，出问题时一眼就能看到包在哪、装到哪了。
            var staged = Path.Combine(App.ROOT_DIR, "OMniX Updates", file.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(file.Path, staged, true);

            ImportMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportStartingUpdater", "正在启动更新器…");

            var errors = _autoUpdaterService.StartSelfUpdateProcess(staged);

            if (errors is not null)
                ImportMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportStartFailed", "启动更新器失败");
        }
        catch (Exception e)
        {
            _logger.Error(e, "导入本地更新包失败");
            ImportMessage = _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportFailPrefix", "导入失败：") + e.Message;
        }
        finally
        {
            IsImportingUpdate = false;
        }
    }

    /// <summary>
    ///     二开：看一眼本地 zip 是不是 OMniX 更新包、是完整包还是增量补丁。
    ///     判断依据：包里得有 OMniX\ 目录；文件名带 -patch- 的就是补丁，
    ///     否则按文件数量分 —— 完整包上千个文件，补丁只有十几个。
    /// </summary>
    private (string Version, bool IsPatch, int FileCount)? InspectLocalZip(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            if (entries.Count == 0)
                return null;

            // 顶层目录名，形如 OMniX\
            var root = entries
                .Select(e => e.FullName.Replace('\\', '/'))
                .Select(n => n.Contains('/') ? n.Split('/')[0] : string.Empty)
                .FirstOrDefault(n => n.StartsWith("OMniX", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(root))
                return null;

            var inner = entries
                .Count(e => e.FullName.Replace('\\', '/').StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));

            if (inner == 0)
                return null;

            var fileName = Path.GetFileName(zipPath);
            var version = Regex.Match(fileName, @"OMniX(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);

            var isPatch = fileName.Contains("-patch-", StringComparison.OrdinalIgnoreCase) || inner < 300;

            return (version.Success ? version.Groups[1].Value : _localizer.GetLocalizedStringOrDefault("/SettingsUi/ImportUnknownVersion", "未知"), isPatch, inner);
        }
        catch
        {
            // 不是 zip、文件损坏、没权限读 —— 都当成「不是更新包」
            return null;
        }
    }

    [RelayCommand]
    private void UpdateOmniX()
    {
        var errors = Array.Empty<Error>();
        try
        {
            errors = _autoUpdaterService.StartSelfUpdateProcess();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error starting update process");
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/AutoUpdateFailTitle", "启动更新失败"), e.Message, TimeSpan.FromSeconds(10));
        }

        if (errors is not null && errors.Any())
        {
            var errorMessages = errors.Select(e => e.Description).ToArray();
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/SettingsUi/AutoUpdateUnavailableTitle", "无法启动更新"), string.Join('\n', errorMessages),
                TimeSpan.FromSeconds(10));
        }
    }


    [RelayCommand]
    private async Task SelectGameAsync(string? game)
    {
        var omnixSelectedGame = await _selectedGameService.GetSelectedGameAsync();

        if (game.IsNullOrEmpty() || game == omnixSelectedGame)
            return;

        var switchGameDialog = new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/SwitchGameTitle", "切换游戏"),
            Content = new TextBlock()
            {
                Text =
                    _localizer.GetLocalizedStringOrDefault("/SettingsUi/SwitchGameContent",
                        "切换游戏会重启应用，这样才能按新游戏重新配置。\n\n" +
                        "要切换吗？"),
                TextWrapping = TextWrapping.WrapWholeWords
            },

            PrimaryButtonText = string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/SwitchGamePrimary", "切换到 {0}"), game),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(switchGameDialog);

        if (result != ContentDialogResult.Primary)
        {
            SelectedGame = game;
            return;
        }

        await _selectedGameService.SetSelectedGame(game);
        await RestartAppAsync(0).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ToggleCharacterSkinsAsCharacters()
    {
        var modManagerOptions =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        var result = await new CharacterSkinsDialog().ShowDialogAsync(modManagerOptions.CharacterSkinsAsCharacters);

        if (result != ContentDialogResult.Primary)
        {
            CharacterAsSkinsCheckbox = modManagerOptions.CharacterSkinsAsCharacters;
            return;
        }


        modManagerOptions.CharacterSkinsAsCharacters = !modManagerOptions.CharacterSkinsAsCharacters;

        await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section, modManagerOptions);

        CharacterAsSkinsCheckbox = modManagerOptions.CharacterSkinsAsCharacters;

        await RestartAppAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private Task NavigateToCommandsSettings()
    {
        _navigationService.NavigateTo(typeof(CommandsSettingsViewModel).FullName!,
            transitionInfo: new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ToggleModUpdateChecker()
    {
        var modUpdateCheckerSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
                BackGroundModCheckerSettings.Key);

        await Task.Run(async () =>
        {
            if (modUpdateCheckerSettings.Enabled)
                await _modUpdateAvailableChecker.DisableAutoCheckerAsync();
            else
                await _modUpdateAvailableChecker.EnableAutoCheckerAsync();

            await Task.Delay(1000).ConfigureAwait(false);
        });

        modUpdateCheckerSettings = await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
            BackGroundModCheckerSettings.Key);

        IsModUpdateCheckerEnabled = modUpdateCheckerSettings.Enabled;
    }

    public async void OnNavigatedTo(object parameter)
    {
        SelectedGame = await _selectedGameService.GetSelectedGameAsync();

        // 进页面顺手认一遍启动方式，玩家一眼能看到现在会用哪个启动器
        try
        {
            await RefreshLaunchInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "识别启动方式失败");
        }

        var modUpdateCheckerOptions =
            await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
                BackGroundModCheckerSettings.Key);

        IsModUpdateCheckerEnabled = modUpdateCheckerOptions.Enabled;
        var gameInfo = await GameService.GetGameInfoAsync(Enum.Parse<SupportedGames>(SelectedGame));

        if (gameInfo is not null)
        {
            // 二开：不再检查 WWMI Loader.exe，改用 XXMI 结构判定（根目录 / 导入器子目录都算）
            PathToGIMIFolderPicker.SetValidators(
                LauncherRootValidators.Validators(gameInfo.GameModelImporterShortName));
        }

        var windowSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);

        var characterDetailsSettings = await _localSettingsService.ReadCharacterDetailsSettingsAsync(SettingScope.App);

        PersistWindowSize = windowSettings.PersistWindowSize;
        PersistWindowPosition = windowSettings.PersistWindowPosition;
        await GenshinProcessManager.TryInitialize();
        await ThreeDMigtoProcessManager.TryInitialize();
        ModCacheSizeGB = _modArchiveRepository.GetTotalCacheSizeInGB().ToString("F");
    }

    [ObservableProperty] private string _maxCacheSizeString = string.Empty;

    private void SetCacheString(int value)
    {
        MaxCacheSizeString = $"{value} GB";
    }

    [RelayCommand]
    private async Task SetCacheLimit(int maxValue)
    {
        var modArchiveSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

        modArchiveSettings.MaxLocalArchiveCacheSizeGb = maxValue;

        await _localSettingsService.SaveSettingAsync(ModArchiveSettings.Key, modArchiveSettings);

        MaxCacheLimit = maxValue;
        SetCacheString(maxValue);
    }


    /// <summary>
    ///     二开：直接打开 Mods 文件夹，省得从资源管理器一层层点进去。
    /// </summary>
    [RelayCommand]
    private void OpenModsFolder()
    {
        var path = PathToModsFolderPicker?.Path;

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to open mods folder {Path}", path);
        }
    }

    /// <summary>
    ///     二开：打开程序所在目录。
    /// </summary>
    [RelayCommand]
    private void OpenAppFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = App.ROOT_DIR,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to open app folder");
        }
    }

    /// <summary>
    ///     二开：清空下载缓存。只删缓存目录里的文件，不动 Mods 本体。
    /// </summary>
    [RelayCommand]
    private async Task ClearModCacheAsync()
    {
        var dir = ArchiveCacheFolderPath?.LocalPath;

        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        await Task.Run(() =>
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 被占用的文件跳过，不影响其它
                }
            }
        }).ConfigureAwait(false);

        ModCacheSizeGB = _modArchiveRepository.GetTotalCacheSizeInGB().ToString("F");
    }

    [RelayCommand]
    private static Task ShowCleanModsFolderDialogAsync()
    {
        var dialog = new ClearEmptyFoldersDialog();
        return dialog.ShowDialogAsync();
    }


    [RelayCommand]
    private Task ShowDisableAllModsDialogAsync()
    {
        var dialog = new DisableAllModsDialog();
        return dialog.ShowDialogAsync();
    }

    // ==================== 多内核外壳：启动方式 / 数据目录 / 游戏资源 ====================

    [ObservableProperty] private string _launchModeText = string.Empty;
    [ObservableProperty] private string _launchExecutableText = string.Empty;
    [ObservableProperty] private string _launchStatusText = string.Empty;
    [ObservableProperty] private string _gameDataRootText = string.Empty;
    [ObservableProperty] private bool _hasLegacyData;

    /// <summary>x:Bind 不做 bool → Visibility 的隐式转换，这里直接给个 Visibility</summary>
    public Visibility HasLegacyDataVisibility => HasLegacyData ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty] private int _selectedLaunchModeIndex;
    [ObservableProperty] private ObservableCollection<string> _launchModeOptions = new();

    /// <summary>当前设置页选中的游戏对应的内核</summary>
    private OMniX.WinUI.Helpers.GameDefinition CurrentGameDefinition
    {
        get
        {
            var core = Enum.TryParse<SupportedGames>(SelectedGame, true, out var parsed)
                ? parsed
                : SupportedGames.WuWa;

            return OMniX.WinUI.Helpers.GameCatalog.ByCore(core) ?? OMniX.WinUI.Helpers.GameCatalog.Fallback;
        }
    }

    /// <summary>
    ///     进设置页时把这一游戏的启动方式认一遍。
    ///     认到什么，左下角「启动游戏」就按什么拉起。
    /// </summary>
    public async Task RefreshLaunchInfoAsync()
    {
        var game = CurrentGameDefinition;

        LaunchModeOptions.Clear();
        LaunchModeOptions.Add(_localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeAuto", "自动（跟随游戏默认）"));
        LaunchModeOptions.Add(_localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeOfficial", "官方启动器"));
        LaunchModeOptions.Add(_localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeXxmi", "XXMI 启动器"));
        LaunchModeOptions.Add(_localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeHoYo", "HoYo 启动器"));

        var configured = _launchResolver.GetConfiguredMode(game);
        SelectedLaunchModeIndex = configured is null ? 0 : (int)configured.Value + 1;

        GameDataRootText = _dataRoot.GamesDataRoot;
        HasLegacyData = GameDataRootService.HasLegacyData();

        await DetectLaunchModeAsync();
    }

    private string ModeName(OMniX.WinUI.Helpers.GameLaunchMode mode) => mode switch
    {
        OMniX.WinUI.Helpers.GameLaunchMode.Official =>
            _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeOfficial", "官方启动器"),
        OMniX.WinUI.Helpers.GameLaunchMode.Xxmi =>
            _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeXxmi", "XXMI 启动器"),
        OMniX.WinUI.Helpers.GameLaunchMode.HoYo =>
            _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeHoYo", "HoYo 启动器"),
        _ => _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchModeManual", "手动指定")
    };

    /// <summary>重新识别当前游戏的启动方式</summary>
    [RelayCommand]
    private async Task DetectLaunchModeAsync()
    {
        var game = CurrentGameDefinition;
        var options = await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);
        var resolution = _launchResolver.Resolve(game, options);

        LaunchModeText = ModeName(resolution.Mode);

        if (resolution.Found)
        {
            LaunchExecutableText = resolution.Executable;
            LaunchStatusText = resolution.IsManual
                ? _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchStatusManual", "用的是你手动指定的启动器")
                : _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchStatusAuto", "自动识别到的，点左下角「启动游戏」就会用它");
        }
        else
        {
            LaunchExecutableText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchNotFound", "没找到");
            LaunchStatusText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/LaunchNotFoundHint",
                "自动识别没找到启动器，可以点「手动添加」自己指定一个 exe");
        }
    }

    /// <summary>下拉选启动方式；选「自动」就是交回给程序自己认</summary>
    [RelayCommand]
    private async Task SetLaunchModeAsync(int index)
    {
        var game = CurrentGameDefinition;
        var mode = index <= 0
            ? (OMniX.WinUI.Helpers.GameLaunchMode?)null
            : (OMniX.WinUI.Helpers.GameLaunchMode)(index - 1);

        await _launchResolver.SetConfiguredModeAsync(game, mode);
        await DetectLaunchModeAsync();
    }

    /// <summary>
    ///     手动指定启动器。文件名是什么都行 —— 官方的、第三方的都能用，
    ///     所以这里不写死文件名，玩家选了什么就用什么。
    /// </summary>
    [RelayCommand]
    private async Task BrowseLauncherAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();

            if (file is null)
                return;

            await _launchResolver.SetManualPathAsync(CurrentGameDefinition, file.Path);
            await DetectLaunchModeAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "手动选择启动器失败");
        }
    }

    /// <summary>清掉手动指定的启动器，回到自动识别</summary>
    [RelayCommand]
    private async Task ClearLauncherAsync()
    {
        await _launchResolver.SetManualPathAsync(CurrentGameDefinition, string.Empty);
        await DetectLaunchModeAsync();
    }

    /// <summary>
    ///     游戏资源管理：列出这个游戏相关的可清理项，玩家勾选后送回收站。
    ///     只碰管理器自己生成的东西 —— 玩家的 Mod 和非官方启动器本体一律不动。
    /// </summary>
    [RelayCommand]
    private async Task ShowGameResourceCleanupAsync()
    {
        var gameId = CurrentGameDefinition.Id;
        var candidates = BuildCleanupCandidates(gameId);

        if (candidates.Count == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupNothingTitle", "没有可清理的内容"),
                _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupNothingBody", "这个游戏的数据目录还没生成"),
                TimeSpan.FromSeconds(5));
            return;
        }

        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupIntro",
                "只列出管理器自己生成的东西。你自己的 Mod 文件、XXMI 这类非官方启动器本体都不在里面，也不会被删。"),
            TextWrapping = TextWrapping.WrapWholeWords
        });

        var boxes = new List<(CheckBox Box, string Path)>();

        foreach (var candidate in candidates)
        {
            var box = new CheckBox
            {
                Content = candidate.Label + "\n" + candidate.Path,
                IsChecked = false,
                FontSize = 12
            };

            panel.Children.Add(box);
            boxes.Add((box, candidate.Path));
        }

        var permanent = new CheckBox
        {
            Content = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupPermanent", "永久删除（不进回收站）"),
            IsChecked = false
        };

        panel.Children.Add(permanent);

        var scroll = new ScrollViewer { Content = panel, MaxHeight = 320 };

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupTitle", "清理这个游戏的数据"),
            Content = scroll,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupPrimary", "删除勾选的项目"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/Cancel", "取消"),
            DefaultButton = ContentDialogButton.Close
        };

        if (await _windowManagerService.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
            return;

        var removed = 0;

        foreach (var (box, path) in boxes)
        {
            if (box.IsChecked != true)
                continue;

            try
            {
                if (permanent.IsChecked == true)
                    OMniX.WinUI.Helpers.RecycleBin.DeleteDirectory(path);
                else
                    OMniX.WinUI.Helpers.RecycleBin.SendDirectory(path);

                removed++;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "清理失败：{Path}", path);
            }
        }

        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupDoneTitle", "清理完成"),
            string.Format(_localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupDoneBody",
                "处理了 {0} 项。勾了回收站的可以在回收站里找回。"), removed),
            TimeSpan.FromSeconds(6));
    }

    /// <summary>
    ///     可清理项：新旧两套位置都看一眼，存在的才列出来。
    /// </summary>
    private List<(string Label, string Path)> BuildCleanupCandidates(string gameId)
    {
        var result = new List<(string, string)>();
        var root = _dataRoot.GameRoot(gameId);
        var legacy = Path.Combine(GameDataRootService.LegacyAppDataRoot, "ApplicationData_" + CurrentGameDefinition.Core);

        void Add(string label, params string[] paths)
        {
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                    result.Add((label, path));
            }
        }

        var characterCache = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupCharacterCache", "角色列表缓存");
        var modIndex = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupModIndex", "Mod 索引数据库");
        var presets = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupPresets", "预设配置");
        var plugins = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupPlugins", "已下载的功能插件");
        var logs = _localizer.GetLocalizedStringOrDefault("/SettingsUi/CleanupLogs", "日志 / 缓存");

        Add(characterCache, Path.Combine(root, "Cache", "Characters"), Path.Combine(legacy, "Cache", "Characters"));
        Add(modIndex, Path.Combine(root, "Cache", "ModIndex"), Path.Combine(legacy, "Cache", "ModIndex"));
        Add(presets, Path.Combine(root, "Presets"), Path.Combine(legacy, "Presets"));
        Add(plugins, Path.Combine(root, "Plugins"), Path.Combine(GameDataRootService.LegacyAppDataRoot, "Plugins"));
        Add(logs, Path.Combine(root, "Logs"), Path.Combine(root, "Cache"), Path.Combine(legacy, "Logs"));

        return result;
    }

    /// <summary>设置页手动触发一次性迁移（启动时的弹窗选了「以后再说」就来这里）</summary>
    [RelayCommand]
    private async Task MigrateLegacyDataAsync()
    {
        if (!GameDataRootService.HasLegacyData())
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateNothingTitle", "没有旧数据"),
                _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateNothingBody", "C 盘上没找到旧版本的 OMniX 数据"),
                TimeSpan.FromSeconds(5));
            return;
        }

        var status = new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateRunning", "正在复制文件…"),
            TextWrapping = TextWrapping.WrapWholeWords
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(status);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 320 });

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/LegacyMigrateTitle", "迁移旧数据"),
            Content = panel
        };

        var showing = _windowManagerService.ShowDialogAsync(dialog);
        MigrationResult migration;

        try
        {
            var progress = new Progress<MigrationProgress>(update =>
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    status.Text = string.Format(
                        _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateItem", "正在搬：{0}（{1}/{2}）"),
                        update.CurrentName, update.DoneFiles, update.TotalFiles)));

            migration = await Task.Run(async () => await _dataRoot.MigrateAsync(progress));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "设置页迁移失败");
            migration = new MigrationResult { Success = false, Message = ex.Message };
        }
        finally
        {
            dialog.Hide();
        }

        try
        {
            await showing;
        }
        catch (Exception)
        {
            // 关窗异常忽略
        }

        // 没搬成（或校验没过）就把两个路径原样摆出来，让玩家自己拷 —— 不能只丢一句“失败”就完事
        if (migration.CopiedItems.Count == 0 || migration.Failures.Count > 0 || !migration.Verified)
            await ShowManualMigrationHintAsync(migration);
        else
            await AskDeleteLegacyAsync(migration);
    }

    private async Task ShowManualMigrationHintAsync(MigrationResult migration)
    {
        var lines = new StackPanel { Spacing = 8 };

        lines.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateManualIntro",
                "自动迁移没能全部完成，你可以自己搬：把旧数据文件夹里的东西复制到新目录。"),
            TextWrapping = TextWrapping.WrapWholeWords
        });

        foreach (var item in migration.Failures)
        {
            lines.Children.Add(new TextBlock
            {
                Text = $"{item.Source}  →  {item.Target}\n{item.Error}",
                FontSize = 12,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.WrapWholeWords
            });
        }

        lines.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateManualPaths", "旧数据：") +
                   GameDataRootService.LegacyAppDataRoot +
                   "\n" +
                   _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateManualTarget", "搬到：") +
                   _dataRoot.GamesDataRoot,
            FontSize = 12,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        });

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateManualTitle", "可以手动迁移"),
            Content = lines,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateGotIt", "我知道了")
        };

        await _windowManagerService.ShowDialogAsync(dialog);
    }

    /// <summary>搬完并校验通过才问要不要清掉 C 盘那份 —— 默认不清。</summary>
    private async Task AskDeleteLegacyAsync(MigrationResult migration)
    {
        var checkBox = new CheckBox
        {
            Content = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateDeleteOld",
                "迁移已校验，删除 C 盘上的旧数据"),
            IsChecked = false
        };

        var permanent = new CheckBox
        {
            Content = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigratePermanent", "永久删除（不进回收站）"),
            IsChecked = false
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(
                _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateDoneBody",
                    "已经把 {0} 个文件夹搬到：\n{1}\n\n旧数据还在 C 盘，删不删随你。"),
                migration.CopiedItems.Count, _dataRoot.GamesDataRoot),
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });
        panel.Children.Add(checkBox);
        panel.Children.Add(permanent);

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateDoneTitle", "迁移完成"),
            Content = panel,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateConfirm", "确定"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/SettingsUi/MigrateCancel", "取消")
        };

        if (await _windowManagerService.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
            return;

        if (checkBox.IsChecked != true)
            return;

        foreach (var item in migration.CopiedItems)
        {
            try
            {
                if (permanent.IsChecked == true)
                    OMniX.WinUI.Helpers.RecycleBin.DeleteDirectory(item.Source);
                else
                    OMniX.WinUI.Helpers.RecycleBin.SendDirectory(item.Source);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "删除旧数据失败：{Path}", item.Source);
            }
        }

        HasLegacyData = GameDataRootService.HasLegacyData();
        OnPropertyChanged(nameof(HasLegacyDataVisibility));
    }

    public void OnNavigatedFrom()
    {
    }
}

public partial class ExportModsDialogModel : ObservableObject
{
    [ObservableProperty] private bool _zipMods = false;
    [ObservableProperty] private bool _keepFolderStructure = true;

    [ObservableProperty] private bool _removeOmniXSettings = false;

    public ObservableCollection<CharacterCheckboxModel> CharacterModsToBackup { get; set; } = new();

    public ObservableCollection<SetModStatus> SetModStatuses { get; set; } = new()
    {
        SetModStatus.KeepCurrent,
        SetModStatus.EnableAllMods,
        SetModStatus.DisableAllMods
    };

    [ObservableProperty] private SetModStatus _setModStatus = SetModStatus.KeepCurrent;

    public ExportModsDialogModel(IEnumerable<IModdableObject> characters)
    {
        SetModStatus = SetModStatus.KeepCurrent;
        foreach (var character in characters) CharacterModsToBackup.Add(new CharacterCheckboxModel(character));
    }
}

public partial class CharacterCheckboxModel : ObservableObject
{
    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private IModdableObject _character;

    public CharacterCheckboxModel(IModdableObject character)
    {
        _character = character;
    }
}