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

    /// <summary>
    ///     装一个内核包：把资源（角色头像、Languages、那几个 json）从程序目录
    ///     复制到 GamesData\&lt;游戏&gt;\ 下，再登记 kernel.json。
    ///     程序本体（管理功能）留在 Core 不动，搬的只有资源 —— 这样游戏才是真·一个包。
    ///     目录名一律沿用 OMniX 原来的：顶层 json、Images\、Languages\。
    /// </summary>
    public GameKernelInfo Install(GameDefinition game, string source = "builtin")
    {
        var target = _dataRoot.GameRoot(game.Id);
        Directory.CreateDirectory(target);

        var builtin = Path.Combine(GameDataRootService.CoreDirectory, "Assets", "Games", game.AssetsFolder);

        if (Directory.Exists(builtin))
            CopyDirectory(builtin, target);

        return Register(game, source);
    }

    /// <summary>整目录复制。已经存在的文件不覆盖 —— 玩家的改动优先。</summary>
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));

            if (!File.Exists(destination))
                File.Copy(file, destination, true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    /// <summary>
    ///     资源是不是真装进包了。只登记了 kernel.json、资源没搬过去的，等于没装好 ——
    ///     出包时目录里可能留了个空壳，这里必须按资源判，不然头像就还在程序内置那。
    /// </summary>
    public bool HasAssets(GameDefinition game) =>
        File.Exists(Path.Combine(_dataRoot.GameRoot(game.Id), "game.json"));

    /// <summary>
    ///     四款游戏的资源本来就随程序带着（Assets\Games\）。
    ///     这里把它们逐个装成包：资源搬进 GamesData\&lt;游戏&gt;\，管理功能留在 Core。
    ///     装过的会跳过，所以只有第一次启动才真的搬东西。
    /// </summary>
    public void EnsureBuiltinKernels()
    {
        foreach (var game in GameCatalog.All)
        {
            if (!IsInstalled(game.Id) || !HasAssets(game))
                Install(game, "builtin");
        }
    }

    /// <summary>资源是不是真装进包了。只登记了 kernel.json、资源没搬过去的，等于没装好。</summary>
    public bool IsReady(GameDefinition game) => IsInstalled(game.Id) && HasAssets(game);

    /// <summary>没装好就装一次。切换游戏前调它，玩家自己删了包也能自动补回来。</summary>
    public void EnsureInstalled(GameDefinition game)
    {
        if (!IsReady(game))
            Install(game, "builtin");
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
