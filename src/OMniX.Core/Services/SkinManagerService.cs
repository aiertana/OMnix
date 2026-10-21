#if DEBUG
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#endif
using System.Collections.Concurrent;
using System.Text.Json;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.Entities.Mods.SkinMod;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Helpers;
using OneOf;
using OneOf.Types;
using Serilog;
using static OMniX.Core.Contracts.Services.RefreshResult;

namespace OMniX.Core.Services;

public sealed class SkinManagerService : ISkinManagerService
{
    private readonly IGameService _gameService;
    private readonly ILogger _logger;
    private readonly ModCrawlerService _modCrawlerService;

    private DirectoryInfo _unloadedModsFolder = null!;
    private DirectoryInfo _activeModsFolder = null!;
    private DirectoryInfo? _threeMigotoFolder;

    private FileSystemWatcher _userIniWatcher = null!;

    private readonly List<ICharacterModList> _characterModLists = new();

    public string ThreeMigotoRootfolder => _threeMigotoFolder?.FullName ?? string.Empty;

    public IReadOnlyCollection<ICharacterModList> CharacterModLists
    {
        get
        {
            lock (_modListLock)
            {
                return _characterModLists.AsReadOnly();
            }
        }
    }


    private readonly object _modListLock = new();
    public bool IsInitialized { get; private set; }

    public SkinManagerService(IGameService gameService, ILogger logger, ModCrawlerService modCrawlerService)
    {
        _gameService = gameService;
        _modCrawlerService = modCrawlerService;
        _logger = logger.ForContext<SkinManagerService>();
    }

    public string UnloadedModsFolderPath => _unloadedModsFolder.FullName;
    public string ActiveModsFolderPath => _activeModsFolder.FullName;

    public bool UnloadingModsEnabled { get; private set; }


    private void AddNewModList(ICharacterModList newModList)
    {
        lock (_modListLock)
        {
            if (_characterModLists.Any(x => x.Character.InternalNameEquals(newModList.Character.InternalName)))
                throw new InvalidOperationException(
                    $"Mod list for character '{newModList.Character.DisplayName}' already exists");

            _characterModLists.Add(newModList);
        }
    }

    public async Task ScanForModsAsync()
    {
        _activeModsFolder.Refresh();

        var characters = _gameService.GetAllModdableObjects();

        var modsFound = new ConcurrentDictionary<Guid, ISkinMod>();


        await Parallel.ForEachAsync(characters, async (modObject, _) =>
        {
            var modObjectFolder = new DirectoryInfo(GetCharacterModFolderPath(modObject));

            var characterModList = new CharacterModList(modObject, modObjectFolder.FullName, logger: _logger);
            AddNewModList(characterModList);

            if (!modObjectFolder.Exists)
            {
                _logger.Verbose("ModdableObject folder for '{Character}' does not exist", modObject.DisplayName);
                return;
            }

            foreach (var modFolder in modObjectFolder.EnumerateDirectories())
            {
                try
                {
                    var mod = await CreateModAsync(modFolder).ConfigureAwait(false);

                    if (!modsFound.TryAdd(mod.Id, mod))
                    {
                        mod = await SkinMod.CreateModAsync(modFolder.FullName, true).ConfigureAwait(false);


                        // If this fails, the mod will be skipped, very unlikely that a duplicate ID will be generated
                        if (!modsFound.TryAdd(mod.Id, mod))
                        {
                            _logger.Error("Failed to generate new ID for mod '{ModName}', OMniX will not track this mod", mod.FullPath);
                            continue;
                        }
                    }


                    characterModList.TrackMod(mod);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to initialize mod '{ModFolder}'", modFolder.FullName);
                }
            }
        }).ConfigureAwait(false);
    }

    private async Task<ISkinMod> CreateModAsync(DirectoryInfo modFolder)
    {
        try
        {
            return await SkinMod.CreateModAsync(modFolder.FullName).ConfigureAwait(false);
        }
        catch (JsonException e)
        {
            modFolder.Refresh();

            var invalidOmniXConfigFile = modFolder.EnumerateFiles(Constants.ModConfigFileName).FirstOrDefault();
            if (invalidOmniXConfigFile is null)
                throw new FileNotFoundException("Could not find OMniX config file", Constants.ModConfigFileName, e);


            _logger.Error(e, "Failed to initialize mod due to invalid config file'{ModFolder}'",
                modFolder.FullName);
            _logger.Information("Renaming invalid config file '{ConfigFile}' to {ConfigFile}.invalid",
                invalidOmniXConfigFile.FullName, invalidOmniXConfigFile.FullName);
            invalidOmniXConfigFile.MoveTo(Path.Combine(modFolder.FullName,
                invalidOmniXConfigFile.Name + ".invalid"));

            return await SkinMod.CreateModAsync(modFolder.FullName).ConfigureAwait(false);
        }
    }

    public async Task<RefreshResult> RefreshModsAsync(string? refreshForCharacter = null, CancellationToken ct = default)
    {
        var modsUntracked = new List<string>();
        var newModsFound = new List<ISkinMod>();
        var duplicateModsFound = new List<DuplicateMods>();
        var errors = new List<string>();

        foreach (var characterModList in _characterModLists)
        {
            ct.ThrowIfCancellationRequested();
            if (refreshForCharacter is not null &&
                !characterModList.Character.InternalNameEquals(refreshForCharacter)) continue;

            var modsDirectory = new DirectoryInfo(characterModList.AbsModsFolderPath);
            modsDirectory.Refresh();

            if (!modsDirectory.Exists)
            {
                _logger.Debug("RefreshModsAsync() Character mod folder for '{Character}' does not exist",
                    characterModList.Character.DisplayName);

                if (characterModList.IsCharacterFolderCreated())
                    throw new InvalidOperationException(
                        $"Character mod folder for '{characterModList.Character.DisplayName}' does not exist, but the character folder is created");

                continue;
            }

            var orphanedMods = new List<CharacterSkinEntry>(characterModList.Mods);

            foreach (var modDirectory in modsDirectory.EnumerateDirectories())
            {
                CharacterSkinEntry? mod = null;

                foreach (var x in characterModList.Mods)
                {
                    if (x.Mod.FullPath.AbsPathCompare(modDirectory.FullName)
                        &&
                        Directory.Exists(Path.Combine(characterModList.AbsModsFolderPath,
                            ModFolderHelpers.GetFolderNameWithDisabledPrefix(modDirectory.Name)))
                        &&
                        Directory.Exists(Path.Combine(characterModList.AbsModsFolderPath,
                            ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(modDirectory.Name)))
                       )
                    {
                        var newName = modDirectory.Name;

                        while (Directory.Exists(Path.Combine(characterModList.AbsModsFolderPath, newName)))
                            newName = DuplicateModAffixHelper.AppendNumberAffix(newName);

                        _logger.Warning(
                            "Mod '{ModName}' has both enabled and disabled folders, renaming folder",
                            modDirectory.Name);

                        duplicateModsFound.Add(new DuplicateMods(x.Mod.Name, newName));
                        x.Mod.Rename(newName);
                        mod = x;
                        mod.Mod.ClearCache();
                        orphanedMods.Remove(x);
                        await TryRefreshIniPathAsync(mod.Mod, errors).ConfigureAwait(false);
                        break;
                    }

                    if (x.Mod.FullPath.AbsPathCompare(modDirectory.FullName))
                    {
                        mod = x;
                        mod.Mod.ClearCache();
                        orphanedMods.Remove(x);
                        await TryRefreshIniPathAsync(mod.Mod, errors).ConfigureAwait(false);
                        break;
                    }

                    var disabledName = ModFolderHelpers.GetFolderNameWithDisabledPrefix(modDirectory.Name);
                    if (x.Mod.FullPath.AbsPathCompare(Path.Combine(characterModList.AbsModsFolderPath, disabledName)))
                    {
                        mod = x;
                        mod.Mod.ClearCache();
                        orphanedMods.Remove(x);
                        await TryRefreshIniPathAsync(mod.Mod, errors).ConfigureAwait(false);
                        break;
                    }
                }

                if (mod is not null) continue;

                try
                {
                    var newMod = await SkinMod.CreateModAsync(modDirectory.FullName).ConfigureAwait(false);

                    if (GetModById(newMod.Id) is not null)
                    {
                        _logger.Debug("Mod '{ModName}' has ID that already exists in mod list, generating new ID",
                            newMod.Name);
                        newMod = await SkinMod.CreateModAsync(modDirectory.FullName, true).ConfigureAwait(false);
                    }


                    characterModList.TrackMod(newMod);
                    newModsFound.Add(newMod);
                    _logger.Debug("Found new mod '{ModName}' in '{CharacterFolder}'", newMod.Name,
                        characterModList.Character.DisplayName);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to create mod from folder '{ModFolder}'", modDirectory.FullName);
                    errors.Add(
                        $"Failed to track new mod folder: '{modDirectory.FullName}' | For character {characterModList.Character.DisplayName}");
                }
            }

            orphanedMods.ForEach(x =>
            {
                characterModList.UnTrackMod(x.Mod);
                modsUntracked.Add(x.Mod.FullPath);
                _logger.Debug("Mod '{ModName}' in '{CharacterFolder}' is no longer tracked", x.Mod.Name,
                    characterModList.Character.DisplayName);
            });
            continue;

            async Task TryRefreshIniPathAsync(ISkinMod mod, IList<string> errorList)
            {
                try
                {
                    await mod.GetModIniPathAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
#if DEBUG
                    throw;
#endif
                    _logger.Error(e, "Failed getting mod .ini path when refreshing mods");
                    errorList.Add(
                        $"Failed to get ini path for mod: '{mod.GetDisplayName()}' | Mod file path: {mod.FullPath}");
                }
            }
        }

        return new RefreshResult(modsUntracked, newModsFound, duplicateModsFound, errors: errors);
    }


    public async Task<OneOf<Success, Error<string>[]>> TransferMods(ICharacterModList source,
        ICharacterModList destination,
        IEnumerable<Guid> modsEntryIds)
    {
        var mods = source.Mods.Where(x => modsEntryIds.Contains(x.Id)).Select(x => x.Mod).ToList();
        foreach (var mod in mods)
        {
            if (!source.Mods.Select(modEntry => modEntry.Mod).Contains(mod))
                throw new InvalidOperationException(
                    $"Mod {mod.Name} is not in source character mod list {source.Character.DisplayName}");


            if (mods.Select(x => x.Name).Any(destination.FolderAlreadyExists))
                throw new InvalidOperationException(
                    $"Mod {mod.Name} already exists in destination character mod list {destination.Character.DisplayName}");
        }

        _logger.Information("Transferring {ModsCount} mods from '{SourceCharacter}' to '{DestinationCharacter}'",
            mods.Count, source.Character.InternalName, destination.Character.InternalName);

        using var sourceDisabled = source.DisableWatcher();
        using var destinationDisabled = destination.DisableWatcher();

        var errors = new List<Error<string>>();
        foreach (var mod in mods)
        {
            source.UnTrackMod(mod);
            mod.MoveTo(destination.AbsModsFolderPath);
            destination.TrackMod(mod);

            try
            {
                var skinSettings = await mod.Settings.ReadSettingsAsync().ConfigureAwait(false);
                skinSettings.CharacterSkinOverride = null;
                await mod.Settings.SaveSettingsAsync(skinSettings).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to clear skin override for mod '{ModName}'", mod.Name);
                errors.Add(new Error<string>(
                    $"Failed to clear skin override for mod '{mod.Name}'. Reason: {e.Message}"));
            }
        }

        return errors.Any() ? errors.ToArray() : new Success();
    }

    public async Task<OneOf<Success, Error<string>[]>> TransferModsToFolder(ICharacterModList source,
        string targetFolderPath, IEnumerable<Guid> modsEntryIds)
    {
        var targetFolder = new DirectoryInfo(targetFolderPath);

        if (!targetFolder.Exists)
            targetFolder.Create();

        var ids = modsEntryIds.ToHashSet();
        var mods = source.Mods.Where(x => ids.Contains(x.Id)).Select(x => x.Mod).ToList();

        var errors = new List<Error<string>>();

        foreach (var mod in mods)
        {
            try
            {
                source.UnTrackMod(mod);
                mod.MoveTo(targetFolder.FullName);

                // 换了归属，原来的皮肤 override 就没意义了
                var skinSettings = await mod.Settings.ReadSettingsAsync().ConfigureAwait(false);
                skinSettings.CharacterSkinOverride = null;
                await mod.Settings.SaveSettingsAsync(skinSettings).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to move mod '{ModName}' to '{TargetFolder}'", mod.Name,
                    targetFolder.FullName);
                errors.Add(new Error<string>(
                    $"Failed to move mod '{mod.Name}'. Reason: {e.Message}"));
            }
        }

        return errors.Any() ? errors.ToArray() : new Success();
    }

    public event EventHandler<ExportProgress>? ModExportProgress;

    public ISkinMod? GetModById(Guid id) =>
        CharacterModLists.SelectMany(x => x.Mods).FirstOrDefault(x => x.Id == id)?.Mod;

    public CharacterSkinEntry? GetModEntryById(Guid id)
    {
        var modEntries = GetAllMods(GetOptions.All);
        return modEntries.FirstOrDefault(x => x.Id == id);
    }

    public Task<bool> IsModListEnabledAsync(IModdableObject moddableObject)
    {
        var modList = GetCharacterModListOrDefault(moddableObject.InternalName);
        return Task.FromResult(modList is not null);
    }

    public Task EnableModListAsync(IModdableObject moddableObject)
    {
        var modList = new CharacterModList(moddableObject, GetCharacterModFolderPath(moddableObject), logger: _logger);

        AddNewModList(modList);

        return RefreshModsAsync(moddableObject.InternalName);
    }

    public Task DisableModListAsync(IModdableObject moddableObject, bool deleteFolder = false)
    {
        var modList = GetCharacterModList(moddableObject);
        var modFolder = new DirectoryInfo(modList.AbsModsFolderPath);
        lock (_modListLock)
        {
            _characterModLists.Remove(modList);
            modList.Dispose();
        }

        if (deleteFolder && modFolder.Exists)
        {
            _logger.Information("Deleting mod folder '{ModFolder}'", modFolder.FullName);
            modFolder.Delete(true);
        }

        return Task.CompletedTask;
    }

    public ISkinMod AddMod(ISkinMod mod, ICharacterModList modList, bool move = false)
    {
        if (GetModById(mod.Id) is not null)
            throw new InvalidOperationException($"Mod with id {mod.Id} is already tracked in a modList");

        var existingMods = modList.Mods.Select(ske => ske.Mod).ToArray();

        foreach (var existingMod in existingMods)
        {
            if (ModFolderHelpers.FolderNameEquals(mod.Name, existingMod.Name))
                throw new InvalidOperationException(
                    $"Mod with name {mod.Name} already exists in modList {modList.Character.DisplayName}");
        }

        modList.InstantiateCharacterFolder();

        using var disableWatcher = modList.DisableWatcher();
        if (move)
            mod.MoveTo(modList.AbsModsFolderPath);
        else
            mod = mod.CopyTo(modList.AbsModsFolderPath);

        modList.TrackMod(mod);
        return mod;
    }

    public void ExportMods(ICollection<ICharacterModList> characterModLists, string exportPath,
        bool removeLocalOmniXSettings = true, bool zip = true, bool keepCharacterFolderStructure = false,
        SetModStatus setModStatus = SetModStatus.KeepCurrent)
    {
        if (characterModLists.Count == 0)
            throw new ArgumentException("Value cannot be an empty collection.", nameof(characterModLists));
        ArgumentNullException.ThrowIfNull(exportPath);

        var exportFolderResultName = $"OMniX_MOD_EXPORT_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

        var exportFolder = new DirectoryInfo(Path.Combine(exportPath, exportFolderResultName));
        exportFolder.Create();

        if (exportFolder.EnumerateFileSystemInfos().Any())
            throw new InvalidOperationException("Export folder is not empty");


        var modsToExport = new List<CharacterSkinEntry>();


        foreach (var characterModList in characterModLists) modsToExport.AddRange(characterModList.Mods);

        double modsProgress = 0;
        double divider = modsToExport.Count + (removeLocalOmniXSettings ? 1 : 0) +
                         (setModStatus != SetModStatus.KeepCurrent ? 1 : 0);
        var modsProgressIncrement = 100 / divider;

        if (!keepCharacterFolderStructure && !zip) // Copy mods unorganized
        {
            var exportedMods = new List<IMod>();
            foreach (var characterSkinEntry in modsToExport)
            {
                var mod = characterSkinEntry.Mod;
                ModExportProgress?.Invoke(this,
                    new ExportProgress(modsProgress += modsProgressIncrement, mod.Name, "Copying Folders"));

                if (CheckForDuplicates(exportFolder, mod)) // Handle duplicate mod names
                {
                    _logger.Information(
                        "Mod '{ModName}' already exists in export folder, appending GUID to folder name",
                        characterSkinEntry.Mod.Name);
                    var oldName = mod.Name;
                    using var disableWatcher = characterSkinEntry.ModList.DisableWatcher();
                    mod.Rename(mod.Name + "__" + Guid.NewGuid().ToString("N"));
                    exportedMods.Add(mod.CopyTo(exportFolder.FullName));
                    mod.Rename(oldName);
                    _logger.Information("Copied mod '{ModName}' to export folder", mod.Name);
                    continue;
                }

                exportedMods.Add(characterSkinEntry.Mod.CopyTo(exportFolder.FullName));
                _logger.Information("Copied mod '{ModName}' to export folder", mod.Name);
            }

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Removing OMniX settings..."));
            RemoveOMniXSettings(removeLocalOmniXSettings, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Setting Mod Status..."));
            SetModsStatus(setModStatus, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(100, null, "Finished"));
            return;
        }

        if (keepCharacterFolderStructure && !zip) // Copy mods organized by character
        {
            var characterToFolder = new Dictionary<IModdableObject, DirectoryInfo>();
            var emptyFoldersCount = 0;

            foreach (var characterModList in characterModLists)
            {
                if (!characterModList.Mods.Any())
                {
                    emptyFoldersCount++;
                    continue; // Skip empty character folders
                }

                // 二开：导出也用中文分类目录名，和 Mods 下的结构保持一致
                var characterFolder = new DirectoryInfo(Path.Combine(exportFolder.FullName,
                    ModFolderHelpers.GetCategoryFolderName(
                        characterModList.Character.ModCategory.InternalName.ToString()),
                    characterModList.Character.InternalName));

                characterToFolder.Add(characterModList.Character, characterFolder);
                characterFolder.Create();
            }

            if (characterToFolder.Count != _gameService.GetAllModdableObjects().Count - emptyFoldersCount)
                throw new InvalidOperationException(
                    "Failed to create character folders in export folder, character mismatch");

            var exportedMods = new List<IMod>();
            foreach (var characterSkinEntry in modsToExport)
            {
                var mod = characterSkinEntry.Mod;
                var destinationFolder = characterToFolder[characterSkinEntry.ModList.Character];
                ModExportProgress?.Invoke(this,
                    new ExportProgress(modsProgress += modsProgressIncrement, mod.Name, "Copying Folders"));

                if (CheckForDuplicates(destinationFolder, mod)) // Handle duplicate mod names
                {
                    _logger.Information(
                        "Mod '{ModName}' already exists in export folder, appending GUID to folder name",
                        characterSkinEntry.Mod.Name);

                    var oldName = mod.Name;
                    using var disableWatcher = characterSkinEntry.ModList.DisableWatcher();
                    mod.Rename(mod.Name + "__" + Guid.NewGuid().ToString("N"));
                    exportedMods.Add(mod.CopyTo(destinationFolder.FullName));
                    mod.Rename(oldName);
                    _logger.Information("Copied mod '{ModName}' to export character folder '{CharacterFolder}'",
                        mod.Name,
                        characterSkinEntry.ModList.Character.InternalName);

                    continue;
                }

                exportedMods.Add(characterSkinEntry.Mod.CopyTo(destinationFolder.FullName));
                _logger.Information("Copied mod '{ModName}' to export character folder '{CharacterFolder}'", mod.Name,
                    characterSkinEntry.ModList.Character.InternalName);
            }

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Removing OMniX settings..."));
            RemoveOMniXSettings(removeLocalOmniXSettings, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(modsProgress += modsProgressIncrement, null, "Setting Mod Status..."));
            SetModsStatus(setModStatus, exportedMods);

            ModExportProgress?.Invoke(this,
                new ExportProgress(100, null, "Finished"));


            return;
        }

        if (zip)
            throw new NotImplementedException();
    }

    private static bool CheckForDuplicates(DirectoryInfo destinationFolder, IMod mod)
    {
        destinationFolder.Refresh();
        foreach (var directory in destinationFolder.EnumerateDirectories())
        {
            if (directory.Name.Equals(mod.Name, StringComparison.CurrentCultureIgnoreCase))
                return true;

            if (directory.Name.EndsWith(mod.Name, StringComparison.CurrentCultureIgnoreCase))
                return true;

            if (mod.Name.Replace(CharacterModList.DISABLED_PREFIX, "") == directory.Name ||
                mod.Name.Replace("DISABLED", "") == directory.Name)
                return true;
        }

        return false;
    }

    private static void SetModsStatus(SetModStatus setModStatus, IEnumerable<IMod> mods)
    {
        switch (setModStatus)
        {
            case SetModStatus.EnableAllMods:
                {
                    foreach (var mod in mods)
                    {
                        var enabledName = mod.Name;
                        enabledName = enabledName.Replace(CharacterModList.DISABLED_PREFIX, "");
                        enabledName = enabledName.Replace("DISABLED", "");
                        if (enabledName != mod.Name)
                            mod.Rename(enabledName);
                    }

                    break;
                }
            case SetModStatus.DisableAllMods:
                {
                    foreach (var mod in mods)
                        if (!mod.Name.StartsWith("DISABLED") || !mod.Name.StartsWith(CharacterModList.DISABLED_PREFIX))
                            mod.Rename(CharacterModList.DISABLED_PREFIX + mod.Name);

                    break;
                }
        }
    }

    private void RemoveOMniXSettings(bool removeLocalOmniXSettings, IEnumerable<IMod> exportedMods)
    {
        if (removeLocalOmniXSettings)
            foreach (var file in exportedMods.Select(mod => new DirectoryInfo(mod.FullPath))
                         .SelectMany(folder => folder
                             .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                             .Where(entry => Constants.IsOmniXFile(entry.Name))))
            {
                _logger.Debug("Deleting local omnix file '{OmniXFile}' in modFolder", file.FullName);
                file.Delete();
            }
    }

    public ICharacterModList GetCharacterModList(string internalName)
    {
        var characterModList = _characterModLists.FirstOrDefault(x => x.Character.InternalNameEquals(internalName));

        if (characterModList is not null)
            return characterModList;

        // 二开：角色库里有、但 Mod 列表里没有的情况——
        // 新建的角色、刚在角色管理里重新启用的角色、或者刚整理过目录的角色。
        // ScanForModsAsync 只在启动时给「当时存在的角色」建列表，RefreshModsAsync 又只遍历已有的列表，
        // 所以新角色永远补不上。原来这里直接 First() 会抛
        // "Sequence contains no matching element"，整个角色页加载失败。
        // 现在按需补建一个空列表。
        lock (_modListLock)
        {
            var again = _characterModLists.FirstOrDefault(x => x.Character.InternalNameEquals(internalName));
            if (again is not null)
                return again;

            var character = _gameService.GetAllModdableObjects()
                .FirstOrDefault(x => x.InternalNameEquals(internalName));

            if (character is null)
                throw new InvalidOperationException($"No moddable object found for '{internalName}'");

            var created = new CharacterModList(character, GetCharacterModFolderPath(character), logger: _logger);

            _characterModLists.Add(created);

            _logger.Information("Created missing mod list for '{Character}'", character.DisplayName);

            return created;
        }
    }

    public ICharacterModList GetCharacterModList(IModdableObject character) => GetCharacterModList(character.InternalName);


    public ICharacterModList? GetCharacterModListOrDefault(string internalName) =>
        _characterModLists.FirstOrDefault(x => x.Character.InternalNameEquals(internalName));

    public async Task InitializeAsync(string activeModsFolderPath, string? unloadedModsFolderPath = null,
        string? threeMigotoRootfolder = null)
    {
        if (IsInitialized)
        {
            _logger.Error("ModManagerService is already initialized");
            return;
        }

        _logger.Debug(
            "Initializing ModManagerService, activeModsFolderPath: {ActiveModsFolderPath}, unloadedModsFolderPath: {UnloadedModsFolderPath}",
            activeModsFolderPath, unloadedModsFolderPath);
        if (unloadedModsFolderPath is not null)
        {
            _unloadedModsFolder = new DirectoryInfo(unloadedModsFolderPath);
            _unloadedModsFolder.Create();
            UnloadingModsEnabled = true;
        }

        if (threeMigotoRootfolder is not null)
        {
            _threeMigotoFolder = new DirectoryInfo(threeMigotoRootfolder);
            _threeMigotoFolder.Refresh();
            if (!_threeMigotoFolder.Exists)
                throw new InvalidOperationException("3DMigoto folder does not exist");

            //_userIniWatcher = new FileSystemWatcher(_threeMigotoFolder.FullName, D3DX_USER_INI);
            //_userIniWatcher.Changed += OnUserIniChanged;
            //_userIniWatcher.NotifyFilter = NotifyFilters.LastWrite;
            //_userIniWatcher.IncludeSubdirectories = false;
            //_userIniWatcher.EnableRaisingEvents = true;
        }

        _activeModsFolder = new DirectoryInfo(activeModsFolderPath);
        _activeModsFolder.Create();
        InitializeFolderStructure();
        await ScanForModsAsync().ConfigureAwait(false);
        IsInitialized = true;

#if DEBUG
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(DebugDuplicateIdChecker).ConfigureAwait(false);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#endif
    }

    private void InitializeFolderStructure()
    {
        var categories = _gameService.GetCategories();

        foreach (var category in categories)
        {
            var categoryFolder = GetCategoryFolderPath(category);
            categoryFolder.Create();
        }
    }

    public DirectoryInfo GetCategoryFolderPath(ICategory category)
    {
        // 二开：分类目录用中文名。这里原来直接用内部名，启动时会把 character 建回来。
        return new DirectoryInfo(Path.Combine(_activeModsFolder.FullName, GetCategoryFolderName(category)));
    }

    /// <summary>
    ///     二开：判断某个目录是不是分类目录。
    ///     目录名已经改成中文，所以中文名和旧的英文内部名都要认。
    /// </summary>
    private bool IsCategoryFolder(string folderName)
    {
        return _gameService.GetCategories().Any(category =>
            ModFolderHelpers.GetCategoryFolderNameCandidates(category.InternalName.ToString())
                .Any(name => string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase)));
    }


    public async Task<int> ReorganizeModsAsync(InternalName? characterFolderToReorganize = null,
        bool disableMods = false)
    {
        if (_activeModsFolder is null) throw new InvalidOperationException("ModManagerService is not initialized");

        var characterFolder = characterFolderToReorganize is null
            ? null
            : _gameService.GetModdableObjectByIdentifier(characterFolderToReorganize);

        if (characterFolderToReorganize is null)
            _logger.Information("Reorganizing mods");
        else
            _logger.Information("Reorganizing mods for '{Character}'", characterFolder?.InternalName);

        _activeModsFolder.Refresh();
        var characters = _gameService.GetAllModdableObjects();

        var disabledCharacters = _gameService.GetAllModdableObjects(GetOnly.Disabled);

        var othersCharacter = _gameService.GetModdableObjectByIdentifier(new InternalName("Others"));
        if (othersCharacter is null)
        {
            _logger.Error("Failed to get 'Others' character");
            return -1;
        }

        var movedMods = 0;

        var folderToReorganize = characterFolder is null
            ? _activeModsFolder
            : new DirectoryInfo(GetCharacterModFolderPath(characterFolder));

        if (!folderToReorganize.Exists)
            return movedMods;

        foreach (var folder in folderToReorganize.EnumerateDirectories())
        {
            // Is a character folder in the root mods folder then move it into the category folder
            // 二开：内部名(英文)和显示名(中文)都能匹配，Mod 文件夹用中文命名也能识别
            var character = characters.Concat(disabledCharacters).FirstOrDefault(x =>
                x.InternalName.Equals(folder.Name) ||
                x.DisplayName.Equals(folder.Name, StringComparison.Ordinal));


            if (character is not null)
            {
                var existingModList = GetCharacterModListOrDefault(character.InternalName);
                if (existingModList is null)
                {
                    _logger.Information(
                        "Character folder '{CharacterFolder}' does not have a mod list, disabled? Ignoring and continuing",
                        folder.FullName);
                    continue;
                }

                if (folder.EnumerateFileSystemInfos().Any())
                    _logger.Warning($"""
                                     During mod reorganization, a moddableObject (Navia, Ayaka... etc) folder was found in root mods folder.
                                     But should be in the their respective category folder. example: Mods/Character/Ayaka/<mod folders>
                                     OMniX will move the mod folders in the moddableObject folder to the moddableObject folder in the category folder.
                                     Then delete the moddableObject folder in the root mods folder if it is empty.
                                     If there any loose files they will be ignored in the root of the moddableObject, but will not be deleted.
                                     => Mods in the folder {folder.FullName} will be moved to {existingModList.AbsModsFolderPath} folder.
                                     """);

                if (HasFilesWithModExtensionsInRoot(folder, character))
                {
                    _logger.Warning($"""
                                     Found files that are usually at the root of a mod in a character folder. Therefore, OMniX will ignore this folder:
                                     {folder.Name} - {folder.FullName}
                                     {character.InternalName} is a reserved name for a character folder. It is suggested to avoid naming a mod folder the same as a character. OMniX uses internal names stored in the Assets folder to determine chracter folder names.
                                     OMniX will ignore this folder and continue.
                                     """);
                    continue;
                }


                foreach (var modFolder in folder.EnumerateDirectories())
                {
                    if (existingModList.FolderAlreadyExists(modFolder.Name))
                    {
                        _logger.Warning(
                            "Mod '{ModName}' already exists in '{CharacterFolder}', skipping mod",
                            modFolder.Name, existingModList.Character.InternalName);
                        continue;
                    }

                    try
                    {
                        AddMod(await SkinMod.CreateModAsync(modFolder), existingModList, true);
                        _logger.Information("Moved mod '{ModName}' to '{CharacterFolder}' mod folder",
                            modFolder.Name, existingModList.Character.InternalName);
                        movedMods++;
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to move mod '{ModName}' to '{CharacterFolder}'", modFolder.FullName,
                            existingModList.Character.InternalName);
                    }
                }


                if (!folder.EnumerateFileSystemInfos().Any())
                {
                    _logger.Information("Deleting empty character folder '{CharacterFolder}'", folder.FullName);
                    folder.Delete();
                }

                continue;
            }


            // Is a category folder => ignore
            if (IsCategoryFolder(folder.Name))
            {
                _logger.Debug("Found category folder '{CategoryFolder}' in root mods folder, ignoring",
                    folder.FullName);
                continue;
            }


            var closestMatchCharacter =
                _modCrawlerService.GetMatchingModdableObjects(folder.FullName).FirstOrDefault();


            if (closestMatchCharacter is null)
            {
                // 二开：先用中英文角色名精确匹配（Mod 文件夹可能直接用中文角色名）
                var cleanFolderName = ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(folder.Name);
                closestMatchCharacter = characters.Concat(disabledCharacters).FirstOrDefault(x =>
                    x.DisplayName.Equals(cleanFolderName, StringComparison.Ordinal) ||
                    x.InternalName.Equals(cleanFolderName));
            }

            if (closestMatchCharacter is null)
            {
                closestMatchCharacter = _gameService.QueryModdableObjects(
                    ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(folder.Name),
                    minScore: 200).OrderByDescending(x => x.Value).FirstOrDefault().Key;
            }

            switch (closestMatchCharacter)
            {
                case null when characterFolderToReorganize is null:
                    // 二开：识别不到的 Mod 文件夹单独建一个占位角色（吉祥物头像），不再全都塞进“其他角色”
                    var placeholderName = ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(folder.Name);
                    var placeholder = await _gameService.GetOrCreatePlaceholderCharacterAsync(placeholderName)
                        .ConfigureAwait(false);

                    if (placeholder is not null)
                    {
                        _logger.Information("Mod folder '{ModFolder}' is unknown. Created placeholder character",
                            folder.Name);
                        closestMatchCharacter = placeholder;
                    }
                    else
                    {
                        _logger.Information(
                            "Mod folder '{ModFolder}' does not seem to belong to a character. Moving to 'Others' folder",
                            folder.Name);
                        closestMatchCharacter = othersCharacter;
                    }

                    break;
                case null:
                    continue; // Mod folder does not belong to this character, but we cant determine which character it belongs to. Move on
            }


            var modList = GetCharacterModList(closestMatchCharacter.InternalName);
            ISkinMod? mod = null;
            try
            {
                mod = await SkinMod.CreateModAsync(folder);
                if (GetModById(mod.Id) is not null)
                {
                    _logger.Debug("Mod '{ModName}' has ID that already exists in mod list, generating new ID",
                        mod.Name);
                    mod = await SkinMod.CreateModAsync(folder, true);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to initialize mod folder '{ModFolder}'", folder.FullName);
                continue;
            }

            modList.InstantiateCharacterFolder();

            try
            {
                using var disableWatcher = modList.DisableWatcher();

                var renameAttempts = 0;
                while (modList.FolderAlreadyExists(mod.Name))
                {
                    var oldName = mod.Name;
                    mod.Rename(DuplicateModAffixHelper.AppendNumberAffix(mod.Name));
                    _logger.Information(
                        "Mod '{ModName}' already exists in '{CharacterFolder}', renaming to {NewModName}",
                        oldName, closestMatchCharacter.InternalName, mod.Name);
                    renameAttempts++;
                    if (renameAttempts <= 10) continue;
                    _logger.Error(
                        "Failed to rename mod '{ModName}' to '{NewModName}' after 10 attempts, skipping mod",
                        mod.Name, mod.Name);
                    break;
                }

                if (renameAttempts > 10) continue;


                mod.MoveTo(GetCharacterModList(closestMatchCharacter.InternalName).AbsModsFolderPath);
                _logger.Information("Moved mod '{ModName}' to '{CharacterFolder}' mod folder", mod.Name,
                    closestMatchCharacter.InternalName);
                movedMods++;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to move mod '{ModName}' to '{CharacterFolder}'", mod.FullPath,
                    closestMatchCharacter.InternalName);
                continue;
            }

            modList.TrackMod(mod);
            if (disableMods && modList.IsModEnabled(mod))
            {
                try
                {
                    modList.DisableMod(mod.Id);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to disable mod '{ModName}'", mod.Name);
                }
            }
        }

        return movedMods;
    }

    private bool HasFilesWithModExtensionsInRoot(DirectoryInfo directoryInfo, IModdableObject? moddableObject = null)
    {
        foreach (var file in directoryInfo.EnumerateFiles())
        {
            if (Constants.ScriptIniNames.Any(x => x.Equals(file.Name, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (file.Name.StartsWith("cover", StringComparison.OrdinalIgnoreCase))
                return true;

            if (file.Name.StartsWith("preview", StringComparison.OrdinalIgnoreCase))
                return true;

            if (moddableObject is not null &&
                file.Name.Equals($"{moddableObject.InternalName}.ini", StringComparison.OrdinalIgnoreCase))
                return true;

            if (ModCrawlerService.ModExtensions.Any(ext =>
                    ext.Equals(file.Extension, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     二开：取分类对应的目录名（中文）。映射表在 ModFolderHelpers 里，
    ///     整理功能（LegacyCharacterOrganizer）也用同一份，避免两处不一致。
    /// </summary>
    private static string GetCategoryFolderName(ICategory category) =>
        ModFolderHelpers.GetCategoryFolderName(category.InternalName.ToString());

    private string GetCharacterModFolderPath(IModdableObject character)
    {
        var category = _gameService.GetCategories()
            .FirstOrDefault(c => c.InternalName.Equals(character.ModCategory.InternalName));

        if (category is null)
            throw new InvalidOperationException(
                $"Failed to get category for character '{character.DisplayName}'");

        var categoryInternalName = category.InternalName.ToString();

        // 二开：角色目录直接放在 Mods 下，用中文角色名（Mods\漂泊者），不再套一层 角色\。
        // 非角色（武器 / 滑翔翼 / 物件…）仍然按分类目录归档（Mods\武器\xxx）。
        var isCharacterCategory = string.Equals(categoryInternalName, "character",
            StringComparison.OrdinalIgnoreCase);

        var standardPath = isCharacterCategory
            ? Path.Combine(_activeModsFolder.FullName, GetCharacterFolderName(character))
            : Path.Combine(_activeModsFolder.FullName, GetCategoryFolderName(category),
                character.InternalName.ToString());

        if (!IsDirectoryEmpty(standardPath))
            return standardPath;

        // 二开：兼容各种旧位置。按优先级探测，有内容的就用，避免迁移后 Mod 消失。
        var candidates = new[]
        {
            // Mods\rover（更早的根目录老位置）
            Path.Combine(_activeModsFolder.FullName, character.InternalName.ToString()),
            // Mods\角色\rover（上一版的结构）
            Path.Combine(_activeModsFolder.FullName, "角色", character.InternalName.ToString()),
            // Mods\角色\漂泊者
            Path.Combine(_activeModsFolder.FullName, "角色", GetCharacterFolderName(character))
        };

        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate, standardPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsDirectoryEmpty(candidate))
                continue;

            _logger.Verbose(
                "Using legacy mod folder for '{Character}': '{LegacyPath}' (standard path is empty)",
                character.DisplayName, candidate);

            return candidate;
        }

        return standardPath;
    }

    /// <summary>
    ///     二开：角色的目录名（中文显示名，非法字符做替换）。名字为空时回落到内部名。
    /// </summary>
    private static string GetCharacterFolderName(IModdableObject character)
    {
        var name = ModFolderHelpers.SanitizeFolderName(character.DisplayName);

        return string.IsNullOrEmpty(name) ? character.InternalName.ToString() : name;
    }

    /// <summary>
    ///     二开：目录不存在或者里面没有任何东西都算「空」
    /// </summary>
    private static bool IsDirectoryEmpty(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return true;

        return !Directory.EnumerateFileSystemEntries(path).Any();
    }

    public ICollection<DirectoryInfo> CleanCharacterFolders()
    {
        var deletedFolders = new List<DirectoryInfo>();
        foreach (var characterModList in _characterModLists)
        {
            var characterFolder = new DirectoryInfo(characterModList.AbsModsFolderPath);
            if (!characterFolder.Exists) continue;

            var characterModFolders = characterFolder.EnumerateDirectories().ToArray();

            foreach (var modFolder in characterModFolders)
            {
                var containsOnlyOmniXFiles = modFolder.EnumerateFileSystemInfos().All(ModFolderHelpers.IsOMniXFileEntry);

                if (!containsOnlyOmniXFiles) continue;

                _logger.Information("Deleting mod folder, due to it only containing omnix files '{ModFolder}'",
                    modFolder.FullName);

                modFolder.Delete(true);
                deletedFolders.Add(modFolder);
            }

            if (characterFolder.EnumerateFileSystemInfos().Any()) continue;

            _logger.Information("Deleting empty character folder '{CharacterFolder}'", characterFolder.FullName);
            characterFolder.Delete();
            deletedFolders.Add(characterFolder);
        }

        foreach (var directory in _activeModsFolder.EnumerateDirectories())
        {
            // 二开：分类目录（中文名或旧英文名）不删
            if (IsCategoryFolder(directory.Name)) continue;

            if (directory.EnumerateFileSystemInfos().Any()) continue;

            _logger.Information("Deleting unknown empty folder '{Folder}'", directory.FullName);
            directory.Delete();
            deletedFolders.Add(directory);
        }

        return deletedFolders;
    }

    public IList<CharacterSkinEntry> GetAllMods(GetOptions getOptions = GetOptions.All)
    {
        // We get them all to avoid locking for too long
        var allMods = CharacterModLists.SelectMany(x => x.Mods).ToList();

        return getOptions switch
        {
            GetOptions.All => allMods,
            GetOptions.Enabled => allMods.Where(x => x.IsEnabled).ToList(),
            GetOptions.Disabled => allMods.Where(x => !x.IsEnabled).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(getOptions), getOptions, null)
        };
    }

    private void OnUserIniChanged(object sender, FileSystemEventArgs e)
    {
        _logger.Debug("d3dx_user.ini was changed");
        UserIniChanged?.Invoke(this, new UserIniChanged());
    }

    public event EventHandler<UserIniChanged>? UserIniChanged;

    private static string D3DX_USER_INI = Constants.UserIniFileName;

    public async Task<string> GetCurrentSwapVariationAsync(Guid characterSkinEntryId)
    {
        if (_threeMigotoFolder is null || !_threeMigotoFolder.Exists)
            return "3DMigoto folder not set";

        var characterSkinEntry = _characterModLists.SelectMany(x => x.Mods)
            .FirstOrDefault(x => x.Id == characterSkinEntryId);
        if (characterSkinEntry is null)
            throw new InvalidOperationException(
                $"CharacterSkinEntry with id {characterSkinEntryId} was not found in any character mod list");

        var mod = characterSkinEntry.Mod;


        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Debug("d3dx_user.ini does not exist in 3DMigoto folder");
            return "Unknown";
        }

        var lines = await File.ReadAllLinesAsync(d3dxUserIni.FullName);

        var sectionStarted = false;
        var returnVar = "Unknown";
        foreach (var line in lines)
        {
            if (IniConfigHelpers.IsComment(line)) continue;
            if (IniConfigHelpers.IsSection(line, "Constants"))
            {
                sectionStarted = true;
                continue;
            }

            if (!sectionStarted) continue;
            if (IniConfigHelpers.IsSection(line)) break;

            var iniKey = IniConfigHelpers.GetIniKey(line);
            if (iniKey is null || !iniKey.EndsWith("swapvar", StringComparison.CurrentCultureIgnoreCase)) continue;

            if (!iniKey.Contains(mod.Name.ToLower())) continue;

            returnVar = IniConfigHelpers.GetIniValue(line) ?? "Unknown";
            break;
        }

        return returnVar;
    }

    public void Dispose()
    {
        _userIniWatcher?.Dispose();
        var allModLists = _characterModLists.ToArray();
        foreach (var modList in allModLists)
        {
            _characterModLists.Remove(modList);
            modList.Dispose();
        }
    }


#if DEBUG
    [DoesNotReturn]
    private async Task DebugDuplicateIdChecker()
    {
        while (true)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            var mods = _characterModLists.SelectMany(x => x.Mods).Select(x => x.Mod);
            var duplicateIds = mods.GroupBy(x => x.Id).Where(x => x.Count() > 1).ToArray();

            if (duplicateIds.Any())
            {
                foreach (var duplicateId in duplicateIds)
                {
                    _logger.Error("Duplicate ID found: {Id}", duplicateId.Key);
                    foreach (var mod in duplicateId)
                        _logger.Error("Mod: {ModName}", mod.Name);
                }

                Debugger.Break();
            }
        }
    }
#endif
}

public class UserIniChanged : EventArgs
{
}

public sealed class ExportProgress : EventArgs
{
    public ExportProgress(double progress, string? modName, string operation)
    {
        Progress = (int)Math.Round(progress);
        ModName = modName;
        Operation = operation;
    }

    public int Progress { get; }
    public string? ModName { get; }
    public string Operation { get; }
}