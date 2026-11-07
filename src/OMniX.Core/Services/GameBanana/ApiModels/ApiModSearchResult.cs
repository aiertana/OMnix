using System.Text.Json.Serialization;

namespace OMniX.Core.Services.GameBanana.ApiModels;

/// <summary>
///     apiv11 搜索 / 订阅动态的响应。两种端点（Util/Search/Results、Util/Game/Subfeed）
///     的记录结构一致，返回时可能是对象（带 _aMetadata）也可能直接是数组。
/// </summary>
public sealed class ApiModSearchResult
{
    [JsonPropertyName("_aRecords")] public List<ApiModSearchRecord> Records { get; init; } = [];

    [JsonPropertyName("_aMetadata")] public ApiModSearchMetadata? Metadata { get; init; }
}

public sealed class ApiModSearchMetadata
{
    [JsonPropertyName("_nRecordCount")] public int? RecordCount { get; init; }

    [JsonPropertyName("_nTotalPages")] public int? TotalPages { get; init; }

    [JsonPropertyName("_nPerpage")] public int? Perpage { get; init; }
}

public sealed class ApiModSearchRecord
{
    [JsonPropertyName("_idRow")] public int Id { get; init; } = -1;

    /// <summary>资源类型：Mod / Tool / Wip 等，商店只展示 Mod。</summary>
    [JsonPropertyName("_sModelName")] public string ModelName { get; init; } = "Mod";

    [JsonPropertyName("_sName")] public string Name { get; init; } = "";

    [JsonPropertyName("_aSubmitter")] public ApiAuthor? Author { get; init; }

    [JsonPropertyName("_aPreviewMedia")] public ApiImagesRoot? PreviewMedia { get; init; }

    [JsonPropertyName("_nLikeCount")] public int? LikeCount { get; init; }

    [JsonPropertyName("_nViewCount")] public int? ViewCount { get; init; }

    [JsonPropertyName("_nDownloadCount")] public int? DownloadCount { get; init; }

    [JsonPropertyName("_tsDateAdded")] public int? DateAdded { get; init; }

    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; init; }

    /// <summary>列表卡片的缩略图直链（优先 530 宽变体，回退原图），无图时为 null。</summary>
    [JsonIgnore]
    public string? ThumbnailUrl
    {
        get
        {
            var image = PreviewMedia?.Images.FirstOrDefault();
            if (image is null || string.IsNullOrEmpty(image.BaseUrl))
            {
                return null;
            }

            // 530 宽的变体加载快得多；缺失就回退原图。
            var file = image.File530 ?? image.File220 ?? image.ImageId;
            return string.IsNullOrEmpty(file) ? null : $"{image.BaseUrl.TrimEnd('/')}/{file}";
        }
    }
}
