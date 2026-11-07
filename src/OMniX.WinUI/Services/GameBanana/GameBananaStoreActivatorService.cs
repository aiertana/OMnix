using System.IO.Pipes;
using System.Text;

using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;

namespace OMniX.WinUI.Services.GameBanana;

/// <summary>
///     GameBanana 独立插件（GamesData\Plugins\GameBananaStore）的激活通道。
///     插件本体是个小启动器 exe：点「运行」后它连上这里的命名管道，
///     主程序收到消息就打开商店窗口；主程序没在跑时启动器会带
///     --open-gamebanana 参数拉起主程序，OnLaunched 里也会查这个参数。
///     这样商店以独立插件形态存在（可整体删除/更新），UI 复用主程序。
/// </summary>
public sealed class GameBananaStoreActivatorService
{
    public const string PipeName = "OMniX.Plugin.GameBananaStore";
    public const string CommandLineFlag = "--open-gamebanana";

    private readonly IWindowManagerService _windowManager;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;

    public GameBananaStoreActivatorService(IWindowManagerService windowManager)
    {
        _windowManager = windowManager;
        // 注意别用 GetForCurrentThread：服务可能在 await 之后的线程池线程上被解析
        // （OnLaunched 尾部 ConfigureAwait(false)），那样拿到的会是 null。
        // MainWindow.DispatcherQueue 是窗口线程的，始终有效。
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

                if (message.Trim().Equals("open-gamebanana", StringComparison.OrdinalIgnoreCase))
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            OpenStoreWindow();
                        }
                        catch (Exception e)
                        {
                            Serilog.Log.Error(e, "GameBanana 插件请求打开商店窗口失败");
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
                Serilog.Log.Warning(e, "GameBanana 插件激活管道监听中断，1 秒后重试");
                await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>打开（或聚焦）商店窗口。管道与命令行两条路共用。</summary>
    public void OpenStoreWindow()
    {
        var window = new Views.Dialogs.GameBananaStoreWindow();
        _windowManager.CreateWindow(window, identifier: "GameBananaStore");
    }

    /// <summary>启动参数里带了 --open-gamebanana 就在初始化完成后打开商店。</summary>
    public static bool LaunchedWithStoreFlag()
    {
        return Environment.GetCommandLineArgs()
            .Any(a => a.Equals(CommandLineFlag, StringComparison.OrdinalIgnoreCase));
    }
}
