using System.Text.Json;

namespace OMniX.WinUI.Services;

/// <summary>
///     二开：一条清理记录（对应的文件已经进了系统回收站）
/// </summary>
public sealed record CleanHistoryEntry(
    DateTimeOffset DeletedAt,
    string CharacterName,
    string ModName,
    string SourcePath,
    long SizeBytes)
{
    public string TimeText => DeletedAt.LocalDateTime.ToString("MM-dd HH:mm");

    public string SizeText => $"{SizeBytes / 1024.0 / 1024.0:F1} MB";
}

/// <summary>
///     二开：清理记录索引。真正的文件还原走系统回收站，这里只记录"清理过什么"。
/// </summary>
public sealed class CleanHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private CleanHistoryService()
    {
    }

    public static CleanHistoryService Instance { get; } = new();

    public string IndexPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OMniX", "clean-history.json");

    public IReadOnlyList<CleanHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(IndexPath))
                return [];

            var json = File.ReadAllText(IndexPath);
            return JsonSerializer.Deserialize<List<CleanHistoryEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Append(IEnumerable<CleanHistoryEntry> entries)
    {
        try
        {
            var all = Load().ToList();
            all.AddRange(entries);

            // 只留最近 200 条，避免文件无限增长
            if (all.Count > 200)
                all = all.OrderByDescending(x => x.DeletedAt).Take(200).ToList();

            var directory = Path.GetDirectoryName(IndexPath)!;
            Directory.CreateDirectory(directory);

            File.WriteAllText(IndexPath, JsonSerializer.Serialize(all, JsonOptions));
        }
        catch
        {
            // 记录失败不影响清理本身
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(IndexPath))
                File.Delete(IndexPath);
        }
        catch
        {
            // ignore
        }
    }
}
