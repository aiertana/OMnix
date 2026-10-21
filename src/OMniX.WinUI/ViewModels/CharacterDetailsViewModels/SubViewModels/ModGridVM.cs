using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.UI.Controls;
using CommunityToolkitWrapper;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.Helpers;
using OMniX.Core.Services.ModPresetService;
using OMniX.Core.Services.ModPresetService.Models;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Views;
using Serilog;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public partial class ModGridVM(
    ISkinManagerService skinManagerService,
    CharacterSkinService characterSkinService,
    NotificationManager notificationService,
    ModPresetService presetService,
    ILocalSettingsService localSettingsService,
    ModNotificationManager modNotificationManager,
    ModSettingsService modSettingsService,
    IWindowManagerService windowManagerService)
    : ObservableRecipient, IRecipient<ModChangedMessage>
{
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly CharacterSkinService _characterSkinService = characterSkinService;
    private readonly NotificationManager _notificationService = notificationService;
    private readonly ModPresetService _presetService = presetService;
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly ModNotificationManager _modNotificationManager = modNotificationManager;
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly IWindowManagerService _windowManagerService = windowManagerService;
    private readonly ILogger _logger = Log.ForContext<ModGridVM>();

    private DispatcherQueue _dispatcherQueue = null!;

    /// <summary>
    ///     二开：兜底。这个 VM 的属性（IsBusy 等）绑定在界面上，
    ///     后台线程改动时通知会跨线程，WinUI 会直接把进程杀掉（0xc000027b），
    ///     所以这里统一把通知转发回 UI 线程。
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        var queue = _dispatcherQueue;

        if (queue is not null && !queue.HasThreadAccess)
        {
            var name = e.PropertyName;
            queue.TryEnqueue(() => base.OnPropertyChanged(new PropertyChangedEventArgs(name)));
            return;
        }

        base.OnPropertyChanged(e);
    }

    private CancellationToken _navigationCt = default;
    private ICharacterModList _modList = null!;
    private ModDetailsPageContext _context = null!;
    private readonly AsyncLock _modRefreshLock = new();
    public BusySetter BusySetter { get; set; }
    public bool IsDescendingSort { get; private set; } = true;
    public ModGridSortingMethod CurrentSortingMethod { get; private set; } = new(ModRowSorter.IsEnabledSorter);


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy = true;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty] private DataGridSelectionMode _gridSelectionMode = DataGridSelectionMode.Extended;
    private bool SingleSelect => GridSelectionMode == DataGridSelectionMode.Single;

    [ObservableProperty] private bool _isModFolderNameColumnVisible;

    [ObservableProperty] private bool _showMultipleModsActiveWarning;

    public bool IsInitialized { get; private set; }

    private List<CharacterSkinEntry> _modsBackend = [];
    public List<CharacterSkinEntry> GetModsBackend() => [.. _modsBackend];
    private Dictionary<Guid, ModPreset[]> _modToPresetMapping = [];
    private readonly List<ModRowVM> _gridModsBackend = [];
    public List<ModRowVM> GetGridModsBackend() => [.. _gridModsBackend];
    // 二开：改用线程安全集合，后台线程增删会自动切回 UI 线程
    public UiObservableCollection<ModRowVM> GridMods { get; } = [];
    public UiObservableCollection<ModRowVM> SelectedMods { get; } = [];

    public bool IsSingleModSelected => SelectedMods.Count == 1;

    public bool ModdableObjectHasAnyMods => _modsBackend.Count != 0;

    public int TrackedMods => _modsBackend.Count;

    private readonly Channel<QueueRefresh> _modRefreshChannel = Channel.CreateBounded<QueueRefresh>(new BoundedChannelOptions(1)
    {
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public readonly struct QueueRefresh(TimeSpan? minWaitTime = null)
    {
        public TimeSpan? MinWaitTime { get; } = minWaitTime;
    };

    private async Task ModRefreshLoopAsync()
    {
        // Runs on the UI thread
        await foreach (var loadModMessage in _modRefreshChannel.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (_navigationCt.IsCancellationRequested)
                break;
            try
            {
                await ReloadAllModsAsync(loadModMessage.MinWaitTime);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _notificationService.ShowNotification("Error refreshing mods", e.Message, null);
            }
        }
    }

    public async Task InitializeAsync(ModDetailsPageContext context, CancellationToken navigationCt = default)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // 二开：把 UI 队列交给界面集合，之后谁在后台线程增删都会被自动切回 UI 线程
        GridMods.DispatcherQueue = _dispatcherQueue;
        SelectedMods.DispatcherQueue = _dispatcherQueue;

        _navigationCt = navigationCt;
        _context = context;
        _modList = _skinManagerService.GetCharacterModList(_context.ShownModObject);
        var settings = await _localSettingsService.ReadCharacterDetailsSettingsAsync();
        GridSelectionMode = settings.SingleSelect ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended;
        IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;
        await InitModsAsync();
        _modList.ModsChanged += ModListOnModsChanged;
        _modNotificationManager.OnModNotification += _modNotificationManager_OnModNotification;
        Messenger.RegisterAll(this);
        _ = _dispatcherQueue.EnqueueAsync(ModRefreshLoopAsync);
        IsBusy = false;
        IsInitialized = true;
        OnInitialized?.Invoke(this, EventArgs.Empty);
    }

    private void _modNotificationManager_OnModNotification(object? sender, ModNotificationManager.ModNotificationEvent e) => QueueModRefresh();

    public void OnNavigateFrom()
    {
        _modRefreshChannel.Writer.TryComplete();
        Messenger.UnregisterAll(this);
        _modList.ModsChanged -= ModListOnModsChanged;
        _modNotificationManager.OnModNotification -= _modNotificationManager_OnModNotification;
        try
        {
            _modRefreshLock.Dispose();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Failed to dispose mod refresh lock");
        }
    }

    private async Task InitModsAsync()
    {
        GridMods.Clear();
        _gridModsBackend.Clear();
        SelectedMods.Clear();

        await Task.Run(async () =>
        {
            var refreshResult = await _skinManagerService
                .RefreshModsAsync(_context.ShownModObject.InternalName, ct: _navigationCt)
                .ConfigureAwait(false);

            await LoadModsAsync().ConfigureAwait(false);

            if (refreshResult.ModsDuplicate.Any())
            {
                var message = $"Duplicate mods were detected in {_context.ModObjectDisplayName}'s mod folder.\n";

                message = refreshResult.ModsDuplicate.Aggregate(message,
                    (current, duplicateMod) =>
                        current +
                        $"Mod: '{duplicateMod.ExistingFolderName}' was renamed to '{duplicateMod.RenamedFolderName}' to avoid conflicts.\n");

                _notificationService.ShowNotification("Duplicate Mods Detected",
                    message,
                    TimeSpan.FromSeconds(10));
            }
        }, _navigationCt);


        GridMods.AddRange(_gridModsBackend.Where(IsInActiveSection));
        SetModSorting(CurrentSortingMethod.SortingMethodType, IsDescendingSort);
    }

    public bool QueueModRefresh(TimeSpan? minWaitTime = null) => _modRefreshChannel.Writer.TryWrite(new QueueRefresh(minWaitTime));

    public async Task ReloadAllModsAsync(TimeSpan? minimumWaitTime = null)
    {
        using var __ = await ModRefreshLockAsync();
        using var _ = BusySetter.StartHardBusy();
        var waitTime = minimumWaitTime is not null ? Task.Delay(minimumWaitTime.Value, _navigationCt) : Task.CompletedTask;

        Guid? selectedModId = SelectedMods.Count == 1 ? SelectedMods.First().Id : null;

        // For now just reuse the grid init
        await InitModsAsync();

        // Make it take at least the minimum time to show the user that something is happening
        await waitTime;

        if (selectedModId.HasValue && GridMods.Any(m => m.Id == selectedModId))
        {
            SetSelectedMod(selectedModId.Value);
        }
    }

    public async Task OnChangeSkinAsync(ModDetailsPageContext context)
    {
        using var __ = await ModRefreshLockAsync();
        _context = context;
        _gridModsBackend.Clear();
        GridMods.Clear();
        SelectedMods.Clear();

        await Task.Run(LoadModsAsync, _navigationCt);

        GridMods.AddRange(_gridModsBackend.Where(IsInActiveSection));
        SetModSorting(CurrentSortingMethod.SortingMethodType, IsDescendingSort);
    }

    private bool RefreshMultipleModsActiveWarning() =>
        ShowMultipleModsActiveWarning = !_context.ShownModObject.IsMultiMod && _modsBackend.Count(m => m.IsEnabled) > 1;

    private async Task LoadModsAsync()
    {
        if (_context.IsCharacter && _context.Skins.Count > 1)
        {
            _modsBackend = await _characterSkinService
                .GetCharacterSkinEntriesForSkinAsync(_context.SelectedSkin, useSettingsCache: true,
                    cancellationToken: _navigationCt)
                .ToListAsync().ConfigureAwait(false);
        }
        else
        {
            _modsBackend = _modList.Mods.ToList();
        }

        _modToPresetMapping = await _presetService
            .FindPresetsForModsAsync(_modsBackend.Select(m => m.Id))
            .ConfigureAwait(false);

        foreach (var x in _modsBackend)
        {
            _navigationCt.ThrowIfCancellationRequested();

            var modVm = await CreateModRowVM(x, _navigationCt).ConfigureAwait(false);
            _gridModsBackend.Add(modVm);
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            RefreshMultipleModsActiveWarning();
            OnModsReloaded?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task<ModRowVM> CreateModRowVM(CharacterSkinEntry characterSkinEntry,
        CancellationToken cancellationToken = default)
    {
        var skinModSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(true, cancellationToken)
            .ConfigureAwait(false);

        var presetNames = _modToPresetMapping.TryGetValue(characterSkinEntry.Id, out var presets)
            ? presets.Select(p => p.Name)
            : [];

        var modNotifications = await _modNotificationManager.GetNotificationsForModAsync(characterSkinEntry.Id);

        var modRowVm = new ModRowVM(characterSkinEntry, skinModSettings, presetNames, modNotifications)
        {
            ToggleEnabledCommand = new AsyncRelayCommand<ModRowVM>(ToggleModAsync),
            UpdateModSettingsCommand = new AsyncRelayCommand<UpdateModSettingsArgument>(UpdateModSettingsAsync),
            // 二开：把 UI 队列交给行 VM，后台线程刷新启用状态时会转发回来
            UiDispatcher = _dispatcherQueue
        };

        // 二开：通知列表也交给 UI 队列
        modRowVm.ModNotifications.DispatcherQueue = _dispatcherQueue;

        return modRowVm;
    }

    private async Task UpdateModVmAsync(CharacterSkinEntry characterSkinEntry, bool useSettingsCache = true,
        CancellationToken cancellationToken = default)
    {
        var existingMod = _gridModsBackend.FirstOrDefault(mod => mod.Id == characterSkinEntry.Id);
        if (existingMod is null)
            throw new InvalidOperationException("Mod not found in grid mods");

        var skinModSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(useSettingsCache, cancellationToken);

        var presetNames = _modToPresetMapping.TryGetValue(characterSkinEntry.Id, out var presets)
            ? presets.Select(p => p.Name)
            : [];

        var modNotifications = await _modNotificationManager.GetNotificationsForModAsync(characterSkinEntry.Id);


        // 二开：UpdateModel 会改 ModNotifications 这些绑到界面的集合，
        // 刷新循环跑在后台线程，直接改会让界面集合更新跨线程 → 崩（InvalidOperationException）
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() =>
                existingMod.UpdateModel(characterSkinEntry, skinModSettings, presetNames, modNotifications));
            return;
        }

        existingMod.UpdateModel(characterSkinEntry, skinModSettings, presetNames, modNotifications);
    }

    /// <summary>
    ///     二开：当前生效的外观分区（null = 全部）
    /// </summary>
    public string? ActiveSectionId { get; private set; }

    /// <summary>
    ///     二开：Mod → 分区
    /// </summary>
    private Dictionary<Guid, string> _modSectionMap = new();

    private string _searchText = string.Empty;

    /// <summary>
    ///     二开：切换外观分区并重新过滤列表
    /// </summary>
    public void ApplySectionFilter(string? sectionId, Dictionary<Guid, string>? sectionMap = null)
    {
        ActiveSectionId = sectionId;
        if (sectionMap is not null)
            _modSectionMap = sectionMap;

        SearchFilterMods(_searchText);
    }

    /// <summary>
    ///     二开：判断某个 Mod 是否属于当前分区
    /// </summary>
    private bool IsInActiveSection(ModRowVM mod) =>
        ActiveSectionId is null ||
        (_modSectionMap.TryGetValue(mod.Id, out var sectionId) && sectionId == ActiveSectionId);

    public ModRowVM[] SearchFilterMods(string searchText)
    {
        _searchText = searchText ?? string.Empty;

        // 二开：文件监听会在后台线程触发刷新，GridMods 是绑到界面的集合，
        // 在后台线程改它会抛 InvalidOperationException 并让进程直接崩掉
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => SearchFilterMods(searchText));
            return [];
        }

        var modsToShow = _gridModsBackend
            .Where(IsInActiveSection)
            .Where(x => x.SearchableText.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        var modsToHide = _gridModsBackend.Except(modsToShow).ToArray();

        foreach (var mod in modsToShow)
        {
            if (GridMods.Contains(mod))
                continue;

            GridMods.Add(mod);
        }

        foreach (var modToHide in modsToHide)
            GridMods.Remove(modToHide);

        return GridMods.ToArray();
    }

    public void ResetModView()
    {
        // 二开：同样只能在 UI 线程改 GridMods
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(ResetModView);
            return;
        }

        var selectedMod = SelectedMods.FirstOrDefault();
        GridMods.Clear();
        GridMods.AddRange(_gridModsBackend.Where(IsInActiveSection));

        if (selectedMod is not null)
            SetSelectedMod(selectedMod.Id);
    }

    public void Receive(ModChangedMessage message)
    {
        if (message.sender == this)
            return;

        var existingMod = _gridModsBackend.FirstOrDefault(mod => mod.Id == message.SkinEntry.Id);
        if (existingMod is null)
            return;
        _ = UpdateModVmAsync(message.SkinEntry, cancellationToken: CancellationToken.None);
    }

    public void SetSelectedMod(Guid modId, bool ignoreFilters = false)
    {
        if (ignoreFilters)
            throw new NotImplementedException();

        var mod = GridMods.FirstOrDefault(x => x.Id == modId);
        if (mod is null)
            return;

        var index = GridMods.IndexOf(mod);
        SelectModEvent?.Invoke(this, new SelectModRowEventArgs(index));
    }


    private async Task ToggleModAsync(ModRowVM? modVmToToggle)
    {
        if (modVmToToggle is null)
            return;

        using var _ = BusySetter.StartSoftBusy();

        var modEntryToToggle = _modsBackend.FirstOrDefault(x => x.Id == modVmToToggle.Id);
        if (modEntryToToggle is null)
            return;

        var otherMods = SingleSelect ? _modsBackend.Where(mod => modVmToToggle.Id != mod.Id && mod.IsEnabled).ToArray() : [];

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    foreach (var skinEntry in otherMods)
                    {
                        if (skinEntry.IsEnabled)
                            _modList.DisableMod(skinEntry.Id);
                    }
                }
                catch (Exception e)
                {
                    _notificationService.ShowNotification("An error occured disabling mod", e.Message, TimeSpan.FromSeconds(5));
                }


                _modList.ToggleMod(modEntryToToggle.Id);
            }, CancellationToken.None);


            await UpdateModVmAsync(modEntryToToggle, false, CancellationToken.None);
            foreach (var otherModEntry in otherMods)
            {
                await UpdateModVmAsync(otherModEntry, false, CancellationToken.None);
                Messenger.Send(new ModChangedMessage(this, otherModEntry, null));
            }
        }
        catch (Exception e)
        {
            _notificationService.ShowNotification("An error occured toggling mod", e.Message, TimeSpan.FromSeconds(5));
        }

        RefreshMultipleModsActiveWarning();
        Messenger.Send(new ModChangedMessage(this, modEntryToToggle, null));
    }

    private async Task UpdateModSettingsAsync(UpdateModSettingsArgument? arg)
    {
        if (arg is null)
            return;

        var (modVm, updateSettingsRequest, messageBody) = arg;

        using var _ = BusySetter.StartSoftBusy();

        var mod = _modsBackend.FirstOrDefault(x => x.Id == modVm.Id);
        if (mod is null)
            return;


        var result = await Task.Run(
            async () => await _modSettingsService.SaveSettingsAsync(mod.Id, updateSettingsRequest, CancellationToken.None).ConfigureAwait(false),
            CancellationToken.None);

        if (result.IsError)
        {
            if (result.HasNotification)
                _notificationService.ShowNotification(result.Notification);
            else
                _notificationService.ShowNotification("An error occured saving mod settings", result.Exception?.ToString() ?? result.ErrorMessage ?? "",
                    TimeSpan.FromSeconds(6));
        }

        if (result.IsSuccess)
        {
            _notificationService.ShowNotification("Mod settings saved", messageBody, TimeSpan.FromSeconds(3));
        }

        await UpdateModVmAsync(mod, true, CancellationToken.None);
        Messenger.Send(new ModChangedMessage(this, mod, null));
    }

    public record UpdateModSettingsArgument(ModRowVM ModVm, UpdateSettingsRequest UpdateSettingsRequest, string MessageBody = "");

    public void ClearSelection() => SelectModEvent?.Invoke(this, new SelectModRowEventArgs(-1));

    [RelayCommand]
    private async Task OpenNewModsWindowAsync(object? modNotification)
    {
        if (modNotification is not ModRowVM_ModNotificationVM notification)
        {
            _logger.Warning("OpenNewModsWindowAsync called with null modModel.");
            return;
        }

        var skinEntry = _modList.Mods.FirstOrDefault(mod => mod.Id == notification.ModId);

        if (skinEntry is null)
        {
            return;
        }

        var existingWindow = _windowManagerService.GetWindow(notification.Id);
        if (existingWindow is not null)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            {
                await Task.Delay(100);
                existingWindow.BringToFront();
            });
            return;
        }


        var modWindow = new ModUpdateAvailableWindow(notification.Id)
        {
            Title =
                $"New Mod Files Available: {ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(skinEntry.Mod.Name)}"
        };
        _windowManagerService.CreateWindow(modWindow, identifier: notification.Id);
        await Task.Delay(100);
        modWindow.BringToFront();
    }

    private void ModListOnModsChanged(object? sender, ModFolderChangedArgs e)
    {
        if (!IsInitialized)
            return;

        _notificationService.ShowNotification(
            $"Folder Activity Detected in {_context.ShownModObject.DisplayName}'s Mod Folder",
            "Files/Folders were changed in the characters mod folder and mods have been refreshed.",
            TimeSpan.FromSeconds(5));


        QueueModRefresh();
    }


    // Only to be used by code behind
    // Contrary to my attempt at MVVM, this is the only way to get the selected mods from the view
    // DataGrid is quite limited in this regard, so it is in charge of the selection and the view model just listens
    public void SelectionChanged_EventHandler(ICollection<ModRowVM> selectedMods, ICollection<ModRowVM> removedMods)
    {
        var anyChanges = false;
        if (selectedMods.Any())
        {
            foreach (var mod in selectedMods)
            {
                if (!SelectedMods.Contains(mod))
                {
                    SelectedMods.Add(mod);
                    anyChanges = true;
                }
            }
        }

        if (removedMods.Any())
        {
            foreach (var mod in removedMods)
            {
                if (SelectedMods.Contains(mod))
                {
                    SelectedMods.Remove(mod);
                    anyChanges = true;
                }
            }
        }

        if (anyChanges)
        {
            OnPropertyChanged(nameof(IsSingleModSelected));
            OnModsSelected?.Invoke(this, new ModRowSelectedEventArgs(SelectedMods));
        }
    }

    // Only to be used by code behind
    public async Task OnKeyDown_EventHandlerAsync(VirtualKey key)
    {
        if (BusySetter.IsHardBusy)
            return;

        var selectedMods = SelectedMods.ToArray();
        if (selectedMods.Length == 0)
            return;

        if (SingleSelect && selectedMods.Length > 1)
        {
            selectedMods = selectedMods.Take(1).ToArray();
            Debugger.Break(); // TODO Handle
        }

        if (key == VirtualKey.Delete)
        {
            DeleteModKeyTriggered?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (key == VirtualKey.Space)
        {
            using var _ = BusySetter.StartSoftBusy();
            if (selectedMods.Any(m => !m.ToggleEnabledCommand.CanExecute(null)))
                return;

            foreach (var mod in selectedMods)
            {
                await mod.ToggleEnabledCommand.ExecuteAsync(mod);
            }
        }
    }

    #region Events

    public event EventHandler<ModRowSelectedEventArgs>? OnModsSelected;

    // Set single selected from code
    public event EventHandler<SelectModRowEventArgs>? SelectModEvent;
    public event EventHandler? DeleteModKeyTriggered;

    public event EventHandler<SortEvent>? SortEvent;

    public event EventHandler? OnModsReloaded;

    public event EventHandler? OnInitialized;

    public class ModRowSelectedEventArgs(IEnumerable<ModRowVM> mods) : EventArgs
    {
        public ICollection<ModRowVM> Mods { get; } = mods.ToArray();
    }

    public class SelectModRowEventArgs(int index) : EventArgs
    {
        public int Index { get; } = index;
    }

    #endregion

    // Used by parent view model

    public void SetModSorting(string sortColumn, bool isDescending, bool isUiTriggered = false)
    {
        if (sortColumn.IsNullOrEmpty())
            return;

        var sorter = ModGridSortingMethod.AllSorters.FirstOrDefault(x => x.SortingMethodType == sortColumn);

        if (sorter is null)
            return;

        CurrentSortingMethod = new ModGridSortingMethod(sorter);
        IsDescendingSort = isDescending;
        SortMods();

        if (isUiTriggered == false)
            SortEvent?.Invoke(this, new SortEvent(sortColumn, isDescending));
    }

    private void SortMods()
    {
        var sortedBackendMods = CurrentSortingMethod.Sort(_gridModsBackend, IsDescendingSort).ToArray();
        _gridModsBackend.Clear();
        _gridModsBackend.AddRange(sortedBackendMods);

        var sortedVisibleMods = CurrentSortingMethod.Sort(GridMods, IsDescendingSort).ToArray();
        for (var newPosition = 0; newPosition < sortedVisibleMods.Length; newPosition++)
        {
            var mod = sortedVisibleMods[newPosition];
            var oldPosition = GridMods.IndexOf(mod);

            if (oldPosition == newPosition)
                continue;

            GridMods.Move(oldPosition, newPosition);
        }
    }

    private Task<LockReleaser> ModRefreshLockAsync() => _modRefreshLock.LockAsync(null, _navigationCt);
}

public class SortEvent(string sortColumn, bool isDescending) : EventArgs
{
    public string SortColumn { get; } = sortColumn;
    public bool IsDescending { get; } = isDescending;
}