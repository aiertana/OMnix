using OMniX.Core.Entities.Mods.FileModels;
using OMniX.Core.Helpers;

namespace OMniX.Core.Entities.Mods.Contract;

public record KeySwapSection
{
    public string SectionName { get; init; } = "Unknown";

    /// <summary>
    ///     二开：美化后的显示名，比如 [KeyHair] → Hair、[KeyMenu] → 菜单
    /// </summary>
    public string DisplayName { get; init; } = "未知";

    /// <summary>
    ///     二开：段内注释里写的说明
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     二开：是不是菜单段（3dmigoto 里通常用来开关整个 Mod 的菜单）
    /// </summary>
    public bool IsMenu { get; init; }

    public string? ForwardKey { get; init; }

    public string? BackwardKey { get; init; }

    /// <summary>
    ///     二开：这个按键控制的变量名（switching / swapvar / armsbrace ...）
    /// </summary>
    public string? VariableName { get; init; }

    /// <summary>
    ///     二开：列表里显示的主标签，优先用变量名（跟 QAQManager 一致），没有再退回段名
    /// </summary>
    public string PrimaryLabel => string.IsNullOrWhiteSpace(VariableName) ? DisplayName : VariableName!;

    /// <summary>
    ///     二开：美化后的按键，比如 "Alt + ←"
    /// </summary>
    public string? ForwardKeyDisplay => KeyHotkeyFormatter.ToDisplay(ForwardKey);

    public string? BackwardKeyDisplay => KeyHotkeyFormatter.ToDisplay(BackwardKey);

    /// <summary>
    ///     二开：拆成键帽显示，比如 [Ctrl, ←]
    /// </summary>
    public IReadOnlyList<string> ForwardKeyTokens => KeyHotkeyFormatter.ToTokens(ForwardKey);

    public IReadOnlyList<string> BackwardKeyTokens => KeyHotkeyFormatter.ToTokens(BackwardKey);

    public bool HasBackwardKey => !string.IsNullOrWhiteSpace(BackwardKey);

    public int? Variants { get; init; }

    public string Type { get; init; } = "Unknown";


    internal static KeySwapSection FromIniKeySwapSection(IniKeySwapSection iniKeySwapSection)
    {
        return new KeySwapSection
        {
            SectionName = iniKeySwapSection.SectionKey,
            DisplayName = KeySwapNameHelper.PrettyName(iniKeySwapSection.SectionKey),
            Description = iniKeySwapSection.Description,
            VariableName = iniKeySwapSection.VariableName,
            IsMenu = KeySwapNameHelper.IsMenuSection(iniKeySwapSection.SectionKey),
            ForwardKey = iniKeySwapSection.ForwardHotkey,
            BackwardKey = iniKeySwapSection.BackwardHotkey,
            Variants = iniKeySwapSection.SwapVar?.Length,
            Type = iniKeySwapSection.Type ?? "Unknown"
        };
    }
}

/// <summary>
///     二开：把 [KeyHair] 这类段名整理成人能看懂的名字
/// </summary>
public static class KeySwapNameHelper
{
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["menu"] = "菜单",
        ["ui"] = "界面",
        ["hair"] = "头发",
        ["eyes"] = "眼睛",
        ["eye"] = "眼睛",
        ["face"] = "脸部",
        ["body"] = "身体",
        ["skin"] = "皮肤",
        ["dress"] = "服装",
        ["outfit"] = "服装",
        ["weapon"] = "武器",
        ["horn"] = "角",
        ["halo"] = "光环",
        ["tail"] = "尾巴",
        ["hand"] = "手",
        ["foot"] = "脚",
        ["legs"] = "腿",
        ["arm"] = "手臂",
        ["wing"] = "翅膀",
        ["nsfw"] = "NSFW",
        ["nude"] = "NSFW",
        ["naked"] = "NSFW",

        // 常见部位 / 配件
        ["makeup"] = "妆容",
        ["chest"] = "胸部",
        ["armsbrace"] = "臂环",
        ["collar"] = "颈饰",
        ["bra"] = "胸衣",
        ["bodysuit"] = "紧身衣",
        ["ribbon"] = "丝带",
        ["skirtband"] = "裙带",
        ["featherskirt"] = "羽毛裙",
        ["sleeves"] = "袖子",
        ["ankle"] = "脚踝",
        ["nails"] = "指甲",
        ["footnails"] = "脚趾甲",
        ["tacetmark"] = "声痕",
        ["bodychain"] = "身体链",
        ["rose"] = "玫瑰",
        ["butterfly"] = "蝴蝶",
        ["outfit"] = "服装",
        ["stockings"] = "丝袜",
        ["nipplering"] = "胸环",
        ["wings"] = "翅膀",
        ["wing"] = "翅膀",
        ["wingcolor"] = "翅膀颜色",
        ["wingcolord"] = "翅膀颜色D"
    };

    /// <summary>
    ///     二开：名字里带这些词时直接给出中文，兜住 [KeysToufaColor] 这类不规范的段名
    /// </summary>
    private static readonly (string Keyword, string Name)[] KeywordNames =
    [
        ("toufa", "头发颜色"),
        ("yanjing", "眼睛颜色"),
        ("yifu", "衣服颜色"),
        ("color", "颜色"),
        ("colour", "颜色")
    ];

    public static string PrettyName(string? sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            return "未知";

        var name = sectionName.Trim().Trim('[', ']').Trim();

        if (name.StartsWith("Key", StringComparison.OrdinalIgnoreCase))
            name = name[3..];

        name = name.Trim('_', '-', ' ');

        if (string.IsNullOrWhiteSpace(name))
            return "默认";

        if (KnownNames.TryGetValue(name, out var pretty))
            return pretty;

        var lowerName = name.ToLowerInvariant();
        foreach (var (keyword, keywordName) in KeywordNames)
        {
            if (!lowerName.Contains(keyword))
                continue;

            // 颜色类只在不认识的时候兜底，避免盖住上面的精确映射
            if (keyword is "color" or "colour")
                continue;

            return keywordName;
        }

        // 首字母大写，其余照原样保留，便于识别自定义段名
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static bool IsMenuSection(string? sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            return false;

        var name = sectionName.Trim().Trim('[', ']').Trim();
        if (name.StartsWith("Key", StringComparison.OrdinalIgnoreCase))
            name = name[3..];

        return name.Equals("menu", StringComparison.OrdinalIgnoreCase);
    }
}