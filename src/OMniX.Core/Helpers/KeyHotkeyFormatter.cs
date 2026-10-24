namespace OMniX.Core.Helpers;

/// <summary>
///     二开：把 3dmigoto 的按键写法转成看得懂的样子
///     例如 "no_ctrl alt vk_left" → "Alt + ←"
/// </summary>
public static class KeyHotkeyFormatter
{
    private static readonly Dictionary<string, string> SpecialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vk_left"] = "←",
        ["vk_right"] = "→",
        ["vk_up"] = "↑",
        ["vk_down"] = "↓",
        ["vk_space"] = "空格",
        ["vk_return"] = "回车",
        ["vk_escape"] = "Esc",
        ["vk_tab"] = "Tab",
        ["vk_back"] = "退格",
        ["vk_delete"] = "Delete",
        ["vk_insert"] = "Insert",
        ["vk_home"] = "Home",
        ["vk_end"] = "End",
        ["vk_pageup"] = "PageUp",
        ["vk_pagedown"] = "PageDown",
        ["vk_oem_4"] = "[",
        ["vk_oem_6"] = "]",
        ["vk_oem_1"] = ";",
        ["vk_oem_7"] = "'",
        ["vk_oem_comma"] = ",",
        ["vk_oem_period"] = ".",
        ["vk_oem_2"] = "/",
        ["vk_oem_5"] = "\\",
        ["vk_oem_minus"] = "-",
        ["vk_oem_plus"] = "="
    };

    /// <summary>
    ///     二开：把按键拆成一个个键帽，例如 "ctrl no_alt left" → [Ctrl, ←]
    /// </summary>
    public static IReadOnlyList<string> ToTokens(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var tokens = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();

            switch (lower)
            {
                case "ctrl":
                case "control":
                    result.Add("Ctrl");
                    break;
                case "alt":
                    result.Add("Alt");
                    break;
                case "shift":
                    result.Add("Shift");
                    break;
                case var _ when lower.StartsWith("no_"):
                    break;
                default:
                    result.Add(TranslateSingleKey(lower));
                    break;
            }
        }

        return result;
    }

    public static string? ToDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var tokens = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();

        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();

            switch (lower)
            {
                case "ctrl":
                case "control":
                    parts.Add("Ctrl");
                    break;
                case "alt":
                    parts.Add("Alt");
                    break;
                case "shift":
                    parts.Add("Shift");
                    break;

                // no_ctrl / no_alt / no_shift 只是“不按”的约束，显示时去掉
                case var _ when lower.StartsWith("no_"):
                    break;

                default:
                    parts.Add(TranslateSingleKey(lower));
                    break;
            }
        }

        return parts.Count == 0 ? raw : string.Join(" + ", parts);
    }

    private static string TranslateSingleKey(string token)
    {
        if (SpecialKeys.TryGetValue(token, out var special))
            return special;

        // 小键盘
        if (token.StartsWith("vk_numpad", StringComparison.Ordinal))
            return "小键盘" + token["vk_numpad".Length..];

        // F1 - F24
        if (token.StartsWith("vk_f", StringComparison.Ordinal) &&
            int.TryParse(token[4..], out var fNum) && fNum is >= 1 and <= 24)
            return "F" + fNum;

        // 其它 vk_xxx：去掉前缀，首字母大写
        if (token.StartsWith("vk_", StringComparison.Ordinal))
        {
            var name = token[3..];
            return name.Length == 0 ? token : char.ToUpperInvariant(name[0]) + name[1..];
        }

        // 单个字母统一大写，数字和符号保持原样
        return token.Length == 1 ? token.ToUpperInvariant() : token;
    }
}
