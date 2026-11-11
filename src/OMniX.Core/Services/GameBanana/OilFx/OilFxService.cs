using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

using OMniX.Core.Services.GameBanana.ApiModels;
using OMniX.Core.Services.GameBanana.Models;

using Serilog;

namespace OMniX.Core.Services.GameBanana.OilFx;

/// <summary>
///     OilFX（GameBanana mod 733773 customoilfx）安装与跟随更新：
///     - 框架：Matcap.ini + Oil\ 贴图放 3DMigoto Mods 根（全局层，加载一次）
///     - 生效：往角色 mod 自己的 .ini 里注入 KatFXMatcap\OilSkin / Clear 包裹
///       （TextureOverrideComponent 3+ 段，首次改前备份 .bak，可一键还原）
///     - 更新：查 mod 附件 id 与本地 state 对比，有新附件一键下载覆盖
///     状态存 %LOCALAPPDATA%\OMniX\oilfx_state.json。
/// </summary>
public sealed class OilFxService(
    IApiGameBananaClient apiGameBanana,
    OMniX.Core.Services.ArchiveService archiveService,
    ILogger logger)
{
    private static readonly object StateLock = new();

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OMniX", "oilfx_state.json");

    /// <summary>挂钩 ini 固定文件名（放 Mods 根）。</summary>
    public const string HookFileName = "OilFX@CharHooks.ini";

    private OilFxState _state = LoadState();

    public OilFxState State => _state;

    /// <summary>查线上最新附件（这个 mod 只有一个包附件，取第一个）。</summary>
    public async Task<ApiModFileInfo?> GetLatestFileAsync(CancellationToken ct = default)
    {
        var profile = await apiGameBanana.GetModProfileAsync(new GbModId(OilFxHooks.ModId), ct).ConfigureAwait(false);
        return profile?.Files?.FirstOrDefault(f => f.FileId > 0);
    }

    /// <summary>安装 / 更新：下载附件 → 解压 → Matcap.ini + Oil\ 铺进 Mods 根。</summary>
    public async Task<string> InstallOrUpdateAsync(string modsRoot, IProgress<int>? progress, CancellationToken ct = default)
    {
        var file = await GetLatestFileAsync(ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("GameBanana 没有返回 OilFX 的附件信息");

        var tmpRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "OMniX_OilFX"));
        var archivePath = Path.Combine(tmpRoot.FullName, file.FileName);

        await using (var fs = File.Create(archivePath))
        {
            var progressAdapter = new Progress<int>(p => progress?.Report(p));
            await apiGameBanana.DownloadModAsync(new GbModFileId(file.FileId), fs, progressAdapter, ct).ConfigureAwait(false);
        }

        var extractDir = Path.Combine(tmpRoot.FullName, "extract");
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        await Task.Run(() => archiveService.ExtractArchive(archivePath, extractDir, overwritePath: true), ct)
            .ConfigureAwait(false);

        // 解压出来的内容可能是根下直接放 Matcap.ini，也可能带一层目录，两种都兜
        var source = Directory.EnumerateFiles(extractDir, "Matcap.ini", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new InvalidOperationException("压缩包里没找到 Matcap.ini（包结构变了？）");
        var sourceRoot = Path.GetDirectoryName(source)!;

        Directory.CreateDirectory(modsRoot);
        File.Copy(source, Path.Combine(modsRoot, "Matcap.ini"), overwrite: true);
        CopyDirectory(Path.Combine(sourceRoot, "Oil"), Path.Combine(modsRoot, "Oil"));

        _state = new OilFxState
        {
            InstalledFileId = file.FileId,
            FileName = file.FileName,
            InstalledAt = DateTime.Now,
            ModsRoot = modsRoot
        };
        SaveState(_state);
        logger.Information("OilFX 框架已安装: {File} → {ModsRoot}", file.FileName, modsRoot);

        try
        {
            Directory.Delete(tmpRoot.FullName, recursive: true);
        }
        catch
        {
            // 临时目录清不掉就留给下次覆盖
        }

        return file.FileName;
    }

    /// <summary>框架是否已在位（Matcap.ini + Oil 目录）。</summary>
    public bool IsFrameworkInstalled(string modsRoot) =>
        File.Exists(Path.Combine(modsRoot, "Matcap.ini")) && Directory.Exists(Path.Combine(modsRoot, "Oil"));

    /// <summary>生成 / 重写角色挂钩 ini；没有勾选任何角色时删除挂钩文件。</summary>
    public void WriteHookIni(string modsRoot, IReadOnlyCollection<string> normalizedCharacters)
    {
        var hookPath = Path.Combine(modsRoot, HookFileName);
        if (normalizedCharacters.Count == 0)
        {
            if (File.Exists(hookPath))
            {
                File.Delete(hookPath);
                logger.Information("OilFX 挂钩已清空，删除 {Hook}", hookPath);
            }

            return;
        }

        var sections = OilFxHooks.Sections
            .Where(s => s.Characters.Any(c => normalizedCharacters.Contains(c)))
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("; OMniX OilFX 角色挂钩（由 OilFX 插件生成，改勾选请回插件界面操作）");
        sb.AppendLine($"; 源: {OilFxHooks.SourceUrl} (customoilfx v12 by Febby315)");
        sb.AppendLine($"; 已启用角色: {string.Join(", ", normalizedCharacters)}");
        sb.AppendLine();

        var conflicts = sections
            .SelectMany(s => OilFxHooks.ConflictTip.Split('+'))
            .Where(c => normalizedCharacters.Contains(OilFxName.Normalize(c)))
            .Distinct()
            .ToList();
        if (conflicts.Count > 0)
        {
            sb.AppendLine("; TIP: 以下角色的部分部位 mod 可能与全局挂钩冲突（原作者标注）:");
            sb.AppendLine($"; {string.Join("+", conflicts)}");
            sb.AppendLine();
        }

        foreach (var section in sections)
        {
            var sectionName = section.Kind == "VB0"
                ? section.Title
                : section.Title.Replace("VS-", "ShaderOverride_VS-");
            sb.AppendLine($"[{sectionName}]");
            sb.AppendLine($"hash = {section.Hash}");
            if (section.MatchIndexCount is not null)
            {
                sb.AppendLine($"match_index_count = {section.MatchIndexCount}");
                sb.AppendLine($"match_first_index = {section.MatchFirstIndex}");
            }

            sb.AppendLine("run = CommandList\\KatFXMatcap\\OilSkin");
            sb.AppendLine();
        }

        File.WriteAllText(hookPath, sb.ToString());
        logger.Information("OilFX 挂钩已写入: {Hook}，{Count} 个段", hookPath, sections.Count);
    }

    /// <summary>当前挂钩启用了哪些角色（解析挂钩 ini 里的"已启用角色"注释行）。</summary>
    public IReadOnlyList<string> ReadEnabledCharacters(string modsRoot)
    {
        var hookPath = Path.Combine(modsRoot, HookFileName);
        if (!File.Exists(hookPath))
        {
            return [];
        }

        try
        {
            var line = File.ReadAllLines(hookPath)
                .FirstOrDefault(l => l.StartsWith("; 已启用角色:"));
            return line is null
                ? []
                : line["; 已启用角色:".Length..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(OilFxName.Normalize)
                    .ToList();
        }
        catch (Exception e)
        {
            logger.Warning(e, "OilFX 挂钩读取失败");
            return [];
        }
    }

    // ─────────────── mod ini 注入引擎（复刻官方 bat 脚本逻辑） ───────────────

    private static readonly Regex SectionHeaderRegex = new(@"^\[(.+)\]$", RegexOptions.Compiled);
    private static readonly Regex ComponentSectionRegex = new(@"^TextureOverride.*Component\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CleanupRunRegex = new(@"^run\s*=\s*CommandListCleanupSharedResources\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OverrideRunRegex = new(@"^run\s*=\s*CommandListOverrideSharedResources\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClearLineRegex = new(@"KatFXMatcap\\Clear\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OilSkinLineRegex = new(@"KatFXMatcap\\OilSkin\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string OilSkinLine = "run = CommandList\\KatFXMatcap\\OilSkin";
    public const string ClearLine = "run = CommandList\\KatFXMatcap\\Clear";

    /// <summary>跳过的特殊目录（框架贴图 / 无 mod 杂物 / 禁用前缀）。</summary>
    private static readonly string[] SkippedFolderNames = ["Oil", "Others"];
    private const string DisabledPrefix = "DISABLED";

    /// <summary>路径任一段以 DISABLED 开头 = 被启动器禁用的 mod，跳过。</summary>
    private static bool IsDisabledPath(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.StartsWith(DisabledPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     扫描整个 Mods 根：每个一级子目录为一组（通常就是角色目录，中文名），
    ///     组内递归找含 TextureOverrideComponent 段的 ini，按 mod 文件夹聚合。
    ///     不依赖角色库的 InternalName——玩家目录用什么名就按什么名归组。
    /// </summary>
    public List<OilFxGroupInfo> ScanModsRoot(string modsRoot)
    {
        var groups = new List<OilFxGroupInfo>();
        if (!Directory.Exists(modsRoot))
        {
            return groups;
        }

        foreach (var dir in Directory.EnumerateDirectories(modsRoot))
        {
            var name = Path.GetFileName(dir);
            if (SkippedFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                name.StartsWith(DisabledPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var disabled = 0;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var subName = Path.GetFileName(sub);
                if (subName.StartsWith(DisabledPrefix, StringComparison.OrdinalIgnoreCase) &&
                    Directory.EnumerateFiles(sub, "*.ini", SearchOption.AllDirectories).Any())
                {
                    disabled++;
                }
            }

            var mods = ScanFolder(dir);
            if (mods.Count > 0 || disabled > 0)
            {
                groups.Add(new OilFxGroupInfo(name, mods, disabled));
            }
        }

        return groups;
    }

    /// <summary>递归扫描一个文件夹下所有"可注入"的 mod 文件夹（含 TextureOverrideComponent 段的 ini，按文件夹聚合）。</summary>
    public List<OilFxModInfo> ScanFolder(string folder)
    {
        var result = new List<OilFxModInfo>();
        if (!Directory.Exists(folder))
        {
            return result;
        }

        // 按 mod 文件夹聚合：一个文件夹 = 一个 mod（同一 mod 的多个 ini 变体算一个开关）
        var byFolder = new Dictionary<string, OilFxModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var ini in Directory.EnumerateFiles(folder, "*.ini", SearchOption.AllDirectories))
        {
            if (IsDisabledPath(ini))
            {
                continue;
            }

            var info = ScanIniFile(ini);
            if (info is null)
            {
                continue;
            }

            var dir = Path.GetDirectoryName(ini) ?? folder;
            if (!byFolder.TryGetValue(dir, out var mod))
            {
                var sub = dir.Length > folder.Length
                    ? dir[folder.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : string.Empty;
                mod = new OilFxModInfo(dir, Path.GetFileName(dir), sub.Length > 0 ? sub : "（根目录）", []);
                byFolder[dir] = mod;
            }

            mod.Inis.Add(info);
        }

        result.AddRange(byFolder.Values);
        return result;
    }

    /// <summary>检查单个 ini 是否可注入；可注入返回信息，否则 null。</summary>
    private OilFxIniInfo? ScanIniFile(string iniPath)
    {
        try
        {
            if (iniPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(iniPath).StartsWith("OilFX@", StringComparison.Ordinal) ||
                string.Equals(Path.GetFileName(iniPath), "Matcap.ini", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var lines = File.ReadAllLines(iniPath);
            // 段头 [TextureOverride...Component N]：先剥方括号取段名，再匹配类型
            // （ComponentSectionRegex 的 ^ 锚定的是段名，不能直接对整行匹配）
            static string? SectionName(string line)
            {
                var m = SectionHeaderRegex.Match(line.TrimStart());
                return m.Success ? m.Groups[1].Value : null;
            }

            var componentNames = lines
                .Select(SectionName)
                .Where(n => n is not null && ComponentSectionRegex.IsMatch(n!))
                .ToList();
            if (componentNames.Count == 0)
            {
                return null;
            }

            var injected = lines.Any(l => OilSkinLineRegex.IsMatch(l.Trim()));
            var hasBackup = File.Exists(iniPath + ".bak");
            var componentCount = componentNames.Count;
            return new OilFxIniInfo(
                iniPath,
                Path.GetFileName(Path.GetDirectoryName(iniPath)) ?? string.Empty,
                Path.GetFileNameWithoutExtension(iniPath),
                componentCount,
                injected,
                hasBackup);
        }
        catch (Exception e)
        {
            logger.Warning(e, "OilFX 扫描 ini 失败: {Ini}", iniPath);
            return null;
        }
    }

    /// <summary>
    ///     往 mod ini 注入 OilSkin/Clear 包裹（官方 bat 同款逻辑）：
    ///     Component ≥ minComp 的段内，Cleanup 行之前插 Clear、Override 行之后插 OilSkin。
    ///     首次改前自动备份 .bak。返回注入的行数。
    /// </summary>
    public int InjectIni(string iniPath, int minComp = 3)
    {
        var lines = File.ReadAllLines(iniPath);

        var bak = iniPath + ".bak";
        if (!File.Exists(bak))
        {
            File.Copy(iniPath, bak);
        }

        var outLines = new List<string>(lines.Length + 16);
        var inTarget = false;
        var patched = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trim = line.TrimStart();

            var header = SectionHeaderRegex.Match(trim);
            if (header.Success)
            {
                var comp = ComponentSectionRegex.Match(header.Groups[1].Value);
                inTarget = comp.Success && int.Parse(comp.Groups[1].Value) >= minComp;
            }

            if (!inTarget)
            {
                outLines.Add(line);
                continue;
            }

            var indentMatch = Regex.Match(line, @"^(\s*)");
            var indent = indentMatch.Success ? indentMatch.Groups[1].Value : string.Empty;

            // Cleanup 之前插 Clear（上一行已是 Clear 就跳过）
            if (CleanupRunRegex.IsMatch(trim))
            {
                var prev = outLines.Count > 0 ? outLines[^1].Trim() : string.Empty;
                if (!ClearLineRegex.IsMatch(prev))
                {
                    outLines.Add(indent + ClearLine);
                    patched++;
                }
            }

            outLines.Add(line);

            // Override 之后插 OilSkin（下一行已是 OilSkin 就跳过）
            if (OverrideRunRegex.IsMatch(trim))
            {
                var next = i + 1 < lines.Length ? lines[i + 1].Trim() : string.Empty;
                if (!OilSkinLineRegex.IsMatch(next))
                {
                    outLines.Add(indent + OilSkinLine);
                    patched++;
                }
            }
        }

        if (patched > 0)
        {
            File.WriteAllLines(iniPath, outLines);
            logger.Information("OilFX 注入 {Ini}: {Count} 行 (Component >= {MinComp})", iniPath, patched, minComp);
        }

        return patched;
    }

    /// <summary>从 .bak 还原 mod ini。没有备份返回 false。</summary>
    public bool RestoreIni(string iniPath)
    {
        var bak = iniPath + ".bak";
        if (!File.Exists(bak))
        {
            return false;
        }

        File.Copy(bak, iniPath, overwrite: true);
        logger.Information("OilFX 已还原 {Ini}", iniPath);
        return true;
    }

    /// <summary>把效果参数写进 Matcap.ini 的 Constants 默认值，并保证 Alt+小键盘热键在位。
    /// 注意 $oilSkin 必须锚定 global persist 行，否则会破坏热键段的循环值列表 "$oilSkin = 0,1,2..."。</summary>
    public void WriteParams(string modsRoot, double oilStrength, bool realisticOil, int oilSkin = 2)
    {
        var iniPath = Path.Combine(modsRoot, "Matcap.ini");
        if (!File.Exists(iniPath))
        {
            return;
        }

        var content = File.ReadAllText(iniPath);
        content = Regex.Replace(content, @"(\$oilStrength\s*=\s*)[\d.]+", $"${{1}}{oilStrength.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)}");
        content = Regex.Replace(content, @"(\$realisticOil\s*=\s*)[\d.]+", $"${{1}}{(realisticOil ? 1 : 0)}");
        content = Regex.Replace(content, @"(global persist \$oilSkin\s*=\s*)\d+", $"${{1}}{Math.Clamp(oilSkin, 0, 6)}");
        content = ApplyKeybindings(content);
        // 湿身款用生成的水感贴图（Oil/Wet.png，冷灰蓝 + 天光反射 + 水珠高光），比原版 Wet.dds 湿润感强
        if (File.Exists(Path.Combine(modsRoot, "Oil", "Wet.png")))
        {
            content = Regex.Replace(
                content, @"(\[ResourceWet\][^\[]*?filename\s*=\s*)Oil[/\\]Wet\.dds",
                "${1}Oil/Wet.png",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
        File.WriteAllText(iniPath, content);
        logger.Information("OilFX 参数已写入: strength={Strength} realistic={Realistic} skin={Skin}", oilStrength, realisticOil, oilSkin);
    }

    /// <summary>游戏内热键覆盖（Alt+小键盘，避免占游戏本键）：0=循环油光风格 1=强度+ 2=强度-。
    /// 注意 3DMigoto 的修饰键语法是空格分隔（如 "alt VK_NUMPAD0"，参照自带热键 mod 的 "no_modifiers VK_UP"），
    /// 写成 "alt+VK_NUMPAD0" 会被当成未知键名，热键整体失效。</summary>
    private static readonly (string Section, string Key)[] HotkeyOverrides =
    [
        ("KeyOilToggle", "alt VK_NUMPAD0"),
        ("KeyOilIncrease", "alt VK_NUMPAD1"),
        ("KeyOilDecrease", "alt VK_NUMPAD2"),
    ];

    /// <summary>把 Matcap.ini 的 [KeyOil*] 段键位改成 Alt+小键盘（框架更新覆盖后重新调用即可恢复）。</summary>
    public string ApplyKeybindings(string content)
    {
        foreach (var (section, key) in HotkeyOverrides)
        {
            // 定位 [Section] 段内的 key = ... 行，整行覆盖键值（键值含空格，必须吃满一行才幂等）
            var pattern = $@"(\[{section}\](?:(?!\[).)*?^\s*key\s*=\s*)[^\r\n]+";
            content = Regex.Replace(
                content, pattern, $"${{1}}{key}",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
        }

        return content;
    }

    /// <summary>读当前参数（解析失败给默认值）。</summary>
    public (double Strength, bool Realistic, int Skin) ReadParams(string modsRoot)
    {
        try
        {
            var content = File.ReadAllText(Path.Combine(modsRoot, "Matcap.ini"));
            var strength = Regex.Match(content, @"\$oilStrength\s*=\s*([\d.]+)");
            var realistic = Regex.Match(content, @"\$realisticOil\s*=\s*([\d.]+)");
            var skin = Regex.Match(content, @"global persist \$oilSkin\s*=\s*(\d+)");
            return (
                strength.Success ? double.Parse(strength.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0.7,
                realistic.Success && realistic.Groups[1].Value != "0",
                skin.Success ? Math.Clamp(int.Parse(skin.Groups[1].Value), 0, 6) : 2);
        }
        catch
        {
            return (0.7, false, 2);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static OilFxState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                return JsonSerializer.Deserialize<OilFxState>(File.ReadAllText(StatePath)) ?? new OilFxState();
            }
        }
        catch
        {
            // 坏了就从未安装开始
        }

        return new OilFxState();
    }

    private static void SaveState(OilFxState state)
    {
        lock (StateLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
    }
}

/// <summary>一个可注入的 mod ini 的扫描结果。</summary>
public sealed record OilFxIniInfo(
    string IniPath,
    string ModFolder,
    string IniName,
    int ComponentCount,
    bool Injected,
    bool HasBackup);

/// <summary>扫描结果的分组：一级目录名（角色 mod 目录，常见中文名）+ 组内可注入 mod 文件夹。
/// DisabledModsCount = 组内被启动器禁用（DISABLED_ 前缀）而未列出的 mod 数，供界面说明数量差异。</summary>
public sealed record OilFxGroupInfo(string GroupName, List<OilFxModInfo> Mods, int DisabledModsCount = 0);

/// <summary>一个 mod（= 一个 mod 文件夹，教程 bat 的操作单位）：文件夹下全部可注入 ini。</summary>
public sealed record OilFxModInfo(
    string FolderPath,
    string FolderName,
    string SubPath,
    List<OilFxIniInfo> Inis)
{
    /// <summary>已注入的 ini 数。</summary>
    public int InjectedCount => Inis.Count(i => i.Injected);

    /// <summary>全部 ini 已注入（驱动开关状态）。</summary>
    public bool Injected => Inis.Count > 0 && Inis.All(i => i.Injected);

    /// <summary>全部 ini 有备份（还原才完整）。</summary>
    public bool HasBackup => Inis.Count > 0 && Inis.All(i => i.HasBackup);
}

public sealed class OilFxState
{
    public int InstalledFileId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public DateTime InstalledAt { get; set; }

    public string ModsRoot { get; set; } = string.Empty;
}
