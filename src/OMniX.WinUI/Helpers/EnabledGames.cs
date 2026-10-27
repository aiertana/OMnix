using OMniX.Core.GamesService;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     外壳认识的游戏内核。
///     以前只留鸣潮一个，另外三款（原神 / 星穹铁道 / 绝区零）的入口全隐藏；
///     改成多内核外壳之后四个都在这里 —— 能不能用取决于内核有没有下载，
///     由 <see cref="OMniX.WinUI.Services.AppManagement.GameKernelService" /> 判定。
///     再加新游戏：往 <see cref="GameCatalog.All" /> 里加一条定义即可，这里不用动。
/// </summary>
public static class EnabledGames
{
    public static readonly SupportedGames[] All =
        GameCatalog.All.Select(game => game.Core).ToArray();

    /// <summary>
    ///     默认游戏（第一个启用的游戏）
    /// </summary>
    public static SupportedGames Default => All[0];

    public static bool IsEnabled(SupportedGames game) => All.Contains(game);
}
