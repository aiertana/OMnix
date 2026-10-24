namespace OMniX.Core.Entities.Mods.FileModels;

public class IniKeySwapSection
{
    public Dictionary<string, string> IniKeyValues { get; } = new();

    public const string KeySwapIniSection = "KeySwap";
    public string SectionKey { get; set; } = KeySwapIniSection;

    public const string ForwardIniKey = "key";

    public string? ForwardHotkey
    {
        get => IniKeyValues.TryGetValue(ForwardIniKey, out var value) ? value : null;
        set => IniKeyValues[ForwardIniKey] = value ?? string.Empty;
    }

    public const string BackwardIniKey = "back";

    public string? BackwardHotkey
    {
        get => IniKeyValues.TryGetValue(BackwardIniKey, out var value) ? value : null;
        set => IniKeyValues[BackwardIniKey] = value ?? string.Empty;
    }

    public const string TypeIniKey = "type";

    public string? Type
    {
        get => IniKeyValues.TryGetValue(TypeIniKey, out var value) ? value : null;
        set => IniKeyValues[TypeIniKey] = value ?? string.Empty;
    }

    public const string SwapVarIniKey = "$swapvar";
    public string[]? SwapVar { get; set; }

    /// <summary>
    ///     二开：段内的第一条注释，很多 Mod 会用注释说明这个按键是干什么的
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     二开：这个按键实际控制的变量名（$switching / $swapvar / $armsbrace 之类）
    /// </summary>
    public string? VariableName { get; set; }

    public bool AnyValues()
    {
        return ForwardHotkey is not null || BackwardHotkey is not null;
    }
}