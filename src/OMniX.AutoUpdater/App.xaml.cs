using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.UI.Xaml;
using Serilog;
using WinUIEx;
using UnhandledExceptionEventArgs = Microsoft.UI.Xaml.UnhandledExceptionEventArgs;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OMniX.AutoUpdater;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();


        var logger = CreateLogger();

        Log.Logger = logger;
    }

    private static ILogger CreateLogger() => new LoggerConfiguration()
        .Enrich.FromLogContext()
        .MinimumLevel.Information()
        .WriteTo.File(LogFilePath)
        .WriteTo.Console()
        .CreateLogger();

    private static readonly string LogFilePath =
        $@"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}\logs\auto-updater-log.txt";

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Information("AutoUpdater OnLaunched");

        // 二开：这行原来挂在方法最后。结果是 —— 只要 MainWindow / MainPage
        // 的构造抛异常，handler 还没挂上，异常直接穿透出去，进程当场死掉。
        // 日志里只剩孤零零一行 "OnLaunched"，什么都交代不了就闪退，
        // 排查时只能靠猜（这次就是这么卡住的）。挂到最前面。
        UnhandledException += OnUnhandledException;

        try
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
            MainWindow.SetWindowSize(900, 600);
            MainWindow.IsMaximizable = false;
            MainWindow.IsMinimizable = false;
            MainWindow.IsResizable = false;
            MainWindow.Title = "OMniX Auto Updater";

            var arguments = Environment.GetCommandLineArgs();

            // 二开：第二个参数是「本地更新包路径」。
            // 用户在设置页选了一个自己下载好的 zip 时，主程序把它传进来，
            // 更新器就跳过 GitHub 检查和下载，直接拿这个包装。
            var launchArgs = arguments.Skip(1).ToArray();

            // 二开：外置更新入口。以前当前版本号只能由主程序传进来，
            // 主程序一旦打不开（崩了、缺运行时、配置坏了），就没人告诉更新器
            // 现在装的是哪一版 —— 版本号是空的，更新器只能退到下整包，
            // 而且用户根本找不到地方点更新，只能自己去 GitHub 翻。
            // 现在补上：没传版本就自己找上一级目录里的 OMniX.exe 读，
            // 于是直接双击更新器（或者包顶层的「手动更新」）也能走增量补丁。
            var currentVersion = launchArgs.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(currentVersion))
            {
                currentVersion = DetectInstalledVersion();
            }

            MainWindow.Content = new MainPage(
                currentVersion ?? string.Empty,
                launchArgs.Skip(1).FirstOrDefault() ?? string.Empty);
            MainWindow.BringToFront();
        }
        catch (Exception ex)
        {
            // 记下来再抛 —— 让上面挂的 handler 弹出提示，别让进程无声无息地死掉。
            Log.Error(ex, "创建更新器界面时出错");
            Log.CloseAndFlush();
            throw;
        }
    }

    /// <summary>
    ///     二开：没拿到版本号时自己找一个。更新器在
    ///     &lt;安装目录&gt;\OMniX\OMniX - Auto Updater\ 里，主程序就在上一级，
    ///     读它的文件版本即可。这样不再依赖主程序传参 ——
    ///     主程序打不开也照样能更新。
    /// </summary>
    private static string DetectInstalledVersion()
    {
        try
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (string.IsNullOrEmpty(baseDir))
            {
                return string.Empty;
            }

            var exe = Path.GetFullPath(Path.Combine(baseDir, "..", "OMniX.exe"));

            if (!File.Exists(exe))
            {
                Log.Warning("探测版本失败：没找到 {Path}", exe);
                return string.Empty;
            }

            var version = FileVersionInfo.GetVersionInfo(exe).FileVersion;

            Log.Information("自动探测到当前版本 {Version}（读自 {Path}）", version, exe);

            return version ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "探测当前版本时出错");
            return string.Empty;
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception");
        Log.Information("------");
        Log.CloseAndFlush();

        e.Handled = true;

        var result = PInvoke.MessageBox(
            new HWND(WinRT.Interop.WindowNative.GetWindowHandle(MainWindow)),
            $"Check the logs file for more info: {LogFilePath}\n\n" +
            $"Press Yes to close the program. Press No to ignore the error and continue",
            $"An error occured: {e.Exception.GetType()} | {e.Exception.Message ?? "null"}",
            MESSAGEBOX_STYLE.MB_ICONSTOP | MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_APPLMODAL |
            MESSAGEBOX_STYLE.MB_SETFOREGROUND);

        if (result == MESSAGEBOX_RESULT.IDYES)
        {
            Current.Exit();
            return;
        }

        Log.Logger = CreateLogger();

        var mainPage = MainWindow.Content as MainPage;
        MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                mainPage?.ViewModel?.Stop("An unknown error occured..");
            }
            catch (Exception exception)
            {
                return;
            }
        });

        return;
    }

    internal static MainWindow MainWindow = null!;
}