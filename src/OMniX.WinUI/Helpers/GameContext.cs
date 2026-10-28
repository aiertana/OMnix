using System;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     当前正在用的那个内核。
///     切换游戏是要重启的，所以启动时定一次就够，中途不会变 ——
///     这样别的服务想按游戏取东西（导入器简称、Mods 目录名），直接同步读这里，
///     不用再 await，也就不会在 UI 线程上等出死锁。
/// </summary>
public static class GameContext
{
    private static GameDefinition _current = GameCatalog.Fallback;

    /// <summary>当前内核。取不到就退回鸣潮，保证任何时候都有值。</summary>
    public static GameDefinition Current => _current;

    /// <summary>启动时按选中的游戏设一次。传的是内部枚举名（WuWa / ZZZ / Genshin / Honkai）。</summary>
    public static void Set(string? coreName)
    {
        try
        {
            _current = GameCatalog.ByCoreName(coreName) ?? GameCatalog.Fallback;
        }
        catch (Exception)
        {
            _current = GameCatalog.Fallback;
        }
    }

    /// <summary>当前游戏的模型导入器简称：WWMI / ZZMI / GIMI / SRMI</summary>
    public static string ImporterShortName => _current.ImporterShortName;

    /// <summary>当前游戏在 GamesData 下的包目录名：WuWa / ZZZ / GI / HSR</summary>
    public static string PackageId => _current.Id;
}
