using Newtonsoft.Json;

namespace OMniX.WinUI.Models.Options;

public class ModManagerOptions
{
    [JsonIgnore] public const string Section = "ModManagerOptions";
    public string? GimiRootFolderPath { get; set; }
    public string? ModsFolderPath { get; set; }
    public string? UnloadedModsFolderPath { get; set; }
    public bool CharacterSkinsAsCharacters { get; set; }
}