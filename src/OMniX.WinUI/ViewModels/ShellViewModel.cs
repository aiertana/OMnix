using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.GamesService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.AppManagement.Updating;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.Views;
using OMniX.WinUI.Views.Dialogs;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;

namespace OMniX.WinUI.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    private readonly UpdateChecker _updateChecker;
    private readonly BusyService _busyService;
    public readonly SelectedGameService SelectedGameService;
    [ObservableProperty] private bool isBackEnabled;
    [ObservableProperty] private bool isNotFirstTimeStartupPage = true;
    [ObservableProperty] private object? selected;
    [ObservableProperty] private int settingsInfoBadgeOpacity = 0;
    [ObservableProperty] private bool _isEnabled = true;
    public readonly IGameService GameService;

    public INavigationService NavigationService { get; }
    public INavigationViewService NavigationViewService { get; }
    public NotificationManager NotificationManager { get; }
    public ElevatorService ElevatorService { get; }
    public GenshinProcessManager GenshinProcessManager { get; }

    [ObservableProperty] private string _launchGameText = "启动游戏";
    [ObservableProperty] private string _appVersionText = string.Empty;

    /// <summary>二开：左侧栏的 DLSS5 渲染链插件</summary>
    public Dlss5PluginService Dlss5Plugin { get; }

    [ObservableProperty] private bool _dlss5Enabled;
    [ObservableProperty] private bool _dlss5Busy;
    [ObservableProperty] private string _dlss5StatusText = "未配置";
    [ObservableProperty] private string _dlss5DetailText = string.Empty;
    [ObservableProperty] private string _dlss5ToolTip = string.Empty;

    /// <summary>二开：Mod 加载开关（改 scripts\d3d11.asi 的名字）</summary>
    [ObservableProperty] private bool _modEnabled;
    [ObservableProperty] private bool _modBusy;
    [ObservableProperty] private string _modToolTip = string.Empty;

    private readonly IWindowManagerService _windowManagerService;
    private readonly ThreeDMigtoProcessManager _threeDMigtoProcessManager;
    private readonly PluginDependencyService _dependencies;
    private readonly GameLaunchService _gameLaunchService;

    public ShellViewModel(INavigationService navigationService, INavigationViewService navigationViewService,
        NotificationManager notificationManager, ElevatorService elevatorService, UpdateChecker updateChecker,
        IGameService gameService, SelectedGameService selectedGameService, BusyService busyService,
        GenshinProcessManager genshinProcessManager, Dlss5PluginService dlss5Plugin,
        IWindowManagerService windowManagerService, ThreeDMigtoProcessManager threeDMigtoProcessManager,
        PluginDependencyService dependencies, GameLaunchService gameLaunchService)
    {
        Dlss5Plugin = dlss5Plugin;
        _windowManagerService = windowManagerService;
        _threeDMigtoProcessManager = threeDMigtoProcessManager;
        _dependencies = dependencies;
        _gameLaunchService = gameLaunchService;
        NavigationService = navigationService;
        NavigationService.Navigated += OnNavigated;
        NavigationViewService = navigationViewService;
        NotificationManager = notificationManager;
        ElevatorService = elevatorService;
        GenshinProcessManager = genshinProcessManager;
        _updateChecker = updateChecker;
        GameService = gameService;
        SelectedGameService = selectedGameService;
        _busyService = busyService;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionText = version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";
        _updateChecker.NewVersionAvailable += OnNewVersionAvailable;
        _busyService.BusyChanged += (sender, e) =>
        {
            if (e.Key == BusyService.MainWindowKey)
                App.MainWindow.DispatcherQueue.TryEnqueue(() => IsEnabled = !e.IsBusy);
        };
    }

    public event EventHandler<bool>? ShowSettingsInfoBadge;

    private void OnNewVersionAvailable(object? sender, UpdateChecker.NewVersionEventArgs e)
    {
        if (_updateChecker.IgnoredVersion == e.Version)
            return;


        var show = e.Version != new Version();
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            SettingsInfoBadgeOpacity = show ? 1 : 0;
            ShowSettingsInfoBadge?.Invoke(this, show);
        });
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        IsBackEnabled = NavigationService.CanGoBack;
        if (e.SourcePageType == typeof(StartupPage) || e.SourcePageType == typeof(StartupViewModel))
            IsNotFirstTimeStartupPage = false; // On the StartupPage => Hide the MenuBar
        else
            IsNotFirstTimeStartupPage = true; // Not on the StartupPage => Show the MenuBar

        if (e.SourcePageType == typeof(SettingsPage))
        {
            Selected = NavigationViewService.SettingsItem;
            return;
        }

        var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType, e.Parameter);
        if (selectedItem != null)
        {
            Selected = selectedItem;
        }
    }

    /// <summary>
    ///     左侧栏底部“启动游戏”。和首页 Hero 上的按钮走同一个服务。
    /// </summary>
    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (!IsNotFirstTimeStartupPage)
            return;

        await _gameLaunchService.LaunchAsync();
    }

    /// <summary>
    ///     二开：刷新插件的开关状态（启动时、切换完开关后都调一次）。
    /// </summary>
    public async Task RefreshDlss5Async()
    {
        await Task.Yield();

        var state = Dlss5Plugin.Detect();
        Dlss5Enabled = state.Deployed && state.Dlss5AddonActive;
        ModEnabled = state.ModLoaderActive;
        Dlss5StatusText = state.StatusText;
        Dlss5DetailText = state.DetailText;
        Dlss5ToolTip = !state.Deployed
            ? state.Ready
                ? "点右侧开关部署加载链（需要管理员权限）"
                : "还缺游戏路径或部署包路径，点开关即可配置"
            : state.Dlss5AddonActive
                ? $"DLSS5 已启用 · {state.GameDir}"
                : $"DLSS5 已停用（addon 已改名）· {state.GameDir}";

        ModToolTip = !state.Deployed
            ? "加载链还没部署，现在启动游戏不会加载 Mod；打开这个开关会顺手把加载链装上"
            : state.ModLoaderActive
                ? "Mod 已启用，启动游戏会加载 Mod"
                : "Mod 已停用（d3d11.asi 改名成 .disabled），启动游戏不加载 Mod";
    }

    /// <summary>
    ///     二开：DLSS5 开关。
    ///     已经部署过加载链时只是给 addon 改名（秒切、不用管理员）；
    ///     还没部署就打开的话，走一次安装流程。
    /// </summary>
    public async Task SetDlss5EnabledAsync(bool enable)
    {
        if (Dlss5Busy)
            return;

        var state = Dlss5Plugin.Detect();

        if (!state.Deployed)
        {
            if (!enable)
            {
                await RefreshDlss5Async();
                return;
            }

            // 部署要用部署包，缺了先让用户下载
            if (!_dependencies.Detect().KitReady)
            {
                var dependencyDialog = new PluginDependencyDialog(_dependencies);

                if (await _windowManagerService.ShowDialogAsync(dependencyDialog) != ContentDialogResult.Primary)
                {
                    await RefreshDlss5Async();
                    return;
                }

                if (!_dependencies.Detect().KitReady)
                {
                    NotificationManager.ShowNotification("还没有 DLSS5 部署包",
                        "点开关重新打开依赖检查，可以在那里直接下载最新版。", TimeSpan.FromSeconds(8));
                    await RefreshDlss5Async();
                    return;
                }
            }

            await InstallLoaderAsync();
            return;
        }

        Dlss5Busy = true;

        try
        {
            var ok = await Dlss5Plugin.SetDlss5AddonAsync(enable);
            NotificationManager.ShowNotification(enable ? "DLSS5 已启用" : "DLSS5 已停用",
                ok ? "下次启动游戏生效" : "改文件名失败，检查一下游戏目录的写入权限",
                TimeSpan.FromSeconds(6));
        }
        finally
        {
            Dlss5Busy = false;
            await RefreshDlss5Async();
        }
    }

    /// <summary>
    ///     二开：Mod 开关。就是把 scripts\d3d11.asi 改名，ASI Loader 加载不到自然就不带 Mod。
    /// </summary>
    public async Task SetModEnabledAsync(bool enable)
    {
        if (ModBusy)
            return;

        var state = Dlss5Plugin.Detect();

        // 加载链没部署时，两个开关就都是关的。
        // 想关：本来就是关的，静默回正，别弹提示拦着；
        // 想开：顺手把加载链装上，装完 Mod 自然就带上了。
        if (!state.Deployed)
        {
            if (!enable)
            {
                await RefreshDlss5Async();
                return;
            }

            if (!_dependencies.Detect().ModReady)
            {
                var dependencyDialog = new PluginDependencyDialog(_dependencies);
                await _windowManagerService.ShowDialogAsync(dependencyDialog);
                await RefreshDlss5Async();
                return;
            }

            await InstallLoaderAsync();
            return;
        }

        // 有加载链但没有 XXMI / WWMI 的话，开关切了也没意义，先引导装
        if (!_dependencies.Detect().ModReady)
        {
            var dependencyDialog = new PluginDependencyDialog(_dependencies);
            await _windowManagerService.ShowDialogAsync(dependencyDialog);
            await RefreshDlss5Async();
            return;
        }

        ModBusy = true;

        try
        {
            var ok = await Dlss5Plugin.SetModLoaderAsync(enable);
            NotificationManager.ShowNotification(enable ? "Mod 已启用" : "Mod 已停用",
                ok ? "下次启动游戏生效" : "改文件名失败，检查一下游戏目录的写入权限",
                TimeSpan.FromSeconds(6));
        }
        finally
        {
            ModBusy = false;
            await RefreshDlss5Async();
        }
    }

    /// <summary>
    ///     二开：第一次开启时把加载链部署进游戏目录（会弹配置和确认）。
    /// </summary>
    private async Task InstallLoaderAsync()
    {
        var state = Dlss5Plugin.Detect();

        if (!state.Ready)
        {
            var dialog = new Dlss5SetupDialog(Dlss5Plugin);
            var picked = await _windowManagerService.ShowDialogAsync(dialog);

            if (picked != ContentDialogResult.Primary)
            {
                await RefreshDlss5Async();
                return;
            }

            state = Dlss5Plugin.Detect();

            if (!state.Ready)
            {
                NotificationManager.ShowNotification("插件还没配好",
                    "游戏可执行文件或部署包目录不对，再点一次开关重新填。", TimeSpan.FromSeconds(8));
                await RefreshDlss5Async();
                return;
            }
        }

        // 会动游戏目录，先让人看清楚再动手
        var confirm = new ContentDialog
        {
            Title = "部署渲染链？",
            Content =
                $"会把加载器、ReShade、DLSS5 铺进：\n{state.GameDir}\n\n那边的 scripts 会被清空重建，游戏自带的 NVIDIA DLSS 库也会被替换成部署包里的版本。",
            PrimaryButtonText = "开始部署",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _windowManagerService.ShowDialogAsync(confirm) != ContentDialogResult.Primary)
        {
            await RefreshDlss5Async();
            return;
        }

        Dlss5Busy = true;
        Dlss5StatusText = "正在部署…";

        try
        {
            var result = await Dlss5Plugin.SetEnabledAsync(true);

            if (result.NeedsSetup)
            {
                NotificationManager.ShowNotification("插件还没配好",
                    "游戏可执行文件或部署包目录不对，再点一次开关重新填。", TimeSpan.FromSeconds(8));
                return;
            }

            if (result.UserCancelled)
            {
                NotificationManager.ShowNotification("已取消", "没有拿到管理员权限，部署没有进行。",
                    TimeSpan.FromSeconds(6));
                return;
            }

            NotificationManager.ShowNotification("渲染链已开启", result.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            Dlss5Busy = false;
            await RefreshDlss5Async();
        }
    }

    [RelayCommand(CanExecute = nameof(IsNotFirstTimeStartupPage))]
    private void OnMenuSettings()
    {
        if (NavigationService.Frame!.Content is SettingsPage)
        {
            NavigationService.NavigateTo(typeof(CharactersViewModel).FullName!);
            return;
        }

        NavigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
    }

    public async Task RefreshGenshinMods()
    {
        if (!IsNotFirstTimeStartupPage)
        {
            return;
        }

        if (ElevatorService.ElevatorStatus == ElevatorStatus.NotRunning)
        {
            NotificationManager.ShowNotification("Elevator is not running",
                "Please start the Elevator first in the Settings page",
                TimeSpan.FromSeconds(5));
        }
        else
            await Task.Run(async () => await ElevatorService.RefreshGenshinMods());
    }
}