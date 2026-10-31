using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Services.AppManagement;

namespace OMniX.WinUI.Services.AppManagement;

/// <summary>
///     识别「这个游戏现在能怎么启动」。
///     鸣潮走官方启动器（直接拉游戏进程会被 kuro 校验弹崩），绝区零走 XXMI，
///     原神 / 崩铁走 HoYo 系启动器。识别不到就让玩家手动填。
///     识别结果直接决定左下角「启动游戏」拉起谁。
/// </summary>
public sealed class GameLaunchResolver
{
    private const string ModeKeyPrefix = "GameLaunch.";
    private const string PathKeyPrefix = "GameLaunchPath.";

    private readonly ILocalSettingsService _localSettingsService;

    public GameLaunchResolver(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    /// <summary>玩家手动指定的启动方式（0 = 没指定，跟随默认）</summary>
    public GameLaunchMode? GetConfiguredMode(GameDefinition game)
    {
        var raw = _localSettingsService.ReadSetting<int?>(ModeKeyPrefix + game.Id);
        return raw is > 0 ? (GameLaunchMode)raw.Value : null;
    }

    public async Task SetConfiguredModeAsync(GameDefinition game, GameLaunchMode? mode)
    {
        await _localSettingsService.SaveSettingAsync(ModeKeyPrefix + game.Id, mode is null ? 0 : (int)mode.Value);
    }

    /// <summary>玩家手动指定的启动器路径（覆盖自动识别，文件名是什么都行）</summary>
    public string GetManualPath(GameDefinition game) =>
        _localSettingsService.ReadSetting<string>(PathKeyPrefix + game.Id) ?? string.Empty;

    public async Task SetManualPathAsync(GameDefinition game, string path)
    {
        await _localSettingsService.SaveSettingAsync(PathKeyPrefix + game.Id, path ?? string.Empty);
    }

    /// <summary>
    ///     识别结果。Mode 是最终采用的方式，Executable 是拉起谁（可能为空 = 没找到）。
    /// </summary>
    public LaunchResolution Resolve(GameDefinition game, ModManagerOptions options)
    {
        var manual = GetManualPath(game);
        var mode = GetConfiguredMode(game) ?? game.DefaultLaunchMode;

        // 手动指定永远优先：玩家说用哪个就用哪个
        if (!string.IsNullOrWhiteSpace(manual) && File.Exists(manual))
            return new LaunchResolution(mode, manual, true);

        var auto = mode switch
        {
            GameLaunchMode.Official => FindOfficial(game, options),
            GameLaunchMode.Xxmi => FindXxmi(options),
            GameLaunchMode.HoYo => FindHoYo(game),
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(auto))
            return new LaunchResolution(mode, auto, false);

        // 首选方式没找到，把其它方式都试一遍，找到一个能用的就告诉玩家改用哪种
        foreach (var fallback in new[] { GameLaunchMode.Official, GameLaunchMode.Xxmi, GameLaunchMode.HoYo })
        {
            if (fallback == mode)
                continue;

            var found = fallback switch
            {
                GameLaunchMode.Official => FindOfficial(game, options),
                GameLaunchMode.Xxmi => FindXxmi(options),
                GameLaunchMode.HoYo => FindHoYo(game),
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(found))
                return new LaunchResolution(fallback, found, false);
        }

        return new LaunchResolution(GameLaunchMode.Manual, string.Empty, false);
    }

    /// <summary>官方启动器：从游戏 exe 往上找 launcher.exe，再退一步扫常见装法</summary>
    private static string FindOfficial(GameDefinition game, ModManagerOptions options)
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.GimiRootFolderPath))
            roots.Add(options.GimiRootFolderPath);

        if (!string.IsNullOrWhiteSpace(options.ModsFolderPath))
            roots.Add(options.ModsFolderPath);

        roots.AddRange(CommonInstallRoots());

        foreach (var root in roots)
        {
            var found = SearchUpwards(root, game, game.LauncherExeNames, 6);
            if (!string.IsNullOrWhiteSpace(found))
                return found;
        }

        return string.Empty;
    }

    /// <summary>XXMI 系：启动器根目录里名字带 launcher / xxmi 的那个 exe</summary>
    private static string FindXxmi(ModManagerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GimiRootFolderPath))
            return string.Empty;

        var found = LauncherLocator.FindLauncherExecutable(options.GimiRootFolderPath);
        return string.IsNullOrWhiteSpace(found) ? string.Empty : found;
    }

    /// <summary>HoYo 系：HoYoShadeHub.exe / launcher.exe，扫常见的几个安装位置</summary>
    private static string FindHoYo(GameDefinition game)
    {
        foreach (var root in CommonInstallRoots())
        {
            var found = SearchUpwards(root, game, game.LauncherExeNames, 3);
            if (!string.IsNullOrWhiteSpace(found))
                return found;
        }

        return string.Empty;
    }

    /// <summary>常见的游戏安装根目录：各盘的 Program Files 与几个已知目录</summary>
    private static IEnumerable<string> CommonInstallRoots()
    {
        var roots = new List<string>();
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";

        roots.Add(programFiles);
        roots.Add(programFilesX86);
        roots.Add(Path.Combine(programFiles, "HoYoShadeHub"));
        roots.Add(Path.Combine(programFiles, "HoYoPlay"));
        roots.Add(Path.Combine(programFiles, "Wuthering Waves"));
        roots.Add(Path.Combine(programFiles, "Star Rail"));
        roots.Add(Path.Combine(programFiles, "Genshin Impact"));

        foreach (var drive in SafeGetDrives())
        {
            roots.Add(Path.Combine(drive, "Program Files"));
            roots.Add(Path.Combine(drive, "Games"));
            roots.Add(Path.Combine(drive, "mod"));
            roots.Add(Path.Combine(drive, "Wuthering Waves"));
            roots.Add(Path.Combine(drive, "Wuthering Waves Game"));
        }

        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SafeGetDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => drive.RootDirectory.FullName)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    ///     从起点往上找若干层，每层试一遍候选文件名。
    ///     各家官方启动器都叫 launcher.exe，候选要是明显装在别的游戏目录里
    ///     （目录名带其它游戏的关键词，比如 Genshin Impact）就跳过，
    ///     不然鸣潮会把原神的启动器认成自己的。
    /// </summary>
    private static string SearchUpwards(string? start, GameDefinition game, IEnumerable<string> exeNames, int maxLevels)
    {
        if (string.IsNullOrWhiteSpace(start))
            return string.Empty;

        var directory = Directory.Exists(start) ? start : Path.GetDirectoryName(start);

        for (var i = 0; i < maxLevels && !string.IsNullOrWhiteSpace(directory); i++)
        {
            foreach (var name in exeNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate) && !BelongsToOtherGame(candidate, game))
                    return candidate;
            }

            // 顺手看一眼常见的一级子目录，别往深处递归（慢）
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(directory).Take(20))
                {
                    foreach (var name in exeNames)
                    {
                        var candidate = Path.Combine(sub, name);
                        if (File.Exists(candidate) && !BelongsToOtherGame(candidate, game))
                            return candidate;
                    }
                }
            }
            catch
            {
                // 没权限看就算了
            }

            directory = Path.GetDirectoryName(directory);
        }

        return string.Empty;
    }

    /// <summary>
    ///     候选启动器所在的目录链（往上数 3 层）要是带着别的游戏安装目录的特征词，
    ///     就认为它装在别人家，不是当前游戏的启动器。
    ///     目录名不带任何游戏特征的（自定义目录）仍然接受，免得误杀。
    /// </summary>
    private static bool BelongsToOtherGame(string candidate, GameDefinition game)
    {
        var ownKeywords = game.InstallFolderKeywords;
        var otherKeywords = GameCatalog.All
            .Where(g => !ReferenceEquals(g, game))
            .SelectMany(g => g.InstallFolderKeywords)
            .ToArray();

        var directory = Path.GetDirectoryName(candidate);
        for (var i = 0; i < 3 && !string.IsNullOrWhiteSpace(directory); i++)
        {
            var folder = Path.GetFileName(directory).ToLowerInvariant();

            if (ownKeywords.Any(k => folder.Contains(k)))
                return false;

            if (otherKeywords.Any(k => folder.Contains(k)))
                return true;

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }
}

public sealed record LaunchResolution(GameLaunchMode Mode, string Executable, bool IsManual)
{
    public bool Found => !string.IsNullOrWhiteSpace(Executable) && File.Exists(Executable);
}
