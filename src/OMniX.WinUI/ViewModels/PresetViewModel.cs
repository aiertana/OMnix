using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;
using OMniX.Core.Services;
using OMniX.Core.Services.ModPresetService;
using OMniX.Core.Services.ModPresetService.Models;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace OMniX.WinUI.ViewModels;

public partial class PresetViewModel(
    ModPresetService modPresetService,
    UserPreferencesService userPreferencesService,
    NotificationManager notificationManager,
    IGameService gameService,
    ISkinManagerService skinManagerService,
    IWindowManagerService windowManagerService,
    CharacterSkinService characterSkinService,
    ILogger logger,
    INavigationService navigationService,
    BusyService busyService,
    ModPresetHandlerService modPresetHandlerService,
    ILocalSettingsService localSettingsService,
    ModRandomizationService modRandomizationService,
    ILanguageLocalizer localizer)
    : ObservableRecipient, INavigationAware
{
    private readonly BusyService _busyService = busyService;
    private readonly CharacterSkinService _characterSkinService = characterSkinService;
    private readonly IWindowManagerService _windowManagerService = windowManagerService;
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly ModPresetService _modPresetService = modPresetService;
    private readonly UserPreferencesService _userPreferencesService = userPreferencesService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly IGameService _gameService = gameService;
    private readonly INavigationService _navigationService = navigationService;
    private readonly ILogger _logger = logger.ForContext<PresetViewModel>();
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly ModPresetHandlerService _modPresetHandlerService = modPresetHandlerService;
    private readonly ModRandomizationService _modRandomizationService = modRandomizationService;
    private readonly ILanguageLocalizer _localizer = localizer;
    private static readonly Random Random = new();


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePresetCommand), nameof(DeletePresetCommand), nameof(ApplyPresetCommand),
        nameof(DuplicatePresetCommand), nameof(RenamePresetCommand), nameof(ReorderPresetsCommand),
        nameof(SaveActivePreferencesCommand), nameof(ApplyPresetCommand), nameof(NavigateToPresetDetailsCommand),
        nameof(ToggleAutoSyncCommand))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;


    [ObservableProperty] private ObservableCollection<ModPresetVm> _presets = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePresetCommand))]
    private string _newPresetNameInput = string.Empty;

    [ObservableProperty] private bool _createEmptyPresetInput;

    [ObservableProperty] private bool _showManualControls;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoSync3DMigotoConfigIsDisabled))]
    private bool _autoSync3DMigotoConfig;

    public bool AutoSync3DMigotoConfigIsDisabled => !AutoSync3DMigotoConfig;

    [ObservableProperty] private bool _resetOnlyEnabledMods = true;
    [ObservableProperty] private bool _alsoReset3DmigotoConfig = true;

    private bool CanCreatePreset()
    {
        return !IsBusy &&
               !NewPresetNameInput.IsNullOrEmpty() &&
               Presets.All(p => !p.Name.Trim().Equals(NewPresetNameInput.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(CanCreatePreset))]
    private async Task CreatePreset()
    {
        IsBusy = true;
        try
        {
            await Task.Run(() => _userPreferencesService.SaveModPreferencesAsync());
            await Task.Run(() => _modPresetService.CreatePresetAsync(NewPresetNameInput, CreateEmptyPresetInput));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationCreateFailed", "新建预设失败"), e.Message, TimeSpan.FromSeconds(5));
        }

        ReloadPresets();
        NewPresetNameInput = string.Empty;
        CreateEmptyPresetInput = false;
        IsBusy = false;
    }

    private bool CanDuplicatePreset() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDuplicatePreset))]
    private async Task DuplicatePreset(ModPresetVm preset)
    {
        IsBusy = true;

        try
        {
            await _modPresetService.DuplicatePresetAsync(preset.Name);
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationDuplicateFailed", "复制预设失败"), e.Message, TimeSpan.FromSeconds(5));
        }

        ReloadPresets();
        IsBusy = false;
    }

    private bool CanDeletePreset() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private async Task DeletePreset(ModPresetVm preset)
    {
        IsBusy = true;

        try
        {
            await Task.Run(() => _modPresetService.DeletePresetAsync(preset.Name));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationDeleteFailed", "删除预设失败"), e.Message, TimeSpan.FromSeconds(5));
        }

        ReloadPresets();
        IsBusy = false;
    }


    private bool CanApplyPreset() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApplyPreset))]
    private async Task ApplyPreset(ModPresetVm? preset)
    {
        if (preset is null)
            return;
        IsBusy = true;

        try
        {
            await Task.Run(async () =>
            {
                await _modPresetService.ApplyPresetAsync(preset.Name).ConfigureAwait(false);
                await _userPreferencesService.SetModPreferencesAsync().ConfigureAwait(false);
            });

            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationApplied", "预设已套用"), $"Preset '{preset.Name}' has been applied",
                TimeSpan.FromSeconds(5));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationApplyFailed", "套用预设失败"), e.Message, TimeSpan.FromSeconds(5));
        }
        finally
        {
            ReloadPresets();
            IsBusy = false;
        }
    }

    private bool CanRenamePreset()
    {
        return !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRenamePreset))]
    private async Task RenamePreset(ModPresetVm preset)
    {
        IsBusy = true;

        try
        {
            await Task.Run(() => _modPresetService.RenamePresetAsync(preset.Name, preset.NameInput.Trim()));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationRenameFailed", "重命名预设失败"), e.Message, TimeSpan.FromSeconds(5));
        }

        ReloadPresets();
        IsBusy = false;
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ReorderPresets()
    {
        IsBusy = true;

        try
        {
            await Task.Run(() => _modPresetService.SavePresetOrderAsync(Presets.Select(p => p.Name)));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationReorderFailed", "保存预设顺序失败"), e.Message, TimeSpan.FromSeconds(5));
        }

        ReloadPresets();
        IsBusy = false;
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SaveActivePreferences()
    {
        using var _ = StartBusy();

        var result = await Task.Run(() => _modPresetHandlerService.SaveActiveModPreferencesAsync());

        if (result.HasNotification)
            _notificationManager.ShowNotification(result.Notification);
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ApplySavedModPreferences()
    {
        using var _ = StartBusy();

        var result = await Task.Run(() => _modPresetHandlerService.ApplyActiveModPreferencesAsync());

        if (result.HasNotification)
            _notificationManager.ShowNotification(result.Notification);
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ToggleReadOnly(ModPresetVm? modPresetVm)
    {
        if (modPresetVm is null)
            return;

        using var _ = StartBusy();

        try
        {
            await Task.Run(() => _modPresetService.ToggleReadOnlyAsync(modPresetVm.Name));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationToggleReadOnlyFailed", "切换只读失败"), e.Message, TimeSpan.FromSeconds(5));
        }

        ReloadPresets();
    }

    [RelayCommand]
    private Task RandomizeMods() => _modRandomizationService.ShowRandomizeModsDialog();


    [RelayCommand]
    private async Task ResetModPreferences()
    {
        using var _ = StartBusy();

        try
        {
            await Task.Run(async () =>
            {
                await _userPreferencesService.ResetPreferencesAsync(ResetOnlyEnabledMods).ConfigureAwait(false);

                if (AlsoReset3DmigotoConfig)
                    await _userPreferencesService.Clear3DMigotoModPreferencesAsync(ResetOnlyEnabledMods)
                        .ConfigureAwait(false);

                _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationPrefsReset", "Mod 设置已重置"),
                    $"Mod preferences have been removed{(AlsoReset3DmigotoConfig ? $" and {Constants.UserIniFileName} have been cleared" : "")}",
                    TimeSpan.FromSeconds(5));
            });
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_NotificationResetFailed", "重置 Mod 设置失败"), e.Message,
                TimeSpan.FromSeconds(5));
        }
    }


    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private void NavigateToPresetDetails(ModPresetVm? modPresetVm)
    {
        if (modPresetVm is null)
            return;

        _navigationService.NavigateTo(typeof(PresetDetailsViewModel).FullName!,
            new PresetDetailsNavigationParameter(modPresetVm.Name));
    }


    private bool CanToggleAutoSync()
    {
        return IsNotBusy;
    }

    [RelayCommand(CanExecute = nameof(CanToggleAutoSync))]
    private async Task ToggleAutoSync()
    {
        AutoSync3DMigotoConfig = !AutoSync3DMigotoConfig;

        var settings = await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(ModPresetSettings.Key);
        settings.AutoSyncMods = AutoSync3DMigotoConfig;
        await _localSettingsService.SaveSettingAsync(ModPresetSettings.Key, settings);
    }

    public async void OnNavigatedTo(object parameter)
    {
        ReloadPresets();

        AutoSync3DMigotoConfig =
            (await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(
                ModPresetSettings.Key)).AutoSyncMods;
    }

    public void OnNavigatedFrom()
    {
    }

    private void ReloadPresets()
    {
        var presets = _modPresetService.GetPresets().OrderBy(i => i.Index);
        Presets.Clear();
        foreach (var preset in presets)
        {
            Presets.Add(new ModPresetVm(preset)
            {
                ToggleReadOnlyCommand = ToggleReadOnlyCommand,
                RenamePresetCommand = RenamePresetCommand,
                DuplicatePresetCommand = DuplicatePresetCommand,
                DeletePresetCommand = DeletePresetCommand,
                ApplyPresetCommand = ApplyPresetCommand,
                NavigateToPresetDetailsCommand = NavigateToPresetDetailsCommand
            });
        }
    }

    public sealed class StartOperation(Action setIsDone) : IDisposable
    {
        public void Dispose()
        {
            setIsDone();
        }
    }

    private StartOperation StartBusy()
    {
        IsBusy = true;
        return new StartOperation(() => IsBusy = false);
    }

    private bool CanAutoSync()
    {
        return AutoSync3DMigotoConfig;
    }
}

public partial class ModPresetVm : ObservableObject
{
    // 二开：这个类是 new 出来的（不经过 DI），所以不能用构造注入，
    // 跟 ModInstallerService 一样用 App.GetService 取。
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public ModPresetVm(ModPreset preset)
    {
        Name = preset.Name;
        NameInput = Name;
        EnabledModsCount = preset.Mods.Count;
        foreach (var mod in preset.Mods)
        {
            Mods.Add(new ModPresetEntryVm(mod));
        }

        CreatedAt = preset.Created;
        IsReadOnly = preset.IsReadOnly;

        // 二开：初始值里不能引用 RenameText（CS0236），改到构造函数里赋。
        RenameButtonText = RenameText;
    }

    public string Name { get; }
    public int EnabledModsCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public ObservableCollection<ModPresetEntryVm> Mods { get; } = new();

    [ObservableProperty] private string _nameInput = string.Empty;

    [ObservableProperty] private bool _isEditingName;

    [ObservableProperty] private string _renameButtonText = string.Empty;
    [ObservableProperty] private bool _isReadOnly;

    [RelayCommand]
    private async Task StartEditingName()
    {
        if (IsEditingName && RenameButtonText == ConfirmText)
        {
            if (NameInput.Trim().IsNullOrEmpty() || NameInput.Trim() == Name)
            {
                ResetInput();
                return;
            }

            if (RenamePresetCommand.CanExecute(this))
            {
                await RenamePresetCommand.ExecuteAsync(this);
                ResetInput();
                return;
            }

            ResetInput();
            return;
        }


        IsEditingName = true;
        NameInput = Name;
        RenameButtonText = ConfirmText;

        void ResetInput()
        {
            NameInput = Name;
            IsEditingName = false;
            RenameButtonText = RenameText;
        }
    }

    public required IAsyncRelayCommand ToggleReadOnlyCommand { get; init; }
    public required IAsyncRelayCommand RenamePresetCommand { get; init; }
    public required IAsyncRelayCommand DuplicatePresetCommand { get; init; }
    public required IAsyncRelayCommand DeletePresetCommand { get; init; }
    public required IAsyncRelayCommand ApplyPresetCommand { get; init; }
    public required IRelayCommand NavigateToPresetDetailsCommand { get; init; }

    private string RenameText => _localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_RenameText", "重命名");
    private string ConfirmText => _localizer.GetLocalizedStringOrDefault("/HomePreset/Preset_ConfirmRenameText", "保存新名字");
}

public partial class ModPresetEntryVm : ObservableObject
{
    public ModPresetEntryVm(ModPresetEntry modEntry)
    {
        ModId = modEntry.ModId;
        Name = modEntry.CustomName ?? modEntry.Name;
        IsMissing = modEntry.IsMissing;
        FullPath = modEntry.FullPath;
        AddedAt = modEntry.AddedAt ?? DateTime.MinValue;
        SourceUrl = modEntry.SourceUrl;
    }

    [ObservableProperty] private Guid _modId;

    [ObservableProperty] private string _name;

    [ObservableProperty] private string _fullPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotMissing))]
    private bool _isMissing;

    public bool IsNotMissing => !IsMissing;

    [ObservableProperty] private DateTime _addedAt;

    [ObservableProperty] private Uri? _sourceUrl;
}