using OMniX.Core.Contracts.Services;
using ModPresetService = OMniX.Core.Services.ModPresetService.ModPresetService;
using OMniX.Core.GamesService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models;
using OMniX.WinUI.ViewModels.Overlay;
using OMniX.WinUI.Views.Overlay;
using Serilog;
using WinRT.Interop;

namespace OMniX.WinUI.Services.Overlay;

/// <summary>
///     游戏内悬浮窗的总管：圆形悬浮球 + Alt+C 全局热键 + Mod 切换面板。
///
///     为什么不做注入式 overlay：Steam / Discord 那种能在独占全屏里显示的东西
///     要走 DirectX Hook 和进程注入，碰反作弊有封号风险。这里只做「无边框窗口化
///     游戏上浮一个置顶窗口」，游戏开独占全屏时窗口会被压在后面，但 Alt+C 热键
///     仍然有效（按键是系统级消息）。
/// </summary>
public class ModOverlayService
{
    private const int HotkeyId = 0x0C01;

    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;

    private OverlayBallWindow? _ball;
    private ModSwitchPanelWindow? _panel;
    private IntPtr _hwnd;
    private bool _hotkeyRegistered;
    private bool _panelVisible;



    public ModOverlayService(IGameService gameService, ISkinManagerService skinManagerService,
        ModPresetService modPresetService,
        ILocalSettingsService localSettingsService, ILogger logger)
    {
        _localSettingsService = localSettingsService;
        _logger = logger.ForContext<ModOverlayService>();
        ViewModel = new ModSwitchViewModel(gameService, skinManagerService,
            modPresetService, localSettingsService, logger);
    }

    public ModSwitchViewModel ViewModel { get; }

    public bool IsEnabled { get; private set; }

    /// <summary>
    ///     启动后调用：按设置决定要不要显示悬浮球并注册热键。
    /// </summary>
    public async Task InitializeAsync()
    {
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<ModOverlaySettings>(
            ModOverlaySettings.Key, SettingScope.App);

        if (!settings.Enabled)
        {
            _logger.Information("游戏内悬浮窗未启用（设置里关着）");
            return;
        }

        await ShowBallAsync(settings.X, settings.Y);
    }

    /// <summary>
    ///     显示 / 隐藏整个悬浮窗（设置开关用）。
    /// </summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<ModOverlaySettings>(
            ModOverlaySettings.Key, SettingScope.App);

        settings.Enabled = enabled;
        await _localSettingsService.SaveSettingAsync(ModOverlaySettings.Key, settings, SettingScope.App);

        if (enabled)
        {
            await ShowBallAsync(settings.X, settings.Y);
        }
        else
        {
            HidePanel();
            _ball?.Hide();
            UnregisterHotKey();
            IsEnabled = false;
        }
    }

    private async Task ShowBallAsync(double x, double y)
    {
        if (_ball is null)
        {
            _ball = new OverlayBallWindow();
            _ball.Clicked += TogglePanel;
            _ball.HotkeyPressed += TogglePanel;
            _ball.DragFinished += OnBallDragFinished;

            if (x > 0 && y > 0)
            {
                await _ball.InitializeAsync(x, y);
            }
            else
            {
                // 没存过位置，让球自己落到主屏右上角，然后再记下初始位置
                await _ball.InitializeAsync(-1, -1);
                var pos = _ball.GetPosition();
                await SavePositionAsync(pos.X, pos.Y);
            }
        }
        else
        {
            _ball.Show();
        }

        EnsureHotKey();
        IsEnabled = true;
        _logger.Information("游戏内悬浮球已显示");
    }

    private async void OnBallDragFinished()
    {
        if (_ball is null)
        {
            return;
        }

        var (x, y) = _ball.GetPosition();
        await SavePositionAsync(x, y);
    }



    #region 全局热键

    private void EnsureHotKey()
    {
        if (_ball is null || _hotkeyRegistered || _ball.Hwnd == IntPtr.Zero)
        {
            return;
        }

        _hwnd = _ball.Hwnd;

        var modifiers = OverlayNative.MOD_ALT | OverlayNative.MOD_NOREPEAT;

        if (!OverlayNative.RegisterHotKey(_hwnd, HotkeyId, modifiers, OverlayNative.VK_C))
        {
            _logger.Warning("Alt+C 注册失败 —— 大概率被别的程序占用了，悬浮球点击仍然可用");
            return;
        }

        _hotkeyRegistered = true;
        _logger.Information("Alt+C 热键已注册");
    }

    private void UnregisterHotKey()
    {
        if (!_hotkeyRegistered)
        {
            return;
        }

        OverlayNative.UnregisterHotKey(_hwnd, HotkeyId);
        _hotkeyRegistered = false;
    }

    #endregion

    #region 面板

    /// <summary>
    ///     Alt+C 或点悬浮球：面板显示 / 隐藏。async void：窗口过程里同步调用的入口。
    /// </summary>
    public async void TogglePanel()
    {
        _logger.Information("悬浮窗：TogglePanel 被调用，当前面板可见={Visible}", _panelVisible);

        if (_panelVisible)
        {
            HidePanel();
            return;
        }

        if (_panel is null)
        {
            _panel = new ModSwitchPanelWindow(this);
            _panel.Closed += (_, _) =>
            {
                _panel = null;
                _panelVisible = false;
            };
        }

        try
        {
            // 先读角色使用频率（异步 IO），"常用排序"要用
            await ViewModel.LoadCharacterUsageAsync();
            ViewModel.Reload();
            _panel.ShowPanel();
            _panelVisible = true;
            _logger.Information("悬浮窗：面板已显示");
        }
        catch (Exception e)
        {
            _logger.Error(e, "悬浮窗：显示面板失败");
        }
    }

    public void HidePanel()
    {
        if (!_panelVisible)
        {
            return;
        }

        _panel?.HidePanel();
        _panelVisible = false;
    }

    #endregion

    /// <summary>
    ///     悬浮球被拖过之后存位置，下次启动还在同一个地方。
    /// </summary>
    public async Task SavePositionAsync(double x, double y)
    {
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<ModOverlaySettings>(
            ModOverlaySettings.Key, SettingScope.App);

        settings.X = x;
        settings.Y = y;

        await _localSettingsService.SaveSettingAsync(ModOverlaySettings.Key, settings, SettingScope.App);
    }
}
