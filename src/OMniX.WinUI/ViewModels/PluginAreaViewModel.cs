using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.WinUI.Contracts.ViewModels;

namespace OMniX.WinUI.ViewModels;

/// <summary>
///     插件专区（占位）：先把视觉框架搭好，后续接真实插件加载机制。
/// </summary>
public partial class PluginAreaViewModel : ObservableRecipient, INavigationAware
{
    public string PluginsFolderPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX", "Plugins");

    public ObservableCollection<PluginPlaceholder> Plugins { get; } = new();

    public PluginAreaViewModel()
    {
        Reload();
    }

    public void OnNavigatedTo(object parameter)
    {
    }

    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    ///     目前只做占位展示：这里放的是「规划中的功能」，不是真实已安装的插件。
    /// </summary>
    private void Reload()
    {
        Plugins.Clear();
        Plugins.Add(new PluginPlaceholder
        {
            Name = "批量导出工具",
            Description = "一次性把选中的 Mod 导出成压缩包，方便备份和分享。",
            Status = "开发中",
            Glyph = "\uE8B7"
        });
        Plugins.Add(new PluginPlaceholder
        {
            Name = "Mod 冲突检测",
            Description = "扫描重复 ini 和同名贴图，告诉你哪些 Mod 会互相覆盖。",
            Status = "开发中",
            Glyph = "\uE9CE"
        });
        Plugins.Add(new PluginPlaceholder
        {
            Name = "GameBanana 同步",
            Description = "自动检查已安装 Mod 的更新，一条列表看完全部待更新项。",
            Status = "开发中",
            Glyph = "\uE753"
        });
    }

    [RelayCommand]
    private async Task OpenPluginsFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(PluginsFolderPath);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(PluginsFolderPath);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception)
        {
            // 打开文件夹失败时忽略
        }
    }
}

public class PluginPlaceholder
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = "开发中";
    public string Glyph { get; init; } = "\uE8B7";
}
