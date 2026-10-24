namespace OMniX.Core.Helpers;

public static class Constants
{
    public static readonly IReadOnlyCollection<string> SupportedImageExtensions = new[]
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".svg", ".webp", ".bitmap" };

    public static readonly IReadOnlyList<string> SupportedArchiveTypes = [".zip", ".rar", ".7z"];

    /// <summary>
    ///     二开：Mod 配置文件名。原来沿用的是 JASM 的命名，现在统一改成 OMniX。
    /// </summary>
    public static readonly string ModConfigFileName = ".OMniX_ModConfig.json";

    /// <summary>
    ///     二开：旧版（JASM 时期）的配置文件名。
    ///     磁盘上还留着大量这种文件，读取时必须认，否则用户的 Mod 配置会全部丢失。
    /// </summary>
    public static readonly string LegacyModConfigFileName = ".JASM_ModConfig.json";

    /// <summary>
    ///     二开：OMniX 生成的文件统一前缀。
    /// </summary>
    public const string OmniXFilePrefix = ".OMniX_";

    /// <summary>
    ///     二开：旧版（JASM 时期）的文件前缀。
    /// </summary>
    public const string LegacyFilePrefix = ".JASM_";

    /// <summary>
    ///     判断文件名是否为 OMniX 生成的文件（新旧命名都算）。
    /// </summary>
    public static bool IsOmniXFile(string name)
    {
        return name.StartsWith(OmniXFilePrefix, StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(LegacyFilePrefix, StringComparison.OrdinalIgnoreCase);
    }
    public static readonly string ShaderFixesFolderName = "ShaderFixes";
    public static readonly string[] ScriptIniNames = ["Script.ini", "merged.ini", "mod.ini"];
    public static readonly string UserIniFileName = "d3dx_user.ini";
}