using System.Text;

namespace OMniX.Core.Helpers;

public static class ModFolderHelpers
{
    public const string DISABLED_PREFIX = "DISABLED_";
    public const string ALT_DISABLED_PREFIX = "DISABLED";

    /// <summary>
    ///     二开：分类内部名 → 目录名的固定映射。
    ///     character / npc 这类英文目录名在中文界面下很突兀，统一改中文。
    ///     用硬编码而不是分类的 DisplayName，是为了避免切换界面语言时目录名跟着变，导致 Mod 全部失联。
    /// </summary>
    private static readonly Dictionary<string, string> CategoryFolderNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["character"] = "角色",
            ["characters"] = "角色",
            ["npc"] = "NPC",
            ["npcs"] = "NPC",
            ["object"] = "大世界",
            ["objects"] = "大世界",
            ["custom"] = "功能",
            ["ui"] = "UI",
            ["weapon"] = "武器",
            ["weapons"] = "武器",
            ["glider"] = "滑翔翼",
            ["gliders"] = "滑翔翼",
            ["other"] = "其他",
            ["others"] = "其他",
            ["vehicle"] = "载具",
            ["vehicles"] = "载具"
        };

    /// <summary>
    ///     二开：角色目录名的清洗。角色名里可能带 : / \ * ? 这类字符，直接当目录名会创建失败。
    /// </summary>
    public static string SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var c in name.Trim())
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        return builder.ToString().Trim().TrimEnd('.');
    }

    /// <summary>
    ///     二开：取分类对应的目录名（中文）。没配映射的还用原来的内部名。
    /// </summary>
    public static string GetCategoryFolderName(string categoryInternalName)
    {
        if (string.IsNullOrWhiteSpace(categoryInternalName))
            return categoryInternalName;

        return CategoryFolderNames.TryGetValue(categoryInternalName, out var name)
            ? name
            : categoryInternalName;
    }

    /// <summary>
    ///     二开：分类目录在磁盘上可能出现的目录名（新的中文名 + 旧的英文内部名）。
    ///     迁移期间两种都要认，否则旧目录里的 Mod 会读不到。
    /// </summary>
    public static IEnumerable<string> GetCategoryFolderNameCandidates(string categoryInternalName)
    {
        if (string.IsNullOrWhiteSpace(categoryInternalName))
            yield break;

        var mapped = GetCategoryFolderName(categoryInternalName);

        yield return mapped;

        if (!string.Equals(mapped, categoryInternalName, StringComparison.OrdinalIgnoreCase))
            yield return categoryInternalName;
    }


    /// <summary>
    /// Gets the folder name without the disabled prefix. If the folder does not have the disabled prefix, it returns the same string.
    /// </summary>
    /// <param name="folderName"></param>
    /// <returns></returns>
    public static string GetFolderNameWithoutDisabledPrefix(string folderName)
    {
        if (folderName.StartsWith(DISABLED_PREFIX, StringComparison.CurrentCultureIgnoreCase))
            return folderName.Replace(DISABLED_PREFIX, string.Empty);

        if (folderName.StartsWith(ALT_DISABLED_PREFIX, StringComparison.CurrentCultureIgnoreCase))
            return folderName.Replace(ALT_DISABLED_PREFIX, string.Empty);

        return folderName;
    }

    /// <summary>
    /// Gets the folder name with the disabled prefix. If the folder already has the disabled prefix, it returns the same string.
    /// If it has the alternate disabled prefix, it returns it with the normal disabled prefix.
    /// </summary>
    /// <param name="folderName"></param>
    /// <returns></returns>
    public static string GetFolderNameWithDisabledPrefix(string folderName)
    {
        if (folderName.StartsWith(DISABLED_PREFIX, StringComparison.CurrentCultureIgnoreCase))
            return folderName;

        if (folderName.StartsWith(ALT_DISABLED_PREFIX, StringComparison.CurrentCultureIgnoreCase))
            return folderName.Replace(ALT_DISABLED_PREFIX, DISABLED_PREFIX);

        return DISABLED_PREFIX + folderName;
    }

    /// <summary>
    /// Compares two folder names, ignoring case and the disabled prefix.
    /// </summary>
    /// <param name="folderName1"></param>
    /// <param name="folderName2"></param>
    /// <param name="absolutePaths">Specify if folderNames are absolute paths</param>
    /// <returns></returns>
    public static bool FolderNameEquals(string folderName1, string folderName2, bool absolutePaths = false)
    {
        if (absolutePaths)
        {
            folderName1 = new DirectoryInfo(folderName1).Name;
            folderName2 = new DirectoryInfo(folderName2).Name;
        }

        return GetFolderNameWithoutDisabledPrefix(folderName1).Equals(GetFolderNameWithoutDisabledPrefix(folderName2),
            StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Compares two absolute paths, ignoring case and trailing directory separators. Also compares the folder names ignoring the disabled prefix.
    /// </summary>
    public static bool AbsModFolderCompare(string absPath, string absOtherPath)
    {
        absPath = absPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLower();
        absOtherPath = absOtherPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLower();

        var folderName = new DirectoryInfo(absPath).Name.ToLower();
        var otherFolderName = new DirectoryInfo(absOtherPath).Name.ToLower();


        if (absPath.Replace(folderName, "") != absOtherPath.Replace(otherFolderName, ""))
            return false;

        return FolderNameEquals(folderName, otherFolderName);
    }

    /// <summary>
    /// Checks if the folder name has the disabled prefix.
    /// </summary>
    /// <param name="folderName"></param>
    /// <param name="absolutePath">Specify if folderName is absolute path or just the folder name</param>
    /// <returns></returns>
    public static bool FolderHasDisabledPrefix(string folderName, bool absolutePath = false)
    {
        if (absolutePath)
            folderName = new DirectoryInfo(folderName).Name;

        return folderName.StartsWith(DISABLED_PREFIX, StringComparison.CurrentCultureIgnoreCase) ||
               folderName.StartsWith(ALT_DISABLED_PREFIX, StringComparison.CurrentCultureIgnoreCase);
    }


    public static bool IsOMniXFileEntry(string entry)
    {
        return Constants.IsOmniXFile(entry);
    }

    public static bool IsOMniXFileEntry(FileSystemInfo entry)
    {
        return Constants.IsOmniXFile(entry.Name);
    }
}