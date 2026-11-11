using System.Text.RegularExpressions;

namespace OilFXTool;

/// <summary>
///     CustomOilFX 注入引擎（与 OMniX 管理器内置的 OilFxService 同源逻辑，独立无依赖）：
///     扫描 Mods 根 → 聚合可注入 mod → 往 mod.ini 插 OilSkin/Clear 包裹（官方 bat 同款）→ 还原 .bak。
/// </summary>
public sealed class OilFxEngine
{
    private static readonly Regex SectionHeaderRegex = new(@"^\[(.+)\]$", RegexOptions.Compiled);
    private static readonly Regex ComponentSectionRegex = new(@"^TextureOverride.*Component\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CleanupRunRegex = new(@"^run\s*=\s*CommandListCleanupSharedResources\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OverrideRunRegex = new(@"^run\s*=\s*CommandListOverrideSharedResources\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClearLineRegex = new(@"KatFXMatcap\\Clear\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OilSkinLineRegex = new(@"KatFXMatcap\\OilSkin\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string OilSkinLine = "run = CommandList\\KatFXMatcap\\OilSkin";
    public const string ClearLine = "run = CommandList\\KatFXMatcap\\Clear";

    private static readonly string[] SkippedFolderNames = ["Oil", "Others"];
    private const string DisabledPrefix = "DISABLED";

    // ─────────────── 扫描 ───────────────

    public bool IsFrameworkInstalled(string modsRoot) =>
        File.Exists(Path.Combine(modsRoot, "Matcap.ini")) && Directory.Exists(Path.Combine(modsRoot, "Oil"));

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

    public List<OilFxModInfo> ScanFolder(string folder)
    {
        var result = new List<OilFxModInfo>();
        if (!Directory.Exists(folder))
        {
            return result;
        }

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

            return new OilFxIniInfo(
                iniPath,
                Path.GetFileName(Path.GetDirectoryName(iniPath)) ?? string.Empty,
                Path.GetFileNameWithoutExtension(iniPath),
                componentNames.Count,
                lines.Any(l => OilSkinLineRegex.IsMatch(l.Trim())),
                File.Exists(iniPath + ".bak"));
        }
        catch
        {
            return null;
        }
    }

    // ─────────────── 注入 / 还原 ───────────────

    /// <summary>往 mod ini 注入 OilSkin/Clear 包裹，首次改前自动备份 .bak。返回注入的行数。</summary>
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

            var indent = Regex.Match(line, @"^(\s*)").Groups[1].Value;

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
        }

        return patched;
    }

    public bool RestoreIni(string iniPath)
    {
        var bak = iniPath + ".bak";
        if (!File.Exists(bak))
        {
            return false;
        }

        File.Copy(bak, iniPath, overwrite: true);
        return true;
    }

    // ─────────────── 参数 / 键位（含自愈） ───────────────

    /// <summary>游戏内热键覆盖（Alt+小键盘；3DMigoto 修饰键语法是空格分隔，加号写法会失效）。</summary>
    private static readonly (string Section, string Key)[] HotkeyOverrides =
    [
        ("KeyOilToggle", "alt VK_NUMPAD0"),
        ("KeyOilIncrease", "alt VK_NUMPAD1"),
        ("KeyOilDecrease", "alt VK_NUMPAD2"),
    ];

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

    /// <summary>写参数 + Alt 键位 + 湿身贴图指向（框架被覆盖后重跑即可恢复）。</summary>
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
        // $oilSkin 必须锚定 global persist 行，否则会破坏热键段的循环值列表 "$oilSkin = 0,1,2..."
        content = Regex.Replace(content, @"(global persist \$oilSkin\s*=\s*)\d+", $"${{1}}{Math.Clamp(oilSkin, 0, 6)}");
        content = ApplyKeybindings(content);
        // 湿身款用水感贴图（冷灰蓝 + 天光反射 + 水珠高光），比原版 Wet.dds 湿润感强
        if (File.Exists(Path.Combine(modsRoot, "Oil", "Wet.png")))
        {
            content = Regex.Replace(
                content, @"(\[ResourceWet\][^\[]*?filename\s*=\s*)Oil[/\\]Wet\.dds",
                "${1}Oil/Wet.png",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
        File.WriteAllText(iniPath, content);
    }

    /// <summary>把 Matcap.ini 的 [KeyOil*] 段键位改成 Alt+小键盘（整行覆盖键值，幂等）。</summary>
    public string ApplyKeybindings(string content)
    {
        foreach (var (section, key) in HotkeyOverrides)
        {
            var pattern = $@"(\[{section}\](?:(?!\[).)*?^\s*key\s*=\s*)[^\r\n]+";
            content = Regex.Replace(
                content, pattern, $"${{1}}{key}",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
        }

        return content;
    }

    // ─────────────── 框架安装（从工具目录 Resources 复制） ───────────────

    /// <summary>把工具目录下的 Resources（Matcap.ini + Oil\）安装到 Mods 根，返回复制的文件数。</summary>
    public int InstallFramework(string modsRoot, string toolResourceDir)
    {
        var count = 0;
        var matcapSrc = Path.Combine(toolResourceDir, "Matcap.ini");
        if (File.Exists(matcapSrc))
        {
            File.Copy(matcapSrc, Path.Combine(modsRoot, "Matcap.ini"), overwrite: true);
            count++;
        }

        var oilSrc = Path.Combine(toolResourceDir, "Oil");
        if (Directory.Exists(oilSrc))
        {
            var oilDst = Path.Combine(modsRoot, "Oil");
            Directory.CreateDirectory(oilDst);
            foreach (var file in Directory.EnumerateFiles(oilSrc, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(oilSrc, file);
                File.Copy(file, Path.Combine(oilDst, rel), overwrite: true);
                count++;
            }
        }

        return count;
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

/// <summary>扫描结果的分组：一级目录名（角色目录）+ 组内可注入 mod 文件夹。</summary>
public sealed record OilFxGroupInfo(string GroupName, List<OilFxModInfo> Mods, int DisabledModsCount = 0);

/// <summary>一个 mod（= 一个 mod 文件夹）：文件夹下全部可注入 ini。</summary>
public sealed record OilFxModInfo(
    string FolderPath,
    string FolderName,
    string SubPath,
    List<OilFxIniInfo> Inis)
{
    public int InjectedCount => Inis.Count(i => i.Injected);
    public bool Injected => Inis.Count > 0 && Inis.All(i => i.Injected);
}
