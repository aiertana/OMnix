using Newtonsoft.Json;

namespace OMniX.Core.GamesService.JsonModels;

internal class JsonBaseNameable
{
    public string? InternalName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? DisplayName { get; set; }
}