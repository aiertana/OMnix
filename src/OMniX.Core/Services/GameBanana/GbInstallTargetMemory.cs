using System.Text.Json;

using Serilog;

namespace OMniX.Core.Services.GameBanana;

/// <summary>
///     GameBanana 下载窗口的安装目标记忆：手动选过一次角色并安装成功后，
///     下次打开同一个 mod（按 ModId）或同网页分类的 mod 自动带上上次的选择。
///     两级映射都存目标角色的 InternalName/ModFilesName，匹配回查由调用方做
///     （InstallTargets 优先，GetCharacterByIdentifier 兜底）。
///     持久化到 %LocalAppData%\OMniX\gb_target_memory.json。
/// </summary>
public static class GbInstallTargetMemory
{
    private static readonly ILogger Logger = Log.ForContext(typeof(GbInstallTargetMemory));

    private static readonly object Lock = new();

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OMniX", "gb_target_memory.json");

    private static MemoryStore? _store;

    private sealed class MemoryStore
    {
        public Dictionary<string, string> Mods { get; set; } = new();

        public Dictionary<string, string> Categories { get; set; } = new();
    }

    /// <summary>查记忆：先按 ModId 精确，再按网页分类泛化；都没有返回 null。</summary>
    public static string? Lookup(string? modId, string normalizedCategory)
    {
        lock (Lock)
        {
            var store = Load();
            if (!string.IsNullOrEmpty(modId) && store.Mods.TryGetValue(modId, out var byMod))
            {
                return byMod;
            }

            return !string.IsNullOrEmpty(normalizedCategory)
                   && store.Categories.TryGetValue(normalizedCategory, out var byCategory)
                ? byCategory
                : null;
        }
    }

    /// <summary>安装成功后记住选择（modId 可空——URL 解析失败时只记分类）。</summary>
    public static void Remember(string? modId, string normalizedCategory, string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return;
        }

        lock (Lock)
        {
            try
            {
                var store = Load();
                if (!string.IsNullOrEmpty(modId))
                {
                    store.Mods[modId] = targetName;
                }

                if (!string.IsNullOrEmpty(normalizedCategory))
                {
                    store.Categories[normalizedCategory] = targetName;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(store));
            }
            catch (Exception e)
            {
                Logger.Warning(e, "GameBanana 安装目标记忆写入失败");
            }
        }
    }

    private static MemoryStore Load()
    {
        if (_store is not null)
        {
            return _store;
        }

        try
        {
            if (File.Exists(StorePath))
            {
                _store = JsonSerializer.Deserialize<MemoryStore>(File.ReadAllText(StorePath)) ?? new MemoryStore();
            }
        }
        catch (Exception e)
        {
            Logger.Warning(e, "GameBanana 安装目标记忆读取失败，从空开始");
        }

        return _store ??= new MemoryStore();
    }
}
