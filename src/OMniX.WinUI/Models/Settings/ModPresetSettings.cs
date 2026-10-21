using Newtonsoft.Json;

namespace OMniX.WinUI.Models.Settings;

public class ModPresetSettings
{
    [JsonIgnore] public const string Key = "ModPresetSettings";

    public bool AutoSyncMods { get; set; } = false;
}