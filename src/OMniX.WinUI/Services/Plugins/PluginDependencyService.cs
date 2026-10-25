using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Serilog;
using OMniX.Core.Contracts.Services;

namespace OMniX.WinUI.Services.Plugins;

/// <summary>
///     两个开关背后要用的三样东西。
/// </summary>
public sealed class DependencySnapshot
{
    public string XxmiLauncherPath { get; init; } = string.Empty;
    public string XxmiRoot { get; init; } = string.Empty;
    public string WwmiRoot { get; init; } = string.Empty;
    public string KitDir { get; init; } = string.Empty;

    public bool XxmiReady =>
        !string.IsNullOrWhiteSpace(XxmiLauncherPath) && File.Exists(XxmiLauncherPath);

    public bool WwmiReady =>
        Directory.Exists(WwmiRoot) && File.Exists(Path.Combine(WwmiRoot, "d3d11.dll"));

    public bool KitReady =>
        !string.IsNullOrWhiteSpace(KitDir) && File.Exists(Path.Combine(KitDir, "install.ps1"));

    /// <summary>开关至少要能控制 Mod，所以 XXMI + WWMI 必须有；DLSS5 包缺了只是那块功能不能用</summary>
    public bool ModReady => XxmiReady && WwmiReady;

    public bool AllReady => ModReady && KitReady;

    public string XxmiText => XxmiReady ? XxmiRoot : App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_XxmiMissing", "未检测到 XXMI Launcher");
    public string WwmiText => WwmiReady ? WwmiRoot : App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_WwmiMissing", "未检测到 WWMI");
    public string KitText => KitReady ? KitDir : App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_KitMissing", "未下载 DLSS5 部署包");
}

public sealed class KitDownloadResult
{
    public bool Success { get; init; }
    public string KitDir { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static KitDownloadResult Fail(string message) => new() { Message = message };
}

/// <summary>下载进度。Percent 为 -1 表示这一阶段没有百分比可算（查版本、解压等）。</summary>
public sealed class DownloadProgress
{
    public double Percent { get; init; }
    public string Text { get; init; } = string.Empty;
}

/// <summary>XXMI 便携版：下载解压后直接能用，不用跑安装器。</summary>
public sealed class XxmiDownloadResult
{
    public bool Success { get; init; }
    public string XxmiRoot { get; init; } = string.Empty;
    public string LauncherPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static XxmiDownloadResult Fail(string message) => new() { Message = message };
}

/// <summary>
///     二开：插件依赖检测。
///     - XXMI / WWMI 只检测、不代装（WWMI 需要在 XXMI 里设好游戏路径，它会自己下）
///     - DLSS5 部署包可以从 GitHub 拉最新版
/// </summary>
public sealed class PluginDependencyService
{
    public const string XxmiRepoUrl = "https://github.com/SpectrumQT/XXMI-Launcher/releases";
    public const string WwmiRepoUrl = "https://github.com/SpectrumQT/WWMI-Package";
    public const string KitRepoUrl = "https://github.com/aiertana/wwmi-dlss5-kit";

    private const string KitLatestApi = "https://api.github.com/repos/aiertana/wwmi-dlss5-kit/releases/latest";
    private const string KitZipballUrl = "https://github.com/aiertana/wwmi-dlss5-kit/archive/refs/heads/main.zip";

    private const string XxmiLatestApi = "https://api.github.com/repos/SpectrumQT/XXMI-Launcher/releases/latest";

    /// <summary>
    ///     插件的默认位置。用户没自定义时用这里 ——
    ///     二开：原来 PluginsRoot 是写死的，插件全在 C 盘，C 盘紧张的人挪不走。
    /// </summary>
    public static string DefaultPluginsRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX", "Plugins");

    private static string _pluginsRoot = DefaultPluginsRoot;

    /// <summary>插件都放这儿：删掉整个目录也不影响管理器和游戏</summary>
    public static string PluginsRoot
    {
        get => _pluginsRoot;
        private set => _pluginsRoot = string.IsNullOrWhiteSpace(value) ? DefaultPluginsRoot : value;
    }

    /// <summary>
    ///     二开：设置页保存、启动时读取都走这里。传空 = 恢复默认位置。
    ///     必须在任何下载/扫描动作之前调用，否则这一轮还是用旧目录。
    /// </summary>
    public static void SetPluginsRoot(string? path) => PluginsRoot = path ?? string.Empty;

    private static readonly string[] XxmiRootCandidates =
    {
        @"D:\mod\XXMI Launcher",
        @"D:\XXMI Launcher",
        @"C:\XXMI Launcher",
        @"E:\XXMI Launcher",
        @"D:\Games\XXMI Launcher",
        @"C:\Games\XXMI Launcher"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Dlss5PluginService _dlss5;
    private readonly ILogger _logger = Log.ForContext<PluginDependencyService>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public PluginDependencyService(IHttpClientFactory httpClientFactory, Dlss5PluginService dlss5)
    {
        _httpClientFactory = httpClientFactory;
        _dlss5 = dlss5;
    }

    public DependencySnapshot Detect()
    {
        var xxmiLauncher = FindXxmiLauncher();
        var xxmiRoot = string.IsNullOrWhiteSpace(xxmiLauncher)
            ? string.Empty
            : Directory.GetParent(Path.GetDirectoryName(xxmiLauncher)!)?.Parent?.FullName ?? string.Empty;

        var wwmiRoot = string.IsNullOrWhiteSpace(xxmiRoot) ? string.Empty : Path.Combine(xxmiRoot, "WWMI");

        return new DependencySnapshot
        {
            XxmiLauncherPath = xxmiLauncher,
            XxmiRoot = xxmiRoot,
            WwmiRoot = wwmiRoot,
            KitDir = _dlss5.ResolveKitDirectory()
        };
    }

    /// <summary>
    ///     先看管理器里配过的 WWMI 启动器路径，再扫常见安装位置。
    /// </summary>
    private string FindXxmiLauncher()
    {
        // 二开：先看用户配置的插件目录 —— 我们自己下载的 XXMI 就在那儿，
        // 它不在下面那份「常见安装位置」列表里（因为那个列表是编译期固定的，
        // 而插件目录现在运行时可改）。
        var configuredExe = Path.Combine(PluginsRoot, "XXMI Launcher", "Resources", "Bin", "XXMI Launcher.exe");

        if (File.Exists(configuredExe))
            return configuredExe;

        foreach (var root in XxmiRootCandidates)
        {
            var exe = Path.Combine(root, "Resources", "Bin", "XXMI Launcher.exe");

            if (File.Exists(exe))
                return exe;
        }

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;

                foreach (var folder in new[] { Path.Combine("mod", "XXMI Launcher"), "XXMI Launcher" })
                {
                    var exe = Path.Combine(drive.RootDirectory.FullName, folder, "Resources", "Bin",
                        "XXMI Launcher.exe");

                    if (File.Exists(exe))
                        return exe;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "扫描 XXMI 安装位置失败");
        }

        return string.Empty;
    }

    /// <summary>
    ///     从 GitHub 拉最新版部署包，解到 targetRoot\WWMI-DLSS5-Kit。
    /// </summary>
    public async Task<KitDownloadResult> DownloadLatestKitAsync(string targetRoot,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();

            progress?.Report(new DownloadProgress { Percent = -1, Text = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_CheckingVersion", "正在查询最新版本…") });

            var (tag, downloadUrl) = await FindLatestAssetAsync(client, KitLatestApi,
                name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                KitZipballUrl, cancellationToken);

            progress?.Report(new DownloadProgress { Percent = -1, Text = string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_DownloadingTag", "开始下载 {0}"), tag) });

            var tempZip = Path.Combine(Path.GetTempPath(), $"omni-dlss5-{Guid.NewGuid():N}.zip");

            try
            {
                await DownloadFileAsync(client, downloadUrl, tempZip, _localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_TitleKit", "下载部署包"), progress, cancellationToken);

                progress?.Report(new DownloadProgress { Percent = -1, Text = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_Extracting", "正在解压…") });

                var extractRoot = Path.Combine(Path.GetTempPath(), $"omni-dlss5-{Guid.NewGuid():N}");
                Directory.CreateDirectory(extractRoot);

                try
                {
                    ZipFile.ExtractToDirectory(tempZip, extractRoot, true);

                    var sourceRoot = FindKitRoot(extractRoot) ?? extractRoot;
                    var finalDir = Path.Combine(targetRoot, "WWMI-DLSS5-Kit");

                    if (Directory.Exists(finalDir))
                        Directory.Delete(finalDir, true);

                    Directory.CreateDirectory(targetRoot);
                    CopyDirectory(sourceRoot, finalDir);

                    await _dlss5.SavePathsAsync(_dlss5.ResolveGameExePath(), finalDir);

                    return new KitDownloadResult
                    {
                        Success = true,
                        KitDir = finalDir,
                        Message = string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_DoneTag", "已下载 {0}"), tag)
                    };
                }
                finally
                {
                    try
                    {
                        Directory.Delete(extractRoot, true);
                    }
                    catch (Exception)
                    {
                        // 临时目录删不掉无所谓
                    }
                }
            }
            finally
            {
                try
                {
                    File.Delete(tempZip);
                }
                catch (Exception)
                {
                    // 同上
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "下载 DLSS5 部署包失败");
            return KitDownloadResult.Fail(ex.Message);
        }
    }

    /// <summary>
    ///     下载 XXMI Launcher 的便携版（zip），解到插件目录下的 XXMI Launcher。
    ///     便携版不用跑安装器，解压完就能启动；游戏路径要在 XXMI 里设一次，之后 WWMI 由它自己下载。
    /// </summary>
    public async Task<XxmiDownloadResult> DownloadLatestXxmiAsync(IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();

            progress?.Report(new DownloadProgress { Percent = -1, Text = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_CheckingXxmi", "正在查询 XXMI 最新版…") });

            var (tag, url) = await FindLatestAssetAsync(client, XxmiLatestApi,
                name => name.Contains("Portable", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                XxmiRepoUrl, cancellationToken);

            if (url == XxmiRepoUrl)
                return XxmiDownloadResult.Fail(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_NoPortableZip", "没找到便携版压缩包，已改为打开下载页"));

            progress?.Report(new DownloadProgress { Percent = -1, Text = string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_DownloadingTag", "开始下载 {0}"), tag) });

            var tempZip = Path.Combine(Path.GetTempPath(), $"omni-xxmi-{Guid.NewGuid():N}.zip");

            try
            {
                await DownloadFileAsync(client, url, tempZip, _localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_TitleXxmi", "下载 XXMI"), progress, cancellationToken);

                progress?.Report(new DownloadProgress { Percent = -1, Text = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_Extracting", "正在解压…") });

                var extractRoot = Path.Combine(Path.GetTempPath(), $"omni-xxmi-{Guid.NewGuid():N}");
                Directory.CreateDirectory(extractRoot);

                try
                {
                    ZipFile.ExtractToDirectory(tempZip, extractRoot, true);

                    var sourceRoot = FindXxmiRoot(extractRoot);

                    if (sourceRoot is null)
                        return XxmiDownloadResult.Fail(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_NoLauncherExe", "压缩包里没找到 XXMI Launcher.exe"));

                    var finalDir = Path.Combine(PluginsRoot, "XXMI Launcher");

                    if (Directory.Exists(finalDir))
                        Directory.Delete(finalDir, true);

                    Directory.CreateDirectory(PluginsRoot);
                    CopyDirectory(sourceRoot, finalDir);

                    var launcher = Path.Combine(finalDir, "Resources", "Bin", "XXMI Launcher.exe");

                    return new XxmiDownloadResult
                    {
                        Success = true,
                        XxmiRoot = finalDir,
                        LauncherPath = launcher,
                        Message = string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_XxmiDoneHint", "已下载 {0}，启动后在里面设一次游戏路径，WWMI 会自动补上"), tag)
                    };
                }
                finally
                {
                    try
                    {
                        Directory.Delete(extractRoot, true);
                    }
                    catch (Exception)
                    {
                        // 临时目录删不掉无所谓
                    }
                }
            }
            finally
            {
                try
                {
                    File.Delete(tempZip);
                }
                catch (Exception)
                {
                    // 同上
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "下载 XXMI 失败");
            return XxmiDownloadResult.Fail(ex.Message);
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "OMniX-Mod-Manager");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        return client;
    }

    /// <summary>从 release 里挑一个符合条件的附件，拿不到就用兜底地址</summary>
    private async Task<(string Tag, string Url)> FindLatestAssetAsync(HttpClient client, string api,
        Func<string, bool> match, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            var json = await client.GetStringAsync(api, cancellationToken);
            var release = JObject.Parse(json);
            var tag = release["tag_name"]?.ToString() ?? "latest";

            var url = (release["assets"] as JArray)?
                .Where(a => match(a["name"]?.ToString() ?? string.Empty))
                .Select(a => a["browser_download_url"]?.ToString())
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

            if (!string.IsNullOrWhiteSpace(url))
                return (tag, url!);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "读取 {Api} 失败，改用兜底地址", api);
        }

        return ("latest", fallback);
    }

    /// <summary>边下边报进度：有 Content-Length 就给百分比，没有就只报已下字节数</summary>
    private static async Task DownloadFileAsync(HttpClient client, string url, string destination,
        string title, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long downloaded = 0;
        var last = DateTimeOffset.MinValue;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            var now = DateTimeOffset.UtcNow;

            if ((now - last).TotalMilliseconds < 120)
                continue;

            last = now;

            progress?.Report(new DownloadProgress
            {
                Percent = total > 0 ? Math.Min(99, downloaded * 100.0 / total) : -1,
                Text = total > 0
                    ? $"{title} · {downloaded / 1048576.0:F1} / {total / 1048576.0:F1} MB"
                    : $"{title} · {downloaded / 1048576.0:F1} MB"
            });
        }

        progress?.Report(new DownloadProgress { Percent = 100, Text = string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_TitleDone", "{0} 完成"), title) });
    }

    /// <summary>压缩包里可能多套一层，找含 XXMI Launcher.exe 的那个根目录</summary>
    private static string? FindXxmiRoot(string root)
    {
        var matches = Directory.GetFiles(root, "XXMI Launcher.exe", SearchOption.AllDirectories);

        if (matches.Length == 0)
            return null;

        var exe = matches.OrderBy(m => m.Length).First();

        // ...\Root\Resources\Bin\XXMI Launcher.exe
        var bin = Path.GetDirectoryName(exe);
        var resources = bin is null ? null : Path.GetDirectoryName(bin);

        return resources is null ? null : Path.GetDirectoryName(resources);
    }

    /// <summary>压缩包里可能多套一层目录，找最浅的那个含 install.ps1 的</summary>
    private static string? FindKitRoot(string root)
    {
        var matches = Directory.GetFiles(root, "install.ps1", SearchOption.AllDirectories);

        return matches.Length == 0
            ? null
            : matches.OrderBy(m => m.Length).Select(Path.GetDirectoryName).FirstOrDefault();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination), true);
    }

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "打开链接失败：{Url}", url);
        }
    }

    /// <summary>
    ///     官方启动器：唯一能过 kuro 校验的入口。
    ///     launcher.exe 在安装根目录，离 Win64 有 4~5 层，所以这里往上多找几层。
    /// </summary>
    public string ResolveOfficialLauncherPath()
    {
        var exe = _dlss5.ResolveGameExePath();

        if (string.IsNullOrWhiteSpace(exe))
            return string.Empty;

        var directory = Path.GetDirectoryName(exe);

        for (var i = 0; i < 6 && directory is not null; i++)
        {
            var launcher = Path.Combine(directory, "launcher.exe");

            if (File.Exists(launcher))
                return launcher;

            directory = Path.GetDirectoryName(directory);
        }

        return string.Empty;
    }
}
