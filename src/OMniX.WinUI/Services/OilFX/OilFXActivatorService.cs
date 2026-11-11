using System.IO.Pipes;
using System.Text;

using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;

namespace OMniX.WinUI.Services.OilFX;

/// <summary>
///     OilFX 独立插件（GamesData\Plugins\OilFX）的激活通道，
///     与 GameBananaStoreActivatorService 同款模式：插件启动器连命名管道
///     请求打开 OilFX 管理窗口；主程序没跑时启动器带 --open-oilfx 拉起。
/// </summary>
public sealed class OilFXActivatorService
{
    public const string PipeName = "OMniX.Plugin.OilFX";
    public const string CommandLineFlag = "--open-oilfx";

    private readonly IWindowManagerService _windowManager;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;

    public OilFXActivatorService(IWindowManagerService windowManager)
    {
        _windowManager = windowManager;
        // 同 GameBanana 激活器：服务可能在 await 之后的线程池线程上被解析，
        // GetForCurrentThread 会拿到 null，必须用主窗口的队列。
        _dispatcherQueue = App.MainWindow.DispatcherQueue;
    }

    /// <summary>OnLaunched（UI 线程）里调一次，开始监听。</summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                if (message.Trim().Equals("open-oilfx", StringComparison.OrdinalIgnoreCase))
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            OpenOilFxWindow();
                        }
                        catch (Exception e)
                        {
                            Serilog.Log.Error(e, "OilFX 插件请求打开管理窗口失败");
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Serilog.Log.Warning(e, "OilFX 插件激活管道监听中断，1 秒后重试");
                await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>打开（或聚焦）OilFX 管理窗口。窗口必须在 UI 线程创建——
    /// OnLaunched 尾部（await 后线程池线程）的命令行调用也走这里，所以统一入队。</summary>
    public void OpenOilFxWindow()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var window = new Views.Dialogs.OilFXWindow();
                _windowManager.CreateWindow(window, identifier: "OilFX");
            }
            catch (Exception e)
            {
                Serilog.Log.Error(e, "创建 OilFX 管理窗口失败");
            }
        });
    }

    /// <summary>启动参数里带了 --open-oilfx 就在初始化完成后打开窗口。</summary>
    public static bool LaunchedWithOilFxFlag()
    {
        return Environment.GetCommandLineArgs()
            .Any(a => a.Equals(CommandLineFlag, StringComparison.OrdinalIgnoreCase));
    }
}
