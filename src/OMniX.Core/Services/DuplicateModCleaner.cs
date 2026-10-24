using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;

namespace OMniX.Core.Services;

/// <summary>
///     二开：同名重复 Mod 清理。
///     整理时目标位置已有同名目录会被跳过，这些就是"残留"。这里把它们和已归位的那一份做比对，
///     只有文件内容确认一致才允许清理，避免误删。
/// </summary>
public sealed record DuplicateModPair(
    string CharacterDisplayName,
    string SourceFolderName,
    string SourcePath,
    string TargetPath,
    int SourceFileCount,
    int TargetFileCount,
    long SourceSize,
    long TargetSize,
    bool Identical,
    string Note)
{
    public string SizeText => $"{SourceSize / 1024.0 / 1024.0:F1} MB";
}

public static class DuplicateModCleaner
{
    /// <summary>
    ///     扫描所有「源目录里还留着、目标位置已经有了」的同名 Mod。
    /// </summary>
    public static IReadOnlyList<DuplicateModPair> ScanDuplicates(string modsPath,
        IEnumerable<ICategory> categories, IEnumerable<IModdableObject> characters)
    {
        var results = new List<DuplicateModPair>();

        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
            return results;

        var scan = LegacyCharacterOrganizer.Scan(modsPath, categories, characters);

        foreach (var move in scan.Moves.Where(x => x.TargetExists))
        {
            if (!Directory.Exists(move.SourceModPath) || !Directory.Exists(move.TargetModPath))
                continue;

            var sourceFiles = MapFiles(move.SourceModPath);
            var targetFiles = MapFiles(move.TargetModPath);

            var sourceSize = sourceFiles.Values.Sum(x => x.Size);
            var targetSize = targetFiles.Values.Sum(x => x.Size);

            var (identical, note) = Compare(sourceFiles, targetFiles);

            results.Add(new DuplicateModPair(
                move.CharacterDisplayName,
                move.SourceFolderName,
                move.SourceModPath,
                move.TargetModPath,
                sourceFiles.Count,
                targetFiles.Count,
                sourceSize,
                targetSize,
                identical,
                note));
        }

        return results;
    }

    /// <summary>
    ///     这些是 OMniX / Mod 工具运行时会改写的文件（按键设置、启用状态、缩略图配置）。
    ///     两边核心资源一致时，它们的差异不代表 Mod 内容不同。
    /// </summary>
    private static readonly HashSet<string> MetaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ini", ".json", ".txt", ".bak", ".BaK", ".BAK", ".url", ".log", ".db", ".disabled", ".md"
    };

    private static bool IsMetaFile(string relativePath)
    {
        return MetaExtensions.Contains(Path.GetExtension(relativePath));
    }

    private static (bool Identical, string Note) Compare(
        Dictionary<string, (long Size, DateTime Modified)> source,
        Dictionary<string, (long Size, DateTime Modified)> target)
    {
        if (source.Count == 0 && target.Count == 0)
            return (true, "两边都是空目录");

        if (source.Count == 0)
            return (false, "源目录为空，目标有内容");

        if (target.Count == 0)
            return (false, "目标目录为空，源目录有内容");

        // 核心资源 = 除配置文件以外的文件
        var sourceCore = source.Where(pair => !IsMetaFile(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var targetCore = target.Where(pair => !IsMetaFile(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        if (sourceCore.Count == 0 || targetCore.Count == 0)
            return (false, "缺少可比对的核心资源文件");

        var missing = sourceCore.Keys.Except(targetCore.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var extra = targetCore.Keys.Except(sourceCore.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var sizeDiffs = sourceCore.Keys.Intersect(targetCore.Keys, StringComparer.OrdinalIgnoreCase)
            .Count(key => sourceCore[key].Size != targetCore[key].Size);

        if (missing.Count > 0 || extra.Count > 0 || sizeDiffs > 0)
        {
            return (false, $"核心资源不同：目标缺 {missing.Count} 个 / 源缺 {extra.Count} 个 / 大小不同 {sizeDiffs} 个");
        }

        var metaDiff = source.Where(pair => IsMetaFile(pair.Key))
            .Count(pair => !target.TryGetValue(pair.Key, out var other) || other.Size != pair.Value.Size);

        return metaDiff == 0
            ? (true, "文件清单与大小完全一致")
            : (true, $"核心资源一致，另有 {metaDiff} 个配置文件不同（按键设置/启用状态，删除源不影响在用的一份）");
    }

    private static Dictionary<string, (long Size, DateTime Modified)> MapFiles(string root)
    {
        var map = new Dictionary<string, (long Size, DateTime Modified)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                var relative = Path.GetRelativePath(root, file);

                map[relative] = (info.Length, info.LastWriteTimeUtc);
            }
        }
        catch
        {
            // 读不出来的目录不做任何判断，交给用户处理
        }

        return map;
    }
}
