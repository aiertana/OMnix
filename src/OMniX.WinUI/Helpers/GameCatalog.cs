using OMniX.Core.GamesService;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     游戏的启动方式。设置页自动识别，左下角「启动游戏」按识别结果拉起。
/// </summary>
public enum GameLaunchMode
{
    /// <summary>官方启动器（鸣潮会校验启动来源，直接拉游戏进程会崩）</summary>
    Official,

    /// <summary>XXMI 系启动器（ZZMI / GIMI / SRMI 那一支）</summary>
    Xxmi,

    /// <summary>HoYo 系启动器（HoYoShadeHub.exe 等）</summary>
    HoYo,

    /// <summary>玩家手动指定的可执行文件</summary>
    Manual
}

/// <summary>
///     一个「游戏内核」的定义。
///     外壳（首页/设置/通知/关于）与游戏无关，这里描述的是可下载、可切换、可清理的内核。
/// </summary>
public sealed class GameDefinition
{
    /// <summary>内核 Id，同时是 GamesData 下的目录名：WuWa / ZZZ / GI / HSR</summary>
    public required string Id { get; init; }

    /// <summary>对应的内部游戏枚举（资源目录、GameService 用这个）</summary>
    public required SupportedGames Core { get; init; }

    /// <summary>Assets\Games\ 下的资源目录名（沿用 XXMI 的历史命名）</summary>
    public required string AssetsFolder { get; init; }

    /// <summary>显示名走 i18n，这是 key 的后半段（/HomePreset/Game_Name_XXX）</summary>
    public required string NameKey { get; init; }

    /// <summary>取不到本地化时的兜底文案</summary>
    public required string FallbackName { get; init; }

    /// <summary>首选启动方式</summary>
    public required GameLaunchMode DefaultLaunchMode { get; init; }

    /// <summary>官方 / HoYo 启动器的常见 exe 名，按顺序找，允许玩家覆盖</summary>
    public required string[] LauncherExeNames { get; init; }

    /// <summary>模型导入器（WWMI / ZZMI / GIMI / SRMI）的 exe 名</summary>
    public required string[] ImporterExeNames { get; init; }

    /// <summary>模型导入器简称，用来推导 Mods 目录（如 WWMI\Mods）</summary>
    public required string ImporterShortName { get; init; }

    /// <summary>内核资源包的下载来源，暂时为空表示「本地已有 / 待补」</summary>
    public string KernelPackageUrl { get; init; } = string.Empty;
}

/// <summary>
///     内核清单。新游戏像加插件一样往这里加一条即可，外壳不用改。
/// </summary>
public static class GameCatalog
{
    public static readonly GameDefinition WuWa = new()
    {
        Id = "WuWa",
        Core = SupportedGames.WuWa,
        AssetsFolder = "WuWa",
        NameKey = "Game_Name_WuWa",
        FallbackName = "鸣潮",
        DefaultLaunchMode = GameLaunchMode.Official,
        LauncherExeNames = ["launcher.exe", "Wuthering Waves.exe"],
        ImporterExeNames = ["WWMI Loader.exe"],
        ImporterShortName = "WWMI",
        KernelPackageUrl = string.Empty
    };

    public static readonly GameDefinition ZZZ = new()
    {
        Id = "ZZZ",
        Core = SupportedGames.ZZZ,
        AssetsFolder = "ZZZ",
        NameKey = "Game_Name_ZZZ",
        FallbackName = "绝区零",
        DefaultLaunchMode = GameLaunchMode.Xxmi,
        LauncherExeNames = ["launcher.exe"],
        ImporterExeNames = ["3DMigoto Loader.exe", "ZZZ3dmLoader.exe"],
        ImporterShortName = "ZZMI",
        KernelPackageUrl = string.Empty
    };

    public static readonly GameDefinition GI = new()
    {
        Id = "GI",
        Core = SupportedGames.Genshin,
        AssetsFolder = "Genshin",
        NameKey = "Game_Name_GI",
        FallbackName = "原神",
        DefaultLaunchMode = GameLaunchMode.HoYo,
        LauncherExeNames = ["HoYoShadeHub.exe", "launcher.exe", "GenshinImpact.exe"],
        ImporterExeNames = ["3DMigoto Loader.exe"],
        ImporterShortName = "GIMI",
        KernelPackageUrl = string.Empty
    };

    public static readonly GameDefinition HSR = new()
    {
        Id = "HSR",
        Core = SupportedGames.Honkai,
        AssetsFolder = "Honkai",
        NameKey = "Game_Name_HSR",
        FallbackName = "崩坏：星穹铁道",
        DefaultLaunchMode = GameLaunchMode.HoYo,
        LauncherExeNames = ["HoYoShadeHub.exe", "launcher.exe", "StarRail.exe"],
        ImporterExeNames = ["3DMigotoLoader.exe"],
        ImporterShortName = "SRMI",
        KernelPackageUrl = string.Empty
    };

    /// <summary>外壳认识的四个内核。顺序即下拉与头像栏的展示顺序。</summary>
    public static readonly IReadOnlyList<GameDefinition> All = [WuWa, ZZZ, GI, HSR];

    public static GameDefinition? ById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return All.FirstOrDefault(game =>
            string.Equals(game.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static GameDefinition? ByCore(SupportedGames core) =>
        All.FirstOrDefault(game => game.Core == core);

    public static GameDefinition? ByCoreName(string? coreName) =>
        Enum.TryParse<SupportedGames>(coreName, true, out var core) ? ByCore(core) : null;

    /// <summary>默认内核，任何时候都要保证能取到</summary>
    public static GameDefinition Fallback => WuWa;
}
