using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace OMniX.WinUI.Services.Plugins;

/// <summary>
///     功能插件的自我介绍。
///     第三方只要往插件目录里丢一个带 plugin.json 的文件夹，管理器就能认出来 ——
///     不需要走我们的编译流程，也不用改管理器代码。
/// </summary>
public sealed class PluginManifest
{
    /// <summary>唯一 Id，也是文件夹名。不填就用文件夹名。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>展示名</summary>
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0";

    public string Author { get; set; } = string.Empty;

    /// <summary>
    ///     形态。功能区的插件本质是「配置项 / 开关」，不是跑任意代码：
    ///     toggle  = 一个可开可关的开关（DLSS5、Mod 加载这类）
    ///     action  = 点一下执行一件事（Mod 修复器这类）
    ///     link    = 只是个入口，点了打开下载页或文件夹
    /// </summary>
    public string Type { get; set; } = "toggle";

    /// <summary>图标字形（Segoe MDL2 Assets）</summary>
    public string Glyph { get; set; } = "\uE9F0";

    /// <summary>徽标文字，例如「已启用」「未安装」</summary>
    public string Badge { get; set; } = string.Empty;

    /// <summary>来源链接（GitHub 发布页 / 官网）</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>开关型插件要操作的文件的相对路径，多个用分号隔开。留空表示只记状态。</summary>
    public string ToggleTargets { get; set; } = string.Empty;

    /// <summary>动作型插件要拉起的文件（相对插件目录）。</summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>当前是否启用。由管理器写回。</summary>
    public bool Enabled { get; set; }

    /// <summary>插件目录的绝对路径（不写进 json）</summary>
    [JsonIgnore] public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>manifest 文件路径（不写进 json）</summary>
    [JsonIgnore] public string ManifestPath { get; set; } = string.Empty;

    [JsonIgnore] public bool IsToggle => string.Equals(Type, "toggle", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore] public bool IsAction => string.Equals(Type, "action", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<string> TargetFiles()
    {
        if (string.IsNullOrWhiteSpace(ToggleTargets))
            return [];

        return ToggleTargets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(relative => Path.Combine(DirectoryPath, relative));
    }

    public static PluginManifest CreateSample(string id) => new()
    {
        Id = id,
        Name = id,
        Description = "拖进插件目录即可被识别",
        Type = "toggle",
        Glyph = "\uE9F0"
    };

    public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    });

    public static PluginManifest? FromJson(string json)
    {
        try
        {
            return JsonConvert.DeserializeObject<PluginManifest>(json, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }
        catch
        {
            return null;
        }
    }
}
