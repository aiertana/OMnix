using System.Net;
using System.Net.Http;
using System.Threading.RateLimiting;

using Microsoft.UI.Xaml;

using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.Services;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Services.GameBanana;
using OMniX.Core.Services.GameBanana.OilFx;

using OMniX.WinUI.Views.Dialogs;

using Polly;
using Polly.RateLimiting;
using Polly.Registry;

using Serilog;

namespace OmniX.Plugin.OilFX.UI;

/// <summary>
///     OilFX 独立版应用：不依赖 OMniX 宿主，自带全部服务。
///     界面源码（OilFXWindow / OilFXViewModel）与宿主共享同一份文件，外观行为完全一致。
/// </summary>
public partial class App : Application
{
    public static IApiGameBananaClient Api { get; private set; } = null!;
    public static ArchiveService Archive { get; private set; } = null!;
    public static ISkinManagerService Skins { get; private set; } = null!;
    public static IGameService Games { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Log.Information("App ctor: InitializeComponent 完成");
        try
        {
            InitializeServices();
            Log.Information("App ctor: 服务初始化完成");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "App ctor: 服务初始化失败");
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log.Information("OnLaunched: 创建窗口");
            new OilFXWindow().Activate();
            Log.Information("OnLaunched: 窗口已激活");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "OnLaunched 失败");
            throw;
        }
    }

    private static void InitializeServices()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OMniX", "logs");
        Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDir, "oilfx-ui-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
            .CreateLogger();

        // GameBanana HTTP 客户端：重试 + 超时（操作频率低，不需要限流）
        // 注意：client 里调的是非泛型 GetPipeline(key)，所以这里必须注册非泛型 pipeline
        var registry = new ResiliencePipelineRegistry<string>();
        registry.GetOrAddPipeline(ApiGameBananaClient.HttpClientName, builder =>
        {
            builder
                .AddRetry(new Polly.Retry.RetryStrategyOptions
                {
                    BackoffType = Polly.DelayBackoffType.Linear,
                    UseJitter = true,
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(200)
                })
                .AddTimeout(new Polly.Timeout.TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(60) });

            builder.TelemetryListener = null;
        });

        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "OMniX-Just_Another_Skin_Manager-Update-Checker");
        http.DefaultRequestHeaders.Add("Accept", "application/json");

        Api = new ApiGameBananaClient(Log.Logger, http, registry);
        Archive = new ArchiveService(Log.Logger);
        Skins = new StandaloneSkinManager(DetectModsRoot());
        Games = new StandaloneGameService();
    }

    /// <summary>探测 XXMI 的 Mods 目录（找不到就用第一个存在的盘符候选，交给用户在界面看提示）。</summary>
    private static string DetectModsRoot()
    {
        var candidates = new[]
        {
            @"D:\mod\XXMI Launcher\WWMI\Mods",
            @"C:\XXMI Launcher\WWMI\Mods",
            @"D:\XXMI Launcher\WWMI\Mods",
            @"D:\Games\XXMI Launcher\WWMI\Mods",
            @"C:\Program Files\XXMI Launcher\WWMI\Mods",
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c))
            {
                return c;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XXMI Launcher", "WWMI", "Mods");
    }
}
