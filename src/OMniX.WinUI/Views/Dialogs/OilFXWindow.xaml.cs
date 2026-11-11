using System.IO;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OMniX.WinUI.ViewModels;

using WinUIEx;

namespace OMniX.WinUI.Views.Dialogs;

/// <summary>
///     OilFX 油光效果管理窗口：独立插件（GamesData\Plugins\OilFX）的宿主渲染端。
///     跟随 GameBanana mod 733773 更新 + 角色启用灯开关 + 效果参数。
/// </summary>
public sealed partial class OilFXWindow : WindowEx
{
    public OilFXViewModel ViewModel { get; }

    public OilFXWindow()
    {
#if STANDALONE
        // 独立版（OmniX.Plugin.OilFX.UI）：服务由独立应用自己构造
        ViewModel = new OilFXViewModel(
            OmniX.Plugin.OilFX.UI.App.Api,
            OmniX.Plugin.OilFX.UI.App.Archive,
            OmniX.Plugin.OilFX.UI.App.Skins,
            OmniX.Plugin.OilFX.UI.App.Games);
#else
        ViewModel = new OilFXViewModel(
            App.GetService<OMniX.Core.Services.GameBanana.IApiGameBananaClient>(),
            App.GetService<OMniX.Core.Services.ArchiveService>(),
            App.GetService<OMniX.Core.Contracts.Services.ISkinManagerService>(),
            App.GetService<OMniX.Core.GamesService.IGameService>());
#endif
        InitializeComponent();

        // 标题栏图标（OMniX娘 圆角版）
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "OilFXIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        // 自定义标题栏（AppTitleBar）：留出右侧系统 caption 按钮区，空白处可拖动窗口
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    /// <summary>置顶开关：置顶时按钮高亮 + 图标变成"取消固定"样式，方便边看窗口边玩游戏。</summary>
    private void PinTopButton_Click(object sender, RoutedEventArgs e)
    {
        IsAlwaysOnTop = !IsAlwaysOnTop;
        PinIcon.Glyph = IsAlwaysOnTop ? "\uE77A" : "\uE718";
        PinTopButton.Background = IsAlwaysOnTop
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : null;
        ToolTipService.SetToolTip(PinTopButton, IsAlwaysOnTop ? "取消置顶" : "置顶窗口（游戏时不用切来切去）");
    }

    /// <summary>角色灯开关切换：把新状态交给 VM 执行注入 / 还原。
    /// 列表重建会重新触发 Toggled，重入时直接忽略，防止无限递归。
    /// 另加"值一致忽略"：绑定初始推值时 UI 与 VM 已同值，只有用户真的拨动才执行。</summary>
    private void CharacterToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRebuilding)
        {
            return;
        }

        if (sender is ToggleSwitch { DataContext: OilFxRowVm vm } sw)
        {
            if (sw.IsOn == vm.IsEnabled)
            {
                return; // 列表重建时绑定推值触发的事件，不是用户操作
            }

            // 关键：TwoWay 绑定的回写晚于 Toggled 事件，此时 vm.IsEnabled 还是旧值。
            // 不手动同步的话，拨"开"会被当成"关"执行（方向颠倒）。
            vm.IsEnabled = sw.IsOn;
            ViewModel.ToggleModCommand.Execute(vm);
        }
    }

    /// <summary>mod 行"还原"按钮：把这一个 mod 的 ini 全部从 .bak 还原。</summary>
    private void RestoreModButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OilFxRowVm { IsModRow: true } vm })
        {
            ViewModel.RestoreModCommand.Execute(vm);
        }
    }

    /// <summary>角色分组头点击：展开 / 收起该组的 mod 列表（VM 增删集合行）。</summary>
    private void GroupHeader_OnTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OilFxRowVm { IsGroupHeader: true } vm })
        {
            ViewModel.ToggleExpand(vm);
        }
    }

    /// <summary>手动选择 mod 文件夹：FolderPicker 选中后交给 VM 扫描注入。</summary>
    private async void ManualFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
            };
            picker.FileTypeFilter.Add("*");

            // WinUI 的 FolderPicker 必须绑一个窗口句柄才能弹出
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                ViewModel.AddManualFolder(folder.Path);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "OilFX 手动选择文件夹失败");
        }
    }
}
