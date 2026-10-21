using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.Helpers;

public static class LocalSettingsServiceExtensions
{
    public static Task<CharacterDetailsSettings> ReadCharacterDetailsSettingsAsync(
        this ILocalSettingsService localSettingsService,
        SettingScope scope = SettingScope.App) =>
        localSettingsService.ReadOrCreateSettingAsync<CharacterDetailsSettings>(CharacterDetailsSettings.Key,
            scope);

    public static Task SaveCharacterDetailsSettingsAsync(this ILocalSettingsService localSettingsService,
        CharacterDetailsSettings settings, SettingScope scope = SettingScope.App) =>
        localSettingsService.SaveSettingAsync(CharacterDetailsSettings.Key, settings, scope);
}