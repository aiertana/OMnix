using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.ViewModels;

namespace OMniX.WinUI.Services;

/// <summary>
///     启动游戏。首页 Hero 和左侧栏底部两个入口共用这一份逻辑。
///     鸣潮会校验启动来源——直接拉游戏进程会弹 “kuro: Use launcher to start game!” 然后崩溃，
///     所以管理器只能帮你把官方启动器打开，剩下那一下得自己点。
/// </summary>
public sealed class GameLaunchService
{
    private readonly Dlss5PluginService _dlss5;
    private readonly PluginDependencyService _dependencies;
    private readonly INavigationService _navigationService;
    private readonly NotificationManager _notificationManager;
    private readonly IWindowManagerService _windowManagerService;

    public GameLaunchService(Dlss5PluginService dlss5, PluginDependencyService dependencies,
        NotificationManager notificationManager, INavigationService navigationService,
        IWindowManagerService windowManagerService)
    {
        _dlss5 = dlss5;
        _dependencies = dependencies;
        _notificationManager = notificationManager;
        _navigationService = navigationService;
        _windowManagerService = windowManagerService;
    }

    public static bool IsGameRunning() =>
        Process.GetProcessesByName("Client-Win64-Shipping").Length > 0;

    public async Task LaunchAsync()
    {
        try
        {
            if (IsGameRunning())
            {
                _notificationManager.ShowNotification("游戏已经在运行了", "先去游戏里忙吧", TimeSpan.FromSeconds(5));
                return;
            }

            var launcher = _dependencies.ResolveOfficialLauncherPath();
            var state = _dlss5.Detect();

            if (string.IsNullOrWhiteSpace(launcher))
            {
                _notificationManager.ShowNotification("尚未设置游戏路径",
                    "在角色页面点一次游戏可执行文件，之后这里就能帮你开官方启动器。",
                    TimeSpan.FromSeconds(8));
                _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "这一步得用官方启动器",
                Content =
                    "鸣潮会检查是谁在启动它：直接拉游戏进程会弹 “kuro: Use launcher to start game!” 然后当场崩溃，" +
                    "所以管理器没法替你点“开始游戏”。\n\n" +
                    "点「打开官方启动器」，在里面点开始就能进；这次会按左边的开关加载：\n" +
                    $"　 Mod：{(state.ModLoaderActive ? "启用" : "停用")}\n" +
                    $"　 DLSS5：{(state.Dlss5AddonActive ? "启用" : "停用")}",
                PrimaryButtonText = "打开官方启动器",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await _windowManagerService.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = Path.GetDirectoryName(launcher)!,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification("打开启动器失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }
}
