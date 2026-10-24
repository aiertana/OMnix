using Newtonsoft.Json;

namespace OMniX.WinUI.Models.Settings;

internal class ModArchiveSettings
{
    [JsonIgnore] public const string Key = "ModArchiveSettings";
    public int MaxLocalArchiveCacheSizeGb { get; set; } = 10;
}