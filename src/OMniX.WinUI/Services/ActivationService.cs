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
using OMniX.WinUI.Helpers;
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
    private readonly GameKernelService _kernelService;
    private readonly GameDataRootService _dataRootService;
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
        LifeCycleService lifeCycleService, GameKernelService kernelService,
        GameDataRootService dataRootService)
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
        _kernelService = kernelService;
        _dataRootService = dataRootService;
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

        // 内置内核（鸣潮）登记一次：不然首页下拉会把它当成没下载
        try
        {
            _kernelService.EnsureBuiltinKernels();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "内核登记失败，下拉里的游戏可能显示为未下载");
        }

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
            await LegacyMigrationPopup();
        });
    }

    /// <summary>
    ///     老用户升级上来时，C 盘 %LOCALAPPDATA%\OMniX 里还留着旧数据。
    ///     检测到就问一句要不要搬到版本目录下的 GamesData —— 搬不搬、搬完删不删都由玩家定。
    /// </summary>
    public const string IgnoreLegacyMigrationKey = "IgnoreLegacyMigration";

    private async Task LegacyMigrationPopup()
    {
        if (await _localSettingsService.ReadOrCreateSettingAsync<bool>(IgnoreLegacyMigrationKey))
            return;

        if (!GameDataRootService.HasLegacyData())
            return;

        var body = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            Text = string.Format(
                _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateBody",
                    "检测到旧版本的数据还在 C 盘：\n{0}\n\n新版把游戏数据放到了：\n{1}\n\n要现在搬过去吗？原来的数据会原样保留，搬完再让你决定删不删。"),
                GameDataRootService.LegacyAppDataRoot, _dataRootService.GamesDataRoot)
        };

        var ask = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateTitle", "发现旧版本数据"),
            Content = body,
            PrimaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateNow", "一键迁移"),
            CloseButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateLater", "以后再说"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await _windowManagerService.ShowDialogAsync(ask).ConfigureAwait(false) != ContentDialogResult.Primary)
        {
            await _localSettingsService.SaveSettingAsync(IgnoreLegacyMigrationKey, true);
            return;
        }

        var migration = await RunMigrationWithProgressAsync();

        if (migration.CopiedItems.Count == 0 || migration.Failures.Count > 0 || !migration.Verified)
            await ManualMigrationHintPopup(migration);
        else
            await DeleteLegacyAfterMigrationPopup(migration);

        await _localSettingsService.SaveSettingAsync(IgnoreLegacyMigrationKey, true);
    }

    private async Task<MigrationResult> RunMigrationWithProgressAsync()
    {
        var status = new TextBlock
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigratingBody", "正在复制文件，别关掉窗口…"),
            TextWrapping = TextWrapping.WrapWholeWords
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(status);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 320 });

        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigratingTitle", "正在迁移旧数据"),
            Content = panel
        };

        var showing = _windowManagerService.ShowDialogAsync(dialog);
        MigrationResult migration;

        try
        {
            var progress = new Progress<MigrationProgress>(update =>
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    status.Text = string.Format(
                        _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigratingItem", "正在搬：{0}（{1}/{2}）"),
                        update.CurrentName, update.DoneFiles, update.TotalFiles)));

            migration = await Task.Run(async () => await _dataRootService.MigrateAsync(progress));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "旧数据迁移失败");
            migration = new MigrationResult { Success = false, Message = ex.Message };
        }
        finally
        {
            dialog.Hide();
        }

        try
        {
            await showing.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 关窗时抛的不用管
        }

        return migration;
    }

    /// <summary>
    ///     自动迁移没成（或校验没过）时的兜底：把两个路径原样告诉玩家，让他自己拷。
    ///     不给个出路的话，玩家就卡在报错里了。
    /// </summary>
    private async Task ManualMigrationHintPopup(MigrationResult migration)
    {
        var lines = new StackPanel { Spacing = 8 };

        lines.Children.Add(new TextBlock
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateManualIntro",
                "自动迁移没能全部完成，你可以手动搬：把左边文件夹里的东西复制到右边文件夹。"),
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
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateManualPaths", "旧数据：") +
                   GameDataRootService.LegacyAppDataRoot +
                   "\n" +
                   _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateManualTarget", "搬到：") +
                   _dataRootService.GamesDataRoot,
            FontSize = 12,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        });

        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateManualTitle", "可以手动迁移"),
            Content = lines,
            CloseButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_GotIt", "我知道了")
        };

        await _windowManagerService.ShowDialogAsync(dialog).ConfigureAwait(false);
    }

    /// <summary>搬完并校验通过，才问玩家要不要清掉 C 盘那份 —— 默认不清。</summary>
    private async Task DeleteLegacyAfterMigrationPopup(MigrationResult migration)
    {
        var checkBox = new CheckBox
        {
            Content = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateDeleteOld",
                "迁移已校验，删除 C 盘上的旧数据"),
            IsChecked = false
        };

        var permanent = new CheckBox
        {
            Content = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigratePermanent", "永久删除（不进回收站）"),
            IsChecked = false
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(
                _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateDoneBody",
                    "已经把 {0} 个文件夹搬到：\n{1}\n\n旧数据还在 C 盘，删不删随你。"),
                migration.CopiedItems.Count, _dataRootService.GamesDataRoot),
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });
        panel.Children.Add(checkBox);
        panel.Children.Add(permanent);

        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateDoneTitle", "迁移完成"),
            Content = panel,
            PrimaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_Confirm", "确定"),
            CloseButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_MigrateCancel", "取消")
        };

        if (await _windowManagerService.ShowDialogAsync(dialog).ConfigureAwait(false) != ContentDialogResult.Primary)
            return;

        if (checkBox.IsChecked != true)
            return;

        foreach (var item in migration.CopiedItems)
        {
            try
            {
                if (permanent.IsChecked == true)
                    RecycleBin.DeleteDirectory(item.Source);
                else
                    RecycleBin.SendDirectory(item.Source);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "删除旧数据失败：{Path}", item.Source);
            }
        }
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
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_FreeBody1", "OMniX 是完全免费的开源软件，不收一分钱，也没有「会员版」「激活码」这种东西。"),
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });

        panel.Children.Add(new TextBlock
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_FreeBody2", "如果有人跟你要钱才给安装包，或者卖激活码、卖会员版，那就是骗子。" +
                   "直接拒绝就好，顺手到交流群或者 GitHub 上说一声，别让下一个人上当。"),
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });

        panel.Children.Add(new TextBlock
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_FreeBody3", "正经的获取途径只有两个：GitHub 仓库，和交流群里的群文件。" +
                   "其它渠道下载到的，请不要用。"),
            FontSize = 12,
            Foreground = secondary,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        });

        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_FreeTitle", "免费软件，别被人收了钱"),
            Content = panel,
            PrimaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_GotIt", "我知道了"),
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
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_AdminBody", "你现在是以管理员身份在跑 OMniX，不推荐这样。\n" +
                   "OMniX 并不是按管理员权限设计的。\n" +
                   "哪怕只是个小 bug，也有可能把文件系统弄乱，后果不小。\n\n" +
                   "建议用普通权限运行 OMniX。\n\n" +
                   "要继续的话风险自负。"),
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var doNotShowAgain = new CheckBox()
        {
            IsChecked = false,
            Content = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_AdminDontShow", "不再显示这个提醒"),
            Margin = new Thickness(0, 10, 0, 0)
        };

        stackPanel.Children.Add(doNotShowAgain);


        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_AdminTitle", "正在以管理员身份运行"),
            Content = stackPanel,
            PrimaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_GotIt", "我知道了"),
            SecondaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_Exit", "退出"),
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
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_RestructBody",
                "这个版本的 OMniX 换了文件夹结构。\n\n" +
                "现在角色按分类组织，每个分类一个文件夹，格式变成：\n" +
                "Mods/分类/角色/<Mod 文件夹>\n" +
                "所以在整理之前，OMniX 看不到你现有的 Mod。\n" +
                "这只做一次，你也可以自己动手整理。\n\n" +
                "另外角色文件夹现在改成按需创建，设置页里也能清理空文件夹。"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var textWarning2 = new TextBlock()
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_RestructBackupTip", "拿不准的话，先把 Mod 备份一份。这套是在自己的 Mod 上验证过的。"),
            FontWeight = FontWeights.Bold,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning2);


        var textWarning3 = new TextBlock()
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_RestructNote",
                "\n这个弹窗会一直出现，直到你在下面选一个。设置页里也有个「重新整理」按钮。\n" +
                "想看具体发生了什么，可以翻日志。"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning3);


        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_RestructTitle", "文件夹结构变了"),
            Content = stackPanel,
            PrimaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_RestructHelpMe", "帮我整理"),
            SecondaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_RestructMyself", "我自己来"),
            CloseButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_Cancel", "取消"),
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
                    _notificationManager.ShowNotification(_languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_ReorganizeFailed", "整理 Mod 失败"),
                        "See logs for more details.", TimeSpan.FromSeconds(5));

                else
                    _notificationManager.ShowNotification(_languageLocalizer.GetLocalizedStringOrDefault("/ServicesMsg/Act_ReorganizeDone", "整理完成"),
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