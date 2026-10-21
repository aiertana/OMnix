namespace OMniX.WinUI.Helpers;

/// <summary>
///     署名信息。
///
///     这些东西是跟着程序集一起编译进去的：就算有人把界面上的文字改掉，
///     exe 的文件属性页和这段常量里仍然留着作者 —— 改界面改不掉 dll 里的字符串。
/// </summary>
public static class AppIdentity
{
    /// <summary>
    ///     作者。
    /// </summary>
    public const string Author = "是湫";

    /// <summary>
    ///     作者的 GitHub 账号。
    /// </summary>
    public const string AuthorAccount = "aiertana";

    /// <summary>
    ///     仓库地址。
    /// </summary>
    public const string Repository = "https://github.com/aiertana/OMnix";

    /// <summary>
    ///     防伪标记。程序集里嵌的指纹，"OMNX" 的十六进制。
    ///     别人就算改了界面文字，这段字符串还在 dll 里，拿 strings / 反编译都能翻出来。
    /// </summary>
    public const string Signature =
        "OMniX|Author:是湫|Account:aiertana|Repo:github.com/aiertana/OMnix|Fingerprint:0x4F4D4E58";

    /// <summary>
    ///     启动日志里打出来的那一行。
    /// </summary>
    public static string Stamp => $"OMniX by {Author}  ·  {Repository}  ·  {Signature}";
}
