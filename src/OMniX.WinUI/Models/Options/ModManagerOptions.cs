using Newtonsoft.Json;

namespace OMniX.WinUI.Models.Options;

public class ModManagerOptions
{
    [JsonIgnore] public const string Section = "ModManagerOptions";
    public string? GimiRootFolderPath { get; set; }
    public string? ModsFolderPath { get; set; }
    public string? UnloadedModsFolderPath { get; set; }

    /// <summary>
    ///     二开：插件（XXMI、DLSS5 部署包、Mod 修复器）存放的文件夹。
    ///     空着就用默认位置（%LOCALAPPDATA%\OMniX\Plugins）。
    ///     之前这个是写死的，全在 C 盘 —— C 盘紧张的用户没法挪。
    /// </summary>
    public string? PluginsFolderPath { get; set; }

    public bool CharacterSkinsAsCharacters { get; set; }
}