using System.Text.Json.Serialization;

namespace OMniX.Core.Services.GameBanana.ApiModels;

/// <summary>
///     游戏根分类（Game/ProfilePage 的 _aModRootCategories）：Skins / UI / Other 等。
///     纯数据 POCO —— 别在这里加计算属性，XamlCompiler 会静默崩溃。
/// </summary>
public sealed class ApiModRootCategory
{
    [JsonPropertyName("_idRow")] public int Id { get; init; } = -1;
    [JsonPropertyName("_sName")] public string Name { get; init; } = "";
    [JsonPropertyName("_sUrl")] public string Url { get; init; } = "";
}

/// <summary>
///     子分类（ModCategory/{id}/SubCategories，裸数组）。接口不给 _idRow，
///     编号要从 _sUrl 末段解析（由客户端转成 GbCategoryItem，不在模型里算）。
/// </summary>
public sealed class ApiModSubCategory
{
    [JsonPropertyName("_sName")] public string Name { get; init; } = "";
    [JsonPropertyName("_sUrl")] public string Url { get; init; } = "";
    [JsonPropertyName("_sIconUrl")] public string IconUrl { get; init; } = "";
    [JsonPropertyName("_nItemCount")] public int ItemCount { get; init; }
}
