using OMniX.WinUI.Services.AppManagement;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     游戏资源目录定位。
///     游戏资源是「包」：放在 GamesData\&lt;游戏&gt;\ 下，随游戏走，可以整包删掉重装。
///     管理功能（程序本体）始终在 Core 里，不跟着搬。
///     包里面的摆放沿用 OMniX 原来的命名，一个字都没改：
///     <code>
///     GamesData\WuWa\
///     ├─ characters.json  game.json  elements.json  …（顶层 json，跟原来一样）
///     ├─ Images\Characters\  AltCharacterSkins\  Elements\
///     └─ Languages\zh-cn\
///     </code>
///     包不存在（游戏还没装）时回退到程序内置那份，老版本升级上来不会开天窗。
/// </summary>
public static class GameAssetsLocator
{
    public static string Resolve(GameDataRootService dataRoot, string assetsFolder)
    {
        var packaged = Path.Combine(dataRoot.GamesDataRoot, assetsFolder);

        if (File.Exists(Path.Combine(packaged, "game.json")))
            return packaged;

        return Path.Combine(GameDataRootService.CoreDirectory, "Assets", "Games", assetsFolder);
    }

    /// <summary>
    ///     取一个游戏的图标文件路径。
    ///     只读包里 game.json 的一个字段、拼一个路径，**不初始化这个游戏** ——
    ///     以前用 GetGameInfoAsync 取四个游戏的图标，等于启动时把没在玩的游戏也全加载一遍，
    ///   界面直接卡死（1.4.0 的教训），而且没加载的就显示成白块。
    /// </summary>
    public static string ResolveIconPath(GameDataRootService dataRoot, Helpers.GameDefinition game)
    {
        // 包里优先，其次程序内置那份，跟 Resolve 一个顺序
        foreach (var root in new[]
                 {
                     Path.Combine(dataRoot.GamesDataRoot, game.Id),
                     Path.Combine(GameDataRootService.CoreDirectory, "Assets", "Games", game.AssetsFolder)
                 })
        {
            try
            {
                var gameJsonPath = Path.Combine(root, "game.json");

                if (!File.Exists(gameJsonPath))
                    continue;

                var icon = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(gameJsonPath))
                    .Value<string>("GameIcon");
                var iconPath = Path.Combine(root, "Images",
                    string.IsNullOrWhiteSpace(icon) ? "Start_Game.png" : icon.Trim());

                if (File.Exists(iconPath))
                    return iconPath;
            }
            catch
            {
                // 这个包读不出来就试下一个来源
            }
        }

        return string.Empty;
    }
}
