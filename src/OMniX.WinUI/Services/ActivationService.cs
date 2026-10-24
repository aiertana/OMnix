using System.Security.Principal;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using CommunityToolkitWrapper;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.Helpers;
using OMniX.WinUI.Activation;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.AppManagement.Updating;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Views;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace OMniX.WinUI.Services;

public class ActivationService : IActivationService
{
    private readonly NotificationManager _notificationManager;
    private readonly ISkinManagerService _skinManagerService;
    private readonly INavigationViewService _navigationViewService;
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILogger _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IGameService _gameService;
    private readonly ILanguageLocalizer _languageLocalizer;
    private readonly ElevatorService _elevatorService;
    private readonly GenshinProcessManager _genshinProcessManager;
    private readonly ThreeDMigtoProcessManager _threeDMigtoProcessManager;
    private readonly UpdateChecker _updateChecker;
    private readonly IWindowManagerService _windowManagerService;
    private readonly AutoUpdaterService _autoUpdaterService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly ModNotificationManager _modNotificationManager;
    private readonly LifeCycleService _lifeCycleService;
    private UIElement? _shell = null;

    private readonly string[] _args = Environment.GetCommandLineArgs().Skip(1).ToArray();

    public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler,
        IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService,
        ILocalSettingsService localSettingsService,
        ElevatorService elevatorService, GenshinProcessManager genshinProcessManager,
        ThreeDMigtoProcessManager threeDMigtoProcessManager, UpdateChecker updateChecker,
        IWindowManagerService windowManagerService, AutoUpdaterService autoUpdaterService, IGameService gameService,
        ILanguageLocalizer languageLocalizer, SelectedGameService selectedGameService,
        ModUpdateAvailableChecker modUpdateAvailableChecker, ILogger logger,
        ModNotificationManager modNotificationManager, INavigationViewService navigationViewService,
        ISkinManagerService skinManagerService, NotificationManager notificationManager,
        LifeCycleService lifeCycleService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        _elevatorService = elevatorService;
        _genshinProcessManager = genshinProcessManager;
        _threeDMigtoProcessManager = threeDMigtoProcessManager;
        _updateChecker = updateChecker;
        _windowManagerService = windowManagerService;
        _autoUpdaterService = autoUpdaterService;
        _gameService = gameService;
        _languageLocalizer = languageLocalizer;
        _selectedGameService = selectedGameService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _modNotificationManager = modNotificationManager;
        _navigationViewService = navigationViewService;
        _skinManagerService = skinManagerService;
        _notificationManager = notificationManager;
        _lifeCycleService = lifeCycleService;
        _logger = logger.ForContext<ActivationService>();
    }

    public async Task ActivateAsync(object activationArgs)
    {
#if DEBUG
        _logger.Information("OMniX starting up in DEBUG mode...");
#elif RELEASE
        _logger.Information("OMniX starting up in RELEASE mode...");
#endif

        await HandleLaunchArgsAsync();

        // Check if there is another instance of OMniX running
        await CheckIfAlreadyRunningAsync();

        // Execute tasks before activation.
        await InitializeAsync();

        // Set the MainWindow Content.
        if (App.MainWindow.Content == null)
        {
            _shell = App.GetService<ShellPage>();
            App.MainWindow.Content = _shell ?? new Frame();
        }

        // 二开：全局点击音效要挂在窗口内容的根上，
        // Content 设好之后才挂得上（早了是 null），点界面任何地方都响。
        // App.MainWindow 的静态类型是 WindowEx，得转到 MainWindow 才有这个方法
        if (App.MainWindow is MainWindow window)
            window.AttachGlobalClick();

        // Handle activation via ActivationHandlers.
        await HandleActivationAsync(activationArgs);

        // Activate the MainWindow.
        App.MainWindow.Activate();

        // Set MainWindow Cleanup on Close.
        App.MainWindow.Closed += OnApplicationExit;

        // Execute tasks after activation.
        await StartupAsync();

        // Show popups
        ShowStartupPopups();
    }

    private async Task CheckIfAlreadyRunningAsync()
    {
        var isOmniXRunningHWND = await IsOmniXRunning();

        if (!isOmniXRunningHWND.HasValue) return;

        var hWnd = isOmniXRunningHWND.Value;

        _logger.Information("OMniX is already running, exiting...");
        try
        {
            PInvoke.ShowWindow(hWnd, SHOW_WINDOW_CMD.SW_RESTORE);
            PInvoke.SetWindowPos(hWnd, new HWND(IntPtr.Zero), 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
            PInvoke.SetForegroundWindow(hWnd);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not bring OMniX to foreground");
            return;
        }

        Application.Current.Exit();
        await Task.Delay(-1);
    }


    private async Task<HWND?> IsOmniXRunning()
    {
        nint? processHandle;
        try
        {
            processHandle = await _lifeCycleService.CheckIfAlreadyRunningAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not determine if OMniX is already running. Assuming not");
            return null;
        }

        if (processHandle == null) return null;

        return new HWND(processHandle.Value);
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));


        if (activationHandler is not null)
        {
            _logger.Debug("Handling activation: {ActivationName}",
                activationHandler?.ActivationName);

            await activationHandler?.HandleAsync(activationArgs)!;
        }

        if (_defaultHandler.CanHandle(activationArgs))
        {
            _logger.Debug("Handling activation: {ActivationName}", _defaultHandler.ActivationName);
            await _defaultHandler.HandleAsync(activationArgs);
        }
    }

    private async Task InitializeAsync()
    {
        await _selectedGameService.InitializeAsync();
        await SetLanguage();
        await SetWindowSettings();
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        _notificationManager.Initialize();
    }


    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
        await _genshinProcessManager.TryInitialize();
        await _threeDMigtoProcessManager.TryInitialize();
        await _updateChecker.InitializeAsync();
        await _modUpdateAvailableChecker.InitializeAsync().ConfigureAwait(false);
        await Task.Run(() => _autoUpdaterService.UpdateAutoUpdater()).ConfigureAwait(false);
        await Task.Run(() => _elevatorService.Initialize()).ConfigureAwait(false);
    }

    const int MinimizedPosition = -32000;

    private async Task SetWindowSettings()
    {
        var screenSize = await _localSettingsService.ReadSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);
        if (screenSize == null)
            return;

        if (screenSize.PersistWindowSize && screenSize.Width != 0 && screenSize.Height != 0)
        {
            _logger.Debug($"Window size loaded: {screenSize.Width}x{screenSize.Height}");
            App.MainWindow.SetWindowSize(screenSize.Width, screenSize.Height);
        }

        if (screenSize.PersistWindowPosition)
        {
            if (screenSize.XPosition != 0 && screenSize.YPosition != 0 &&
                screenSize.XPosition != MinimizedPosition && screenSize.YPosition != MinimizedPosition)
                App.MainWindow.AppWindow.Move(new PointInt32(screenSize.XPosition, screenSize.YPosition));
            else
                App.MainWindow.CenterOnScreen();

            if (screenSize.IsFullScreen)
                App.MainWindow.Maximize();
        }
    }

    private async void OnApplicationExit(object sender, WindowEventArgs args)
    {
        if (App.ShutdownComplete) return;

        args.Handled = true;

        if (App.IsShuttingDown)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            {
                var softShutdownGracePeriod = TimeSpan.FromSeconds(2);
                await Task.Delay(softShutdownGracePeriod);

                _logger.Warning(
                    "OMniX shutdown took too long (>{maxShutdownGracePeriod}s), ignoring cleanup and exiting...",
                    softShutdownGracePeriod);
                App.ShutdownComplete = true;
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    Application.Current.Exit();
                    App.MainWindow.Close();
                });
            });

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            {
                var maxShutdownGracePeriod = TimeSpan.FromSeconds(5);
                await Task.Delay(maxShutdownGracePeriod);

                _logger.Fatal("OMniX failed to close after {maxShutdownGracePeriod} seconds, forcing exit...",
                    maxShutdownGracePeriod);
                Environment.Exit(1);
            });
            return;
        }


        await _lifeCycleService.StartShutdownAsync().ConfigureAwait(false);
    }

    // Declared here for now, might move to a different class later.
    private const string IgnoreAdminWarningKey = "IgnoreAdminPrivelegesWarning";

    private async Task HandleLaunchArgsAsync()
    {
        if (_args.Length == 0)
            return;

        var supportedGames = Enum.GetNames<SupportedGames>().Select(v => v.ToLower()).ToArray();

        if (_args.Any(arg => arg.Contains("help", StringComparison.OrdinalIgnoreCase)) &&
            // WinUI doesnt seem to launch like a regular console app.
            // This kinda works, but it's not perfect.
            // Overriding the main entry point didn't seem to work either.
            PInvoke.AttachConsole(unchecked((uint)-1)))
        {
            // TODO: Use CommandLineParser or something similar.
            Console.WriteLine("OMniX Command line arguments:");

            Console.WriteLine(
                $"      --game <game> - Launch OMniX with the specified game selected. Supported games: {string.Join('|', supportedGames)}");

            Console.WriteLine(
                "           --switch - If used with --game, will switch to the selected game if OMniX is already running. This is done by exiting the already running instance.");

            Application.Current.Exit();
            await Task.Delay(-1);
        }


        await _selectedGameService.InitializeAsync().ConfigureAwait(false);
        var notSelectedGames = await _selectedGameService.GetNotSelectedGameAsync().ConfigureAwait(false);

        var launchGameArgIndex =
            Array.FindIndex(_args, arg => arg.Equals("--game", StringComparison.OrdinalIgnoreCase));
        if (launchGameArgIndex == -1)
            return;

        var launchGameArgValue = _args.ElementAtOrDefault(launchGameArgIndex + 1);
        if (launchGameArgValue.IsNullOrEmpty())
        {
            _logger.Warning("No game specified for arg: --game <{ValidGames}>",
                string.Join('|', Enum.GetNames<SupportedGames>().Select(v => v.ToLower())));
            return;
        }

        var selectedGame = Enum.TryParse<SupportedGames>(launchGameArgValue, true, out var game)
            ? game
            : SupportedGames.Genshin;

        if (!notSelectedGames.Contains(selectedGame))
            return;

        var otherProcess = _lifeCycleService.GetOtherInstanceProcess();

        if (otherProcess is not null)
        {
            if (!_args.Contains("--switch"))
            {
                // If the other instance is running, and the switch flag is not present, return.
                // Will be handled by the OtherInstance check later.
                return;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                _logger.Information("Closing running instance of OMniX to switch to {SelectedGame}", selectedGame);
                otherProcess.CloseMainWindow();
                await otherProcess.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to close running instance of OMniX");
                return;
            }

            try
            {
                await _selectedGameService.SaveSelectedGameAsync(selectedGame.ToString()).ConfigureAwait(false);
                await _selectedGameService.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // If this errors then I don't know what to do.
                _logger.Error(e, "Failed to save selected game");
            }

            return;
        }

        try
        {
            await _selectedGameService.SaveSelectedGameAsync(selectedGame.ToString()).ConfigureAwait(false);
            await _selectedGameService.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // If this errors then I don't know what to do.
            _logger.Error(e, "Failed to save selected game");
            return;
        }


        _logger.Information("Game selected via launch args: {SelectedGame}", selectedGame);
    }

    private void ShowStartupPopups()
    {
        App.MainWindow.DispatcherQueue.EnqueueAsync(async () =>
        {
            await Task.Delay(2000);
            await FreeSoftwareNoticePopup();
            await AdminWarningPopup();
            await Task.Delay(1000);
            await NewFolderStructurePopup();
        });
    }

    /// <summary>免费声明只在第一次启动时弹一次，勾了「不再提示」或者看过了就写进本地设置。</summary>
    public const string IgnoreFreeSoftwareNoticeKey = "IgnoreFreeSoftwareNotice";

    private async Task FreeSoftwareNoticePopup()
    {
        if (await _localSettingsService.ReadOrCreateSettingAsync<bool>(IgnoreFreeSoftwareNoticeKey)) return;

        var secondary = Application.Current.Resources["OmniXTextSecondaryBrush"] as Brush;

        var panel = new StackPanel { Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = "OMniX 是完全免费的开源软件，不收一分钱，也没有「会员版」「激活码」这种东西。",
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });

        panel.Children.Add(new TextBlock
        {
            Text = "如果有人跟你要钱才给安装包，或者卖激活码、卖会员版，那就是骗子。" +
                   "直接拒绝就好，顺手到交流群或者 GitHub 上说一声，别让下一个人上当。",
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });

        panel.Children.Add(new TextBlock
        {
            Text = "正经的获取途径只有两个：GitHub 仓库，和交流群里的群文件。" +
                   "其它渠道下载到的，请不要用。",
            FontSize = 12,
            Foreground = secondary,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });

        var dialog = new ContentDialog
        {
            Title = "免费软件，别被人收了钱",
            Content = panel,
            PrimaryButtonText = "我知道了",
            DefaultButton = ContentDialogButton.Primary
        };

        await _windowManagerService.ShowDialogAsync(dialog).ConfigureAwait(false);

        await _localSettingsService.SaveSettingAsync(IgnoreFreeSoftwareNoticeKey, true);
    }

    private async Task AdminWarningPopup()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator)) return;

        var ignoreWarning = await _localSettingsService.ReadSettingAsync<bool>(IgnoreAdminWarningKey);

        if (ignoreWarning) return;

        var stackPanel = new StackPanel();
        var textWarning = new TextBlock()
        {
            Text = "你现在是以管理员身份在跑 OMniX，不推荐这样。\n" +
                   "OMniX 并不是按管理员权限设计的。\n" +
                   "哪怕只是个小 bug，也有可能把文件系统弄乱，后果不小。\n\n" +
                   "建议用普通权限运行 OMniX。\n\n" +
                   "要继续的话风险自负。",
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var doNotShowAgain = new CheckBox()
        {
            IsChecked = false,
            Content = "不再显示这个提醒",
            Margin = new Thickness(0, 10, 0, 0)
        };

        stackPanel.Children.Add(doNotShowAgain);


        var dialog = new ContentDialog
        {
            Title = "正在以管理员身份运行",
            Content = stackPanel,
            PrimaryButtonText = "我知道了",
            SecondaryButtonText = "退出",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Secondary) Application.Current.Exit();

        if (doNotShowAgain.IsChecked == true)
            await _localSettingsService.SaveSettingAsync(IgnoreAdminWarningKey, true);
    }

    public const string IgnoreNewFolderStructureKey = "IgnoreNewFolderStructureWarning";

    private async Task NewFolderStructurePopup()
    {
        if (!_skinManagerService.IsInitialized)
        {
            await _localSettingsService.SaveSettingAsync(IgnoreNewFolderStructureKey, true);
        }

        var ignoreWarning = await _localSettingsService.ReadOrCreateSettingAsync<bool>(IgnoreNewFolderStructureKey);

        if (ignoreWarning) return;

        var stackPanel = new StackPanel();
        var textWarning = new TextBlock()
        {
            Text = """
                   这个版本的 OMniX 换了文件夹结构。

                   现在角色按分类组织，每个分类一个文件夹，格式变成：
                   Mods/分类/角色/<Mod 文件夹>
                   所以在整理之前，OMniX 看不到你现有的 Mod。
                   这只做一次，你也可以自己动手整理。

                   另外角色文件夹现在改成按需创建，设置页里也能清理空文件夹。
                   """,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var textWarning2 = new TextBlock()
        {
            Text = "拿不准的话，先把 Mod 备份一份。这套是在自己的 Mod 上验证过的。",
            FontWeight = FontWeights.Bold,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning2);


        var textWarning3 = new TextBlock()
        {
            Text = """

                   这个弹窗会一直出现，直到你在下面选一个。设置页里也有个「重新整理」按钮。
                   想看具体发生了什么，可以翻日志。
                   """,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning3);


        var dialog = new ContentDialog
        {
            Title = "文件夹结构变了",
            Content = stackPanel,
            PrimaryButtonText = "帮我整理",
            SecondaryButtonText = "我自己来",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            _navigationViewService.IsEnabled = false;

            try
            {
                var movedModsCount = await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync()); // Mods folder

                await _skinManagerService.RefreshModsAsync();

                if (movedModsCount == -1)
                    _notificationManager.ShowNotification("整理 Mod 失败",
                        "See logs for more details.", TimeSpan.FromSeconds(5));

                else
                    _notificationManager.ShowNotification("整理完成",
                        $"Moved {movedModsCount} mods to new character folders", TimeSpan.FromSeconds(5));
            }
            finally
            {
                _navigationViewService.IsEnabled = true;
                await _localSettingsService.SaveSettingAsync(IgnoreNewFolderStructureKey, true);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await _localSettingsService.SaveSettingAsync(IgnoreNewFolderStructureKey, true);
        }
        else
        {
        }
    }


    private async Task SetLanguage()
    {
        var selectedLanguage = (await _localSettingsService.ReadOrCreateSettingAsync<AppSettings>(AppSettings.Key))
            .Language?.ToLower().Trim();
        if (selectedLanguage == null)
        {
            return;
        }

        var supportedLanguages = _languageLocalizer.AvailableLanguages;
        var language = supportedLanguages.FirstOrDefault(lang =>
            lang.LanguageCode.Equals(selectedLanguage, StringComparison.CurrentCultureIgnoreCase));

        if (language != null)
            await _languageLocalizer.SetLanguageAsync(language);
    }
}