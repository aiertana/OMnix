using OMniX.Core.Helpers;

namespace OMniX.WinUI.Services.AppManagement;

/// <summary>
///     在启动器根目录（例如 D:\mod\XXMI Launcher）中查找可用于启动的 exe，
///     并根据模型导入器简称推导 Mods 文件夹（例如 D:\mod\XXMI Launcher\WWMI\Mods）
/// </summary>
public static class LauncherLocator
{
    /// <summary>
    ///     规范的 Mods 文件夹名
    /// </summary>
    public const string ModsFolderName = "Mods";

    /// <summary>
    ///     已知的启动器 exe 所在子目录（相对启动器根目录）
    /// </summary>
    private static readonly string[] SearchSubDirectories =
    [
        "Resources\\Bin", "Resources\\bin", "Resources", "Bin", "bin", "Resources\\Packages", ""
    ];

    /// <summary>
    ///     优先匹配的 exe 名称片段
    /// </summary>
    private static readonly string[] PreferredNameParts = ["launcher", "start", "xxmi"];

    /// <summary>
    ///     需要忽略的 exe 名称片段
    /// </summary>
    private static readonly string[] IgnoredNameParts =
    [
        "unins", "crash", "setup", "updater", "update", "elevator", "vc_redist", "dxwebsetup", "dotnet"
    ];

    public static string? FindLauncherExecutable(string? launcherRootFolder)
    {
        if (launcherRootFolder.IsNullOrEmpty() || !Directory.Exists(launcherRootFolder))
            return null;

        var candidates = new List<string>();

        foreach (var subDirectory in SearchSubDirectories)
        {
            var directory = subDirectory.Length == 0
                ? launcherRootFolder
                : Path.Combine(launcherRootFolder, subDirectory);

            if (!Directory.Exists(directory))
                continue;

            candidates.AddRange(Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly));
        }

        candidates = candidates.Where(candidate => !IsIgnored(candidate)).Distinct().ToList();

        // 优先选择名字里带 launcher / xxmi 的可执行文件
        return candidates
            .Select(candidate => (Path: candidate, Rank: GetPreferredNameRank(candidate)))
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => Path.GetFileName(candidate.Path))
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static int GetPreferredNameRank(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        for (var index = 0; index < PreferredNameParts.Length; index++)
        {
            if (fileName.Contains(PreferredNameParts[index], StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return int.MaxValue;
    }

    /// <summary>
    ///     推导 Mods 文件夹：{启动器根目录}\{导入器简称}\Mods，忽略大小写地匹配已存在目录
    /// </summary>
    public static string GetModsFolder(string launcherRootFolder, string modelImporterShortName)
    {
        var importerFolder = ResolveDirectory(launcherRootFolder, modelImporterShortName);

        if (importerFolder is null)
            return Path.Combine(launcherRootFolder, modelImporterShortName, "Mods");

        return ResolveDirectory(importerFolder, "Mods") ?? Path.Combine(importerFolder, "Mods");
    }

    /// <summary>
    ///     二开：判断文件夹能不能当作启动器根目录（现在走 XXMI，不再有 WWMI Loader.exe）。
    ///     满足下面任意一条就算通过：
    ///     1) 根目录里有 XXMI 的标志文件（XXMI Launcher Config.json）
    ///     2) 根目录下面有该游戏的导入器目录，比如 XXMI Launcher\WWMI
    ///     3) 直接选到了导入器目录本身（里面有 Mods 文件夹或 d3dx.ini）
    /// </summary>
    public static bool IsValidLauncherRoot(string? folder, string? modelImporterShortName)
    {
        if (folder.IsNullOrEmpty() || !Directory.Exists(folder))
            return false;

        // 1) XXMI 根目录标志
        if (File.Exists(Path.Combine(folder, "XXMI Launcher Config.json")))
            return true;

        // 2) 导入器子目录
        if (!modelImporterShortName.IsNullOrEmpty() &&
            Directory.Exists(Path.Combine(folder, modelImporterShortName)))
            return true;

        // 3) 直接选到导入器目录
        if (Directory.Exists(Path.Combine(folder, "Mods")) ||
            File.Exists(Path.Combine(folder, "d3dx.ini")))
            return true;

        return false;
    }

    /// <summary>
    ///     二开：在启动器根目录 / 导入器目录中查找<b>实际存在</b>的 Mod 文件夹（名字不一定规范）
    /// </summary>
    public static DetectedModsFolder? TryDetectExistingModsFolder(string? launcherRootFolder,
        string modelImporterShortName)
    {
        if (launcherRootFolder.IsNullOrEmpty() || !Directory.Exists(launcherRootFolder))
            return null;

        var parentFolders = new List<string>();

        if (!modelImporterShortName.IsNullOrEmpty())
        {
            var importerFolder = ResolveDirectory(launcherRootFolder, modelImporterShortName);
            if (importerFolder is not null)
                parentFolders.Add(importerFolder);
        }

        parentFolders.Add(launcherRootFolder);

        foreach (var parentFolder in parentFolders)
        {
            var candidate = Directory.GetDirectories(parentFolder)
                .Where(directory =>
                {
                    // 只认 mods / mod（忽略分隔符号），避免把 Models 之类的目录误判成 Mod 目录
                    var name = Path.GetFileName(directory)
                        .Replace("_", string.Empty)
                        .Replace("-", string.Empty)
                        .Replace(" ", string.Empty);

                    return name.Equals("mod", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("mods", StringComparison.OrdinalIgnoreCase);
                })
                .Select(directory => new DetectedModsFolder(directory, CountMods(directory)))
                .Where(detected => detected.ModCount > 0)
                .OrderByDescending(detected => detected.IsWellNamed)
                .ThenByDescending(detected => detected.ModCount)
                .FirstOrDefault();

            if (candidate is not null)
                return candidate;
        }

        return null;
    }

    /// <summary>
    ///     Mod 数量：3DMigto 的 Mods 目录里一个 Mod 就是一个子文件夹
    /// </summary>
    private static int CountMods(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string? ResolveDirectory(string parentFolder, string folderName)
    {
        if (!Directory.Exists(parentFolder))
            return null;

        return Directory.GetDirectories(parentFolder)
            .FirstOrDefault(directory =>
                string.Equals(Path.GetFileName(directory), folderName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIgnored(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        return IgnoredNameParts.Any(part => fileName.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
///     二开：识别到的已有 Mod 文件夹
/// </summary>
public record DetectedModsFolder(string FolderPath, int ModCount)
{
    /// <summary>
    ///     文件夹名是否已经是规范的 "Mods"
    /// </summary>
    public bool IsWellNamed =>
        string.Equals(Path.GetFileName(FolderPath), LauncherLocator.ModsFolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     同目录下的规范路径（原地改名后的路径，不会换位置）
    /// </summary>
    public string WellNamedPath => Path.Combine(Path.GetDirectoryName(FolderPath)!, LauncherLocator.ModsFolderName);
}
