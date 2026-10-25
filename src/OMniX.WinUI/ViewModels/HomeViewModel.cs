using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using OMniX.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Models;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using System.Collections.ObjectModel;

namespace OMniX.WinUI.ViewModels;

/// <summary>
///     OMniX 首页：卡片墙 + 当前游戏 Hero + 引导卡 + 插件专区入口
/// </summary>
public partial class HomeViewModel : ObservableRecipient, INavigationAware
{
    private readonly GenshinProcessManager _genshinProcessManager;
    private readonly IGameService _gameService;
    private readonly LifeCycleService _lifeCycleService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly INavigationService _navigationService;
    private readonly NotificationManager _notificationManager;
    private readonly SelectedGameService _selectedGameService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly GameLaunchService _gameLaunchService;
    private readonly ILanguageLocalizer _localizer;

    /// <summary>Hero 背景图存在这个键里；没有就用内置的默认图。</summary>
    public const string HeroBackgroundKey = "Home.Hero.BackgroundImage";

    private static readonly Uri DefaultHeroBackground = new("ms-appx:///Assets/Heroes/wuthering-waves.png");

    [ObservableProperty] private ImageSource? _gameIconImage = null;
    [ObservableProperty] private string _gameIconPath = string.Empty;
    [ObservableProperty] private ImageSource? _heroBackgroundImage = null;
    [ObservableProperty] private bool _hasCustomHeroBackground;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private bool _isGameReady;
    [ObservableProperty] private string _launchGameText = string.Empty;
    [ObservableProperty] private string _modsSummary = string.Empty;
    [ObservableProperty] private GameSwitchItem? _selectedGame = null;
    [ObservableProperty] private string _selectedGameName = "OMniX";

    /// <summary>用户换的背景图都收在这儿，原图移走了也不影响。</summary>
    public static string HeroBackgroundFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX", "Heroes");

    public ObservableCollection<GameSwitchItem> Games { get; } = new();

    /// <summary>
    ///     还没配置游戏 / Mods 路径时，首页顶部显示引导卡
    /// </summary>
    public Visibility SetupHintVisibility => IsConfigured ? Visibility.Collapsed : Visibility.Visible;

    partial void OnIsConfiguredChanged(bool value) => OnPropertyChanged(nameof(SetupHintVisibility));

    public HomeViewModel(INavigationService navigationService, IGameService gameService,
        ILocalSettingsService localSettingsService, SelectedGameService selectedGameService,
        GenshinProcessManager genshinProcessManager, IThemeSelectorService themeSelectorService,
        LifeCycleService lifeCycleService, NotificationManager notificationManager,
        GameLaunchService gameLaunchService, ILanguageLocalizer localizer)
    {
        _navigationService = navigationService;
        _gameService = gameService;
        _localSettingsService = localSettingsService;
        _selectedGameService = selectedGameService;
        _genshinProcessManager = genshinProcessManager;
        _themeSelectorService = themeSelectorService;
        _lifeCycleService = lifeCycleService;
        _notificationManager = notificationManager;
        _gameLaunchService = gameLaunchService;
        _localizer = localizer;

        LaunchGameText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Home_LaunchGameText", "启动游戏");
        ModsSummary = _localizer.GetLocalizedStringOrDefault("/HomePreset/Home_ModsSummaryNotConfigured", "尚未配置");
        IsDarkTheme = _themeSelectorService.Theme == ElementTheme.Dark;
        _gameService.Initialized += OnGameServiceInitialized;
    }

    public void OnNavigatedTo(object parameter)
    {
        IsBusy = true;
        App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            finally
            {
                IsBusy = false;
            }
        });
    }

    public void OnNavigatedFrom()
    {
    }

    private void OnGameServiceInitialized(object? sender, EventArgs e) =>
        App.MainWindow.DispatcherQueue.TryEnqueue(async () => await RefreshAsync());

    /// <summary>
    ///     刷新配置状态、当前游戏信息、可切换游戏列表
    /// </summary>
    public async Task RefreshAsync()
    {
        ModManagerOptions? options = null;
        try
        {
            options = await _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section);
        }
        catch (Exception)
        {
            // 配置还没建立
        }

        IsConfigured = options is not null &&
                       !string.IsNullOrWhiteSpace(options.ModsFolderPath) &&
                       !string.IsNullOrWhiteSpace(options.GimiRootFolderPath);

        if (IsConfigured && Directory.Exists(options!.ModsFolderPath))
        {
            try
            {
                var count = Directory.GetDirectories(options.ModsFolderPath!).Length;
                ModsSummary = string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Home_ModsSummaryCount", "{0} 个 Mod 文件夹"), count);
            }
            catch (Exception)
            {
                ModsSummary = _localizer.GetLocalizedStringOrDefault("/HomePreset/Home_ModsSummaryConfigured", "已配置");
            }
        }
        else
        {
            ModsSummary = _localizer.GetLocalizedStringOrDefault("/HomePreset/Home_ModsSummaryNotConfigured", "尚未配置");
        }

        // 当前游戏信息（GameService 未初始化时会抛异常，属于正常情况）
        try
        {
            SelectedGameName = _gameService.GameShortName;
            GameIconPath = _gameService.GameIcon;
            GameIconImage = TryLoadImage(GameIconPath);
            IsGameReady = true;
        }
        catch (Exception)
        {
            IsGameReady = false;
            SelectedGameName = await _selectedGameService.GetSelectedGameAsync();
            GameIconPath = string.Empty;
            GameIconImage = null;
        }

        await LoadSwitchableGamesAsync();
        await LoadHeroBackgroundAsync();
    }

    /// <summary>
    ///     读一下用户换过的背景图，没有就回到内置那张。
    /// </summary>
    private async Task LoadHeroBackgroundAsync()
    {
        try
        {
            var custom = await _localSettingsService.ReadSettingAsync<string>(HeroBackgroundKey);

            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            {
                HeroBackgroundImage = TryLoadImage(custom);
                HasCustomHeroBackground = HeroBackgroundImage is not null;

                if (HasCustomHeroBackground)
                    return;
            }
        }
        catch (Exception)
        {
            // 设置读不出来就当没换过
        }

        HeroBackgroundImage = new BitmapImage(DefaultHeroBackground);
        HasCustomHeroBackground = false;
    }

    /// <summary>
    ///     把选中的图复制到应用自己的目录再记下来，
    ///     这样原图被删掉或者移动了，首页背景还在。
    /// </summary>
    public async Task<bool> SetHeroBackgroundAsync(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return false;

            Directory.CreateDirectory(HeroBackgroundFolder);

            var extension = Path.GetExtension(sourcePath);

            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";

            // 换个文件名，避免 BitmapImage 读到旧缓存
            var target = Path.Combine(HeroBackgroundFolder, $"hero-{DateTime.Now:yyyyMMddHHmmss}{extension}");
            File.Copy(sourcePath, target, true);

            foreach (var old in Directory.GetFiles(HeroBackgroundFolder, "hero-*"))
            {
                if (string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Delete(old);
                }
                catch (Exception)
                {
                    // 删不掉就留着，不影响
                }
            }

            await _localSettingsService.SaveSettingAsync(HeroBackgroundKey, target);

            HeroBackgroundImage = TryLoadImage(target);
            HasCustomHeroBackground = HeroBackgroundImage is not null;

            return HasCustomHeroBackground;
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Home_NotificationBgFailed", "换背景图失败"), e.Message, TimeSpan.FromSeconds(8));
            return false;
        }
    }

    /// <summary>清掉自定义背景，回到内置那张。</summary>
    public async Task ResetHeroBackgroundAsync()
    {
        try
        {
            await _localSettingsService.SaveSettingAsync<string?>(HeroBackgroundKey, null);

            foreach (var old in Directory.GetFiles(HeroBackgroundFolder, "hero-*"))
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception)
                {
                    // 删不掉就留着
                }
            }
        }
        catch (Exception)
        {
            // 设置写不进去也要把界面恢复成默认
        }

        HeroBackgroundImage = new BitmapImage(DefaultHeroBackground);
        HasCustomHeroBackground = false;
    }

    public void OpenHeroBackgroundFolder()
    {
        try
        {
            Directory.CreateDirectory(HeroBackgroundFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = HeroBackgroundFolder,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Home_NotificationFolderOpenFailed", "打不开文件夹"), e.Message, TimeSpan.FromSeconds(6));
        }
    }

    private async Task LoadSwitchableGamesAsync()
    {
        try
        {
            var selected = await _selectedGameService.GetSelectedGameAsync();
            var items = new List<GameSwitchItem>();

            var selectedInfo = await SafeGetGameInfoAsync(selected);
            items.Add(new GameSwitchItem
            {
                ShortName = selected,
                Name = selectedInfo?.GameName ?? selected,
                IconPath = selectedInfo?.GameIcon ?? string.Empty,
                IsSelected = true
            });

            foreach (var game in await _selectedGameService.GetNotSelectedGameAsync())
            {
                var info = await SafeGetGameInfoAsync(game.ToString());
                items.Add(new GameSwitchItem
                {
                    ShortName = game.ToString(),
                    Name = info?.GameName ?? game.ToString(),
                    IconPath = info?.GameIcon ?? string.Empty,
                    IsSelected = false
                });
            }

            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                Games.Clear();
                foreach (var item in items)
                    Games.Add(item);
                SelectedGame = items.FirstOrDefault(i => i.IsSelected) ?? items.FirstOrDefault();
            });
        }
        catch (Exception)
        {
            // 游戏资源还没准备好，列表为空即可
        }
    }

    private static ImageSource? TryLoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return new BitmapImage(new Uri(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<GameInfo?> SafeGetGameInfoAsync(string game)
    {
        try
        {
            if (Enum.TryParse<SupportedGames>(game, out var supportedGame))
                return await GameService.GetGameInfoAsync(supportedGame);
        }
        catch (Exception)
        {
            // ignored
        }

        return null;
    }

    /// <summary>
    ///     切换游戏：需要重启应用让各服务重新按游戏初始化
    /// </summary>
    [RelayCommand]
    private async Task SelectGameAsync(GameSwitchItem? target)
    {
        if (target is null || IsBusy)
            return;

        var current = await _selectedGameService.GetSelectedGameAsync();
        if (string.Equals(current, target.ShortName, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            IsBusy = true;
            await _lifeCycleService.RestartAsync(notifyOnError: true,
                postShutdownLogic: () => _selectedGameService.SetSelectedGame(target.ShortName));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Home_NotificationSwitchGameFailed", "切换游戏失败"), e.Message, TimeSpan.FromSeconds(8));
            IsBusy = false;
        }
    }

    /// <summary>
    ///     启动当前游戏。和左侧栏底部那个按钮走同一套逻辑（都只会打开官方启动器）。
    /// </summary>
    [RelayCommand]
    private Task LaunchGameAsync() => _gameLaunchService.LaunchAsync();

    /// <summary>
    ///     浅色 / 深色主题切换
    /// </summary>
    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var next = IsDarkTheme ? ElementTheme.Light : ElementTheme.Dark;
        await _themeSelectorService.SetThemeAsync(next);
        IsDarkTheme = next == ElementTheme.Dark;
    }

    [RelayCommand]
    private void OpenSetup() => _navigationService.NavigateTo(typeof(StartupViewModel).FullName!);

    [RelayCommand]
    private void OpenModLibrary() => _navigationService.NavigateTo(typeof(ModsOverviewVM).FullName!);

    [RelayCommand]
    private void OpenCharacters() => _navigationService.NavigateTo(typeof(CharacterManagerViewModel).FullName!);

    [RelayCommand]
    private void OpenCharacterGallery() =>
        _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!,
            _gameService.GetCategories().FirstOrDefault(), true);

    [RelayCommand]
    private void OpenPresets() => _navigationService.NavigateTo(typeof(PresetViewModel).FullName!);

    [RelayCommand]
    private void OpenModInstaller() => _navigationService.NavigateTo(typeof(ModInstallerVM).FullName!);

    [RelayCommand]
    private void OpenPluginArea() => _navigationService.NavigateTo(typeof(PluginAreaViewModel).FullName!);

    [RelayCommand]
    private void OpenNotifications() => _navigationService.NavigateTo(typeof(NotificationsViewModel).FullName!);

    [RelayCommand]
    private void OpenSettings() => _navigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
}

public class GameSwitchItem
{
    public string ShortName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
}
