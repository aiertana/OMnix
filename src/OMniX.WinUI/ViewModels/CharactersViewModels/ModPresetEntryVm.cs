using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OMniX.WinUI.ViewModels.CharactersViewModels;

public partial class ModPresetEntryVm : ObservableObject
{
    /// <summary>
    ///     二开：预设名的默认颜色（跟随主题的正文色）。
    /// </summary>
    private static readonly Brush NormalTextBrush =
        Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var brush) && brush is Brush b
            ? b
            : new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

    private bool _isActive;
    private bool _isPinned;

    public ModPresetEntryVm(string name, bool isActive, bool isPinned,
        IAsyncRelayCommand applyPresetCommand,
        IAsyncRelayCommand pinCommand,
        IAsyncRelayCommand saveCommand,
        IAsyncRelayCommand deleteCommand,
        IAsyncRelayCommand renameCommand)
    {
        Name = name;
        _isActive = isActive;
        _isPinned = isPinned;

        ApplyPresetCommand = applyPresetCommand;
        PinCommand = pinCommand;
        SaveCommand = saveCommand;
        DeleteCommand = deleteCommand;
        RenameCommand = renameCommand;
    }

    public string Name { get; }

    /// <summary>
    ///     二开：这个预设是不是「当前正在用的」。是的话名字显示成绿色。
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
                OnPropertyChanged(nameof(NameBrush));
        }
    }

    /// <summary>
    ///     二开：有没有被置顶。置顶后「针」是实心的深灰色，再点一下取消。
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (!SetProperty(ref _isPinned, value))
                return;

            OnPropertyChanged(nameof(PinBrush));
            OnPropertyChanged(nameof(PinGlyph));
            OnPropertyChanged(nameof(PinToolTip));
        }
    }

    /// <summary>
    ///     二开：正在用的预设用绿色，其它用正常文字色。
    /// </summary>
    public Brush NameBrush => IsActive
        ? new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
        : NormalTextBrush;

    /// <summary>
    ///     二开：置顶过的「针」是深灰色，没置顶的是浅灰。
    /// </summary>
    public Brush PinBrush => IsPinned
        ? new SolidColorBrush(Color.FromArgb(255, 72, 74, 80))
        : new SolidColorBrush(Color.FromArgb(255, 154, 158, 166));

    /// <summary>
    ///     二开：置顶后换成「实心针」，一眼能看出是钉住的还是没钉。
    /// </summary>
    public string PinGlyph => IsPinned ? "\uE77A" : "\uE840";

    public string PinToolTip => IsPinned ? "取消置顶" : "置顶这个预设";

    public IAsyncRelayCommand ApplyPresetCommand { get; }

    /// <summary>
    ///     二开：置顶。
    /// </summary>
    public IAsyncRelayCommand PinCommand { get; }

    /// <summary>
    ///     二开：用当前已启用的 Mod 组合覆盖这个预设。
    /// </summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>
    ///     二开：删除预设。
    /// </summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>
    ///     二开：给预设改名。
    /// </summary>
    public IAsyncRelayCommand RenameCommand { get; }
}
