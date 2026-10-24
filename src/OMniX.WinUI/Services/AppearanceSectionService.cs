using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Models.Settings;

namespace OMniX.WinUI.Services;

/// <summary>
///     二开：外观分区的读写。分区和 Mod 归属都存在本地设置里，不改动 Mod 文件夹
/// </summary>
public class AppearanceSectionService(ILocalSettingsService localSettingsService)
{
    private readonly ILocalSettingsService _localSettings = localSettingsService;

    private static string SectionKey(string internalName) => $"AppearanceSections_{internalName}";

    private static string MapKey(string internalName) => $"ModSectionMap_{internalName}";

    public async Task<List<AppearanceSection>> GetSectionsAsync(string internalName)
    {
        var settings =
            await _localSettings.ReadOrCreateSettingAsync<AppearanceSectionSettings>(SectionKey(internalName));
        return settings.Sections;
    }

    public async Task<AppearanceSection> AddSectionAsync(string internalName, string name)
    {
        var settings =
            await _localSettings.ReadOrCreateSettingAsync<AppearanceSectionSettings>(SectionKey(internalName));

        var section = new AppearanceSection { Name = name };
        settings.Sections.Add(section);

        await _localSettings.SaveSettingAsync(SectionKey(internalName), settings);
        return section;
    }

    public async Task RenameSectionAsync(string internalName, string sectionId, string newName)
    {
        var settings =
            await _localSettings.ReadOrCreateSettingAsync<AppearanceSectionSettings>(SectionKey(internalName));

        var section = settings.Sections.FirstOrDefault(x => x.Id == sectionId);
        if (section is null)
            return;

        section.Name = newName;
        await _localSettings.SaveSettingAsync(SectionKey(internalName), settings);
    }

    public async Task DeleteSectionAsync(string internalName, string sectionId)
    {
        var settings =
            await _localSettings.ReadOrCreateSettingAsync<AppearanceSectionSettings>(SectionKey(internalName));

        settings.Sections.RemoveAll(x => x.Id == sectionId);
        await _localSettings.SaveSettingAsync(SectionKey(internalName), settings);

        // 分区没了，属于它的 Mod 回到“全部”
        var map = await _localSettings.ReadOrCreateSettingAsync<ModSectionMapping>(MapKey(internalName));
        var keysToRemove = map.ModToSection.Where(x => x.Value == sectionId).Select(x => x.Key).ToArray();

        foreach (var key in keysToRemove)
            map.ModToSection.Remove(key);

        await _localSettings.SaveSettingAsync(MapKey(internalName), map);
    }

    public async Task<Dictionary<Guid, string>> GetModSectionsAsync(string internalName)
    {
        var map = await _localSettings.ReadOrCreateSettingAsync<ModSectionMapping>(MapKey(internalName));

        var result = new Dictionary<Guid, string>();
        foreach (var (modId, sectionId) in map.ModToSection)
        {
            if (Guid.TryParse(modId, out var guid))
                result[guid] = sectionId;
        }

        return result;
    }

    public async Task SetModSectionAsync(string internalName, Guid modId, string? sectionId)
    {
        var map = await _localSettings.ReadOrCreateSettingAsync<ModSectionMapping>(MapKey(internalName));

        var key = modId.ToString();
        if (sectionId is null)
            map.ModToSection.Remove(key);
        else
            map.ModToSection[key] = sectionId;

        await _localSettings.SaveSettingAsync(MapKey(internalName), map);
    }
}
