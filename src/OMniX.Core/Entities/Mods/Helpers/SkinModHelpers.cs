using System.Text.RegularExpressions;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Helpers;

namespace OMniX.Core.Entities.Mods.Helpers;

public static class SkinModHelpers
{
    public static string? UriPathToModRelativePath(ISkinMod mod, string? uriPath)
    {
        if (string.IsNullOrWhiteSpace(uriPath))
            return null;

        var modPath = mod.FullPath;

        var modUri = Uri.TryCreate(modPath, UriKind.Absolute, out var result) &&
                     result.Scheme == Uri.UriSchemeFile
            ? result
            : null;

        var uri = Uri.TryCreate(uriPath, UriKind.Absolute, out var uriResult) &&
                  uriResult.Scheme == Uri.UriSchemeFile
            ? uriResult
            : null;

        // This is technically the only path that should be used.
        if (modUri is not null && uri is not null)
        {
            var relativeUri = modUri.MakeRelativeUri(uri);

            // 二开：modUri 没有末尾斜杠，MakeRelativeUri 会把 Mod 目录名自己也带出来，
            // 所以这里要去掉。注意只能去掉「开头」那一段——
            // 原来的写法是 Replace(...)，它会替换所有匹配项，
            // 一旦 Mod 里出现同名的嵌套目录（Mod\同名\xxx\mod.ini），两层会一起被删掉，
            // 结果路径少一层，下次就读不到 ini → Mod 的变体全部识别不出来。
            var modName = modUri.Segments.LastOrDefault()?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(modName))
                modName = mod.Name;

            var relativePath = relativeUri.OriginalString;
            var prefix = $"{modName}/";

            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath[prefix.Length..];

            return relativePath;
        }


        if (Uri.IsWellFormedUriString(uriPath, UriKind.Absolute))
        {
            var filename = Path.GetFileName(uriPath);
            return string.IsNullOrWhiteSpace(filename) ? null : filename;
        }

        var absPath = Path.GetFileName(uriPath);

        var file = Path.GetFileName(absPath);
        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    public static Uri? RelativeModPathToAbsPath(string modPath, string? relativeModPath)
    {
        if (string.IsNullOrWhiteSpace(relativeModPath))
            return null;

        var uri = Uri.TryCreate(Path.Combine(modPath, relativeModPath), UriKind.Absolute, out var result) &&
                  result.Scheme == Uri.UriSchemeFile
            ? result
            : null;

        return uri;
    }

    public static bool IsInModFolder(ISkinMod mod, Uri path)
    {
        if (path.Scheme != Uri.UriSchemeFile)
            return false;

        var fsPath = path.LocalPath;


        return fsPath.StartsWith(mod.FullPath, StringComparison.OrdinalIgnoreCase);
    }


    public static Uri? StringUrlToUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return Uri.IsWellFormedUriString(url, UriKind.Absolute) ? new Uri(url) : null;
    }

    public static Guid StringToGuid(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return Guid.NewGuid();

        return Guid.TryParse(guid, out var result) ? result : Guid.NewGuid();
    }

    public static readonly string[] _imageNamePriority = new[] { ".omnix_cover", "preview", "cover" };

    public static Uri[] DetectModPreviewImages(string modDirPath)
    {
        var modDir = new DirectoryInfo(modDirPath);
        if (!modDir.Exists)
            return Array.Empty<Uri>();

        // 二开：分三层找，越靠前越可信
        // 1) 根层且文件名像预览图（preview / cover / .omnix_cover）
        var images = CollectImages(modDir, true);

        // 2) 根层任意图片 —— 根目录里的图基本都是拿来当封面的
        if (images.Count == 0)
            images = CollectImages(modDir, false);

        // 3) 子目录里文件名像预览图的。
        //    这里必须限制命名，否则会把 mod 的贴图（动辄几百张）全当成预览图。
        if (images.Count == 0)
        {
            foreach (var subDir in EnumerateSubDirectories(modDir, 3))
                images.AddRange(CollectImages(subDir, true));
        }

        if (images.Count == 0)
            return Array.Empty<Uri>();

        // Sort images by priority
        foreach (var imageName in _imageNamePriority.Reverse())
        {
            var image = images.FirstOrDefault(x => x.Name.ToLower().StartsWith(imageName));
            if (image is null)
                continue;

            images.Remove(image);
            images.Insert(0, image);
        }

        return images.Select(x => new Uri(x.FullName)).ToArray();
    }

    private static List<FileInfo> CollectImages(DirectoryInfo dir, bool requireNameHint)
    {
        var result = new List<FileInfo>();

        foreach (var file in dir.EnumerateFiles())
        {
            if (!Constants.SupportedImageExtensions.Contains(file.Extension.ToLower()))
                continue;

            if (requireNameHint && !_imageNamePriority.Any(i => file.Name.ToLower().StartsWith(i)))
                continue;

            result.Add(file);
        }

        return result;
    }

    private static IEnumerable<DirectoryInfo> EnumerateSubDirectories(DirectoryInfo dir, int maxDepth)
    {
        if (maxDepth <= 0)
            yield break;

        foreach (var subDir in dir.EnumerateDirectories())
        {
            yield return subDir;

            foreach (var nested in EnumerateSubDirectories(subDir, maxDepth - 1))
                yield return nested;
        }
    }

    /// <summary>
    ///     二开：从 mod 的 ini 里认出快捷切换键（3DMigoto 的 key = xxx）。
    /// </summary>
    public static string? DetectHotKey(string modDirPath)
    {
        var modDir = new DirectoryInfo(modDirPath);

        if (!modDir.Exists)
            return null;

        var iniFiles = modDir.EnumerateFiles("*.ini").ToList();

        if (iniFiles.Count == 0)
        {
            iniFiles = modDir.EnumerateDirectories()
                .SelectMany(dir => dir.EnumerateFiles("*.ini", SearchOption.AllDirectories))
                .Take(20)
                .ToList();
        }

        foreach (var iniFile in iniFiles)
        {
            try
            {
                foreach (var line in File.ReadLines(iniFile.FullName))
                {
                    var match = Regex.Match(line, @"^\s*key\s*=\s*(?<value>[^\r\n;]+)",
                        RegexOptions.IgnoreCase);

                    if (!match.Success)
                        continue;

                    var value = match.Groups["value"].Value.Trim();

                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch
            {
                // 读不了就跳过这个 ini
            }
        }

        return null;
    }

    /// <summary>
    ///     二开：识别到的一个快捷键 / 变体变量（值 + 来源文件 + 类型）。
    ///     Kind 为「按键」（key = xxx）或「变体」（$xxx 这种可调参数）。
    /// </summary>
    public sealed record HotKeyCandidate(string Key, string Source, string Kind = "按键");

    /// <summary>
    ///     二开：上一次检测的情况（路径、扫到几个 ini、读失败几个、认出几个）。
    ///     排查「列表是空的」时用，安装页会把这条写进日志。
    /// </summary>
    public static string LastDetectSummary { get; private set; } = "还没跑过检测";

    /// <summary>
    ///     二开：一次最多扫多少个 ini。超过就停，避免在大 Mod 包上卡住。
    /// </summary>
    private const int MaxIniFiles = 60;

    /// <summary>
    ///     二开：把 mod 里所有能认出来的快捷键都找出来，安装页会列出来让人选。
    /// </summary>
    public static List<HotKeyCandidate> DetectHotKeys(string modDirPath)
    {
        var results = new List<HotKeyCandidate>();

        var modDir = new DirectoryInfo(modDirPath);

        if (!modDir.Exists)
        {
            LastDetectSummary = $"目录不存在: {modDirPath}";
            return results;
        }

        var iniFiles = SafeFindIniFiles(modDir, out var scanNote);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failedReads = 0;

        foreach (var iniFile in iniFiles)
        {
            try
            {
                foreach (var line in File.ReadLines(iniFile.FullName))
                {
                    // 按键绑定：key = VK_NUMPAD1
                    var keyMatch = Regex.Match(line, @"^\s*key\s*=\s*(?<value>[^\r\n;]+)",
                        RegexOptions.IgnoreCase);

                    if (keyMatch.Success)
                    {
                        var value = keyMatch.Groups["value"].Value.Trim();

                        if (!string.IsNullOrEmpty(value) && seen.Add("key:" + value))
                            results.Add(new HotKeyCandidate(value, iniFile.Name, "按键"));

                        continue;
                    }

                    // 二开：变体变量。3DMigoto 里 global $strength / $strengthS = ... 这类可调参数，
                    // 界面上会显示成「变体：N」，用户需要看到它们才能调。
                    // 不要求必须带 =，global $active 这种纯声明也要算。
                    var variableMatch = Regex.Match(line,
                        @"^\s*(?:global\s+(?:persist\s+)?)?\$(?<name>[A-Za-z_]\w*)",
                        RegexOptions.IgnoreCase);

                    if (!variableMatch.Success)
                        continue;

                    var variableName = "$" + variableMatch.Groups["name"].Value;

                    if (seen.Add("var:" + variableName))
                        results.Add(new HotKeyCandidate(variableName, iniFile.Name, "变体"));
                }
            }
            catch
            {
                // 单个 ini 读不了不影响其它 ini
                failedReads++;
            }
        }

        LastDetectSummary =
            $"路径={modDirPath} | {scanNote} | 读失败={failedReads} | 识别={results.Count}";

        return results;
    }

    /// <summary>
    ///     二开：找 ini 文件。先在本体找，找不到再一层层往下找。
    /// </summary>
    private static List<FileInfo> SafeFindIniFiles(DirectoryInfo modDir, out string note)
    {
        var list = new List<FileInfo>();
        var errors = 0;

        try
        {
            list.AddRange(modDir.EnumerateFiles("*.ini"));
        }
        catch
        {
            errors++;
        }

        if (list.Count == 0)
        {
            // 二开：不能直接用 EnumerateFiles(path, "*.ini", AllDirectories)。
            // 压缩包解出来的目录名经常带方括号（[DaisyMeow]XXX.zip），
            // 一层层手动往下走比一次性递归稳。
            var queue = new Queue<DirectoryInfo>();
            queue.Enqueue(modDir);

            var visited = 0;

            while (queue.Count > 0 && list.Count < MaxIniFiles && visited < 300)
            {
                var current = queue.Dequeue();
                visited++;

                try
                {
                    foreach (var file in current.EnumerateFiles("*.ini"))
                    {
                        if (list.Count >= MaxIniFiles)
                            break;

                        list.Add(file);
                    }

                    foreach (var child in current.EnumerateDirectories())
                        queue.Enqueue(child);
                }
                catch
                {
                    errors++;
                }
            }

            note = $"递归扫描 目录数={visited}";
        }
        else
        {
            note = "本体就有 ini";
        }

        note += $" | ini={list.Count} | 扫描异常={errors}";

        return list;
    }
}