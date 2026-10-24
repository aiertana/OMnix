using OMniX.WinUI.Services;

namespace OMniX.WinUI.Contracts.Services;

public interface ILocalSettingsService
{
    /// <summary>
    /// OMniX/ folder
    /// </summary>
    public string GameScopedSettingsLocation { get; }

    /// <summary>
    /// OMniX/ApplicationData_Genshin
    /// </summary>
    public string ApplicationDataFolder { get; }

    public void SetApplicationDataFolderName(string folderName);

    public Task<T?> ReadSettingAsync<T>(string key, SettingScope settingScope = SettingScope.Game);

    public Task<T> ReadOrCreateSettingAsync<T>(string key, SettingScope settingScope = SettingScope.Game)
        where T : new();

    public Task SaveSettingAsync<T>(string key, T value, SettingScope settingScope = SettingScope.Game)
        where T : notnull;

    public T? ReadSetting<T>(string key, SettingScope settingScope = SettingScope.Game);
}