using System.Text;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;

namespace OMniX.Core.Services;

/// <summary>
///     二开：角色分类目录整理。参考 QAQM 的「刷新角色列表 / 整理助手」。
///     只移动分类目录里的真实 Mod 文件夹，跳过同名，清理已经空掉的旧目录，不删除任何 Mod 内容。
/// </summary>
public sealed record LegacyCharacterMove(
    string CharacterDisplayName,
    string CharacterInternalName,
    string TargetCategoryName,
    string SourceFolderName,
    string SourceFolderPath,
    string ModName,
    string SourceModPath,
    string TargetModPath,
    bool TargetExists);

public sealed record LegacyCharacterScan(
    IReadOnlyList<LegacyCharacterMove> Moves,
    IReadOnlyList<string> SourceFolders,
    IReadOnlyList<string> TargetCharacters,
    IReadOnlyList<string> EmptyFolders)
{
    public bool HasCandidates => Moves.Count > 0;

    public int ModCount => Moves.Count;

    public int FolderCount => SourceFolders.Count;
}

public sealed record LegacyCharacterMigrateResult(
    int MovedMods,
    IReadOnlyList<string> SkippedConflicts,
    IReadOnlyList<string> RemovedEmptyFolders,
    IReadOnlyList<string> Errors);

public static class LegacyCharacterOrganizer
{
    /// <summary>
    ///     扫描分类目录下名字不规范的旧角色目录（中文名、大小写不一致、旧管理器结构等）。
    /// </summary>
    public static LegacyCharacterScan Scan(string modsPath, IEnumerable<ICategory> categories,
        IEnumerable<IModdableObject> characters)
    {
        var moves = new List<LegacyCharacterMove>();
        var sourceFolders = new List<string>();
        var targets = new List<string>();
        var emptyFolders = new List<string>();

        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
            return new LegacyCharacterScan(moves, sourceFolders, targets, emptyFolders);

        var allCharacters = characters.ToList();

        // 二开：分类目录本身（角色 / NPC / 武器 / 滑翔翼…）不参与「根目录角色文件夹」的整理。
        // 用户可能有和分类同名的自定义角色（比如自定义角色就叫「武器」），
        // 不排除的话整个分类目录会被当成角色目录搬走。
        var categoryFolderNames = categories
            .SelectMany(category => ModFolderHelpers
                .GetCategoryFolderNameCandidates(category.InternalName.ToString()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A. Mods 根目录下的角色分类目录（QAQM 旧结构：Mods\尤诺\xxx）
        foreach (var folder in SafeEnumerateDirectories(modsPath))
        {
            if (categoryFolderNames.Contains(folder.Name))
                continue;

            var character = MatchCharacter(allCharacters, folder.Name);

            if (character is null)
                continue;

            var categoryName = character.ModCategory?.InternalName.ToString();

            if (string.IsNullOrWhiteSpace(categoryName))
                continue;

            // 二开：角色直接放 Mods 根目录、用中文名（Mods\漂泊者），不再套一层 角色\
            var canonicalDir = Path.Combine(modsPath, GetTargetRelativePath(character));

            // 已经在规范位置就跳过。
            // 二开：这里不能只比文件夹名 —— Mods\心 和 角色\心 名字一样但位置不同，
            // 只比名字会让根目录的老位置永远整理不过去。
            if (string.Equals(folder.FullName, canonicalDir, StringComparison.OrdinalIgnoreCase))
                continue;

            var rootModFolders = SafeEnumerateDirectories(folder.FullName);

            if (rootModFolders.Count == 0)
            {
                emptyFolders.Add(folder.FullName);
                continue;
            }

            sourceFolders.Add(folder.FullName);

            if (!targets.Contains(character.DisplayName))
                targets.Add(character.DisplayName);

            foreach (var modFolder in rootModFolders)
            {
                var targetPath = Path.Combine(canonicalDir, modFolder.Name);

                moves.Add(new LegacyCharacterMove(
                    character.DisplayName,
                    character.InternalName.ToString(),
                    Path.GetFileName(canonicalDir),
                    folder.Name,
                    folder.FullName,
                    modFolder.Name,
                    modFolder.FullName,
                    targetPath,
                    Directory.Exists(targetPath)));
            }
        }

        // C. 二开：Mods 根目录下认不出归属的东西归类。
        //    看着像 Mod（有 .ini）→ 统一放「大世界」；不像 Mod（压缩包 / 散落文件）→ 放 others。
        var worldFolderName = ModFolderHelpers.GetCategoryFolderName("object");
        var otherFolderName = "others";

        foreach (var folder in SafeEnumerateDirectories(modsPath))
        {
            if (categoryFolderNames.Contains(folder.Name))
                continue;

            // others 本身就是兜底目录，不挪
            if (string.Equals(folder.Name, otherFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            // 能认出角色的已经在 A 里处理了
            if (MatchCharacter(allCharacters, folder.Name) is not null)
                continue;

            var looksLikeMod = LooksLikeModFolder(folder);
            var targetParent = Path.Combine(modsPath, looksLikeMod ? worldFolderName : otherFolderName);
            var targetPath = Path.Combine(targetParent, folder.Name);

            if (string.Equals(folder.FullName, targetPath, StringComparison.OrdinalIgnoreCase) ||
                Directory.Exists(targetPath))
                continue;

            if (looksLikeMod)
            {
                // 有命名的（大世界 / 武器 / 滑翔翼…）已经在各自目录里，这里只收无主的
                if (!targets.Contains(worldFolderName))
                    targets.Add(worldFolderName);
            }

            sourceFolders.Add(folder.FullName);

            moves.Add(new LegacyCharacterMove(
                looksLikeMod ? worldFolderName : otherFolderName,
                folder.Name,
                looksLikeMod ? worldFolderName : otherFolderName,
                folder.Name,
                modsPath,
                folder.Name,
                folder.FullName,
                targetPath,
                Directory.Exists(targetPath)));
        }

        foreach (var category in categories)
        {
            var categoryInternalName = category.InternalName.ToString();

            // 二开：旧的英文分类目录（character）也要扫，里面的残留 Mod 要搬到中文目录去
            var categoryPaths = ModFolderHelpers.GetCategoryFolderNameCandidates(categoryInternalName)
                .Select(name => Path.Combine(modsPath, name))
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var categoryPath in categoryPaths)
            {
                foreach (var folder in SafeEnumerateDirectories(categoryPath))
                {
                    var character = MatchCharacter(allCharacters, folder.Name);

                    // 认不出来就不是角色目录，不碰
                    if (character is null)
                        continue;

                    // 二开：按对象自己的分类算目标（角色 → Mods\中文名，非角色 → Mods\分类\xxx），
                    // 不能用外层目录的分类，否则扫 武器\ 时会把内容当成角色搬走。
                    var canonicalParent = Path.Combine(modsPath, GetTargetRelativePath(character));

                    // 已经在规范位置，无需整理
                    if (string.Equals(folder.FullName, canonicalParent, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var modFolders = SafeEnumerateDirectories(folder.FullName);

                    if (modFolders.Count == 0)
                    {
                        emptyFolders.Add(folder.FullName);
                        continue;
                    }

                    sourceFolders.Add(folder.FullName);

                    if (!targets.Contains(character.DisplayName))
                        targets.Add(character.DisplayName);

                    foreach (var modFolder in modFolders)
                    {
                        var targetPath = Path.Combine(canonicalParent, modFolder.Name);

                        moves.Add(new LegacyCharacterMove(
                            character.DisplayName,
                            character.InternalName.ToString(),
                            Path.GetFileName(canonicalParent),
                            folder.Name,
                            folder.FullName,
                            modFolder.Name,
                            modFolder.FullName,
                            targetPath,
                            Directory.Exists(targetPath)));
                    }
                }
            }
        }

        return new LegacyCharacterScan(moves, sourceFolders, targets, emptyFolders);
    }

    /// <summary>
    ///     执行整理。跳过同名目标，移动完成后清理空掉的旧目录。
    /// </summary>
    public static LegacyCharacterMigrateResult Migrate(LegacyCharacterScan scan)
    {
        var moved = 0;
        var conflicts = new List<string>();
        var removed = new List<string>();
        var errors = new List<string>();

        foreach (var move in scan.Moves)
        {
            try
            {
                if (!Directory.Exists(move.SourceModPath))
                    continue;

                if (Directory.Exists(move.TargetModPath))
                {
                    // 不覆盖同名 Mod
                    conflicts.Add($"{move.CharacterDisplayName}/{move.ModName}");
                    continue;
                }

                var targetParent = Path.GetDirectoryName(move.TargetModPath);
                if (!string.IsNullOrEmpty(targetParent) && !Directory.Exists(targetParent))
                    Directory.CreateDirectory(targetParent);

                MoveDirectory(move.SourceModPath, move.TargetModPath);
                moved++;
            }
            catch (Exception e)
            {
                errors.Add($"{move.ModName}: {e.Message}");
            }
        }

        // 整理后把空掉的旧目录删掉（只删空目录，不会碰 Mod 内容）
        foreach (var sourceFolder in scan.SourceFolders)
        {
            try
            {
                if (!Directory.Exists(sourceFolder))
                    continue;

                if (Directory.EnumerateFileSystemEntries(sourceFolder).Any())
                    continue;

                Directory.Delete(sourceFolder);
                removed.Add(sourceFolder);
            }
            catch (Exception e)
            {
                errors.Add($"{sourceFolder}: {e.Message}");
            }
        }

        return new LegacyCharacterMigrateResult(moved, conflicts, removed, errors);
    }

    /// <summary>
    ///     「刷新并补充」：为缺失的角色创建规范分类目录。只建空文件夹，不动已有内容。
    /// </summary>
    public static int EnsureCharacterFolders(string modsPath, IEnumerable<ICategory> categories,
        IEnumerable<IModdableObject> characters)
    {
        if (string.IsNullOrWhiteSpace(modsPath))
            return 0;

        var categoryNames = categories.Select(x => x.InternalName.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 二开：分类目录名（角色 / 武器 / 滑翔翼…）。
        // 用户可能有和分类同名的自定义角色，补目录时不能把分类目录当成角色的老位置搬走。
        var categoryFolderNames = categories
            .SelectMany(category =>
                ModFolderHelpers.GetCategoryFolderNameCandidates(category.InternalName.ToString()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = 0;

        foreach (var character in characters)
        {
            var categoryName = character.ModCategory?.InternalName.ToString();
            if (string.IsNullOrWhiteSpace(categoryName) || !categoryNames.Contains(categoryName))
                continue;

            // 二开：角色 → Mods\中文名（不再套一层 角色\）；非角色 → Mods\分类\internalName
            var folderPath = Path.Combine(modsPath, GetTargetRelativePath(character));

            if (Directory.Exists(folderPath))
                continue;

            // 二开：磁盘上可能已经有同一个角色的目录，只是写法不同
            // （多空格、全角半角、·/・、大小写）。以前会当成「这个角色没有目录」
            // 再建一个空的，于是同一个角色在磁盘上出现两个目录，Mod 还留在旧的那个里。
            // 这里认出这种目录就直接改成规范名。
            if (string.Equals(categoryName, "character", StringComparison.OrdinalIgnoreCase))
            {
                var sameNameFolder = FindSameNameFolder(modsPath, character, categoryFolderNames);

                if (sameNameFolder is not null)
                {
                    try
                    {
                        Directory.Move(sameNameFolder, folderPath);
                        created++;
                        continue;
                    }
                    catch
                    {
                        // 改不动就继续按老逻辑处理（搬旧位置或建空目录）
                    }
                }
            }

            // 老位置里有真实 Mod 就搬过来，别建一个空的把内容挡住
            var handled = false;

            foreach (var oldPath in GetLegacyPaths(modsPath, character, categoryFolderNames))
            {
                if (string.Equals(oldPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
                    IsDirectoryEmpty(oldPath))
                    continue;

                try
                {
                    var parent = Path.GetDirectoryName(folderPath);

                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    Directory.Move(oldPath, folderPath);
                    created++;
                    handled = true;
                    break;
                }
                catch
                {
                    // 搬失败就退回到新建空目录
                }
            }

            if (handled)
                continue;

            try
            {
                Directory.CreateDirectory(folderPath);
                created++;
            }
            catch
            {
                // 单个角色建目录失败不影响其它角色
            }
        }

        // 二开：清掉已经空掉的旧英文分类目录（character 之类），中文分类目录不动
        foreach (var category in categories)
        {
            var internalName = category.InternalName.ToString();
            var mappedName = ModFolderHelpers.GetCategoryFolderName(internalName);

            if (string.Equals(internalName, mappedName, StringComparison.OrdinalIgnoreCase))
                continue;

            var legacyPath = Path.Combine(modsPath, internalName);

            try
            {
                if (!Directory.Exists(legacyPath))
                    continue;

                if (!Directory.EnumerateFileSystemEntries(legacyPath).Any())
                    Directory.Delete(legacyPath, false);
            }
            catch
            {
                // 删不掉无所谓，下次整理还会处理
            }
        }

        return created;
    }

    /// <summary>
    ///     二开：在 Mods 根目录下找「其实就是这个角色」的目录，
    ///     只是名字写法不同（空格、标点、全角半角、大小写）。
    /// </summary>
    private static string? FindSameNameFolder(string modsPath, IModdableObject moddableObject,
        ISet<string> categoryFolderNames)
    {
        var targets = new[]
        {
            ModFolderHelpers.NormalizeName(moddableObject.DisplayName),
            ModFolderHelpers.NormalizeName(moddableObject.InternalName.ToString())
        }.Where(target => !string.IsNullOrEmpty(target)).Distinct().ToArray();

        if (targets.Length == 0)
            return null;

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(modsPath))
            {
                var name = Path.GetFileName(directory);

                // 分类目录（角色 / 武器 / 大世界…）不能当成角色目录
                if (categoryFolderNames.Contains(name))
                    continue;

                if (targets.Contains(ModFolderHelpers.NormalizeName(name)))
                    return directory;
            }
        }
        catch
        {
            // 目录读不到就当没找到
        }

        return null;
    }

    /// <summary>
    ///     二开：对象可能存在的旧目录位置，补全时按顺序找有内容的那个搬过来。
    /// </summary>
    private static IEnumerable<string> GetLegacyPaths(string modsPath, IModdableObject moddableObject,
        ISet<string> categoryFolderNames)
    {
        var internalName = moddableObject.InternalName.ToString();
        var displayName = ModFolderHelpers.SanitizeFolderName(moddableObject.DisplayName);
        var categoryName = moddableObject.ModCategory?.InternalName.ToString() ?? string.Empty;

        // Mods\{internalName}（更早的根目录老位置）。分类目录本身不能当角色目录搬。
        if (!categoryFolderNames.Contains(internalName))
            yield return Path.Combine(modsPath, internalName);

        if (!string.IsNullOrEmpty(displayName) && !categoryFolderNames.Contains(displayName))
            yield return Path.Combine(modsPath, displayName);

        // 上一版结构：Mods\角色\internalName
        foreach (var categoryFolder in ModFolderHelpers.GetCategoryFolderNameCandidates(categoryName))
        {
            yield return Path.Combine(modsPath, categoryFolder, internalName);

            if (!string.IsNullOrEmpty(displayName))
                yield return Path.Combine(modsPath, categoryFolder, displayName);
        }
    }

    /// <summary>
    ///     二开：常见别名。用户手写的目录名和官方译名经常差一个字，或者用俗称。
    /// </summary>
    private static readonly Dictionary<string, string> FolderAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["女主"] = "Rover",
            ["男主"] = "Rover",
            ["漂泊者"] = "Rover",
            ["珂莱塔"] = "Carlotta",
            ["希格莉卡"] = "Sigrika"
        };

    private static IModdableObject? MatchCharacter(IEnumerable<IModdableObject> characters, string folderName)
    {
        var target = NormalizeFolderName(folderName);

        if (target.Length == 0)
            return null;

        // 二开：先按别名解析（女主 → Rover 这种）
        var aliasKey = FolderAliases.Keys.FirstOrDefault(key =>
            string.Equals(NormalizeFolderName(key), target, StringComparison.OrdinalIgnoreCase));

        if (aliasKey is not null)
        {
            var aliased = characters.FirstOrDefault(x =>
                x.InternalName.ToString().Equals(FolderAliases[aliasKey], StringComparison.OrdinalIgnoreCase));

            if (aliased is not null)
                return aliased;
        }

        return characters.FirstOrDefault(x =>
                   NormalizeFolderName(x.InternalName.ToString()) == target ||
                   NormalizeFolderName(x.DisplayName) == target)
               // 兜底：目录名里带角色名（比如「尤诺合集」「yinlin_mods」）。
               // 太短的（单字）不做这个判断，否则会误匹配一大片。
               ?? (target.Length >= 2
                   ? characters.FirstOrDefault(x =>
                       NormalizeFolderName(x.InternalName.ToString()).Contains(target) ||
                       NormalizeFolderName(x.DisplayName).Contains(target))
                   : null);
    }

    /// <summary>
    ///     二开：把目录名归一化后再比对。
    ///     各个来源的命名习惯不一样（DISABLED_ 前缀、空格、下划线、连字符、大小写都可能出现），
    ///     只做精确相等会漏掉很多本来能认出来的角色目录。
    /// </summary>
    private static string NormalizeFolderName(string name)
    {
        var value = ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(name);

        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));

        return builder.ToString();
    }

    /// <summary>
    ///     二开：对象在 Mods 下的目标相对路径。
    ///     角色直接放根目录用中文名（Mods\漂泊者），非角色放分类目录（Mods\武器\internalName）。
    /// </summary>
    private static string GetTargetRelativePath(IModdableObject moddableObject)
    {
        var categoryName = moddableObject.ModCategory?.InternalName.ToString() ?? string.Empty;

        if (string.Equals(categoryName, "character", StringComparison.OrdinalIgnoreCase))
        {
            var displayName = ModFolderHelpers.SanitizeFolderName(moddableObject.DisplayName);

            return string.IsNullOrEmpty(displayName)
                ? moddableObject.InternalName.ToString()
                : displayName;
        }

        return Path.Combine(ModFolderHelpers.GetCategoryFolderName(categoryName),
            moddableObject.InternalName.ToString());
    }

    /// <summary>
    ///     二开：看起来是不是一个 3DMigoto Mod 目录（本体或一层子目录里有 .ini）。
    ///     只看一层，避免在大目录上递归扫描拖慢整理。
    /// </summary>
    private static bool LooksLikeModFolder(DirectoryInfo folder)
    {
        try
        {
            if (folder.EnumerateFiles("*.ini").Any())
                return true;

            foreach (var subFolder in folder.EnumerateDirectories())
                if (subFolder.EnumerateFiles("*.ini").Any())
                    return true;
        }
        catch
        {
            // 读不了就当不是 Mod
        }

        return false;
    }

    /// <summary>
    ///     二开：目录不存在或者里面什么都没有都算「空」
    /// </summary>
    private static bool IsDirectoryEmpty(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return true;

        try
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return true;
        }
    }

    private static List<DirectoryInfo> SafeEnumerateDirectories(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateDirectories()
                .Where(x => !x.Name.StartsWith('.'))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void MoveDirectory(string source, string target)
    {
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException)
        {
            // 跨卷时 Directory.Move 会失败，退化成复制 + 删除
            CopyDirectory(source, target);
            Directory.Delete(source, true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
