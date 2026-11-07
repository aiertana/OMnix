using OMniX.Core.Services.GameBanana.ApiModels;
using OMniX.Core.Services.GameBanana.Models;

namespace OMniX.Core.Services.GameBanana;

public interface IApiGameBananaClient
{
    /// <summary>
    /// Checks if the GameBanana API is reachable.
    /// </summary>
    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the mod profile from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModProfile if mod exists or null</returns>
    public Task<ApiModProfile?> GetModProfileAsync(GbModId modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the mod files info from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModFilesInfo if mod exists or null</returns>
    public Task<ApiModFilesInfo?> GetModFilesInfoAsync(GbModId modId, CancellationToken cancellationToken = default);


    /// <summary>
    /// Gets the mod file info from the GameBanana API.
    /// </summary>
    /// <param name="modId">The Game banana's mod Id</param>
    /// <param name="modFileId">The Game banana's mod files Id</param>
    /// <param name="cancellationToken"></param>
    /// <returns>ApiModFileInfo if file exists or null</returns>
    [Obsolete("Use GetModFilesInfoAsync instead")]
    public Task<ApiModFileInfo?> GetModFileInfoAsync(GbModId modId, GbModFileId modFileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     按游戏分页搜索 / 浏览 Mod。query 为空时按游戏浏览全部（Mod/Index），
    ///     sort 取 apiv11 的 _sSort 值（如 Generic_LatestModified）。
    ///     给了 categoryId 时按分类浏览（此时忽略 query）。
    /// </summary>
    public Task<ApiModSearchResult?> SearchModsAsync(int gameId, string? query, int page = 1, int perPage = 24,
        string? sort = null, int? categoryId = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     游戏的根分类（Skins / UI / Other/Misc 等）。
    /// </summary>
    public Task<IReadOnlyList<GbCategoryItem>> GetRootCategoriesAsync(int gameId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     根分类下的子分类（如 Skins 下的各角色）。编号从 _sUrl 末段解析。
    /// </summary>
    public Task<IReadOnlyList<GbCategoryItem>> GetSubCategoriesAsync(int categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     下载图片字节（缩略图 / 头像 / 分类图标统一走这条通道，
    ///     才能带上代理与限流重试 —— XAML 内建的网络图下载不可靠）。
    /// </summary>
    public Task<byte[]> GetImageAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the mod file exists on GameBanana.
    /// </summary>
    public Task<bool> ModFileExists(GbModFileId modFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download mod file from GameBanana.
    /// </summary>
    /// <param name="modFileId">The Game banana's mod files Id</param>
    /// <param name="destinationFile">File  stream to write the contents to</param>
    /// <param name="progress">Reports to as a percentage from 0 to 100</param>
    /// <param name="cancellationToken">Cancels the download but does not delete the destinationFile</param>
    /// <exception cref="InvalidOperationException">When mod is not found</exception>
    /// <exception cref="HttpRequestException"></exception>
    public Task DownloadModAsync(GbModFileId modFileId, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken = default);
}