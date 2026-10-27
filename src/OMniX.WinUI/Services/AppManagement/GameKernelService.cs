using Newtonsoft.Json;
using OMniX.WinUI.Helpers;

namespace OMniX.WinUI.Services.AppManagement;

/// <summary>
///     游戏内核：一个游戏 + 它那套管理功能（角色列表、Mod 总览、预设…）。
///     外壳是空的，内核装了才有东西可管。装没装看 GamesData\&lt;游戏&gt;\Core\kernel.json。
/// </summary>
public sealed class GameKernelService
{
    private const string KernelFileName = "kernel.json";

    private readonly GameDataRootService _dataRoot;

    public GameKernelService(GameDataRootService dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public string KernelDirectory(string gameId) => _dataRoot.EnsureGameDirectory(gameId, "Core");

    public bool IsInstalled(string gameId) => File.Exists(Path.Combine(KernelDirectory(gameId), KernelFileName));

    /// <summary>首页下拉、功能专区头像栏共用一个判定</summary>
    public bool IsInstalled(GameDefinition game) => IsInstalled(game.Id);

    public GameKernelInfo? GetInfo(string gameId)
    {
        var path = Path.Combine(KernelDirectory(gameId), KernelFileName);

        if (!File.Exists(path))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<GameKernelInfo>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     登记一个内核。鸣潮是内置的：外壳第一次起来就把它记为已装，
    ///     这样玩家不用先「下载」才能用。其余游戏等资源包到了再登记。
    /// </summary>
    public GameKernelInfo Register(GameDefinition game, string source = "builtin")
    {
        var info = new GameKernelInfo
        {
            GameId = game.Id,
            CoreName = game.Core.ToString(),
            AssetsFolder = game.AssetsFolder,
            DisplayName = game.FallbackName,
            Version = "1.0.0",
            Source = source,
            InstalledAt = DateTime.Now
        };

        var path = Path.Combine(KernelDirectory(game.Id), KernelFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonConvert.SerializeObject(info, Formatting.Indented));

        return info;
    }

    /// <summary>首次启动时把内置内核（鸣潮）登记好</summary>
    public void EnsureBuiltinKernels()
    {
        foreach (var game in GameCatalog.All)
        {
            if (game.KernelPackageUrl.Length == 0 && game.Id == GameCatalog.WuWa.Id && !IsInstalled(game.Id))
                Register(game, "builtin");
        }
    }

    /// <summary>玩家要清理这个游戏的内核</summary>
    public void Uninstall(string gameId)
    {
        var directory = KernelDirectory(gameId);

        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

public sealed class GameKernelInfo
{
    public string GameId { get; set; } = string.Empty;
    public string CoreName { get; set; } = string.Empty;
    public string AssetsFolder { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Source { get; set; } = "builtin";
    public DateTime InstalledAt { get; set; }
}
