using OMniX.Core.GamesService;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     二开：当前启用的游戏。
///     目前只做鸣潮，原神 / 星穹铁道 / 绝区零 的选项与相关入口先隐藏。
///     以后要恢复某个游戏，把对应枚举加进 <see cref="All" /> 即可。
/// </summary>
public static class EnabledGames
{
    public static readonly SupportedGames[] All = [SupportedGames.WuWa];

    /// <summary>
    ///     默认游戏（第一个启用的游戏）
    /// </summary>
    public static SupportedGames Default => All[0];

    public static bool IsEnabled(SupportedGames game) => All.Contains(game);
}
