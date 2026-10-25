namespace OMniX.WinUI.Models.Settings;

using OMniX.Core.Contracts.Services;

/// <summary>
///     二开：外观分区。用来把一个角色下的 Mod 按“套”分组管理
/// </summary>
public class AppearanceSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Appear_DefaultName", "新分区");
}

/// <summary>
///     二开：某个角色下的全部分区
/// </summary>
public class AppearanceSectionSettings
{
    public List<AppearanceSection> Sections { get; set; } = new();
}

/// <summary>
///     二开：Mod 属于哪个分区（ModId → SectionId）
/// </summary>
public class ModSectionMapping
{
    public Dictionary<string, string> ModToSection { get; set; } = new();
}

/// <summary>
///     二开：界面上下拉框里的一项，Id 为空表示“全部”
/// </summary>
public class SectionItemVm
{
    public static SectionItemVm All => new(string.Empty, App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/Appear_All", "全部"));

    public SectionItemVm(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public override string ToString() => Name;
}
