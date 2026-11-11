using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.GamesService.Requests;
using OMniX.Core.Services;

using OneOf;
using OneOf.Types;

namespace OmniX.Plugin.OilFX.UI;

/// <summary>
///     宿主服务的轻量替身：OilFXViewModel 对 ISkinManagerService 只读 ActiveModsFolderPath，
///     对 IGameService 只调 GetCharacters（独立版无角色库 → 返回空，界面按目录名分组）。
///     其余成员与管理界面无关，一律不支持。
/// </summary>
internal sealed class StandaloneSkinManager(string modsRoot) : ISkinManagerService
{
    public string UnloadedModsFolderPath => modsRoot + "_unloaded";

    public string ActiveModsFolderPath => modsRoot;

    public string ThreeMigotoRootfolder => modsRoot;

    public bool IsInitialized => true;

    public IReadOnlyCollection<ICharacterModList> CharacterModLists => [];

    public void Dispose()
    {
    }

    public Task InitializeAsync(string activeModsFolderPath, string? unloadedModsFolderPath, string? threeMigotoRootfolder = null)
        => Task.CompletedTask;

    private static NotSupportedException NotSupported()
        => new("OilFX 独立版不支持此操作（该功能属于 OMniX 管理器本体）");

    public DirectoryInfo GetCategoryFolderPath(ICategory category) => throw NotSupported();
    public ICharacterModList GetCharacterModList(string internalName) => throw NotSupported();
    public ICharacterModList GetCharacterModList(IModdableObject character) => throw NotSupported();
    public ICharacterModList? GetCharacterModListOrDefault(string internalName) => throw NotSupported();
    public Task<int> ReorganizeModsAsync(InternalName? characterFolderToReorganize = null, bool disableMods = false) => throw NotSupported();
    public Task<RefreshResult> RefreshModsAsync(string? refreshForCharacter = null, CancellationToken ct = default) => throw NotSupported();
    public Task<OneOf<Success, Error<string>[]>> TransferMods(ICharacterModList source, ICharacterModList destination, IEnumerable<Guid> modsEntryIds) => throw NotSupported();
    public Task<OneOf<Success, Error<string>[]>> TransferModsToFolder(ICharacterModList source, string targetFolderPath, IEnumerable<Guid> modsEntryIds) => throw NotSupported();
    public Task<string> GetCurrentSwapVariationAsync(Guid characterSkinEntryId) => throw NotSupported();
    public void ExportMods(ICollection<ICharacterModList> characterModLists, string exportPath, bool removeLocalOmniXSettings = true, bool zip = true, bool keepCharacterFolderStructure = false, SetModStatus setModStatus = SetModStatus.KeepCurrent) => throw NotSupported();
    public ISkinMod? GetModById(Guid id) => throw NotSupported();
    public CharacterSkinEntry? GetModEntryById(Guid id) => throw NotSupported();
    public Task EnableModListAsync(IModdableObject moddableObject) => throw NotSupported();
    public Task DisableModListAsync(IModdableObject moddableObject, bool deleteFolder = false) => throw NotSupported();
    public ISkinMod AddMod(ISkinMod mod, ICharacterModList modList, bool move = false) => throw NotSupported();
    public ICollection<DirectoryInfo> CleanCharacterFolders() => throw NotSupported();
    public IList<CharacterSkinEntry> GetAllMods(GetOptions getOptions = GetOptions.All) => throw NotSupported();

#pragma warning disable CS0067
    public event EventHandler<ExportProgress>? ModExportProgress;
#pragma warning restore CS0067
}

/// <summary>角色库替身：返回空角色列表（分组头直接用 mod 目录名，无头像走首字占位）。</summary>
internal sealed class StandaloneGameService : IGameService
{
    private static NotSupportedException NotSupported()
        => new("OilFX 独立版不支持此操作（该功能属于 OMniX 管理器本体）");

    public GameInfo GameInfo => throw NotSupported();
    public string GameName => string.Empty;
    public string GameShortName => string.Empty;
    public string GameIcon => string.Empty;
    public string GameServiceSettingsFilePath => string.Empty;
    public Uri GameBananaUrl => new("https://gamebanana.com");
    public string OtherCharacterInternalName => "Other";
    public string GlidersCharacterInternalName => "Gliders";
    public string WeaponsCharacterInternalName => "Weapons";

    public List<ICharacter> GetCharacters(bool includeDisabled = false)
        => StandaloneCharacterLibrary.GetCharacters(includeDisabled);

    public Task<ICollection<InternalName>> PreInitializedReadModObjectsAsync(string assetsDirectory) => throw NotSupported();
    public Task InitializeAsync(string assetsDirectory, string localSettingsDirectory) => throw NotSupported();
    public Task InitializeAsync(InitializationOptions options) => Task.CompletedTask;
    public Task SetCharacterOverrideAsync(ICharacter character, OverrideCharacterRequest request) => throw NotSupported();
    public Task DisableCharacterAsync(ICharacter character) => throw NotSupported();
    public Task EnableCharacterAsync(ICharacter character) => throw NotSupported();
    public Task ResetOverrideForCharacterAsync(ICharacter character) => throw NotSupported();
    public Task<ICharacter> CreateCharacterAsync(CreateCharacterRequest characterRequest) => throw NotSupported();
    public Task<ICharacter?> GetOrCreatePlaceholderCharacterAsync(string folderName) => throw NotSupported();
    public Task<(string json, ICharacter character)> CreateJsonCharacterExportAsync(CreateCharacterRequest characterRequest) => throw NotSupported();
    public Task<ICharacter> EditCustomCharacterAsync(InternalName internalName, EditCustomCharacterRequest characterRequest) => throw NotSupported();
    public Task<ICharacter> DeleteCustomCharacterAsync(InternalName internalName) => throw NotSupported();
    public ICharacter? QueryCharacter(string keywords, IEnumerable<ICharacter>? restrictToCharacters = null, int minScore = 100) => null;
    public ICharacter? GetCharacterByIdentifier(string internalName, bool includeDisabledCharacters = false) => null;
    public Dictionary<ICharacter, int> QueryCharacters(string searchQuery, IEnumerable<ICharacter>? restrictToCharacters = null, int minScore = 100, bool includeDisabledCharacters = false) => [];
    public Dictionary<IModdableObject, int> QueryModdableObjects(string searchQuery, ICategory? category = null, int minScore = 100) => [];
    public List<IGameElement> GetElements() => [];
    public List<IGameClass> GetClasses() => [];
    public List<IRegion> GetRegions() => [];
    public List<ICharacter> GetDisabledCharacters() => [];
    public List<ICategory> GetCategories() => [];
    public List<IModdableObject> GetModdableObjects(ICategory category, GetOnly getOnlyStatus = GetOnly.Enabled) => [];
    public List<IModdableObject> GetAllModdableObjects(GetOnly getOnlyStatus = GetOnly.Enabled) => [];
    public List<T> GetAllModdableObjectsAsCategory<T>(GetOnly getOnlyStatus = GetOnly.Enabled) where T : IModdableObject => [];
    public IModdableObject? GetModdableObjectByIdentifier(InternalName? internalName, GetOnly getOnlyStatus = GetOnly.Enabled) => null;
    public bool IsMultiMod(IModdableObject moddableObject) => false;

#pragma warning disable CS0067
    public event EventHandler? Initialized;
#pragma warning restore CS0067
}
