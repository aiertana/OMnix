using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using OMniX.Core.Entities;
using Windows.UI;

namespace OMniX.WinUI.ViewModels.CharacterManagerViewModels;

public partial class ModModel : ObservableObject
{
    public ModModel(Guid modId, string displayName, string dateAdded, string fullPath, bool isEnabled,
        IAsyncRelayCommand<ModModel?> openFolderCommand,
        IAsyncRelayCommand<ModModel?> deleteCommand,
        IAsyncRelayCommand<ModModel?> repairCommand)
    {
        ModId = modId;
        _displayName = displayName;
        _editName = displayName;
        _fullPath = fullPath;
        _isEnabled = isEnabled;
        DateAdded = dateAdded;
        OpenFolderCommand = openFolderCommand;
        DeleteCommand = deleteCommand;
        RepairCommand = repairCommand;
    }

    public static ModModel FromEntry(CharacterSkinEntry entry,
        IAsyncRelayCommand<ModModel?> openFolderCommand,
        IAsyncRelayCommand<ModModel?> deleteCommand,
        IAsyncRelayCommand<ModModel?> repairCommand)
    {
        var mod = entry.Mod;

        var dateAdded = mod.Settings.TryGetSettings(out var settings) && settings.DateAdded.HasValue
            ? settings.DateAdded.Value.ToShortDateString()
            : "未知";

        return new ModModel(entry.Id, mod.GetDisplayName(), dateAdded, mod.FullPath, entry.IsEnabled,
            openFolderCommand, deleteCommand, repairCommand);
    }

    /// <summary>
    ///     二开：Mod 的唯一 Id，用来启用 / 禁用。
    /// </summary>
    public Guid ModId { get; }

    [ObservableProperty] private string _displayName;

    [ObservableProperty] private string _fullPath;

    /// <summary>
    ///     二开：是否正在就地改名（不弹窗，直接在列表里编辑）。
    /// </summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>
    ///     二开：编辑中的名字。
    /// </summary>
    [ObservableProperty] private string _editName;

    /// <summary>
    ///     二开：是否已启用。点左边的圆点就能切换，切换后圆点和名字变绿 / 变灰。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameBrush))]
    [NotifyPropertyChangedFor(nameof(DotToolTip))]
    private bool _isEnabled;

    public string DateAdded { get; }

    public Brush NameBrush => new SolidColorBrush(IsEnabled
        ? Color.FromArgb(255, 34, 197, 94)
        : Color.FromArgb(255, 140, 142, 148));

    public string DotToolTip => IsEnabled ? "已启用，点一下禁用" : "已禁用，点一下启用";

    /// <summary>
    ///     二开：进入就地改名状态。
    /// </summary>
    public void BeginEdit()
    {
        EditName = DisplayName;
        IsEditing = true;
    }

    /// <summary>
    ///     二开：退出就地改名状态。
    /// </summary>
    public void EndEdit() => IsEditing = false;

    /// <summary>
    ///     二开：打开这个 Mod 所在的文件夹
    /// </summary>
    public IAsyncRelayCommand<ModModel?> OpenFolderCommand { get; }

    /// <summary>
    ///     二开：从磁盘删除这个 Mod
    /// </summary>
    public IAsyncRelayCommand<ModModel?> DeleteCommand { get; }

    /// <summary>
    ///     二开：用 Wuwa_Mod_Fixer 修这个 Mod（MOD 目录自动填成它的路径）
    /// </summary>
    public IAsyncRelayCommand<ModModel?> RepairCommand { get; }
}
