using System;
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
            MainWindow.Title = "OMniX 自动更新器";

            var arguments = Environment.GetCommandLineArgs();

            // 二开：第二个参数是「本地更新包路径」。
            // 用户在设置页选了一个自己下载好的 zip 时，主程序把它传进来，
            // 更新器就跳过 GitHub 检查和下载，直接拿这个包装。
            var launchArgs = arguments.Skip(1).ToArray();

            MainWindow.Content = new MainPage(
                launchArgs.FirstOrDefault() ?? string.Empty,
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