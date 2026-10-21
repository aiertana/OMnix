using OMniX.Core.Entities.Mods.FileModels;

namespace OMniX.Core.Helpers;

// This class just holds code that i don't know where to put yet.
public static class IniConfigHelpers
{
    public static IniKeySwapSection? ParseKeySwap(ICollection<string> fileLines, string sectionLine)
    {
        var skinModKeySwap = new IniKeySwapSection
        {
            SectionKey = sectionLine.Trim()
        };

        // 二开：3dmigoto 的 key 段通常写成 $变量 = 0,1,2，没写 $swapvar 时用它来算变体数
        string[]? fallbackSwapVar = null;

        foreach (var line in fileLines)
        {
            // 二开：把段内第一条注释当成这个按键的说明
            if (IsComment(line))
            {
                skinModKeySwap.Description ??= line.Trim().TrimStart(';').Trim();
                continue;
            }

            if (IsIniKey(line, IniKeySwapSection.ForwardIniKey))
                skinModKeySwap.ForwardHotkey = GetIniValue(line);

            else if (IsIniKey(line, IniKeySwapSection.BackwardIniKey))
                skinModKeySwap.BackwardHotkey = GetIniValue(line);

            else if (IsIniKey(line, IniKeySwapSection.TypeIniKey))
                skinModKeySwap.Type = GetIniValue(line);

            else if (IsIniKey(line, IniKeySwapSection.SwapVarIniKey))
            {
                skinModKeySwap.SwapVar = SplitSwapVar(GetIniValue(line));
                skinModKeySwap.VariableName ??= "swapvar";
            }

            else if (line.TrimStart().StartsWith('$'))
            {
                // 二开：记录 $armsbrace = 0,1, 这类变量，作为变体数和变量名的后备来源
                var varName = GetIniKey(line);
                var varValue = GetIniValue(line);
                var cleanName = varName?.Trim().TrimStart('$');

                if (cleanName is not null && !cleanName.EndsWith("B", StringComparison.OrdinalIgnoreCase))
                {
                    skinModKeySwap.VariableName ??= cleanName;

                    if (fallbackSwapVar is null && varValue is not null)
                        fallbackSwapVar = SplitSwapVar(varValue);
                }
            }

            else if (IsSection(line))
                break;
        }

        skinModKeySwap.SwapVar ??= fallbackSwapVar;

        var result = skinModKeySwap.AnyValues() ? skinModKeySwap : null;
        return result;
    }

    public static string? GetIniValue(string line)
    {
        if (IsComment(line)) return null;

        var split = line.Split('=');

        if (split.Length <= 2) return split.Length != 2 ? null : split[1].Trim();


        split[1] = string.Join("", split.Skip(1));
        return split[1].Trim();
    }

    public static string? GetIniKey(string line)
    {
        if (IsComment(line)) return null;

        var split = line.Split('=');
        return split.Length != 2 ? split.FirstOrDefault()?.Trim() : split[0].Trim();
    }

    public static bool IsComment(string line) => line.Trim().StartsWith(";");

    public static bool IsSection(string line, string? sectionKey = null)
    {
        line = line.Trim();
        if (sectionKey is null && line.StartsWith("[") && line.EndsWith("]"))
            return true;

        if (sectionKey is not null && line.Equals($"[{sectionKey}]", StringComparison.CurrentCultureIgnoreCase))
            return true;

        if (sectionKey is not null && (sectionKey.StartsWith("[") && sectionKey.EndsWith("]")) &&
            line.Equals(sectionKey, StringComparison.CurrentCultureIgnoreCase))
            return true;

        return false;
    }

    public static bool IsIniKey(string line, string key) =>
        line.Trim().StartsWith(key, StringComparison.CurrentCultureIgnoreCase);

    public static string? FormatIniKey(string key, string? value) =>
        value is not null ? $"{key} = {value}" : null;

    /// <summary>
    ///     二开：把 "0,1,2," 这种值列表拆干净，末尾逗号不会算一个变体
    /// </summary>
    private static string[]? SplitSwapVar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }
}