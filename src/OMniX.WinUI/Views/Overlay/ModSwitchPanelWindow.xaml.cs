using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OMniX.WinUI.Services.Overlay;
using OMniX.WinUI.ViewModels.Overlay;
using Windows.Graphics;
using Windows.UI;

namespace OMniX.WinUI.Views.Overlay;

/// <summary>
///     悬浮球点开的小面板：角色标签 → Mod 封面卡片 → 开关 / 预设。
///     切换完会顺带刷新 3Dmigoto，不用回游戏按 F10（需要同步进程在跑）。
/// </summary>
public sealed partial class ModSwitchPanelWindow : WindowEx
{
    /// <summary>
    ///     缩放 = 100% 时的基准窗口尺寸（1:1 方形）。
    /// </summary>
    private const int BaseWidth = 700;

    private const int BaseHeight = 700;

    private readonly ModOverlayService _service;

    private bool _pinned = true;

    public ModSwitchPanelWindow(ModOverlayService service)
    {
        _service = service;

        InitializeComponent();

        ScaledRoot.DataContext = _service.ViewModel;
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
        }

        IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        ApplyScale(ViewModel.UiScale);
        LoadBackgroundImage();
    }

    private ModSwitchViewModel ViewModel => _service.ViewModel;

    /// <summary>
    ///     背景图从未打包应用的输出目录读（ms-appx 在 unpackaged 下不可靠）。
    /// </summary>
    private void LoadBackgroundImage()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "OverlayPanelBg.png");
            if (File.Exists(path))
            {
                BackgroundImage.Source = new BitmapImage(new Uri(path));
            }
        }
        catch (Exception)
        {
            // 背景图读不出来就算了，面板还有半透明白底兜底
        }
    }

    /// <summary>
    ///     A- / A+ 缩放：内容 RenderTransform 放大，窗口尺寸同步乘上去；
    ///     内容层尺寸 = 窗口 / scale，这样布局按小尺寸排、渲染放大后正好铺满，
    ///     且 Grid 的 * 行能正常撑开（不然预设行会被顶到上面去）。
    /// </summary>
    private void ApplyScale(double scale)
    {
        ContentScale.ScaleX = scale;
        ContentScale.ScaleY = scale;
        ScaledRoot.Width = BaseWidth / scale;
        ScaledRoot.Height = BaseHeight / scale;
        AppWindow.Resize(new SizeInt32((int)(BaseWidth * scale), (int)(BaseHeight * scale)));
    }

    public void ShowPanel()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = ViewModel.UiScale;
        AppWindow.Move(new PointInt32((int)(workArea.Width - BaseWidth * scale - 40), 120));
        AppWindow.Show();
        Activate();
    }

    public void HidePanel()
    {
        AppWindow.Hide();
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModSwitchViewModel.UiScale))
        {
            ApplyScale(ViewModel.UiScale);
        }
        else if (e.PropertyName == nameof(ModSwitchViewModel.IsCharacterRowCollapsed) && CharacterRow is not null)
        {
            CharacterRow.Visibility = ViewModel.IsCharacterRowCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _service.HidePanel();
    }

    /// <summary>
    ///     图钉：切换面板是否置顶。
    /// </summary>
    private void PinButton_OnClick(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _pinned;
        }

        PinButton.Content = _pinned ? "\uE718" : "\uE77A";
        ToolTipService.SetToolTip(PinButton, _pinned ? "取消置顶" : "保持置顶");
    }

    #region 拖动窗口（顶栏 / 状态栏按住拖）

    private bool _dragPressed;

    private bool _dragMoving;

    private NativePoint _dragStartCursor;

    private PointInt32 _dragStartWindow;

    private void WindowDrag_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragPressed = true;
        _dragMoving = false;
        _dragStartCursor = GetCursorPosition();
        _dragStartWindow = AppWindow.Position;
    }

    private void WindowDrag_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragPressed)
        {
            return;
        }

        var cursor = GetCursorPosition();
        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;

        // 位移够大才算拖窗口，否则只是点了个按钮，别把窗口挪走
        if (!_dragMoving && Math.Abs(dx) + Math.Abs(dy) < 4)
        {
            return;
        }

        if (!_dragMoving)
        {
            _dragMoving = true;
            (sender as UIElement)?.CapturePointer(e.Pointer);
        }

        AppWindow.Move(new PointInt32(_dragStartWindow.X + dx, _dragStartWindow.Y + dy));
    }

    private void WindowDrag_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragPressed = false;

        if (_dragMoving)
        {
            (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        }

        _dragMoving = false;
    }

    private static NativePoint GetCursorPosition()
    {
        GetCursorPos(out var point);
        return point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    #endregion

    /// <summary>
    ///     点角色标签：切换当前角色。角色行只看滚轮横向滚动，不做拖动。
    /// </summary>
    private void Tab_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CharacterTabVm tab)
        {
            ViewModel.SelectTabCommand.Execute(tab);
        }
    }

    private void SortCustom_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SortMode = 0;
    }

    private void SortName_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SortMode = 1;
    }

    private void SortStatus_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SortMode = 2;
    }

    private void SortTime_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SortMode = 3;
    }

    /// <summary>
    ///     开关被拨动 → 交给 VM 改文件夹名。
    ///     VM 改完会把 IsEnabled 写回来，绑定又会触发一次 Toggled，
    ///     所以这里先比对一下，状态一致就不重复执行。
    /// </summary>
    private void ModToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { DataContext: ModSwitchItemVm item } toggle)
        {
            return;
        }

        if (item.IsEnabled == toggle.IsOn)
        {
            return;
        }

        ViewModel.ToggleModCommand.Execute(item);
    }
}
