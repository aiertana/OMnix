using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.GamesService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.AppManagement.Updating;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Views;
using Microsoft.UI.Xaml.Navigation;

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

    public ShellViewModel(INavigationService navigationService, INavigationViewService navigationViewService,
        NotificationManager notificationManager, ElevatorService elevatorService, UpdateChecker updateChecker,
        IGameService gameService, SelectedGameService selectedGameService, BusyService busyService,
        GenshinProcessManager genshinProcessManager)
    {
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
    ///     左侧栏底部“启动游戏”按钮
    /// </summary>
    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (!IsNotFirstTimeStartupPage)
            return;

        try
        {
            await GenshinProcessManager.CheckStatus();

            if (GenshinProcessManager.ProcessStatus == ProcessStatus.NotInitialized)
            {
                NotificationManager.ShowNotification("尚未设置游戏路径",
                    "请在角色页面点击“启动游戏”并选择游戏可执行文件", TimeSpan.FromSeconds(8));
                NavigationService.NavigateTo(typeof(CharactersViewModel).FullName!);
                return;
            }

            await GenshinProcessManager.StartProcess();
        }
        catch (Exception e)
        {
            NotificationManager.ShowNotification("启动游戏失败", e.Message, TimeSpan.FromSeconds(8));
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