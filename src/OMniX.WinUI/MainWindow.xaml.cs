using Microsoft.UI.Xaml;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Helpers.Xaml;

using Windows.UI.ViewManagement;

namespace OMniX.WinUI;

public sealed partial class MainWindow : WindowEx
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

    private readonly UISettings settings;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();

        // Theme change code picked from https://github.com/microsoft/WinUI-Gallery/pull/1239
        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged; // cannot use FrameworkElement.ActualThemeChanged event
    }

    /// <summary>
    ///     二开：全局点击音效。
    ///     Content 就是 ShellPage，它是窗口内容的根元素，
    ///     监听挂在它上面，等于整个窗口任何位置按下（包括空白区域）都能收到。
    ///
    ///     必须等 ActivationService 把 Content 设好之后再调，早了 Content 还是 null。
    ///     音效开关走 UiSoundService.Enabled，设置里关掉就真的一声不出。
    /// </summary>
    public void AttachGlobalClick()
    {
        if (Content is FrameworkElement root)
            MotionFx.AttachGlobalClick(root);
    }

    // this handles updating the caption button colors correctly when indows system theme is changed
    // while the app is open
    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        // This calls comes off-thread, hence we will need to dispatch it to current app's thread
        dispatcherQueue.TryEnqueue(() =>
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
        });
    }
}