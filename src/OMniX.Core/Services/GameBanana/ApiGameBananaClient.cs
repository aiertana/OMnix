using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OMniX.Core.Services.GameBanana.ApiModels;
using OMniX.Core.Services.GameBanana.Models;
using Polly;
using Polly.RateLimiting;
using Polly.Registry;
using Serilog;

namespace OMniX.Core.Services.GameBanana;

public sealed class ApiGameBananaClient(
    ILogger logger,
    HttpClient httpClient,
    ResiliencePipelineProvider<string> resiliencePipelineProvider)
    : IApiGameBananaClient
{
    private readonly ILogger _logger = logger.ForContext<ApiGameBananaClient>();
    private readonly HttpClient _httpClient = httpClient;
    private readonly ResiliencePipeline _resiliencePipeline = resiliencePipelineProvider.GetPipeline(HttpClientName);
    public const string HttpClientName = nameof(IApiGameBananaClient);

    private const string DownloadUrl = "https://gamebanana.com/dl/";
    private const string ApiUrl = "https://gamebanana.com/apiv11/Mod/";
    private const string HealthCheckUrl = "https://gamebanana.com/apiv11";

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(HealthCheckUrl, cancellationToken).ConfigureAwait(false);

        foreach (var (key, value) in response.Headers)
        {
            if (key.Contains("Deprecation", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Deprecated", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("GameBanana API is deprecated: {Key}={Value}", key, value);
                Debugger.Break();
                break;
            }
        }

        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<ApiModProfile?> GetModProfileAsync(GbModId modId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modId);

        var modPageApiUrl = GetModInfoUrl(modId);

        using var response = await SendRequest(modPageApiUrl, cancellationToken).ConfigureAwait(false);

        _logger.Debug("Got response from GameBanana: {response}", response.StatusCode);
        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var apiResponse =
            await JsonSerializer.DeserializeAsync<ApiModProfile>(contentStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);


        if (apiResponse == null)
        {
            _logger.Error("Failed to deserialize GameBanana response: {content}", contentStream);
            throw new HttpRequestException(
                $"Failed to deserialize GameBanana response. Reason: {response?.ReasonPhrase}");
        }

        return apiResponse;
    }

    public async Task<ApiModFilesInfo?> GetModFilesInfoAsync(GbModId modId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modId);

        var requestUrl = GetModFilesInfoUrl(modId);

        using var response = await SendRequest(requestUrl, cancellationToken).ConfigureAwait(false);

        _logger.Debug("Got response from GameBanana: {response}", response.StatusCode);
        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var apiResponse =
            await JsonSerializer.DeserializeAsync<ApiModFilesInfo>(contentStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);


        if (apiResponse == null)
        {
            _logger.Error("Failed to deserialize GameBanana response: {content}", contentStream);
            throw new HttpRequestException(
                $"Failed to deserialize GameBanana response. Reason: {response?.ReasonPhrase}");
        }

        return apiResponse;
    }

    public async Task<ApiModFileInfo?> GetModFileInfoAsync(GbModId modId, GbModFileId modFileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modFileId);

        var modFilesInfo = await GetModFilesInfoAsync(modId, cancellationToken).ConfigureAwait(false);

        return modFilesInfo?.Files.FirstOrDefault(x => x.FileId.ToString() == modFileId);
    }

    public async Task<bool> ModFileExists(GbModFileId modFileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modFileId);

        var requestUrl = GetAltUrlForModInfo(modFileId);

        using var response = await SendRequest(requestUrl, cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return !content.Contains("error:", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri GetAltUrlForModInfo(GbModFileId modFileId)
    {
        return new Uri(
            $"https://api.gamebanana.com/Core/Item/Data?itemid={modFileId}&itemtype=File&fields=file");
    }

    public async Task DownloadModAsync(GbModFileId modFileId, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modFileId, nameof(modFileId));
        ArgumentNullException.ThrowIfNull(destinationFile);
        var downloadUrl = DownloadUrl + modFileId;


        using var response = await _httpClient
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Mod not found.");

        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException(
                $"Failed to download mod from GameBanana. Reason: {response?.ReasonPhrase}");

        var contentLength = response.Content.Headers.ContentLength;

        await using var downloadStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        if (contentLength is not null && progress is not null)
            _ = Task.Run(() => DownloadMonitor(contentLength.Value, destinationFile.Name, progress, cancellationToken),
                cancellationToken);

        await downloadStream.CopyToAsync(destinationFile, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DownloadMonitor(long totalSizeBytes, string downloadFilePath, IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var file = new FileInfo(downloadFilePath);
            while (!cancellationToken.IsCancellationRequested && file.Length < totalSizeBytes)
            {
                file.Refresh();
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                var fileSize = file.Length;
                progress.Report((int)Math.Round((decimal)fileSize / (decimal)totalSizeBytes * 100));
            }
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GetImageAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendRequest(new Uri(url), cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private Uri GetModFilesInfoUrl(GbModId gbModId)
    {
        return new Uri(ApiUrl + gbModId + "/DownloadPage");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GbCategoryItem>> GetRootCategoriesAsync(int gameId,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://gamebanana.com/apiv11/Game/{gameId}/ProfilePage?_csvProperties=_aModRootCategories";
        using var response = await SendRequest(new Uri(url), cancellationToken).ConfigureAwait(false);
        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var payload = await JsonSerializer.DeserializeAsync<ApiModRootCategoriesPayload>(
            contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return payload?.RootCategories
            .Where(c => c.Id > 0)
            .Select(c => new GbCategoryItem(c.Id, c.Name))
            .ToList() ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GbCategoryItem>> GetSubCategoriesAsync(int categoryId,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://gamebanana.com/apiv11/ModCategory/{categoryId}/SubCategories";
        using var response = await SendRequest(new Uri(url), cancellationToken).ConfigureAwait(false);
        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var payload = await JsonSerializer.DeserializeAsync<List<ApiModSubCategory>>(
            contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var items = new List<GbCategoryItem>();
        foreach (var sub in payload ?? [])
        {
            // 接口不给分类编号，从 profile url 末段解析
            var tail = sub.Url.Split('/').LastOrDefault();
            if (int.TryParse(tail, out var id) && id > 0)
            {
                items.Add(new GbCategoryItem(id, sub.Name, sub.ItemCount, sub.IconUrl));
            }
        }

        return items;
    }

    private sealed class ApiModRootCategoriesPayload
    {
        [JsonPropertyName("_aModRootCategories")]
        public ApiModRootCategory[] RootCategories { get; init; } = [];
    }

    /// <inheritdoc />
    public async Task<ApiModSearchResult?> SearchModsAsync(int gameId, string? query, int page = 1, int perPage = 24,
        string? sort = null, int? categoryId = null, CancellationToken cancellationToken = default)
    {
        // 分类浏览 > 关键词搜索 > 按游戏浏览全部（Mod/Index，分页几千条）。
        // Subfeed 只是「最近发布动态」，只给一页，不适合当商店默认列表。
        var sb = new StringBuilder();
        if (categoryId is { } catId)
        {
            sb.Append("https://gamebanana.com/apiv11/Mod/Index")
              .Append("?_aFilters%5BGeneric_Category%5D=").Append(catId);
        }
        else if (!string.IsNullOrWhiteSpace(query))
        {
            sb.Append("https://gamebanana.com/apiv11/Util/Search/Results")
              .Append("?_idGameRow=").Append(gameId)
              .Append("&_sSearchString=").Append(Uri.EscapeDataString(query))
              .Append("&_sModelName=Mod");
        }
        else
        {
            sb.Append("https://gamebanana.com/apiv11/Mod/Index")
              .Append("?_aFilters%5BGeneric_Game%5D=").Append(gameId);
        }

        sb.Append("&_nPage=").Append(page)
          .Append("&_nPerpage=").Append(perPage);

        if (!string.IsNullOrWhiteSpace(sort))
        {
            sb.Append("&_sSort=").Append(Uri.EscapeDataString(sort));
        }

        using var response = await SendRequest(new Uri(sb.ToString()), cancellationToken).ConfigureAwait(false);
        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using var doc = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // subfeed 返回裸数组，搜索返回带 _aMetadata 的对象 —— 统一装回 ApiModSearchResult
        ApiModSearchResult result;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var records = doc.RootElement.Deserialize<List<ApiModSearchRecord>>() ?? [];
            result = new ApiModSearchResult { Records = records };
        }
        else
        {
            result = doc.RootElement.Deserialize<ApiModSearchResult>() ?? new ApiModSearchResult();
        }

        return result;
    }

    private Uri GetModInfoUrl(GbModId gbModId)
    {
        return new Uri(ApiUrl + gbModId + "/ProfilePage");
    }

    private async Task<HttpResponseMessage> SendRequest(Uri downloadsApiUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        retry:
        try
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);

            if (IgnorePollyLimiterScope.IsIgnored)
            {
                response = await _httpClient.GetAsync(downloadsApiUrl, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Use anonymous state object to avoid closure allocation
                var state = new { url = downloadsApiUrl, httpClient = _httpClient };

                response = await _resiliencePipeline.ExecuteAsync(
                        async (context, token) => await context.httpClient.GetAsync(context.url, token)
                            .ConfigureAwait(false),
                        state, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (RateLimiterRejectedException e)
        {
            _logger.Debug("Rate limit exceeded, retrying after {retryAfter}", e.RetryAfter);
            var delay = e.RetryAfter ?? TimeSpan.FromSeconds(2);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            goto retry;
        }


        if (!response.IsSuccessStatusCode)
        {
            _logger.Error("Failed to get mod info from GameBanana: {response} | Url: {Url}", response,
                downloadsApiUrl);
            throw new HttpRequestException(
                $"Failed to get mod info from GameBanana. Reason: {response?.ReasonPhrase ?? "Unknown"} | Url: {downloadsApiUrl}");
        }

        _logger.Debug("Response received {0} | {1}", DateTime.Now, downloadsApiUrl);

        return response;
    }
}