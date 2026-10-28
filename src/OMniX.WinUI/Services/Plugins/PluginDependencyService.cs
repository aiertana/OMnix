using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Serilog;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Helpers;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Services;

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

    /// <summary>
    ///     WWMI 的两个标志文件：注入用的 d3d11.dll，或 3Dmigoto 的 d3dx.ini
    ///     （有的整合包只带后者，只认 dll 会误报「没装」）
    /// </summary>
    public bool WwmiReady =>
        Directory.Exists(WwmiRoot) &&
        (File.Exists(Path.Combine(WwmiRoot, "d3d11.dll")) ||
         File.Exists(Path.Combine(WwmiRoot, "d3dx.ini")));

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

    /// <summary>
    ///     二开：XXMI 根目录的标志文件。便携版解压出来一定有这个文件，
    ///     认它就等于认 XXMI —— 装在哪个盘、文件夹叫什么名字都不影响。
    /// </summary>
    private const string XxmiConfigFileName = "XXMI Launcher Config.json";

    /// <summary>上次成功定位到的 XXMI 根目录。下次直接用它，省掉整盘扫描</summary>
    private const string XxmiRootSettingKey = "PluginDependency.XxmiRoot";

    /// <summary>用户在对话框里手动指定的 XXMI 根目录，优先级最高</summary>
    private const string XxmiRootManualKey = "PluginDependency.XxmiRootManual";

    /// <summary>
    ///     二开：鸣潮真正跑起来的 exe。XXMI 配置里记的是进程名（Wuthering Waves.exe），
    ///     DLSS5 要铺进去的是这个，所以先用它试。
    /// </summary>
    private const string WuWaGameExeName = "Client-Win64-Shipping.exe";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Dlss5PluginService _dlss5;
    private readonly ILocalSettingsService _localSettings;
    private readonly ILogger _logger = Log.ForContext<PluginDependencyService>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    private string _rememberedXxmiRoot = string.Empty;
    private string _manualXxmiRoot = string.Empty;

    /// <summary>游戏路径只在每轮启动里补一次，别每次刷新都去写设置</summary>
    private bool _gamePathApplied;

    private static readonly TimeSpan DetectCacheLifetime = TimeSpan.FromSeconds(15);

    private (DateTimeOffset At, string Root, string Launcher) _xxmiCache =
        (DateTimeOffset.MinValue, string.Empty, string.Empty);

    public PluginDependencyService(IHttpClientFactory httpClientFactory, Dlss5PluginService dlss5,
        ILocalSettingsService localSettings)
    {
        _httpClientFactory = httpClientFactory;
        _dlss5 = dlss5;
        _localSettings = localSettings;

        _ = LoadRememberedXxmiRootAsync();
    }

    public DependencySnapshot Detect()
    {
        var xxmiRoot = ResolveXxmiInstallation(out var xxmiLauncher);

        // 二开：导入器随游戏走 —— 鸣潮是 WWMI、绝区零是 ZZMI、原神 GIMI、崩铁 SRMI。
        // 以前写死 WWMI，切到别的游戏后这里还在报鸣潮那套，Mod 开关跟着判错。
        var wwmiRoot = string.IsNullOrWhiteSpace(xxmiRoot) ? string.Empty : ResolveImporterFolder(xxmiRoot);

        ApplyGamePathFromXxmi(xxmiRoot);

        _logger.Information("插件依赖检测：XXMI={XxmiRoot} 启动器={Launcher} {Importer}={ImporterRoot}",
            string.IsNullOrWhiteSpace(xxmiRoot) ? "(未找到)" : xxmiRoot,
            string.IsNullOrWhiteSpace(xxmiLauncher) ? "(未找到)" : xxmiLauncher,
            GameContext.ImporterShortName,
            string.IsNullOrWhiteSpace(wwmiRoot) ? "(未找到)" : wwmiRoot);

        return new DependencySnapshot
        {
            XxmiLauncherPath = xxmiLauncher,
            XxmiRoot = xxmiRoot,
            WwmiRoot = wwmiRoot,
            KitDir = _dlss5.ResolveKitDirectory()
        };
    }

    /// <summary>
    ///     二开：XXMI 的配置文件里记着游戏装在哪（Importers.&lt;导入器&gt;.Importer.game_folder）。
    ///     插件要用的游戏路径还没配过时，顺手从这儿填上 —— 「启动游戏」也就能直接用了。
    /// </summary>
    private void ApplyGamePathFromXxmi(string xxmiRoot)
    {
        if (_gamePathApplied || string.IsNullOrWhiteSpace(xxmiRoot))
            return;

        _gamePathApplied = true;

        try
        {
            // 已经配好并且文件还在就别动它
            if (!string.IsNullOrWhiteSpace(_dlss5.GameExePath) && File.Exists(_dlss5.GameExePath))
                return;

            var gameExe = TryReadGameExeFromXxmi(xxmiRoot);

            if (string.IsNullOrWhiteSpace(gameExe))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _dlss5.SaveGamePathAsync(gameExe);
                    _logger.Information("从 XXMI 配置读到游戏路径并填好：{GameExe}", gameExe);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "写入游戏路径失败");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "从 XXMI 配置读游戏路径失败");
        }
    }

    /// <summary>
    ///     二开：从 XXMI Launcher Config.json 里解析鸣潮的 exe。
    ///     XXMI 记的是进程名（Wuthering Waves.exe）和相对目录（Client/Binaries/Win64），
    ///     DLSS5 要铺进去的是 Client-Win64-Shipping.exe，所以先试它、再退回 XXMI 记的名字。
    /// </summary>
    public static string? TryReadGameExeFromXxmi(string xxmiRoot)
    {
        if (string.IsNullOrWhiteSpace(xxmiRoot))
            return null;

        try
        {
            var configPath = Path.Combine(xxmiRoot, XxmiConfigFileName);

            if (!File.Exists(configPath))
                return null;

            // 二开：读哪个导入器的段由当前游戏定，别再写死鸣潮的 WWMI
            var importer = JObject.Parse(File.ReadAllText(configPath))["Importers"]
                ?[GameContext.ImporterShortName]?["Importer"];

            if (importer is null)
                return null;

            var gameFolder = importer["game_folder"]?.ToString();

            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
                return null;

            var exeNames = importer["game_exe_names"]?.ToObject<string[]>() ?? [];
            var relPaths = importer["process_exe_rel_paths"]?.ToObject<string[]>() ?? [];

            // XXMI 记的是 Client/Binaries/Win64（正斜杠），拼进路径前先换成系统分隔符
            var relativePaths = relPaths
                .Select(relPath => relPath.Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar))
                .Append(string.Empty)
                .ToArray();

            foreach (var exeName in new[] { WuWaGameExeName }.Concat(exeNames))
            foreach (var relPath in relativePaths)
            {
                var candidate = string.IsNullOrWhiteSpace(relPath)
                    ? Path.Combine(gameFolder, exeName)
                    : Path.Combine(gameFolder, relPath, exeName);

                if (File.Exists(candidate))
                    return candidate;
            }

            // exe 改名过：在 XXMI 记的那几个目录里按名字找一遍
            foreach (var folder in relativePaths
                         .Select(relPath => string.IsNullOrWhiteSpace(relPath)
                             ? gameFolder
                             : Path.Combine(gameFolder, relPath))
                         .Where(Directory.Exists))
            {
                var found = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(file => string.Equals(Path.GetFileName(file), WuWaGameExeName,
                        StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }
        catch (Exception ex)
        {
            Log.ForContext<PluginDependencyService>().Debug(ex, "读 XXMI 配置里的游戏路径失败");
        }

        return null;
    }

    /// <summary>
    ///     二开：找 XXMI。以前是拿 6 条写死的绝对路径去撞，
    ///     装在别的盘、或者文件夹叫 XXMI-Launcher、少个空格，就一律「未检测到」。
    ///     现在按安装特征判定（XXMI Launcher Config.json + Resources\Bin 里的 exe），
    ///     路径只是线索：配过的插件目录 → 上次找到的位置 → 正在运行的进程 →
    ///     常见安装位置 → 最后才是有界的整盘扫描。
    /// </summary>
    private string ResolveXxmiInstallation(out string launcherPath)
    {
        var now = DateTimeOffset.UtcNow;

        // 找不到时要扫盘，别让每次刷新都扫一遍
        if (now - _xxmiCache.At < DetectCacheLifetime)
        {
            launcherPath = _xxmiCache.Launcher;

            return _xxmiCache.Root;
        }

        launcherPath = string.Empty;

        foreach (var candidate in EnumerateXxmiCandidates())
        {
            if (!TryResolveXxmiRoot(candidate, out var root, out var exe))
                continue;

            launcherPath = exe;
            _xxmiCache = (now, root, exe);
            RememberXxmiRoot(root);

            return root;
        }

        _xxmiCache = (now, string.Empty, string.Empty);

        return string.Empty;
    }

    private IEnumerable<string> EnumerateXxmiCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 二开：用户在对话框里手动指定的位置说了算 —— 装在再刁钻的地方也能救回来
        if (!string.IsNullOrWhiteSpace(_manualXxmiRoot) && seen.Add(_manualXxmiRoot))
            yield return _manualXxmiRoot;

        foreach (var candidate in GetConfiguredXxmiPaths())
        {
            if (seen.Add(candidate))
                yield return candidate;
        }

        if (!string.IsNullOrWhiteSpace(_rememberedXxmiRoot) && seen.Add(_rememberedXxmiRoot))
            yield return _rememberedXxmiRoot;

        foreach (var candidate in GetRunningXxmiPaths())
        {
            if (seen.Add(candidate))
                yield return candidate;
        }

        foreach (var candidate in GetWellKnownXxmiPaths())
        {
            if (seen.Add(candidate))
                yield return candidate;
        }

        foreach (var candidate in ScanDrivesForXxmi())
        {
            if (seen.Add(candidate))
                yield return candidate;
        }
    }

    /// <summary>我们自己下载/用户指定的插件目录</summary>
    private static IEnumerable<string> GetConfiguredXxmiPaths()
    {
        foreach (var name in new[] { "XXMI Launcher", "XXMI-Launcher", "XXMI", "XXMI_Launcher" })
            yield return Path.Combine(PluginsRoot, name);

        // 用户把 XXMI 直接解到插件根目录里（没有多套一层文件夹）
        yield return PluginsRoot;
    }

    /// <summary>正在运行的 XXMI —— 用户刚开过的话，进程路径最可靠</summary>
    private static List<string> GetRunningXxmiPaths()
    {
        var paths = new List<string>();

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!process.ProcessName.Contains("xxmi", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var path = process.MainModule?.FileName;

                    if (!string.IsNullOrWhiteSpace(path))
                        paths.Add(path);
                }
                catch (Exception)
                {
                    // 拿不到路径的进程（权限不够）跳过
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // 枚举进程失败就当没有
        }

        return paths;
    }

    private static IEnumerable<string> GetWellKnownXxmiPaths()
    {
        var names = new[] { "XXMI Launcher", "XXMI-Launcher", "XXMI" };

        var roots = new List<string?>
        {
            @"D:\mod",
            @"C:\",
            @"D:\",
            @"E:\",
            @"F:\",
            @"D:\Games",
            @"E:\Games",
            @"D:\Tools",
            @"E:\Tools",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            foreach (var name in names)
                yield return Path.Combine(root, name);

            // 根目录本身就是 XXMI（解压时少套了一层）
            yield return root;
        }
    }

    /// <summary>兜底：各固定盘按名字扫两层，只在前面都没找到时才跑</summary>
    private static List<string> ScanDrivesForXxmi()
    {
        var matches = new List<string>();

        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception)
        {
            return matches;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;

                foreach (var directory in EnumerateDirectoriesSafe(drive.RootDirectory.FullName))
                {
                    var name = Path.GetFileName(directory);

                    if (LooksLikeXxmiFolder(name))
                    {
                        matches.Add(directory);
                        continue;
                    }

                    if (ScanSkipFolderNames.Contains(name.ToLowerInvariant()))
                        continue;

                    foreach (var child in EnumerateDirectoriesSafe(directory))
                    {
                        if (!LooksLikeXxmiFolder(Path.GetFileName(child)))
                            continue;

                        matches.Add(child);
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // 单个盘扫不动不影响其它盘
            }
        }

        return matches;
    }

    private static readonly HashSet<string> ScanSkipFolderNames = new(StringComparer.Ordinal)
    {
        "windows",
        "$recycle.bin",
        "system volume information",
        "recovery",
        "node_modules",
        ".git",
        ".vs",
        "appdata"
    };

    private static bool LooksLikeXxmiFolder(string name) =>
        ModFolderHelpers.NormalizeName(name).Contains("xxmi", StringComparison.Ordinal);

    /// <summary>
    ///     判断一个候选路径是不是 XXMI：给 exe 也行，给目录也行。
    ///     目录的判据是「里面有 Resources\Bin 的启动器 exe」或「根目录有标志文件」。
    /// </summary>
    private bool TryResolveXxmiRoot(string candidate, out string root, out string launcherPath)
    {
        root = string.Empty;
        launcherPath = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        try
        {
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(candidate))
                    return false;

                launcherPath = candidate;
                root = FindXxmiRootAbove(candidate);

                return !string.IsNullOrWhiteSpace(root);
            }

            if (!Directory.Exists(candidate))
                return false;

            var exe = FindXxmiLauncherExe(candidate);

            if (string.IsNullOrWhiteSpace(exe))
                return false;

            launcherPath = exe;
            root = FindXxmiRootAbove(exe);

            if (string.IsNullOrWhiteSpace(root))
                root = candidate;

            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "检查 XXMI 位置 {Candidate} 失败", candidate);

            return false;
        }
    }

    private static string FindXxmiLauncherExe(string folder)
    {
        // 便携版固定布局：Resources\Bin\XXMI Launcher.exe
        var binFolder = Path.Combine(folder, "Resources", "Bin");

        foreach (var keyword in new[] { "xxmi", "launcher" })
        {
            var exe = FindExeByKeyword(binFolder, keyword);

            if (!string.IsNullOrWhiteSpace(exe))
                return exe;
        }

        // 直接放在根目录的版本
        foreach (var keyword in new[] { "xxmi", "launcher" })
        {
            var exe = FindExeByKeyword(folder, keyword);

            if (!string.IsNullOrWhiteSpace(exe))
                return exe;
        }

        // 目录结构改过的版本：有标志文件才往下再找一层，避免翻到无关目录
        if (!File.Exists(Path.Combine(folder, XxmiConfigFileName)))
            return string.Empty;

        foreach (var child in EnumerateDirectoriesSafe(folder))
        {
            var exe = FindExeByKeyword(child, "xxmi");

            if (!string.IsNullOrWhiteSpace(exe))
                return exe;
        }

        return string.Empty;
    }

    private static string FindExeByKeyword(string folder, string keyword)
    {
        try
        {
            if (!Directory.Exists(folder))
                return string.Empty;

            return Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                       .FirstOrDefault(file => Path.GetFileNameWithoutExtension(file)
                           .Contains(keyword, StringComparison.OrdinalIgnoreCase))
                   ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>从 exe 往上找根目录：优先认标志文件，没有再按 Resources\Bin 的层级退两级</summary>
    private static string FindXxmiRootAbove(string exePath)
    {
        var folder = Path.GetDirectoryName(exePath);

        if (string.IsNullOrWhiteSpace(folder))
            return string.Empty;

        var current = new DirectoryInfo(folder);

        for (var i = 0; i < 4 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, XxmiConfigFileName)))
                return current.FullName;

            current = current.Parent;
        }

        return Directory.GetParent(folder)?.Parent?.FullName ?? string.Empty;
    }

    /// <summary>
    ///     导入器目录名大小写/写法可能不同，按归一化名找，最后才退回默认拼法。
    ///     二开：找哪个导入器由当前游戏定（WWMI / ZZMI / GIMI / SRMI），不再写死 WWMI。
    /// </summary>
    private static string ResolveImporterFolder(string xxmiRoot)
    {
        var importer = GameContext.ImporterShortName;

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(xxmiRoot))
            {
                if (string.Equals(Path.GetFileName(directory), importer, StringComparison.OrdinalIgnoreCase))
                    return directory;
            }

            foreach (var directory in Directory.EnumerateDirectories(xxmiRoot))
            {
                if (ModFolderHelpers.NormalizeName(Path.GetFileName(directory)) == importer.ToLowerInvariant())
                    return directory;
            }
        }
        catch (Exception)
        {
            // 读不到就退回默认拼法
        }

        return Path.Combine(xxmiRoot, importer);
    }

    private static List<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateDirectories(path).ToList()
                : new List<string>();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    private async Task LoadRememberedXxmiRootAsync()
    {
        try
        {
            _rememberedXxmiRoot =
                await _localSettings.ReadSettingAsync<string>(XxmiRootSettingKey, SettingScope.App) ??
                string.Empty;

            _manualXxmiRoot =
                await _localSettings.ReadSettingAsync<string>(XxmiRootManualKey, SettingScope.App) ??
                string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "读取上次的 XXMI 位置失败");
        }
    }

    /// <summary>
    ///     二开：用户在对话框里手动指定 XXMI 根目录。这个位置优先级最高，
    ///     以后每次检测都先用它。返回 null 表示设置成功，否则返回给用户看的错误说明。
    /// </summary>
    public string? SetXxmiRootManually(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return _localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_ManualEmpty",
                "先选一个文件夹");

        if (!TryResolveXxmiRoot(folder, out var root, out _) || string.IsNullOrWhiteSpace(root))
            return string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/PluginDepSvc_ManualInvalid",
                "这个文件夹里没有 XXMI Launcher：{0}"), folder);

        _manualXxmiRoot = root;
        _xxmiCache = (DateTimeOffset.MinValue, string.Empty, string.Empty);

        _ = Task.Run(async () =>
        {
            try
            {
                await _localSettings.SaveSettingAsync(XxmiRootManualKey, root, SettingScope.App);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "保存手动指定的 XXMI 位置失败");
            }
        });

        return null;
    }

    private void RememberXxmiRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(root, _rememberedXxmiRoot, StringComparison.OrdinalIgnoreCase))
            return;

        _rememberedXxmiRoot = root;

        _ = Task.Run(async () =>
        {
            try
            {
                await _localSettings.SaveSettingAsync(XxmiRootSettingKey, root, SettingScope.App);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "记录 XXMI 位置失败");
            }
        });
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
