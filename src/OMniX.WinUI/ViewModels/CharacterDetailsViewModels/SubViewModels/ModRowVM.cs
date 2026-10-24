using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Entities;
using OMniX.Core.Entities.Mods.Contract;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Services.Notifications;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public partial class ModRowVM : ObservableObject
{
    public Guid Id { get; init; }
    [ObservableProperty] private bool _isSelected;

    private bool _isEnabled;

    /// <summary>
    ///     二开：UI 线程队列，由 ModGridVM 在 UI 线程创建时注入
    /// </summary>
    public DispatcherQueue? UiDispatcher { get; set; }

    /// <summary>
    ///     二开：兜底。这个 VM 上任何属性的变更通知都强制在 UI 线程发出。
    ///     后台线程（文件监听 / 刷新循环）改动属性时，绑定控件会在错误的线程上收到通知，
    ///     WinUI 会直接 stow 掉异常把进程杀掉（0xc000027b），所以这里一律转发。
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        var queue = UiDispatcher;

        if (queue is not null && !queue.HasThreadAccess)
        {
            var name = e.PropertyName;
            queue.TryEnqueue(() => base.OnPropertyChanged(new PropertyChangedEventArgs(name)));
            return;
        }

        base.OnPropertyChanged(e);
    }

    /// <summary>
    ///     启用状态。文件监听会在后台线程刷新它，
    ///     而界面上的 CheckBox 绑定了它，直接改会跨线程崩（0x8001010E / 0xc000027b），
    ///     所以这里统一转发回 UI 线程再改。
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            var queue = UiDispatcher;
            if (queue is not null && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(() =>
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                });
                return;
            }

            SetProperty(ref _isEnabled, value);
        }
    }

    [ObservableProperty] private string _displayName = string.Empty;

    [ObservableProperty] private string _folderName = string.Empty;

    [ObservableProperty] private string _absFolderPath = string.Empty;

    [ObservableProperty] private DateTime _dateAdded;

    [ObservableProperty] private string _dateAddedFormated = string.Empty;

    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string[] _presets = [];
    [ObservableProperty] private string _inPresets = string.Empty;

    [ObservableProperty] private string _description = string.Empty;


    // 二开：改用线程安全集合
    public UiObservableCollection<ModRowVM_ModNotificationVM> ModNotifications { get; } = new();


    public ModRowVM(CharacterSkinEntry characterSkinEntry, ModSettings? modSettings, IEnumerable<string> presetNames,
        IEnumerable<ModNotification> modNotifications)
    {
        Id = characterSkinEntry.Mod.Id;
        UpdateModel(characterSkinEntry, modSettings, presetNames, modNotifications);
    }

    public void UpdateModel(CharacterSkinEntry characterSkinEntry, ModSettings? modSettings,
        IEnumerable<string> presetNames, IEnumerable<ModNotification> modNotifications)
    {
        IsEnabled = characterSkinEntry.IsEnabled;
        DisplayName = characterSkinEntry.Mod.GetDisplayName();
        FolderName = characterSkinEntry.Mod.Name;
        AbsFolderPath = characterSkinEntry.Mod.FullPath;
        DateAdded = modSettings?.DateAdded ?? DateTime.MinValue;
        DateAddedFormated = DateAdded.ToString("d");
        Author = modSettings?.Author ?? string.Empty;
        Description = modSettings?.Description ?? string.Empty;
        Presets = presetNames.ToArray();
        InPresets = string.Join(',', Presets);
        ModNotifications.Clear();
        ModNotifications.AddRange(modNotifications.Select(m => new ModRowVM_ModNotificationVM(m)));


        SearchableText = $"{DisplayName}{FolderName}{Author}{string.Join(null, Presets)}{DateAdded:d}{Description}";
    }

    public void TriggerPropertyChanged(string? propertyName) => OnPropertyChanged(propertyName ?? string.Empty);

    public string SearchableText { get; private set; } = string.Empty;

    public required IAsyncRelayCommand ToggleEnabledCommand { get; init; }
    public required IAsyncRelayCommand UpdateModSettingsCommand { get; set; }
}

/// Referencing nested types does not work from xaml
public partial class ModRowVM_ModNotificationVM(ModNotification modNotification) : ObservableObject
{
    public DateTime Time { get; init; } = modNotification.Time;
    public Guid Id { get; init; } = modNotification.Id;
    public Guid ModId { get; init; } = modNotification.ModId;
    public AttentionType AttentionType { get; init; } = modNotification.AttentionType;
}

public sealed class ModGridSortingMethod : SortingMethod<ModRowVM>
{
    public ModGridSortingMethod(Sorter<ModRowVM> sortingMethodType, ModRowVM? firstItem = null,
        ICollection<ModRowVM>? lastItems = null) : base(sortingMethodType, firstItem, lastItems)
    {
    }

    public static readonly ModRowSorter[] AllSorters =
    [
        ModRowSorter.IsEnabledSorter,
        ModRowSorter.DatedAddedSorter,
        ModRowSorter.DisplayNameSorter,
        ModRowSorter.ModFolderSorter,
        ModRowSorter.AuthSorter,
        ModRowSorter.PresetsSorter
    ];
}

public sealed class ModRowSorter : Sorter<ModRowVM>
{
    private ModRowSorter(string sortingMethodType, SortFunc firstSortFunc, AdditionalSortFunc? secondSortFunc = null,
        AdditionalSortFunc? thirdSortFunc = null) : base(sortingMethodType, firstSortFunc, secondSortFunc,
        thirdSortFunc)
    {
    }

    public static readonly string IsEnabledName = nameof(ModRowVM.IsEnabled);

    public static ModRowSorter IsEnabledSorter { get; } = new(IsEnabledName,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(x => x.IsEnabled).ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName)
                : mod.OrderBy(x => x.IsEnabled).ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName)
    );


    public static readonly string DateAddedName = nameof(ModRowVM.DateAdded);

    public static ModRowSorter DatedAddedSorter { get; } = new(DateAddedName,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(x => (x.DateAdded)).ThenBy(x => x.DisplayName)
                : mod.OrderBy(x => (x.DateAdded)).ThenBy(x => x.DisplayName));


    public static readonly string DisplayName = nameof(ModRowVM.DisplayName);

    public static ModRowSorter DisplayNameSorter { get; } = CreateStringSorter(DisplayName, vm => vm.DisplayName);

    public static readonly string ModFolderName = nameof(ModRowVM.FolderName);

    public static ModRowSorter ModFolderSorter { get; } = CreateStringSorter(ModFolderName, vm => vm.FolderName);


    public static readonly string AuthorName = nameof(ModRowVM.Author);

    public static ModRowSorter AuthSorter { get; } = CreateStringSorter(AuthorName, vm => vm.Author);


    public static readonly string PresetsName = nameof(ModRowVM.Presets);

    public static ModRowSorter PresetsSorter { get; } = new(PresetsName,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(x => (x.Presets.Length))
                    .ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName)
                : mod.OrderBy(x => (x.Presets.Length))
                    .ThenByDescending(x => x.DateAdded)
                    .ThenBy(x => x.DisplayName));


    private static ModRowSorter CreateStringSorter(string name, Func<ModRowVM, string> predicate) => new(name,
        (mod, isDescending) =>
            isDescending
                ? mod.OrderByDescending(predicate).ThenByDescending(x => x.DateAdded)
                : mod.OrderBy(predicate).ThenByDescending(x => x.DateAdded));
}