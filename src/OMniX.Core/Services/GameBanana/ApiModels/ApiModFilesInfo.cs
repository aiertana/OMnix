using System.Text.Json.Serialization;

namespace OMniX.Core.Services.GameBanana.ApiModels;

public class ApiModFilesInfo
{
    [JsonPropertyName("_bIsTrashed")] public bool IsTrashed { get; init; }
    [JsonPropertyName("_bIsWithheld")] public bool IsWithheld { get; init; }

    [JsonPropertyName("_aFiles")] public ICollection<ApiModFileInfo> Files { get; init; } = [];
}