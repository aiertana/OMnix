using System.Text.Json.Serialization;

namespace OMniX.Core.Services.GameBanana.ApiModels;

public class ApiModProfile
{
    [JsonPropertyName("_idRow")] public int ModId { get; init; } = -1;
    [JsonPropertyName("_sName")] public string? ModName { get; init; }

    [JsonPropertyName("_aSubmitter")] public ApiAuthor? Author { get; init; }
    [JsonPropertyName("_aPreviewMedia")] public ApiImagesRoot? PreviewMedia { get; init; }

    [JsonPropertyName("_sProfileUrl")] public string? ModPageUrl { get; init; }

    [JsonPropertyName("_aFiles")] public ICollection<ApiModFileInfo>? Files { get; init; }

    /// <summary>mod 所属的子分类（Skins 下就是角色名，用于自动识别安装目标角色）。</summary>
    [JsonPropertyName("_aCategory")] public ApiCategoryRef? Category { get; init; }

    /// <summary>mod 所属的根分类（Skins / UI 等）。</summary>
    [JsonPropertyName("_aSuperCategory")] public ApiCategoryRef? SuperCategory { get; init; }
}

/// <summary>分类引用（profile 里的面包屑节点）。</summary>
public sealed class ApiCategoryRef
{
    [JsonPropertyName("_idRow")] public int Id { get; init; } = -1;
    [JsonPropertyName("_sName")] public string? Name { get; init; }
}

public sealed class ApiAuthor
{
    [JsonPropertyName("_sName")] public string? AuthorName { get; init; }
    [JsonPropertyName("_sAvatarUrl")] public string? AvatarImageUrl { get; init; }
    [JsonPropertyName("_sProfileUrl")] public string? ProfileUrl { get; init; }
}

public sealed class ApiImagesRoot
{
    [JsonPropertyName("_aImages")] public ApiImageUrl[] Images { get; init; } = [];
}

public sealed class ApiImageUrl
{
    [JsonPropertyName("_sFile")] public string? ImageId { get; init; }
    [JsonPropertyName("_sBaseUrl")] public string? BaseUrl { get; init; }
    [JsonPropertyName("_sFile530")] public string? File530 { get; init; }
    [JsonPropertyName("_sFile220")] public string? File220 { get; init; }
}