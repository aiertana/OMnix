using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.ViewModels;

namespace OMniX.WinUI.Services;

/// <summary>
///     启动游戏。左侧栏底部那个大按钮走这里。
///     拉谁由 <see cref="GameLaunchResolver" /> 说了算：
///     鸣潮走官方启动器，绝区零走 XXMI，原神 / 崩铁走 HoYo，
///     玩家在设置页手动指定过就以手动的为准。
/// </summary>
public sealed class GameLaunchService
{
    private readonly Dlss5PluginService _dlss5;
    private readonly PluginDependencyService _dependencies;
    private readonly GameLaunchResolver _resolver;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly SelectedGameService _selectedGameService;
    private readonly INavigationService _navigationService;
    private readonly NotificationManager _notificationManager;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public GameLaunchService(Dlss5PluginService dlss5, PluginDependencyService dependencies,
        GameLaunchResolver resolver, ILocalSettingsService localSettingsService,
        SelectedGameService selectedGameService, NotificationManager notificationManager,
        INavigationService navigationService, IWindowManagerService windowManagerService)
    {
        _dlss5 = dlss5;
        _dependencies = dependencies;
        _resolver = resolver;
        _localSettingsService = localSettingsService;
        _selectedGameService = selectedGameService;
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
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_GameRunningTitle", "游戏已经在运行了"),
                    _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_GameRunningBody", "先去游戏里忙吧"),
                    TimeSpan.FromSeconds(5));
                return;
            }

            var game = await ResolveCurrentGameAsync();
            var options = await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);
            var resolution = _resolver.Resolve(game, options);

            if (!resolution.Found)
            {
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_NoGamePathTitle", "没找到启动器"),
                    _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_NoGamePathBody",
                        "自动识别没找到这个游戏的启动器。去设置 → 游戏与路径 → 启动方式，点「手动添加」自己指定一个。"),
                    TimeSpan.FromSeconds(8));

                _navigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
                return;
            }

            // 鸣潮会校验启动来源：直接拉游戏进程会弹 “kuro: Use launcher to start game!” 然后崩溃，
            // 所以走官方启动器时要先跟玩家说清楚 —— 那一下“开始游戏”得他自己点。
            if (resolution.Mode == GameLaunchMode.Official && game.Id == GameCatalog.WuWa.Id)
            {
                if (!await ConfirmOfficialLauncherAsync())
                    return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = resolution.Executable,
                WorkingDirectory = Path.GetDirectoryName(resolution.Executable)!,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_OpenFailed", "打开启动器失败"),
                e.Message, TimeSpan.FromSeconds(8));
        }
    }

    private async Task<GameDefinition> ResolveCurrentGameAsync()
    {
        var selected = await _selectedGameService.GetSelectedGameAsync();

        return GameCatalog.ByCoreName(selected)
               ?? GameCatalog.ById(selected)
               ?? GameCatalog.Fallback;
    }

    private async Task<bool> ConfirmOfficialLauncherAsync()
    {
        var state = _dlss5.Detect();

        var enabledText = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_Enabled", "启用");
        var disabledText = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_Disabled", "停用");

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_OfficialLauncherTitle", "这一步得用官方启动器"),
            Content =
                _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_OfficialLauncherIntro", "鸣潮会检查是谁在启动它：直接拉游戏进程会弹 “kuro: Use launcher to start game!” 然后当场崩溃，" +
                "所以管理器没法替你点“开始游戏”。\n\n" +
                "点「打开官方启动器」，在里面点开始就能进；这次会按左边的开关加载：\n") +
                string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_ModStatusLine", "　 Mod：{0}\n"),
                    state.ModLoaderActive ? enabledText : disabledText) +
                string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_Dlss5StatusLine", "　 DLSS5：{0}"),
                    state.Dlss5AddonActive ? enabledText : disabledText),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_OpenLauncher", "打开官方启动器"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Launch_Cancel", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

        return await _windowManagerService.ShowDialogAsync(dialog) == ContentDialogResult.Primary;
    }
}
