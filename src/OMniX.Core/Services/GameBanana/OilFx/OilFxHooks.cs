namespace OMniX.Core.Services.GameBanana.OilFx;

/// <summary>
///     OilFX 角色挂钩段（源：GameBanana mod 733773 customoilfx v12 by Febby315）。
///     每个段是一个 3DMigoto override 块：VB0（顶点缓冲，最精准）或 VS（着色器，
///     常按 @上/@下 半身拆分，且一个 hash 常覆盖多个角色）。
///     段名里的角色与 OMniX 角色库的匹配在 OilFxService 里做 normalize 比较。
/// </summary>
public sealed record OilFxHookSection
{
    /// <summary>VS = ShaderOverride，VB0 = TextureOverride（带 match_index）。</summary>
    public required string Kind { get; init; }

    /// <summary>原始段名（含 @标记 与 + 组合，如 "VS-千咲+弗洛洛@上+赞妮@上"）。</summary>
    public required string Title { get; init; }

    public required string Hash { get; init; }

    public int? MatchIndexCount { get; init; }

    public int? MatchFirstIndex { get; init; }

    /// <summary>段内出现的全部角色名（@标记已剥离）。懒计算——record 的对象
    /// 初始化器先跑构造函数再赋值属性，构造函数里读 Title 必然是 null。</summary>
    public IReadOnlyList<string> Characters => _characters ??= (
        Title is null
            ? []
            : Title[(Title.IndexOf('-') + 1)..]
                .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => OilFxName.Normalize(c))
                .Where(c => c.Length > 0)
                .ToArray());
    private IReadOnlyList<string>? _characters;
}

public static class OilFxName
{
    /// <summary>和 3DMigoto ini 一样宽松地比名字：去空白、大小写、@标记、*。</summary>
    public static string Normalize(string name)
    {
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            name = name[..at];
        }

        return name.Replace("*", "").Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
    }
}

public static class OilFxHooks
{
    /// <summary>GameBanana mod id（customoilfx 源页面）。</summary>
    public const int ModId = 733773;

    /// <summary>原作者的冲突说明（这些角色的 @发/部位 mod 会和全局挂钩抢渲染）。</summary>
    public const string ConflictTip =
        "锁瞑@发+心月狐@发+景燃@发+穗穗@发+玄翎@发+洛瑟菈@发+达尼娅@发+绯雪@发+西格莉卡@发+陆赫斯@下+爱弥斯@发+莫宁@发+琳奈@发+千咲@发+女漂@发";

    /// <summary>源 mod 页面（窗口里直接打开）。</summary>
    public const string SourceUrl = "https://gamebanana.com/mods/733773";

    /// <summary>全部已知挂钩段（含原作者注释里的备选段，如锁瞑 VB0、绯雪@下）。</summary>
    public static readonly IReadOnlyList<OilFxHookSection> Sections =
    [
        new() { Kind = "VB0", Title = "VB0-绯雪", Hash = "91cff509", MatchIndexCount = 56400, MatchFirstIndex = 289329 },
        new() { Kind = "VB0", Title = "VB0-锁瞑", Hash = "79d7f632" },
        new() { Kind = "VS", Title = "VS-锁瞑", Hash = "a4a939baec7ca47d" },
        new() { Kind = "VS", Title = "VS-绯雪@下", Hash = "3e7bb648e306c671" },
        new() { Kind = "VS", Title = "VS-玄翎+达尼娅+绯雪@上+琳奈@上", Hash = "1f324d354402418c" },
        new() { Kind = "VS", Title = "VS-西格莉卡", Hash = "4db910d810ca1ef3" },
        new() { Kind = "VS", Title = "VS-千咲+弗洛洛@上+赞妮@上+菲比@上+科莱塔+椿@上+今汐+鉴心@上+维里奈@上+灯灯@上+桃祈@上+白芷+炽霞@上", Hash = "b3c7ad652f7a1c40" },
        new() { Kind = "VS", Title = "VS-尤诺+夏空@下", Hash = "eab5f4dfc9061589" },
        new() { Kind = "VS", Title = "VS-奥古斯塔+露帕+洛可可+卜灵@上+单瑾@上", Hash = "e00893f9d07dc9b0" },
        new() { Kind = "VS", Title = "VS-卡提希娅+坎特蕾拉+椿@下+守岸人+折枝@上+吟霖@上+泱泱@下", Hash = "02f5c4b333a78fa0" },
        new() { Kind = "VS", Title = "VS-心月狐@上", Hash = "7063a29e3f10134e" },
        new() { Kind = "VS", Title = "VS-心月狐@下+女漂@上", Hash = "234475e8531a74f4" },
        new() { Kind = "VS", Title = "VS-清霄@上", Hash = "a6e22aca8db3164a" },
        new() { Kind = "VS", Title = "VS-清霄@下+女漂@下", Hash = "6ffcf365937e50bd" },
        new() { Kind = "VS", Title = "VS-穗穗@上", Hash = "2a81ef23d64f25ca" },
        new() { Kind = "VS", Title = "VS-穗穗@下", Hash = "b3323babc68e065f" },
        new() { Kind = "VS", Title = "VS-洛瑟菈@上+莫宁@上", Hash = "193ccef848a6738a" },
        new() { Kind = "VS", Title = "VS-洛瑟菈@下", Hash = "a11b5261969f9555" },
        new() { Kind = "VS", Title = "VS-露西@上", Hash = "f9e93739b430eb2e" },
        new() { Kind = "VS", Title = "VS-露西@下", Hash = "6dd634933d8f0837" },
        new() { Kind = "VS", Title = "VS-爱弥斯@上", Hash = "bbf85dd9ade8ab7f" },
        new() { Kind = "VS", Title = "VS-爱弥斯@下", Hash = "b3323babc68e065f" },
        new() { Kind = "VS", Title = "VS-嘉贝莉娜@上", Hash = "10e75c7e82ef2441" },
        new() { Kind = "VS", Title = "VS-嘉贝莉娜@下", Hash = "a91f30ca4524d995" },
        new() { Kind = "VS", Title = "VS-长离@上", Hash = "6e5d4bc00bc160ee" },
        new() { Kind = "VS", Title = "VS-长离@下", Hash = "3b36d7d284d20522" },
        new() { Kind = "VS", Title = "VS-散华@上", Hash = "0b25e6cbdb8cc86f" },
        new() { Kind = "VS", Title = "VS-散华@下", Hash = "b70cd6cd952fe5bb" },
        new() { Kind = "VS", Title = "VS-夏空@上", Hash = "1fc4177ef7899caa" },
        new() { Kind = "VS", Title = "VS-釉瑚@上", Hash = "1786f9b7c30aa24c" },
        new() { Kind = "VS", Title = "VS-泱泱@上", Hash = "3ba352c448cd29cc" },
        new() { Kind = "VS", Title = "VS-莫宁@下", Hash = "1e00731e53cdcb38" },
        new() { Kind = "VS", Title = "VS-琳奈@下", Hash = "c30da0cb86079064" },
        new() { Kind = "VS", Title = "VS-*弗洛洛@下+折枝@下+长离@下+桃祈@下", Hash = "3b36d7d284d20522" },
        new() { Kind = "VS", Title = "VS-赞妮@下", Hash = "3dc8de31d35a0d54" },
        new() { Kind = "VS", Title = "VS-菲比@下", Hash = "88313b06acfd2752" },
        new() { Kind = "VS", Title = "VS-吟霖@下+鉴心@下+维里奈@下+安可@下+卜灵@下+灯灯@下+釉瑚@下+单瑾@下+炽霞@下", Hash = "15a2c90dd35b8c07" },
    ];

    /// <summary>段是否适用于给定角色（角色名 normalize 后任一命中）。</summary>
    public static bool Matches(OilFxHookSection section, string normalizedCharacter) =>
        section.Characters.Contains(normalizedCharacter);
}
