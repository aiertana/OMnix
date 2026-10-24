using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.Entities.Mods.Contract;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Helpers;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models.CustomControlTemplates;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using Microsoft.UI.Dispatching;
using Serilog;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public partial class ContextMenuVM(
    ISkinManagerService skinManagerService,
    IGameService gameService,
    NotificationManager notificationManager,
    ILogger logger,
    ModSettingsService modSettingsService)
    : ObservableRecipient
{
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly IGameService _gameService = gameService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ILogger _logger = logger.ForContext<ContextMenuVM>();
    private readonly ModSettingsService _modSettingsService = modSettingsService;

    private DispatcherQueue _dispatcherQueue = null!;
    private CancellationToken _navigationCt = default;
    private ICharacterModList _modList = null!;
    private ModDetailsPageContext _context = null!;
    private BusySetter _busySetter = null!;

    private List<Guid> _selectedMods = [];

    [ObservableProperty] private int _selectedModsCount;
    [ObservableProperty] private bool _isCharacter;
    [ObservableProperty] private bool _multipleSkins;

    public ObservableCollection<SuggestedModObject> SuggestedModdableObjects { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveModsCommand))]
    private SuggestedModObject? _selectedSuggestedModObject;

    [ObservableProperty] private string _moveModsSearchText = string.Empty;

    /// <summary>
    ///     二开：Mods 目录下可以移动过去的文件夹（大世界 / 武器 / others / 别的角色…）。
    ///     直接按磁盘上的文件夹名列出，比只搜索角色更直观。
    /// </summary>
    public ObservableCollection<ModFolderTarget> ModFolders { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveModsToFolderCommand))]
    private ModFolderTarget? _selectedModFolder;

    /// <summary>
    ///     二开：重新读 Mods 目录下的文件夹列表。
    /// </summary>
    public void RefreshModFolders()
    {
        ModFolders.Clear();

        if (_modList is null)
            return;

        var currentFolder = _modList.AbsModsFolderPath;
        var modsRoot = Path.GetDirectoryName(currentFolder);

        if (string.IsNullOrEmpty(modsRoot) || !Directory.Exists(modsRoot))
            return;

        try
        {
            foreach (var folder in new DirectoryInfo(modsRoot).EnumerateDirectories()
                         .OrderBy(x => x.Name, StringComparer.CurrentCulture))
            {
                // 当前角色自己的目录不用列
                if (string.Equals(folder.FullName, currentFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                ModFolders.Add(new ModFolderTarget(folder.Name, folder.FullName));
            }
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Could not read mod folders from {Root}", modsRoot);
        }
    }


    public ObservableCollection<SelectCharacterTemplate> SelectableCharacterSkins { get; init; } = new();
    [ObservableProperty] private bool _modHasCharacterSkinOverride;

    [ObservableProperty] private SelectedSkinVm? _modCharacterSkinOverride;

    public Task InitializeAsync(ModDetailsPageContext context, BusySetter busySetter, CancellationToken navigationCt)

    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _navigationCt = navigationCt;
        ChangeSkin(context);
        _busySetter = busySetter;

        _modList = _skinManagerService.GetCharacterModList(context.ShownModObject);

        // 二开：读 Mods 下的文件夹列表，供「移动到其他文件夹」用
        RefreshModFolders();

        return Task.CompletedTask;
    }

    public void ChangeSkin(ModDetailsPageContext context)
    {
        _context = context;
        IsCharacter = context.IsCharacter;
        SelectableCharacterSkins.Clear();
        if (!context.IsCharacter) return;

        MultipleSkins = context.Skins.Count > 1;

        foreach (var characterSkin in context.Skins)
        {
            var template = new SelectCharacterTemplate(characterSkin);
            template.IsSelected = characterSkin == context.SelectedSkin;
            SelectableCharacterSkins.Add(template);
        }
    }

    public bool CanOpenContextMenu => SelectedModsCount > 0 && _busySetter.IsNotHardBusy;

    public void SetSelectedMods(IEnumerable<Guid> selectedMods)
    {
        _selectedMods = selectedMods.ToList();
        SelectedModsCount = _selectedMods.Count;
        MoveModsCommand.NotifyCanExecuteChanged();
        MoveModsToFolderCommand.NotifyCanExecuteChanged();

        if (_selectedMods.Count != 1) return;
        var mod = _modList.Mods.FirstOrDefault(m => m.Id == _selectedMods[0]);
        if (mod is null || !mod.Mod.Settings.TryGetSettings(out var modSettings)) return;

        var skinOverride = ResolveSkinOverride(mod, modSettings);

        ModHasCharacterSkinOverride = skinOverride is not null;
        ModCharacterSkinOverride = skinOverride is not null ? new SelectedSkinVm(skinOverride) : null;
        SelectNewCharacterSkinCommand.NotifyCanExecuteChanged();
        OverrideModCharacterSkinCommand.NotifyCanExecuteChanged();
    }

    private ICharacterSkin? ResolveSkinOverride(CharacterSkinEntry skinEntry, ModSettings settings)
    {
        if (settings.CharacterSkinOverride.IsNullOrEmpty() || !_context.IsCharacter)
            return null;

        // Check selected characters skins first
        var selectedSkin = _context.Skins.FirstOrDefault(s => s.InternalNameEquals(settings.CharacterSkinOverride));
        if (selectedSkin != null)
            return selectedSkin;

        // Check all skins
        var allSkins = _gameService.GetCharacters();
        selectedSkin = allSkins.SelectMany(c => c.Skins).FirstOrDefault(s => s.InternalNameEquals(settings.CharacterSkinOverride));

        return selectedSkin;
    }

    private List<CharacterSkinEntry> ResolveSelectedMods() => _modList.Mods.Where(m => _selectedMods.Contains(m.Id)).ToList();


    #region Commands

    private bool CanMoveMods() => SelectedModsCount > 0
                                  && !_busySetter.IsWorking
                                  && SelectedSuggestedModObject is not null;

    [RelayCommand(CanExecute = nameof(CanMoveMods))]
    private async Task MoveModsAsync()
    {
        using var busy = _busySetter.StartHardBusy();
        var destinationModList = _skinManagerService.GetCharacterModListOrDefault(SelectedSuggestedModObject?.InternalName ?? "");


        if (destinationModList is null)
        {
            _logger.Warning("Destination mod list not found");
            _notificationManager.ShowNotification("找不到目标 Mod 列表",
                "Destination mod list not found", TimeSpan.FromSeconds(5));
            return;
        }

        var selectedMods = ResolveSelectedMods();

        try
        {
            await Task.Run(() => _skinManagerService.TransferMods(_modList, destinationModList,
                selectedMods.Select(modEntry => modEntry.Id)));
        }
        catch (InvalidOperationException e)
        {
            _logger.Error(e, "Error moving mods");
            _notificationManager
                .ShowNotification("这个操作不合法",
                    $"Cannot move mods\n{e.Message}, see logs for details.", TimeSpan.FromSeconds(10));
            return;
        }

        _notificationManager.ShowNotification($"{SelectedModsCount} Mods Moved",
            $"Successfully moved {string.Join(",", selectedMods.Select(m => m.Mod.GetDisplayName()))} mods to {destinationModList.Character.DisplayName}",
            TimeSpan.FromSeconds(5));

        ModsMoved?.Invoke(this, EventArgs.Empty);
    }

    private bool CanMoveModsToFolder() => SelectedModsCount > 0
                                         && !_busySetter.IsWorking
                                         && SelectedModFolder is not null;

    /// <summary>
    ///     二开：把选中的 Mod 移动到 Mods 下任意一个文件夹（按文件夹名选）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveModsToFolder))]
    private async Task MoveModsToFolderAsync()
    {
        var target = SelectedModFolder;

        if (target is null)
            return;

        using var busy = _busySetter.StartHardBusy();

        var selectedMods = ResolveSelectedMods();
        var names = string.Join(", ", selectedMods.Select(modEntry => modEntry.Mod.GetDisplayName()));

        try
        {
            await Task.Run(() => _skinManagerService.TransferModsToFolder(_modList, target.FullPath,
                selectedMods.Select(modEntry => modEntry.Id)));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error moving mods to folder {Folder}", target.FullPath);
            _notificationManager.ShowNotification("移动失败", e.Message, TimeSpan.FromSeconds(10));
            return;
        }

        _notificationManager.ShowNotification("已移动",
            $"{selectedMods.Count} 个 Mod 已移动到「{target.Name}」：{names}", TimeSpan.FromSeconds(6));

        ModsMoved?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSelectNewCharacterSkin(SelectCharacterTemplate? characterTemplate) =>
        SelectedModsCount == 1 && characterTemplate != null && _context.IsCharacter;

    [RelayCommand(CanExecute = nameof(CanSelectNewCharacterSkin))]
    private void SelectNewCharacterSkin(SelectCharacterTemplate? characterTemplate)
    {
        if (characterTemplate == null || !_context.IsCharacter) return;

        var characterSkinToSet = _context.Character.Skins.FirstOrDefault(charSkin =>
            charSkin.InternalName.Equals(characterTemplate.InternalName));

        if (characterSkinToSet == null)
            return;

        foreach (var selectableCharacterSkin in SelectableCharacterSkins) selectableCharacterSkin.IsSelected = false;
        characterTemplate.IsSelected = true;
        OverrideModCharacterSkinCommand.NotifyCanExecuteChanged();
    }

    private bool CanOverrideModCharacterSkin()
    {
        var selectedTemplate = SelectableCharacterSkins.Where(c => c.IsSelected).ToArray();
        if (selectedTemplate.Length != 1) return false;
        var selectedCharacterSkin = selectedTemplate[0];

        if (!_context.IsCharacter) return false;

        var isDifferentSkinSelected =
                // If mod already has an override
                (ModCharacterSkinOverride is not null &&
                 !ModCharacterSkinOverride.InternalName.Equals(selectedCharacterSkin.InternalName, StringComparison.OrdinalIgnoreCase)) ||
                // If mod has no override
                ModCharacterSkinOverride is null && !_context.SelectedSkin.InternalNameEquals(selectedCharacterSkin.InternalName)
            ;

        return _busySetter.IsNotHardBusy && SelectableCharacterSkins.Any(c => c.IsSelected) && SelectedModsCount == 1 && isDifferentSkinSelected;
    }

    [RelayCommand(CanExecute = nameof(CanOverrideModCharacterSkin))]
    private async Task OverrideModCharacterSkin()
    {
        var selectedCharacterSkin = SelectableCharacterSkins.FirstOrDefault(c => c.IsSelected);
        if (selectedCharacterSkin == null || !_context.IsCharacter) return;

        var character = _context.Character;

        var characterSkinToSet = character.Skins.FirstOrDefault(charSkin => charSkin.InternalNameEquals(selectedCharacterSkin.InternalName));

        if (characterSkinToSet == null)
        {
            Debugger.Break(); // Should not happen
            return;
        }

        var modEntry = _modList.Mods.FirstOrDefault(m => m.Id == _selectedMods[0]);
        if (modEntry is null) return;


        var result = await _modSettingsService.SetCharacterSkinOverrideLegacy(modEntry.Id, characterSkinToSet.InternalName);


        if (result.IsT0)
        {
            _notificationManager.ShowNotification("已改掉这个 Mod 的皮肤归属",
                $"Set skin override for mod '{modEntry.Mod.GetDisplayName()}' to {characterSkinToSet.DisplayName}", null);
        }
        else
        {
            var error = result.IsT1 ? result.AsT1.ToString() : result.AsT2.ToString();
            _logger.Error("Failed to override character skin for mod {modName}", modEntry.Mod.GetDisplayName());
            _notificationManager.ShowNotification(
                $"Failed to override character skin for mod {modEntry.Mod.GetDisplayName()}",
                $"An Error Occurred. Reason: {error}",
                TimeSpan.FromSeconds(5));
        }


        ModCharactersSkinOverriden?.Invoke(this, EventArgs.Empty);
        CloseFlyout?.Invoke(this, EventArgs.Empty);
    }

    #endregion


    #region Events

    public event EventHandler? ModsMoved;
    public event EventHandler? ModCharactersSkinOverriden;
    public event EventHandler? CloseFlyout;

    #endregion


    #region EventHandlers

    public void OnFlyoutClosing()
    {
        SuggestedModdableObjects.Clear();
        SelectedSuggestedModObject = null;
        MoveModsSearchText = string.Empty;
        ModHasCharacterSkinOverride = false;
        ModCharacterSkinOverride = null;
    }

    public void SearchTextChanged(string senderText)
    {
        SuggestedModdableObjects.Clear();
        SelectedSuggestedModObject = null;
        if (string.IsNullOrWhiteSpace(senderText))
        {
            return;
        }

        var result = _gameService.QueryModdableObjects(senderText, minScore: 120)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(x => new SuggestedModObject(x.Key));

        SuggestedModdableObjects.AddRange(result);
    }

    public void OnSuggestionChosen(SuggestedModObject? modObject)
    {
        if (modObject is null)
            return;

        SelectedSuggestedModObject = modObject;
        SuggestedModdableObjects.Clear();
    }

    #endregion
}

public sealed class SuggestedModObject(IModdableObject moddableObject)
{
    public InternalName InternalName { get; } = moddableObject.InternalName;
    public string DisplayName { get; } = moddableObject.DisplayName;

    public override string ToString() => DisplayName;
}

public sealed class SelectedSkinVm(ICharacterSkin skin)
{
    public string DisplayName => skin.DisplayName;
    public string InternalName => skin.InternalName;
}

/// <summary>
///     二开：「移动到其他文件夹」下拉里的一项。Name 就是 Mods 下真实的文件夹名。
/// </summary>
public sealed class ModFolderTarget(string name, string fullPath)
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;

    public override string ToString() => Name;
}