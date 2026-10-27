using System.Diagnostics;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;

namespace OMniX.WinUI.Services.AppManagement;

/// <summary>
///     数据根目录。
///     以前所有数据都堆在 C 盘 %LOCALAPPDATA%\OMniX，版本一多就找不到、C 盘也吃不消。
///     改造后：程序本体在 Core\，游戏数据在版本目录下的 GamesData\，按游戏隔离：
///     <code>
///     OMniX1.3.x\
///     ├─ Core\          ← 管理器本体（exe 在这里）
///     ├─ GamesData\     ← 游戏数据（可让玩家改到别的盘）
///     │  └─ WuWa\
///     │     ├─ Mods\ Presets\ Plugins\ Cache\ Logs\
///     └─ 启动OMniX.exe
///     </code>
/// </summary>
public sealed class GameDataRootService
{
    private const string GamesDataFolderName = "GamesData";
    private const string CoreFolderName = "Core";
    private const string LauncherFileName = "启动OMniX.exe";

    /// <summary>自定义根目录存在 App 级设置里（与游戏无关）</summary>
    private const string CustomRootSettingKey = "GamesData.CustomRoot";

    private readonly ILocalSettingsService _localSettingsService;
    private string? _resolvedRoot;

    public GameDataRootService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    /// <summary>程序本体所在目录（版本目录下的 Core\）</summary>
    public static string CoreDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>
    ///     版本目录（Core\ 的上一级）。判定依据：该目录里有启动器 exe 或 GamesData。
    ///     开发调试时 BaseDirectory 在 bin\ 下，找不到就逐级往上兜。
    /// </summary>
    public static string InstallRoot
    {
        get
        {
            var directory = new DirectoryInfo(CoreDirectory);

            for (var i = 0; i < 5 && directory?.Parent is not null; i++)
            {
                directory = directory.Parent;

                if (File.Exists(Path.Combine(directory.FullName, LauncherFileName)) ||
                    Directory.Exists(Path.Combine(directory.FullName, GamesDataFolderName)) ||
                    Directory.Exists(Path.Combine(directory.FullName, CoreFolderName)))
                {
                    return directory.FullName;
                }
            }

            return directory?.FullName ?? CoreDirectory;
        }
    }

    /// <summary>默认的 GamesData（版本目录下）</summary>
    public static string DefaultRoot => Path.Combine(InstallRoot, GamesDataFolderName);

    /// <summary>玩家改过就用玩家的，否则用版本目录下的</summary>
    public string GamesDataRoot
    {
        get
        {
            if (_resolvedRoot is not null)
                return _resolvedRoot;

            var custom = _localSettingsService.ReadSetting<string>(CustomRootSettingKey, SettingScope.App);

            _resolvedRoot = string.IsNullOrWhiteSpace(custom) ? DefaultRoot : custom;
            return _resolvedRoot;
        }
    }

    public bool HasCustomRoot =>
        !string.IsNullOrWhiteSpace(_localSettingsService.ReadSetting<string>(CustomRootSettingKey, SettingScope.App));

    /// <summary>改根目录。只记路径，不搬数据（搬数据走 MigrateAsync）。</summary>
    public async Task SetCustomRootAsync(string? path)
    {
        await _localSettingsService.SaveSettingAsync(CustomRootSettingKey, path ?? string.Empty, SettingScope.App);
        _resolvedRoot = null;
    }

    public string GameRoot(string gameId) =>
        Path.Combine(GamesDataRoot, string.IsNullOrWhiteSpace(gameId) ? GameCatalog.Fallback.Id : gameId);

    /// <summary>按游戏隔离的子目录，不存在就建</summary>
    public string EnsureGameDirectory(string gameId, string subFolder)
    {
        var path = Path.Combine(GameRoot(gameId), subFolder);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Mods 目录。玩家在设置页指定的路径优先，没指定才用 GamesData 下的。</summary>
    public string ResolveModsPath(string gameId, string? configuredModsPath) =>
        string.IsNullOrWhiteSpace(configuredModsPath)
            ? EnsureGameDirectory(gameId, "Mods")
            : configuredModsPath;

    /// <summary>旧数据位置（C 盘）</summary>
    public static string LegacyAppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX");

    public static bool HasLegacyData()
    {
        try
        {
            return Directory.Exists(LegacyAppDataRoot) &&
                   (Directory.EnumerateFileSystemEntries(LegacyAppDataRoot).Any());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     列出旧数据里值得搬的东西：按游戏隔离的配置目录 + 全局插件目录。
    ///     只列不搬，方便玩家在界面上看清要搬什么。
    /// </summary>
    public static IReadOnlyList<LegacyItem> EnumerateLegacyItems()
    {
        var items = new List<LegacyItem>();

        if (!Directory.Exists(LegacyAppDataRoot))
            return items;

        foreach (var directory in Directory.EnumerateDirectories(LegacyAppDataRoot))
        {
            var name = Path.GetFileName(directory);

            // ApplicationData / ApplicationData_WuWa / ApplicationData_Debug ...
            if (name.StartsWith("ApplicationData", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = name.Length > "ApplicationData".Length
                    ? name["ApplicationData".Length..].Trim('_')
                    : string.Empty;

                // 去掉 _Debug 尾巴再认游戏
                suffix = suffix.Replace("_Debug", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('_');

                var gameId = GameCatalog.ById(suffix)?.Id
                             ?? GameCatalog.ByCoreName(suffix)?.Id
                             ?? GameCatalog.Fallback.Id;

                items.Add(new LegacyItem(directory, Path.Combine("GamesData", gameId, "Profile"), true));
            }
            else if (string.Equals(name, "Plugins", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new LegacyItem(directory, Path.Combine("GamesData", GameCatalog.Fallback.Id, "Plugins"), false));
            }
        }

        return items;
    }

    /// <summary>
    ///     把旧数据搬到 GamesData。只复制不删除 —— 原数据原样留在 C 盘，
    ///     搬完校验通过之后要不要删，交给玩家在弹窗里决定。
    /// </summary>
    public async Task<MigrationResult> MigrateAsync(IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new MigrationResult();
        var items = EnumerateLegacyItems().Where(item => Directory.Exists(item.Source)).ToList();

        if (items.Count == 0)
        {
            result.Message = "没有检测到旧的 C 盘数据";
            return result;
        }

        var root = GamesDataRoot;
        var totalFiles = 0L;
        var doneFiles = 0L;

        foreach (var item in items)
        {
            totalFiles += Directory.EnumerateFiles(item.Source, "*", SearchOption.AllDirectories).Count();
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.Combine(root, item.RelativeTarget);

            try
            {
                Directory.CreateDirectory(target);
                CopyDirectory(item.Source, target, (_, _) =>
                {
                    doneFiles++;
                    progress?.Report(new MigrationProgress(
                        Path.GetFileName(item.Source),
                        totalFiles == 0 ? 0 : (int)(doneFiles * 100 / totalFiles),
                        (int)doneFiles,
                        (int)totalFiles));
                }, cancellationToken);

                result.CopiedItems.Add(new MigrationItem(item.Source, target, CountSize(target)));
            }
            catch (Exception ex)
            {
                result.Failures.Add(new MigrationFailure(item.Source, target, ex.Message));
            }
        }

        result.Success = result.Failures.Count == 0;

        if (result.Success)
        {
            // 校验：源文件数与目标文件数必须一致，少一个都算没搬干净
            var verified = result.CopiedItems.All(item =>
                Directory.Exists(item.Target) &&
                Directory.EnumerateFiles(item.Source, "*", SearchOption.AllDirectories).Count() ==
                Directory.EnumerateFiles(item.Target, "*", SearchOption.AllDirectories).Count());

            result.Verified = verified;
            result.Message = verified ? "迁移完成，已校验文件数量一致" : "文件已复制，但校验时发现数量对不上，请手动核对";
        }
        else
        {
            result.Message = $"有 {result.Failures.Count} 项迁移失败";
        }

        return result;
    }

    /// <summary>迁移校验通过之后，玩家勾选了才真删 C 盘旧数据</summary>
    public void DeleteLegacyData(IEnumerable<MigrationItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                if (Directory.Exists(item.Source))
                    Directory.Delete(item.Source, true);
            }
            catch
            {
                // 删不掉就算了，原数据留着不影响使用
            }
        }
    }

    private static long CountSize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch
        {
            return 0;
        }
    }

    private static void CopyDirectory(string source, string target, Action<string, string> onFile,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destination, true);
            onFile(file, destination);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), onFile, cancellationToken);
        }
    }
}

public sealed record LegacyItem(string Source, string RelativeTarget, bool IsGameProfile);

public sealed record MigrationItem(string Source, string Target, long Bytes);

public sealed record MigrationFailure(string Source, string Target, string Error);

public sealed record MigrationProgress(string CurrentName, int Percent, int DoneFiles, int TotalFiles);

public sealed class MigrationResult
{
    public bool Success { get; set; }
    public bool Verified { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<MigrationItem> CopiedItems { get; } = [];
    public List<MigrationFailure> Failures { get; } = [];

    public long TotalBytes => CopiedItems.Sum(item => item.Bytes);
}
