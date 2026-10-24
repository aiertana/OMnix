using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Helpers;
using OMniX.Core.Services.ModPresetService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Models;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using OMniX.WinUI.ViewModels.CharacterGalleryViewModels;
using Microsoft.UI.Dispatching;
using Serilog;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigationService = App.GetService<INavigationService>();
    private readonly IGameService _gameService = App.GetService<IGameService>();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<CharacterDetailsViewModel>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly NotificationManager _notificationService = App.GetService<NotificationManager>();
    private readonly ILocalSettingsService _localSettingsService = App.GetService<ILocalSettingsService>();
    private readonly ModNotificationManager _modNotificationManager = App.GetService<ModNotificationManager>();
    private readonly ModDragAndDropService _modDragAndDropService = App.GetService<ModDragAndDropService>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ModPresetService _presetService = App.GetService<ModPresetService>();
    private readonly AppearanceSectionService _sectionService = App.GetService<AppearanceSectionService>();

    public Func<Task>? GridLoadedAwaiter { get; set; }

    private readonly CancellationTokenSource _navigationCancellationTokenSource = new();
    public CancellationToken CancellationToken;

    private bool IsReturning => CancellationToken.IsCancellationRequested || _isErrorNavigateBack;
    private bool _isErrorNavigateBack;
    private ICharacterModList _modList = null!;
    [ObservableProperty] private bool _isNavigationFinished;

    private readonly BusySetter _busySetter;

    [ObservableProperty] private string _loadingItemText = "Character";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSoftBusy), nameof(IsWorking))]
    private bool _isSoftBusy; // App is doing something, but the user can still do other things


    private bool _isHardBusy; // App is doing something, and the user can't do anything on the page

    public bool IsHardBusy
    {
        get => _isHardBusy;
        set
        {
            if (value == _isHardBusy) return;
            _isHardBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotHardBusy));
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(CanChangeInGameSkins));
            NotifyCommands();
        }
    }

    public bool IsNotSoftBusy => !IsSoftBusy;
    public bool IsNotHardBusy => !IsHardBusy;

    public bool IsWorking => IsSoftBusy || IsHardBusy;


    public CharacterDetailsViewModel()
    {
        _busySetter = new BusySetter(this);
    }


    #region 二开：外观分区

    public ObservableCollection<SectionItemVm> SectionItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSections))]
    private SectionItemVm? _selectedSectionItem;

    [ObservableProperty] private bool _isAddingSection;

    [ObservableProperty] private string _newSectionName = string.Empty;

    public bool HasSections => SectionItems.Count > 1;

    private string CharacterInternalName => _modList?.Character.InternalName ?? string.Empty;

    partial void OnSelectedSectionItemChanged(SectionItemVm? value) => _ = ApplySelectedSectionAsync();

    /// <summary>
    ///     二开：加载当前角色的外观分区
    /// </summary>
    public async Task LoadSectionsAsync()
    {
        if (_modList is null)
            return;

        var sections = await _sectionService.GetSectionsAsync(CharacterInternalName);

        SectionItems.Clear();
        SectionItems.Add(SectionItemVm.All);

        foreach (var section in sections)
            SectionItems.Add(new SectionItemVm(section.Id, section.Name));

        SelectedSectionItem = SectionItems.First();
    }

    /// <summary>
    ///     二开：切到某个分区后刷新 Mod 列表
    /// </summary>
    private async Task ApplySelectedSectionAsync()
    {
        if (_modList is null)
            return;

        var sectionId = SelectedSectionItem is null || string.IsNullOrEmpty(SelectedSectionItem.Id)
            ? null
            : SelectedSectionItem.Id;

        var map = await _sectionService.GetModSectionsAsync(CharacterInternalName);
        ModGridVM.ApplySectionFilter(sectionId, map);
    }

    [RelayCommand]
    private void StartAddSection()
    {
        NewSectionName = "新分区";
        IsAddingSection = true;
    }

    [RelayCommand]
    private async Task ConfirmAddSection()
    {
        if (_modList is null)
            return;

        var name = string.IsNullOrWhiteSpace(NewSectionName) ? "新分区" : NewSectionName.Trim();
        var section = await _sectionService.AddSectionAsync(CharacterInternalName, name);

        IsAddingSection = false;
        await LoadSectionsAsync();

        SelectedSectionItem = SectionItems.FirstOrDefault(x => x.Id == section.Id);
    }

    [RelayCommand]
    private async Task DeleteSection()
    {
        if (_modList is null || SelectedSectionItem is null || string.IsNullOrEmpty(SelectedSectionItem.Id))
            return;

        await _sectionService.DeleteSectionAsync(CharacterInternalName, SelectedSectionItem.Id);
        await LoadSectionsAsync();
    }

    /// <summary>
    ///     二开：把列表里选中的 Mod 归入当前分区
    /// </summary>
    [RelayCommand]
    private async Task AssignSelectedModsToSection()
    {
        if (_modList is null || SelectedSectionItem is null)
            return;

        var sectionId = string.IsNullOrEmpty(SelectedSectionItem.Id) ? null : SelectedSectionItem.Id;

        foreach (var mod in ModGridVM.SelectedMods.ToArray())
            await _sectionService.SetModSectionAsync(CharacterInternalName, mod.Id, sectionId);

        await ApplySelectedSectionAsync();
    }

    /// <summary>
    ///     二开：只启用当前分区的 Mod，其它分区的停用
    /// </summary>
    [RelayCommand]
    private async Task EnableSection()
    {
        if (_modList is null || SelectedSectionItem is null)
            return;

        var sectionId = SelectedSectionItem.Id;
        var map = await _sectionService.GetModSectionsAsync(CharacterInternalName);

        foreach (var mod in ModGridVM.GetGridModsBackend().ToArray())
        {
            var inSection = !string.IsNullOrEmpty(sectionId) &&
                            map.TryGetValue(mod.Id, out var modSection) && modSection == sectionId;

            if (inSection && !mod.IsEnabled)
                await mod.ToggleEnabledCommand.ExecuteAsync(mod);
            else if (!inSection && mod.IsEnabled)
                await mod.ToggleEnabledCommand.ExecuteAsync(mod);
        }
    }

    #endregion


    public ModGridVM ModGridVM { get; private set; } = App.GetService<ModGridVM>();
    public ModPaneVM ModPaneVM { get; private set; } = App.GetService<ModPaneVM>();
    public ContextMenuVM ContextMenuVM { get; private set; } = App.GetService<ContextMenuVM>();

    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            await InitAsync(parameter, _busySetter.StartHardBusy()).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            ErrorNavigateBack(e);
            return;
        }
    }

    private async Task InitAsync(object parameter, BusySetter.CommandTracker commandTracker)
    {
        CancellationToken = _navigationCancellationTokenSource.Token;
        if (IsReturning)
            return;
        OnInitializingStarted?.Invoke(this, EventArgs.Empty);

        // Init character card
        InitCharacterCard(parameter);
        LoadingItemText = "Mods";

        // Yield to UI, render character card, specifically the image
        await Task.Delay(100, CancellationToken);
        if (IsReturning) return;


        // Load mods
        await InitModGridAsync();

        // 二开：加载外观分区（暂时关闭，用于排查详细视图卡死）
        // await LoadSectionsAsync();
        LoadingItemText = "ModPane";

        if (IsReturning) return;

        // Yield to UI, render grid
        await Task.Delay(50, CancellationToken);
        commandTracker.Finish();
        if (IsReturning) return;

        // Init Mod Pane
        await InitModPaneAsync();
        LoadingItemText = "Toolbar";
        if (IsReturning) return;

        await InitToolbarAsync();
        LoadingItemText = "右键菜单";

        await InitContextMenuAsync();
        LoadingItemText = "Grid";


        // Wait for the grid to load the datasource
        if (GridLoadedAwaiter is not null)
            await GridLoadedAwaiter();
        GridLoadedAwaiter = null;

        // Now that the grid is loaded, we can select the first mod
        AutoSelectFirstMod();


        // Finished initializing
        IsNavigationFinished = true;
        OnInitializingFinished?.Invoke(this, EventArgs.Empty);
        NotifyCommands();
    }

    private void AutoSelectFirstMod()
    {
        var modToSelect = ModGridVM.GridMods.FirstOrDefault(m => m.IsEnabled) ?? ModGridVM.GridMods.FirstOrDefault();

        if (modToSelect is null)
            return;

        ModGridVM.SetSelectedMod(modToSelect.Id);
    }

    private async Task SetSortOrder()
    {
        var settings = await ReadSettingsAsync();
        if (settings.SortByDescending == null || settings.SortingMethod == null)
            return;

        ModGridVM.SetModSorting(settings.SortingMethod, settings.SortByDescending.Value);
    }


    private async Task InitModGridAsync()
    {
        ModGridVM.BusySetter = _busySetter;
        ModGridVM.OnModsReloaded += OnModsReloaded;
        ModGridVM.DeleteModKeyTriggered += ModGridVM_DeleteModKeyTriggered;
        await SetSortOrder();
        await ModGridVM.InitializeAsync(CreateContext(), CancellationToken);
        ModGridVM.OnModsSelected += OnModsSelected;
        if (IsReturning)
            return;


        ModGridVM.IsBusy = false;
        OnModsLoaded?.Invoke(this, EventArgs.Empty);
    }

    private async Task InitContextMenuAsync()
    {
        await ContextMenuVM.InitializeAsync(CreateContext(), _busySetter, CancellationToken);
        ContextMenuVM.ModsMoved += ContextMenuVM_ModsMoved;
        ContextMenuVM.ModCharactersSkinOverriden += ContextMenuVM_ModsMoved;
    }


    private void ContextMenuVM_ModsMoved(object? sender, EventArgs e)
    {
        ModGridVM.QueueModRefresh();
    }

    private void ModGridVM_DeleteModKeyTriggered(object? sender, EventArgs e)
    {
        if (!DeleteModsCommand.CanExecute(null))
            return;
        DeleteModsCommand.ExecuteAsync(null);
    }

    private void OnModsReloaded(object? sender, EventArgs e) => UpdateTrackedMods();

    private async void OnModsSelected(object? sender, ModGridVM.ModRowSelectedEventArgs args)
    {
        ContextMenuVM.SetSelectedMods(args.Mods.Select(m => m.Id));
        IsSingleModSelected = args.Mods.Count == 1;
        DeleteModsCommand.NotifyCanExecuteChanged();
        var selectedMod = args.Mods.FirstOrDefault();

        ModPaneVM.QueueLoadMod(selectedMod?.Id);

        if (selectedMod is null)
            return;

        var recentlyAddedModNotifications = args.Mods.SelectMany(x =>
            x.ModNotifications.Where(notification => notification.AttentionType == AttentionType.Added)).ToArray();

        if (recentlyAddedModNotifications.Length == 0) return;


        foreach (var modNotification in recentlyAddedModNotifications)
        {
            await _modNotificationManager.RemoveModNotificationAsync(modNotification.Id);

            foreach (var newModModel in args.Mods)
            {
                var notification = newModModel.ModNotifications.FirstOrDefault(x => x.Id == modNotification.Id);
                if (notification is not null) newModModel.ModNotifications.Remove(notification);
            }
        }


        // TODO: Handle notification
    }

    private async Task InitModPaneAsync()
    {
        // Init Mod Pane
        ModPaneVM.BusySetter = _busySetter;
        await ModPaneVM.OnNavigatedToAsync(DispatcherQueue.GetForCurrentThread(), CancellationToken);
        if (IsReturning)
            return;
    }

    public async void OnNavigatedFrom()
    {
        try
        {
            await _navigationCancellationTokenSource.CancelAsync();
            ModGridVM.OnModsSelected -= OnModsSelected;
            ModGridVM.OnModsReloaded -= OnModsReloaded;
            ContextMenuVM.ModsMoved -= ContextMenuVM_ModsMoved;
            ContextMenuVM.ModCharactersSkinOverriden -= ContextMenuVM_ModsMoved;
            ModGridVM.DeleteModKeyTriggered -= ModGridVM_DeleteModKeyTriggered;

            try
            {
                await Task.Run(() => _navigationCancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500)));
            }
            catch (Exception e)
            {
                // ignored
            }

            if (ModGridVM.IsInitialized)
                ModGridVM.OnNavigateFrom();
            if (ModPaneVM.IsInitialized)
                ModPaneVM.OnNavigatedFrom();


            await Task.Run(async () =>
            {
                var delay = Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);

                var settings = await ReadSettingsAsync().ConfigureAwait(false);
                settings.SortingMethod = ModGridVM.CurrentSortingMethod.SortingMethodType;
                settings.SortByDescending = ModGridVM.IsDescendingSort;
                await SaveSettingsAsync(settings).ConfigureAwait(false);
                await delay.ConfigureAwait(false);

                try
                {
                    _navigationCancellationTokenSource?.Dispose();
                }
                catch (Exception e)
                {
                    // ignored
                }
            }, CancellationToken.None);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error while navigating from CharacterDetailsViewModel");
            Debugger.Break();
        }
    }


    private InternalName? ParseNavigationArg(object parameter)
    {
        return parameter switch
        {
            CharacterGridItemModel characterGridItemModel => characterGridItemModel.Character.InternalName,
            INameable iInternalName => iInternalName.InternalName,
            string internalNameString => new InternalName(internalNameString),
            InternalName internalName1 => internalName1,
            _ => null
        };
    }

    private void ErrorNavigateBack(Exception? exception = null)
    {
        if (_isErrorNavigateBack)
            return;
        _isErrorNavigateBack = true;

        Task.Run(async () =>
        {
            await Task.Delay(500);
            if (exception is not null)
                _notificationService.ShowNotification("加载角色详情页出错了",
                    exception.Message, null);
            else
                _notificationService.ShowNotification("加载角色详情页出错了", "",
                    null);
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (_navigationService.CanGoBack)
                    _navigationService.GoBack();
                else
                    _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!);
            });
        });
    }

    [RelayCommand]
    private async Task GoToGalleryScreen()
    {
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<CharacterDetailsSettings>(
            CharacterDetailsSettings.Key, SettingScope.App);

        settings.GalleryView = true;

        await _localSettingsService.SaveSettingAsync(CharacterDetailsSettings.Key, settings, SettingScope.App);

        _navigationService.NavigateTo(typeof(CharacterGalleryViewModel).FullName!, ShownModObject.InternalName);
        _navigationService.ClearBackStack(1);
    }

    private Task<CharacterDetailsSettings> ReadSettingsAsync() =>
        _localSettingsService.ReadOrCreateSettingAsync<CharacterDetailsSettings>(CharacterDetailsSettings.Key,
            SettingScope.App);

    private Task SaveSettingsAsync(CharacterDetailsSettings settings) =>
        _localSettingsService.SaveSettingAsync(CharacterDetailsSettings.Key, settings, SettingScope.App);


    private async Task CommandWrapperAsync(bool startHardBusy, Func<Task> command)
    {
        using var _ = startHardBusy ? _busySetter.StartHardBusy() : _busySetter.StartSoftBusy();
        try
        {
            await command();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _notificationService.ShowNotification("执行命令时出了未知错误", e.Message,
                TimeSpan.FromSeconds(5));
        }
    }

    private IRelayCommand[]? _viewModelCommands;

    private void NotifyCommands()
    {
        if (_viewModelCommands is null)
        {
            var commands = new List<IRelayCommand>();
            foreach (var propertyInfo in GetType()
                         .GetProperties()
                         .Where(p => p.PropertyType.IsAssignableTo(typeof(IRelayCommand))))
            {
                var value = propertyInfo.GetValue(this);

                if (value is IRelayCommand relayCommand)
                    commands.Add(relayCommand);
            }

            _viewModelCommands = commands.ToArray();
        }

        _viewModelCommands.ForEach(c => c.NotifyCanExecuteChanged());
    }
}

public partial class BusySetter(CharacterDetailsViewModel viewModel) : ObservableObject
{
    private readonly CharacterDetailsViewModel _viewModel = viewModel;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSoftBusy), nameof(IsWorking))]
    private bool _isSoftBusy; // App is doing something, but the user can still do other things

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotHardBusy), nameof(IsWorking))]
    private bool _isHardBusy; // App is doing something, and the user can't do anything on the page

    public bool IsNotSoftBusy => !IsSoftBusy;
    public bool IsNotHardBusy => !IsHardBusy;

    public bool IsWorking => IsSoftBusy || IsHardBusy;

    public event EventHandler? SoftBusyChanged;
    public event EventHandler? HardBusyChanged;


    private readonly ConcurrentDictionary<Guid, byte> _trackedSoftCommands = [];
    private readonly ConcurrentDictionary<Guid, byte> _trackedHardCommands = [];

    private void Refresh()
    {
        var oldSoftBusy = IsSoftBusy;
        var oldHardBusy = IsHardBusy;

        IsSoftBusy = !_trackedSoftCommands.IsEmpty;
        IsHardBusy = !_trackedHardCommands.IsEmpty;
        _viewModel.IsHardBusy = IsHardBusy;
        _viewModel.IsSoftBusy = IsSoftBusy;

        if (oldSoftBusy != IsSoftBusy)
            SoftBusyChanged?.Invoke(this, EventArgs.Empty);

        if (oldHardBusy != IsHardBusy)
            HardBusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public CommandTracker StartSoftBusy()
    {
        var tracker = new CommandTracker(this, false);
        _trackedSoftCommands.TryAdd(tracker.CommandId, 0);
        Refresh();
        return tracker;
    }

    public CommandTracker StartHardBusy()
    {
        var tracker = new CommandTracker(this, true);
        _trackedHardCommands.TryAdd(tracker.CommandId, 0);
        Refresh();
        return tracker;
    }


    public readonly struct CommandTracker(BusySetter busySetter, bool isHardBusy) : IDisposable
    {
        public readonly Guid CommandId = Guid.NewGuid();

        public void Dispose() => Finish();

        public void Finish()
        {
            if (isHardBusy)
                busySetter._trackedHardCommands.TryRemove(CommandId, out _);
            else
                busySetter._trackedSoftCommands.TryRemove(CommandId, out _);

            busySetter.Refresh();
        }
    }
}