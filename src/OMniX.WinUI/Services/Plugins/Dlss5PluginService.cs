using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.Contracts.Services;
using Serilog;

namespace OMniX.WinUI.Services.Plugins;

/// <summary>
///     DLSS5 渲染链插件的运行时状态。
/// </summary>
public sealed class Dlss5PluginState
{
    public bool LoaderPresent { get; init; }
    public bool ScriptsPresent { get; init; }
    public bool Dlss5AddonPresent { get; init; }
    public int AddonCount { get; init; }

    /// <summary>scripts\d3d11.asi 在（WWMI 会加载 Mod）</summary>
    public bool ModLoaderActive { get; init; }

    /// <summary>两个 DLSS5 addon 都是正常文件名（没被禁用）</summary>
    public bool Dlss5AddonActive { get; init; }

    public string GameExePath { get; init; } = string.Empty;
    public string GameDir { get; init; } = string.Empty;
    public string KitDir { get; init; } = string.Empty;

    /// <summary>加载链完整（ASI Loader + scripts 都在）= 插件处于开启状态</summary>
    public bool Deployed => LoaderPresent && ScriptsPresent;

    public bool GameExeValid => !string.IsNullOrWhiteSpace(GameExePath) && File.Exists(GameExePath);

    public bool KitValid => !string.IsNullOrWhiteSpace(KitDir) && File.Exists(Path.Combine(KitDir, "install.ps1"));

    /// <summary>两条路径都齐了才能部署</summary>
    public bool Ready => GameExeValid && KitValid;

    public string StatusText => Deployed
        ? Dlss5AddonActive
            ? string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_OnWithAddons", "已开启 · {0} 个插件"), AddonCount)
            : App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_OnAddonsDisabled", "已开启（DLSS5 插件被停用）")
        : Ready
            ? App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_Off", "未开启")
            : App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_NotConfigured", "未配置");

    public string DetailText => Deployed
        ? $"{GameDir}"
        : Ready
            ? App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_ReadyHint", "开关打开后会把加载器、ReShade、DLSS5 部署进游戏目录（需要管理员权限）")
            : App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_MissingHint", "还缺游戏路径或部署包路径，点开关可以现在就配");

    public string ModStatusText => ModLoaderActive
        ? App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_ModOn", "Mod 已启用")
        : App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_ModOff", "Mod 已停用");
}

public sealed class Dlss5RunResult
{
    public bool Success { get; init; }
    public bool NeedsSetup { get; init; }
    public bool UserCancelled { get; init; }
    public string Message { get; init; } = string.Empty;

    public static Dlss5RunResult Ok(string message) => new() { Success = true, Message = message };

    public static Dlss5RunResult Fail(string message) => new() { Message = message };

    public static Dlss5RunResult NeedSetup() => new() { NeedsSetup = true, Message = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_NeedSetup", "还没有配置路径") };
}

/// <summary>
///     二开：DLSS5 渲染链插件。
///     管理器这边只负责「检测有没有装」和「调部署包的脚本」，
///     真正的部署逻辑留在 WWMI-DLSS5-Deploy 里；把那个目录删掉，插件就只是检测不到、开关打不开，
///     管理器本身和游戏启动完全不受影响。
/// </summary>
public sealed class Dlss5PluginService
{
    public const string KitPathKey = "Plugin.Dlss5.KitPath";
    public const string GamePathKey = "Plugin.Dlss5.GamePath";

    private const string GameExeName = "Client-Win64-Shipping.exe";

    /// <summary>装在哪都行，这里只是给个默认猜测</summary>
    private static readonly string[] KitCandidates =
    {
        @"D:\mod\WWMI-DLSS5-Deploy",
        @"D:\WWMI-DLSS5-Deploy",
        @"C:\WWMI-DLSS5-Deploy"
    };

    private static readonly string[] GameExeCandidates =
    {
        @"D:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe",
        @"C:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe",
        @"E:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe",
        @"F:\Wuthering Waves\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe"
    };

    private readonly ILogger _logger = Log.ForContext<Dlss5PluginService>();
    private readonly ILocalSettingsService _settings;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public Dlss5PluginService(ILocalSettingsService settings)
    {
        _settings = settings;
    }

    public string? KitDirectory => _settings.ReadSetting<string>(KitPathKey);

    public string? GameExePath => _settings.ReadSetting<string>(GamePathKey);

    /// <summary>先看用户配过的，再退回到管理器里已经设过的游戏启动路径，最后猜常见安装位置</summary>
    public string ResolveGameExePath()
    {
        var configured = GameExePath;

        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        try
        {
            var launcher = App.GetService<GenshinProcessManager>();
            var processPath = launcher.ProcessPath;

            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                return processPath;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "读取游戏启动路径失败");
        }

        return ProbeInstalledGame() ?? string.Empty;
    }

    /// <summary>常见安装位置扫一遍，扫到了就不用手动选了</summary>
    private static string? ProbeInstalledGame()
    {
        foreach (var candidate in GameExeCandidates)
            if (File.Exists(candidate))
                return candidate;

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;

                var probe = Path.Combine(drive.RootDirectory.FullName,
                    "Wuthering Waves", "Wuthering Waves Game", "Client", "Binaries", "Win64", GameExeName);

                if (File.Exists(probe))
                    return probe;
            }
        }
        catch (Exception)
        {
            // 盘符枚举失败就当没装
        }

        return null;
    }

    /// <summary>
    ///     二开：只写游戏路径。部署包和游戏路径是两个键，
    ///     补游戏路径的时候不能顺手把部署包那个键清掉。
    /// </summary>
    public async Task SaveGamePathAsync(string gameExePath) =>
        await _settings.SaveSettingAsync(GamePathKey, gameExePath);

    public string ResolveKitDirectory()
    {
        var configured = KitDirectory;

        if (!string.IsNullOrWhiteSpace(configured) &&
            File.Exists(Path.Combine(configured, "install.ps1")))
            return configured;

        foreach (var candidate in KitCandidates)
            if (File.Exists(Path.Combine(candidate, "install.ps1")))
                return candidate;

        return string.Empty;
    }

    public Dlss5PluginState Detect()
    {
        var exePath = ResolveGameExePath();
        var kitDir = ResolveKitDirectory();
        var gameDir = string.IsNullOrWhiteSpace(exePath) ? string.Empty : Path.GetDirectoryName(exePath) ?? string.Empty;

        var loaderPresent = false;
        var scriptsPresent = false;
        var addonCount = 0;
        var dlss5Present = false;
        var modLoaderActive = false;
        var dlss5AddonActive = false;

        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            var loader = Path.Combine(gameDir, "d3d11.dll");
            loaderPresent = File.Exists(loader) && new FileInfo(loader).Length > 1024 * 1024;

            var scriptsDir = Path.Combine(gameDir, "scripts");
            scriptsPresent = File.Exists(Path.Combine(scriptsDir, "global.ini"));

            if (Directory.Exists(scriptsDir))
            {
                var addons = Directory.GetFiles(scriptsDir, "*.addon64");
                addonCount = addons.Length;
                dlss5Present = addons.Any(path =>
                    Path.GetFileName(path).Contains("dlss5", StringComparison.OrdinalIgnoreCase));

                // 两个开关都是「改文件名」实现的：停用就加 .disabled 后缀，加载器自然跳过
                modLoaderActive = File.Exists(Path.Combine(scriptsDir, "d3d11.asi"));
                dlss5AddonActive = !File.Exists(Path.Combine(scriptsDir, "dlss5-feed.addon64.disabled"));
            }
        }

        return new Dlss5PluginState
        {
            LoaderPresent = loaderPresent,
            ScriptsPresent = scriptsPresent,
            Dlss5AddonPresent = dlss5Present,
            AddonCount = addonCount,
            ModLoaderActive = modLoaderActive,
            Dlss5AddonActive = dlss5AddonActive,
            GameExePath = exePath,
            GameDir = gameDir,
            KitDir = kitDir
        };
    }

    /// <summary>
    ///     开关 Mod 加载：只把 scripts\d3d11.asi 改名成 d3d11.asi.disabled，不动别的文件。
    /// </summary>
    public Task<bool> SetModLoaderAsync(bool enable)
    {
        return Task.Run(() =>
        {
            var state = Detect();

            if (string.IsNullOrWhiteSpace(state.GameDir))
                return false;

            var scriptsDir = Path.Combine(state.GameDir, "scripts");

            if (!Directory.Exists(scriptsDir))
                return false;

            return ToggleByName(scriptsDir, "d3d11.asi", enable);
        });
    }

    /// <summary>
    ///     开关 DLSS5：把两个 addon64 改名。ReShade 那边加载不到就当没有，WWMI 不受影响。
    /// </summary>
    public Task<bool> SetDlss5AddonAsync(bool enable)
    {
        return Task.Run(() =>
        {
            var state = Detect();

            if (string.IsNullOrWhiteSpace(state.GameDir))
                return false;

            var scriptsDir = Path.Combine(state.GameDir, "scripts");

            if (!Directory.Exists(scriptsDir))
                return false;

            var feed = ToggleByName(scriptsDir, "dlss5-feed.addon64", enable);
            var reno = ToggleByPattern(scriptsDir, "renodx-dlss5-*.addon64", enable);

            return feed && reno;
        });
    }

    private static bool ToggleByName(string directory, string fileName, bool enable)
    {
        var path = Path.Combine(directory, fileName);
        var disabled = path + ".disabled";

        try
        {
            if (enable)
            {
                if (File.Exists(path))
                    return true;

                if (!File.Exists(disabled))
                    return false;

                File.Move(disabled, path);
                return true;
            }

            if (File.Exists(disabled))
                return true;

            if (!File.Exists(path))
                return false;

            File.Move(path, disabled);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ToggleByPattern(string directory, string pattern, bool enable)
    {
        try
        {
            if (enable)
            {
                var disabled = Directory.GetFiles(directory, pattern + ".disabled");

                if (disabled.Length == 0)
                    return Directory.GetFiles(directory, pattern).Length > 0;

                foreach (var path in disabled)
                    File.Move(path, path[..^".disabled".Length]);

                return true;
            }

            var files = Directory.GetFiles(directory, pattern);

            if (files.Length == 0)
                return Directory.GetFiles(directory, pattern + ".disabled").Length > 0;

            foreach (var path in files)
                File.Move(path, path + ".disabled");

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task SavePathsAsync(string gameExePath, string kitDirectory)
    {
        await _settings.SaveSettingAsync(GamePathKey, gameExePath);
        await _settings.SaveSettingAsync(KitPathKey, kitDirectory);
    }

    /// <summary>
    ///     开启 = 跑 install.ps1；关闭 = 跑 uninstall.ps1。
    ///     两个脚本都要管理员权限（硬链接 + 目录联接），所以用 runas 拉起一个控制台窗口让用户看着跑。
    /// </summary>
    public async Task<Dlss5RunResult> SetEnabledAsync(bool enable)
    {
        var state = Detect();

        if (!state.Ready)
            return Dlss5RunResult.NeedSetup();

        var script = Path.Combine(state.KitDir, enable ? "install.ps1" : "uninstall.ps1");

        if (!File.Exists(script))
            return Dlss5RunResult.Fail(string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_ScriptNotFound", "部署包里找不到 {0}"), Path.GetFileName(script)));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -GameDir \"{state.GameDir}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var process = Process.Start(psi);

            if (process is null)
                return Dlss5RunResult.Fail(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_StartFailed", "没能启动部署进程"));

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return Dlss5RunResult.Fail(
                    enable
                        ? string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_DeployExitCode", "部署脚本退出码 {0}，看看那个控制台窗口里的报错"), process.ExitCode)
                        : string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_UninstallExitCode", "卸载脚本退出码 {0}，看看那个控制台窗口里的报错"), process.ExitCode));

            var after = Detect();

            return enable
                ? Dlss5RunResult.Ok(after.Deployed
                    ? _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_DeployDone", "部署完成。用库洛官方启动器进游戏，图形设置选 DirectX 11")
                    : _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_NoLoaderChain", "脚本跑完了，但没检测到加载链，检查一下游戏目录"))
                : Dlss5RunResult.Ok(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_Uninstalled", "已卸载，游戏目录恢复成干净状态"));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new Dlss5RunResult { UserCancelled = true, Message = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Dlss5Svc_AdminCancelled", "取消了管理员授权") };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DLSS5 插件开关失败");
            return Dlss5RunResult.Fail(ex.Message);
        }
    }
}
