using System.Text.Json;

using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Services;

using Serilog;

namespace OmniX.Plugin.OilFX.UI;

/// <summary>
///     独立版轻量角色库：直接读 OMniX 的角色资产（characters.json + 中文语言覆盖 + 头像图片），
///     让 OilFX 列表显示角色中文名和头像 —— 和宿主版一致。
///     资产目录：优先用宿主的 GamesData\WuWa（数据更新），找不到就用插件自带的 Assets\Games\WuWa。
/// </summary>
internal static class StandaloneCharacterLibrary
{
    private const string GameFolder = "WuWa";
    private static List<ICharacter>? _cache;

    public static List<ICharacter> GetCharacters(bool includeDisabled)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        _cache = Load();
        Log.Information("OilFX 独立版角色库: 加载 {Count} 个角色", _cache.Count);
        return _cache;
    }

    private static List<ICharacter> Load()
    {
        var assetsDir = ResolveAssetsDirectory();
        if (assetsDir is null)
        {
            Log.Warning("OilFX 独立版角色库: 未找到角色资产目录（宿主 GamesData 和插件自带都没有）");
            return [];
        }

        Log.Information("OilFX 独立版角色库: 使用 {Dir}", assetsDir);

        try
        {
            var json = File.ReadAllText(Path.Combine(assetsDir, "characters.json"));
            using var doc = JsonDocument.Parse(json);

            // 中文显示名覆盖（Languages\zh-cn\characters.json: [{InternalName, DisplayName}]）
            var displayNames = LoadDisplayNames(assetsDir);

            var imageFolder = Path.Combine(assetsDir, "Images", "Characters");
            var list = new List<ICharacter>();

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var internalName = GetString(el, "InternalName");
                if (string.IsNullOrEmpty(internalName))
                {
                    continue;
                }

                var displayName = displayNames.GetValueOrDefault(internalName)
                                  ?? GetString(el, "DisplayName")
                                  ?? internalName;
                var image = GetString(el, "Image");
                var modFilesName = GetString(el, "ModFilesName") ?? internalName;
                var rarity = el.TryGetProperty("Rarity", out var r) && r.TryGetInt32(out var ri) ? ri : 5;
                DateTime? releaseDate = null;
                if (el.TryGetProperty("ReleaseDate", out var rd) && rd.ValueKind == JsonValueKind.String)
                {
                    releaseDate = rd.GetString() is { } s && DateTime.TryParse(s, out var d) ? d : null;
                }

                Uri? imageUri = null;
                if (!string.IsNullOrEmpty(image))
                {
                    var imagePath = Path.Combine(imageFolder, image);
                    if (File.Exists(imagePath))
                    {
                        imageUri = new Uri(imagePath);
                    }
                }

                var keys = new List<string>();
                if (el.TryGetProperty("Keys", out var ks) && ks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var k in ks.EnumerateArray())
                    {
                        var v = k.GetString();
                        if (!string.IsNullOrEmpty(v))
                        {
                            keys.Add(v);
                        }
                    }
                }

                list.Add(new SimpleCharacter(internalName, displayName, modFilesName, imageUri, keys, rarity, releaseDate));
            }

            return list;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OilFX 独立版角色库: 解析失败");
            return [];
        }
    }

    private static Dictionary<string, string> LoadDisplayNames(string assetsDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var langPath = Path.Combine(assetsDir, "Languages", "zh-cn", "characters.json");
            if (!File.Exists(langPath))
            {
                return result;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(langPath));
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var internalName = GetString(el, "InternalName");
                var displayName = GetString(el, "DisplayName");
                if (!string.IsNullOrEmpty(internalName) && !string.IsNullOrEmpty(displayName))
                {
                    result[internalName] = displayName;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OilFX 独立版角色库: 语言覆盖解析失败");
        }

        return result;
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>宿主 GamesData\WuWa 优先（向上搜 6 级），插件自带 Assets\Games\WuWa 兜底。</summary>
    private static string? ResolveAssetsDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "GamesData", GameFolder, "characters.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "Games", GameFolder, "characters.json");
        return File.Exists(bundled) ? Path.GetDirectoryName(bundled) : null;
    }
}
