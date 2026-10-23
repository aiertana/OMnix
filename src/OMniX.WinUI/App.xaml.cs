using System.Diagnostics;
using System.Reflection;
using System.Threading.RateLimiting;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.Services;
using OMniX.Core.Services.CommandService;
using OMniX.Core.Services.GameBanana;
using OMniX.Core.Services.ModPresetService;
using OMniX.WinUI.Activation;
using OMniX.WinUI.Configuration;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.AppManagement.Updating;
using OMniX.WinUI.Services.ModExport;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.ViewModels;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using OMniX.WinUI.ViewModels.CharacterGalleryViewModels;
using OMniX.WinUI.ViewModels.CharacterManagerViewModels;
using OMniX.WinUI.ViewModels.SettingsViewModels;
using OMniX.WinUI.Views;
using OMniX.WinUI.Views.CharacterManager;
using OMniX.WinUI.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Polly.RateLimiting;
using Polly.Retry;
using Serilog;
using Serilog.Events;
using Serilog.Templates;
using CreateCommandViewModel = OMniX.WinUI.ViewModels.SettingsViewModels.CreateCommandViewModel;
using GameBananaService = OMniX.WinUI.Services.ModHandling.GameBananaService;
using NotificationManager = OMniX.WinUI.Services.Notifications.NotificationManager;

namespace OMniX.WinUI;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host { get; }

    public static T GetService<T>()
        where T : class
    {
        if ((Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");

        return service;
    }

    public static DirectoryInfo GetUniqueTmpFolder()
    {
        var tmpFolder = new DirectoryInfo(Path.Combine(TMP_DIR, Guid.NewGuid().ToString()));
        if (!tmpFolder.Exists)
            tmpFolder.Create();
        return tmpFolder;
    }

    public static string TMP_DIR { get; } = Path.Combine(Path.GetTempPath(), "OMniX_TMP");
    public static string ROOT_DIR { get; } = AppDomain.CurrentDomain.BaseDirectory;
    public static string ASSET_DIR { get; } = Path.Combine(ROOT_DIR, "Assets");

    public static WindowEx MainWindow { get; } = new MainWindow();
    public static UIElement? AppTitlebar { get; set; }

    public static bool OverrideShutdown { get; set; }
    public static bool IsShuttingDown { get; set; }
    public static bool ShutdownComplete { get; set; }

    public static bool UnhandledExceptionHandled { get; set; }

    // 二开：诊断用。记录所有“首次异常”（含被 catch 掉的），
    // 进程被 WinRT 直接杀掉时（0xc000027b）托管 handler 拿不到栈，
    // 只能靠崩溃前最后一条 FIRST-CHANCE 定位。
    private static void HookFirstChanceException()
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            try
            {
                var exception = args.Exception;

                if (exception is not (InvalidOperationException or System.Runtime.InteropServices.COMException))
                    return;

                Serilog.Log.Error(exception, "FIRST-CHANCE {Type}: {Message}",
                    exception.GetType().Name, exception.Message);
            }
            catch
            {
                // 诊断代码不能影响主流程
            }
        };
    }

    /// <summary>
    ///     二开：把系统 accent 色换成本项目 logo 的珊瑚红。
    ///     ContentDialog 的主按钮在 WinUI 模板里是硬编码 AccentButtonStyle 的，
    ///     设 PrimaryButtonStyle 不生效，只能从生成 accent 画刷的源头改。
    /// </summary>
    private static void ApplyOmniXAccent()
    {
        var accent = Windows.UI.Color.FromArgb(255, 0xE5, 0x48, 0x4D);

        var resources = Application.Current.Resources;

        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = Windows.UI.Color.FromArgb(255, 0xFF, 0x7A, 0x84);
        resources["SystemAccentColorLight2"] = Windows.UI.Color.FromArgb(255, 0xFF, 0x9E, 0xA6);
        resources["SystemAccentColorLight3"] = Windows.UI.Color.FromArgb(255, 0xFF, 0xC0, 0xC4);
        resources["SystemAccentColorDark1"] = Windows.UI.Color.FromArgb(255, 0xC9, 0x17, 0x2A);
        resources["SystemAccentColorDark2"] = Windows.UI.Color.FromArgb(255, 0xA5, 0x10, 0x20);
        resources["SystemAccentColorDark3"] = Windows.UI.Color.FromArgb(255, 0x7E, 0x0A, 0x17);

        resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(accent);
        resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xE5, 0x48, 0x4D));
        resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xE5, 0x48, 0x4D));
        resources["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(accent);
        resources["AccentTextFillColorSecondaryBrush"] = new SolidColorBrush(accent);
    }

    public App()
    {
        InitializeComponent();

        try
        {
            HookFirstChanceException();
        }
        catch
        {
            // 诊断代码失败不能影响启动
        }

        try
        {
            ApplyOmniXAccent();
        }
        catch
        {
            // 主题改色失败不能影响启动
        }

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog((context, configuration) =>
            {
                configuration.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);

                configuration.Filter.ByExcluding(logEvent =>
                    logEvent.Exception is RateLimiterRejectedException);
                configuration.Enrich.FromLogContext();


                configuration.ReadFrom.Configuration(context.Configuration);
                var mt = new ExpressionTemplate(
                    "[{@t:yyyy-MM-dd'T'HH:mm:ss} {@l:u3} {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] {@m}\n{@x}");
                // 二开：1 秒刷盘 + 允许多实例共享写入。
                // 之前是独占打开，上一个进程没退出时新实例会因打不开日志文件而启动失败
                configuration.WriteTo.File(formatter: mt, "logs\\log.txt",
                    shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1));
                if (Debugger.IsAttached) configuration.WriteTo.Debug();
            })
            .ConfigureServices((context, services) =>
            {
                // Default Activation Handler
                services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                // Other Activation Handlers
                services.AddTransient<IActivationHandler, FirstTimeStartupActivationHandler>();

                // Services
                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddSingleton<INavigationViewService, NavigationViewService>();

                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService, NavigationService>();

                services.AddSingleton<IWindowManagerService, WindowManagerService>();
                services.AddSingleton<NotificationManager>();
                services.AddSingleton<ModNotificationManager>();
                services.AddTransient<ModDragAndDropService>();
                services.AddSingleton<IArchivePasswordPromptService, ArchivePasswordPromptService>();
                services.AddSingleton<CharacterSkinService>();

                services.AddSingleton<ElevatorService>();
                services.AddSingleton<GenshinProcessManager>();
                services.AddSingleton<ThreeDMigtoProcessManager>();

                services.AddSingleton<UpdateChecker>();
                services.AddSingleton<AutoUpdaterService>();

                services.AddSingleton<ImageHandlerService>();
                services.AddSingleton<SelectedGameService>();

                services.AddSingleton<LifeCycleService>();
                services.AddSingleton<BusyService>();
                services.AddSingleton<JsonExporterService>();

                // Core Services
                services.AddSingleton<IFileService, FileService>();
                services.AddSingleton<IGameService, GameService>();
                services.AddSingleton<ISkinManagerService, SkinManagerService>();
                services.AddSingleton<ModCrawlerService>();
                services.AddSingleton<ModSettingsService>();
                // 二开：外观分区
                services.AddSingleton<AppearanceSectionService>();
                services.AddSingleton<ModPresetHandlerService>();
                services.AddSingleton<KeySwapService>();
                services.AddSingleton<ILanguageLocalizer, Localizer>();
                services.AddSingleton<ModPresetService>();
                services.AddSingleton<UserPreferencesService>();
                services.AddSingleton<ArchiveService>();
                services.AddSingleton<ModArchiveRepository>();
                services.AddSingleton<GameBananaCoreService>();
                services.AddSingleton<CommandService>();
                services.AddSingleton<CommandHandlerService>();

                services.AddTransient<HttpLoggerHandler>();
                services.AddSingleton<GameBananaService>();

                // Even though I've followed the docs, I keep getting "Exception thrown: 'System.IO.IOException' in System.Net.Sockets.dll"
                // I've read just about every microsoft docs page httpclients, and I can't figure out what I'm doing wrong
                // Also tried with httpclientfactory, but that didn't work either

                services.AddHttpClient<IApiGameBananaClient, ApiGameBananaClient>(client =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "OMniX-Just_Another_Skin_Manager-Update-Checker");
                        client.DefaultRequestHeaders.Add("OmniX-Version", $"{Assembly.GetExecutingAssembly().GetName().Version!}");
                        client.DefaultRequestHeaders.Add("Accept", "application/json");
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler()
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                    }).AddHttpMessageHandler<HttpLoggerHandler>();

                // I'm preeeetty sure this is not correctly set up, not used to polly 8.x.x
                // But it does rate limit, so I guess it's fine for now
                services.AddResiliencePipeline(ApiGameBananaClient.HttpClientName, (builder, context) =>
                {
                    var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions()
                    {
                        QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
                        QueueLimit = 20,
                        TokenLimit = 5,
                        AutoReplenishment = true,
#if DEBUG
                        TokensPerPeriod = 1,
#else
                        TokensPerPeriod = 5,
#endif
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1)
                    });

                    builder
                        .AddRateLimiter(limiter)
                        .AddRetry(new RetryStrategyOptions()
                        {
                            BackoffType = DelayBackoffType.Linear,
                            UseJitter = true,
                            MaxRetryAttempts = 8,
                            Delay = TimeSpan.FromMilliseconds(200)
                        });

                    builder.TelemetryListener = null;
                    context.OnPipelineDisposed(() =>
                    {
                        // This is never called, so I'm not sure if this is correct
                        Log.Debug("Disposing rate limiter");
                        limiter.Dispose();
                    });
                });
                services.AddSingleton<ModUpdateAvailableChecker>();
                services.AddSingleton<ModInstallerService>();


                services.AddHttpClient(Options.DefaultName, (client) =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "OMniX-Just_Another_Skin_Manager");
                        client.DefaultRequestHeaders.Add("OmniX-Version", $"{Assembly.GetExecutingAssembly().GetName().Version!}");
                        client.DefaultRequestHeaders.Add("Accept", "application/json");
                    })
                    .AddHttpMessageHandler<HttpLoggerHandler>()
                    .AddPolicyHandler(
                        HttpPolicyExtensions.HandleTransientHttpError()
                            .WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMilliseconds(500), 3, null, true))
                    );

                // Views and ViewModels
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<StartupViewModel>();
                services.AddTransient<StartupPage>();
                services.AddTransient<ShellPage>();
                services.AddTransient<ShellViewModel>();
                services.AddTransient<NotificationsViewModel>();
                services.AddTransient<NotificationsPage>();
                services.AddTransient<CharactersViewModel>();
                services.AddTransient<CharactersPage>();
                services.AddTransient<DebugViewModel>();
                services.AddTransient<DebugPage>();
                services.AddTransient<CharacterManagerViewModel>();
                services.AddTransient<CharacterManagerPage>();
                services.AddTransient<EditCharacterViewModel>();
                services.AddTransient<EditCharacterPage>();
                services.AddTransient<EasterEggVM>();
                services.AddTransient<EasterEggPage>();
                services.AddTransient<ModsOverviewVM>();
                services.AddTransient<ModsOverviewPage>();
                services.AddTransient<ModInstallerVM>();
                services.AddTransient<ModInstallerPage>();
                services.AddTransient<PresetViewModel>();
                services.AddTransient<PresetPage>();
                services.AddTransient<PresetDetailsViewModel>();
                services.AddTransient<PresetDetailsPage>();
                services.AddTransient<ModSelectorViewModel>();
                services.AddTransient<ModSelector>();
                services.AddTransient<CommandsSettingsViewModel>();
                services.AddTransient<CommandsSettingsPage>();
                services.AddTransient<CharacterGalleryPage>();
                services.AddTransient<CharacterGalleryViewModel>();
                services.AddTransient<CommandProcessViewer>();
                services.AddTransient<CommandProcessViewerViewModel>();
                services.AddTransient<CreateCommandView>();
                services.AddTransient<CreateCommandViewModel>();
                services.AddTransient<ViewModels.CharacterDetailsViewModels.CharacterDetailsViewModel>();
                services.AddTransient<ModPaneVM>();
                services.AddTransient<ModGridVM>();
                services.AddTransient<ContextMenuVM>();
                services.AddTransient<CreateCharacterPage>();
                services.AddTransient<CreateCharacterViewModel>();
                services.AddTransient<HomeViewModel>();
                services.AddTransient<HomePage>();
                services.AddTransient<PluginAreaViewModel>();
                services.AddTransient<PluginAreaPage>();

                // 二开：DLSS5 渲染链插件（只做检测 + 调部署包脚本，删掉部署包不影响管理器）
                services.AddSingleton<Dlss5PluginService>();
                services.AddSingleton<PluginDependencyService>();

                // 二开：首页和左侧栏两个「启动游戏」共用同一套逻辑
                services.AddSingleton<GameLaunchService>();

                // 二开：界面风格（简易版 / 标准版），只切换动效不动配色
                services.AddSingleton<ThemeStyleService>();

                // 二开：交互音效，跟着界面风格走，简易版静音
                services.AddSingleton<UiSoundService>();

                // Configuration
                services.Configure<LocalSettingsOptions>(
                    context.Configuration.GetSection(nameof(LocalSettingsOptions)));

                services.AddSingleton<ModRandomizationService>();
            }).Build();

        UnhandledException += App_UnhandledException;
    }

    // Incremented when an error window is opened, decremented when it is closed
    // Just avoid spamming the user with error windows
    private int _ErrorWindowsOpen = 0;

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Log.Error(e.Exception, """

                               --------------------------------------------------------------------
                                    _   _    ____  __  __                                        
                                   | | / \  / ___||  \/  |                                       
                                _  | |/ _ \ \___ \| |\/| |                                       
                               | |_| / ___ \ ___) | |  | |                                       
                                \___/_/   \_\____/|_|  |_| _ _   _ _____ _____ ____  _____ ____  
                               | ____| \ | |/ ___/ _ \| | | | \ | |_   _| ____|  _ \| ____|  _ \ 
                               |  _| |  \| | |  | | | | | | |  \| | | | |  _| | |_) |  _| | | | |
                               | |___| |\  | |__| |_| | |_| | |\  | | | | |___|  _ <| |___| |_| |
                               |_____|_|_\_|\____\___/_\___/|_|_\_| |_| |_____|_| \_\_____|____/ 
                                  / \  | \ | | | | | | \ | | |/ / \ | |/ _ \ \      / / \ | |    
                                 / _ \ |  \| | | | | |  \| | ' /|  \| | | | \ \ /\ / /|  \| |    
                                / ___ \| |\  | | |_| | |\  | . \| |\  | |_| |\ V  V / | |\  |    
                               /_/___\_\_| \_|__\___/|_|_\_|_|\_\_| \_|\___/  \_/\_/  |_| \_|    
                               | ____|  _ \|  _ \ / _ \|  _ \                                    
                               |  _| | |_) | |_) | | | | |_) |                                   
                               | |___|  _ <|  _ <| |_| |  _ <                                    
                               |_____|_| \_\_| \_\\___/|_| \_\                                   
                               --------------------------------------------------------------------
                               """);

        // show error dialog
        var window = new ErrorWindow(e.Exception, () => _ErrorWindowsOpen--)
        {
            IsAlwaysOnTop = true,
            Title = "OMniX - Unhandled Exception",
            SystemBackdrop = new MicaBackdrop()
        };

        window.Activate();
        _ErrorWindowsOpen++;
        window.CenterOnScreen();

        GetService<NotificationManager>()
            .ShowNotification("An error occured!",
                "OMniX may be in an unstable state could crash at any moment. It is suggested to restart the app.",
                TimeSpan.FromMinutes(60));

        if (_ErrorWindowsOpen > 4)
        {
            // If there are too many error windows open, just close the app
            // This is to prevent the app from spamming the user with error windows
            Environment.Exit(1);
        }
    }


    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 二开：启动时把署名写进日志。这段字符串跟着 dll 一起编译，
        // 别人改界面文字改不掉它
        System.Diagnostics.Trace.WriteLine(OMniX.WinUI.Helpers.AppIdentity.Stamp);

        Environment.SetEnvironmentVariable("WEBVIEW2_USE_VISUAL_HOSTING_FOR_OWNED_WINDOWS", "1");
        await GetService<ILanguageLocalizer>().InitializeAsync();
        await GetService<ThemeStyleService>().InitializeAsync();
        await GetService<UiSoundService>().InitializeAsync();
        NotImplemented.NotificationManager = GetService<NotificationManager>();
        base.OnLaunched(args);
        await GetService<IActivationService>().ActivateAsync(args).ConfigureAwait(false);
    }
}