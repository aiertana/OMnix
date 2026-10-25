using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace OMniX.Core.GamesService;

internal class GameSettingsManager
{
    private readonly ILogger _logger;
    internal string SettingsFilePath { get; }
    private readonly DirectoryInfo _settingsDirectory;
    private readonly DirectoryInfo _customImageFolder;
    internal readonly DirectoryInfo CustomCharacterImageFolder;
    internal readonly DirectoryInfo CharacterOverrideImageFlder;

    private const string GameServiceFileName = "GameService";
    private GameServiceRoot? _settings;

    internal GameSettingsManager(ILogger logger, DirectoryInfo settingsDirectory)
    {
        _settingsDirectory = settingsDirectory;
        _logger = logger.ForContext<GameSettingsManager>();
        SettingsFilePath = Path.Combine(settingsDirectory.FullName, GameServiceFileName + ".json");
        _customImageFolder = new DirectoryInfo(Path.Combine(settingsDirectory.FullName, "CustomImages"));
        _customImageFolder.Create();

        CustomCharacterImageFolder = new DirectoryInfo(Path.Combine(_customImageFolder.FullName, "CustomModdableObjects"));
        CustomCharacterImageFolder.Create();

        // 二开：内置角色的头像也放进同一个文件夹（CustomImages\CustomModdableObjects）。
        // 以前分成 CustomModdableObjects / CharacterOverrides 两处，用户想手动备份或换机器时
        // 常常只拷走一个，另一半头像就"丢了"。文件名分别是中文名（自定义）和内部名（内置），
        // 不会撞车，合在一起反而好管。
        CharacterOverrideImageFlder = CustomCharacterImageFolder;
    }

    internal async Task CleanupUnusedImagesAsync()
    {
        await ReadSettingsAsync(useCache: false).ConfigureAwait(false);

        var parsedImageUris = new List<Uri>();

        var overrideImagesPath = _settings.CharacterOverrides.Where(i => i.Value.Image != null).ToList();

        foreach (var (internalName, @override) in overrideImagesPath)
        {
            var image = ParseImagePath(@override.Image);
            if (image is null)
                continue;

            if (!File.Exists(image.LocalPath))
            {
                _logger.Warning("Image file for {InternalName} not found at {ImageFilePath}", internalName, image.LocalPath);
                continue;
            }

            parsedImageUris.Add(image);
        }

        var customCharacterImagesPath = _settings.CustomCharacters.Where(i => i.Value.Image != null).ToList();

        foreach (var (internalName, customCharacter) in customCharacterImagesPath)
        {
            var image = ParseImagePath(customCharacter.Image);
            if (image is null)
                continue;
            if (!File.Exists(image.LocalPath))
            {
                _logger.Warning("Image file for {InternalName} not found at {ImageFilePath}", internalName, image.LocalPath);
                continue;
            }

            parsedImageUris.Add(image);
        }


        // 两个目录现在是同一个，去重后再算「没被引用」的图片，否则每张图都会被数两遍
        var foundImageFiles = CustomCharacterImageFolder.GetFiles()
            .Concat(CharacterOverrideImageFlder.GetFiles())
            .Select(p => new Uri(p.FullName))
            .Distinct();

        var unusedImages = foundImageFiles.Except(parsedImageUris).ToList();

        if (unusedImages.Count == 0)
            return;

        _logger.Information("Found {UnusedImagesCount} unused images that will be deleted in AppData folder", unusedImages.Count);

        // 二开：不再直接 File.Delete。
        // 这个清理是自动跑的，一旦它判断错了（比如配置曾经被清空过、角色短暂读不到），
        // 用户导入的头像就永久没了，回收站里也找不回来。
        // 改成挪到 CustomImages\DeletedImages 目录，误删还能自己捞回来。
        var trashFolder = new DirectoryInfo(Path.Combine(_customImageFolder.FullName, "DeletedImages"));
        trashFolder.Create();

        foreach (var unusedImage in unusedImages)
        {
            try
            {
                if (!File.Exists(unusedImage.LocalPath))
                    continue;

                var target = Path.Combine(trashFolder.FullName, Path.GetFileName(unusedImage.LocalPath));

                if (File.Exists(target))
                    File.Delete(target);

                _logger.Debug("Moving unused image file {UnusedImage} to trash folder", unusedImage.LocalPath);
                File.Move(unusedImage.LocalPath, target);
                _logger.Information("Moved unused image file {UnusedImage} to trash folder", unusedImage.LocalPath);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to move unused image file {UnusedImage}", unusedImage.LocalPath);
            }
        }
    }

    [MemberNotNull(nameof(_settings))]
    internal async Task<GameServiceRoot> ReadSettingsAsync(bool useCache = true)
    {
        if (useCache && _settings != null)
            return _settings;

        if (!File.Exists(SettingsFilePath))
        {
            _settings = new GameServiceRoot();
            await SaveSettingsAsync().ConfigureAwait(false);
            return _settings;
        }

        try
        {
            var rootSettings =
                JsonConvert.DeserializeObject<GameServiceRootUntyped>(await File.ReadAllTextAsync(SettingsFilePath)
                    .ConfigureAwait(false)) ??
                new GameServiceRootUntyped();

            var characterOverrides = await ParseUntypedJsonAsync<JsonCharacterOverride>(rootSettings.CharacterOverrides).ConfigureAwait(false);

            var characterCategory = Category.CreateForCharacter();
            var customCharacters = await ParseUntypedJsonAsync<JsonCustomCharacter>(rootSettings.CustomModObjects[characterCategory.InternalName])
                .ConfigureAwait(false);

            var illegalCharacterNames = new List<KeyValuePair<string, JsonCustomCharacter>>();

            foreach (var customCharacterKeyValue in customCharacters)
            {
                var key = customCharacterKeyValue.Key;
                var value = customCharacterKeyValue.Value;
                if (key.IsNullOrEmpty())
                {
                    illegalCharacterNames.Add(customCharacterKeyValue);
                    continue;
                }

                var illegalCharacterKeys = Path.GetInvalidFileNameChars();

                if (key.Any(c => illegalCharacterKeys.Contains(c)))
                {
                    illegalCharacterNames.Add(customCharacterKeyValue);
                    continue;
                }
            }

            foreach (var illegalCharacterName in illegalCharacterNames)
            {
                customCharacters.Remove(illegalCharacterName.Key);
                _logger.Warning("Illegal character name found in settings file: {Key} | Object: {Object}", illegalCharacterName.Key,
                    JsonConvert.SerializeObject(illegalCharacterName.Value, Formatting.Indented));
            }

            _settings = new GameServiceRoot
            {
                CharacterOverrides = characterOverrides,
                CustomCharacters = customCharacters
            };
        }
        catch (Exception e)
        {
            var invalidFilePath = SettingsFilePath;
            var newInvalidFilePath = Path.Combine(_settingsDirectory.FullName, GameServiceFileName + ".json.invalid");
            _logger.Error(e, "Failed to read settings file, renaming invalid file {InvalidFilePath} to {NewInvalidFilePath}", invalidFilePath,
                newInvalidFilePath);
            _settings = new GameServiceRoot();

            if (File.Exists(invalidFilePath))
            {
                try
                {
                    if (File.Exists(newInvalidFilePath))
                        File.Delete(newInvalidFilePath);
                    File.Move(invalidFilePath, newInvalidFilePath);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Failed to rename invalid settings file {InvalidFilePath} to {NewInvalidFilePath}", invalidFilePath,
                        newInvalidFilePath);
                }
            }
        }


        return _settings;

        Task<Dictionary<string, T>> ParseUntypedJsonAsync<T>(Dictionary<string, object>? unTypedDictionary)
        {
            if (unTypedDictionary == null)
                return Task.FromResult(new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase));

            var characterOverrides = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in unTypedDictionary)
            {
                if (key.IsNullOrEmpty())
                {
                    _logger.Warning("Empty key found in settings file");
                    continue;
                }

                try
                {
                    if (value is not JObject json)
                    {
                        Debugger.Break();
                        _logger.Error("Failed to parse character override for {Key}", key);
                        continue;
                    }

                    var typedJson = json.ToObject<T>();

                    if (typedJson == null)
                        continue;
                    characterOverrides.Add(key, typedJson);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to parse character override for {Key}", key);
                }
            }

            return Task.FromResult(characterOverrides);
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (_settings is null)
            return;

        await File.WriteAllTextAsync(SettingsFilePath, JsonConvert.SerializeObject(_settings.ToJson(), Formatting.Indented)).ConfigureAwait(false);
    }

    internal async Task SetCharacterOverrideAsync(InternalName id, Action<JsonCharacterOverride> setterAction)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);

        var @override = settings.CharacterOverrides.GetOrAdd(id);

        setterAction(@override);

        await SaveSettingsAsync().ConfigureAwait(false);
    }


    internal async Task<Uri?> GetCharacterImageOverrideAsync(InternalName id)
    {
        await ReadSettingsAsync().ConfigureAwait(false);

        return GetCharacterOverrideImagePath(id);
    }

    // TODO: Not happy with this, should consider reworking custom characters and overrides
    internal async Task<Uri?> SetCharacterImageOverrideAsync(InternalName id, Uri? image)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);

        var characterOverride = settings.CharacterOverrides.GetOrAdd(id);

        var existingImage = GetCharacterOverrideImagePath(id);

        if (image == null)
        {
            characterOverride.Image = null;
            await SaveSettingsAsync().ConfigureAwait(false);

            if (File.Exists(existingImage?.LocalPath))
                File.Delete(existingImage.LocalPath);

            return null;
        }


        var imageToCopy = new FileInfo(image.LocalPath);
        if (!imageToCopy.Exists)
            throw new InvalidOperationException("Image file does not exist");

        var destinationPath = CreateCharacterOverrideImagePath(id, imageToCopy.Extension);

        imageToCopy.CopyTo(destinationPath.LocalPath, true);


        characterOverride.Image = destinationPath.LocalPath;
        await SaveSettingsAsync().ConfigureAwait(false);

        return destinationPath;
    }

    internal async Task SetIsDisabledOverride(InternalName id, bool isDisabled)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);
        if (settings.CharacterOverrides.TryGetValue(id.Id, out var @override))
            @override.IsDisabled = isDisabled;

        else
            settings.CharacterOverrides.Add(id.Id, new JsonCharacterOverride() { IsDisabled = isDisabled });

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal async Task RemoveOverride(InternalName id)
    {
        var settings = await ReadSettingsAsync().ConfigureAwait(false);

        var characterOverride = settings.CharacterOverrides.FirstOrDefault(kv => id.Equals(kv.Key)).Value;


        if (File.Exists(characterOverride?.Image))
            File.Delete(characterOverride.Image);


        settings.CharacterOverrides.Remove(id.Id);

        if (characterOverride?.IsDisabled is not null)
            settings.CharacterOverrides.Add(id.Id,
                new JsonCharacterOverride() { IsDisabled = characterOverride.IsDisabled });

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal Uri? GetCustomCharacterImagePath(InternalName internalName)
    {
        if (_settings is null)
            return null;

        if (!_settings.CustomCharacters.TryGetValue(internalName.Id, out var @override))
            return null;

        return ParseImagePath(@override.Image);
    }


    internal Uri CreateCustomCharacterImagePath(InternalName internalName, string fileExtension)
        => new(Path.Combine(CustomCharacterImageFolder.FullName, $"{internalName.Id}{fileExtension}"));

    internal Uri CreateCharacterOverrideImagePath(InternalName internalName, string fileExtension)
        => new(Path.Combine(CharacterOverrideImageFlder.FullName, $"{internalName.Id}{fileExtension}"));

    internal Uri? GetCharacterOverrideImagePath(InternalName internalName)
    {
        if (_settings is null)
            return null;

        if (!_settings.CharacterOverrides.TryGetValue(internalName.Id, out var @override))
            return null;

        return ParseImagePath(@override.Image);
    }

    private Uri? ParseImagePath(string? path)
    {
        if (path.IsNullOrEmpty())
            return null;

        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile ? uri : null;
    }

    internal async Task AddCustomCharacterAsync(ICharacter customCharacter)
    {
        await ReadSettingsAsync().ConfigureAwait(false);

        var jsonCustomCharacter = JsonCustomCharacter.FromCharacter(customCharacter);

        _settings.CustomCharacters[customCharacter.InternalName.Id] = jsonCustomCharacter;

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    internal async Task ReplaceCustomCharacterAsync(ICharacter customCharacter)
    {
        await ReadSettingsAsync().ConfigureAwait(false);

        if (!_settings.CustomCharacters.TryGetValue(customCharacter.InternalName.Id, out _))
            throw new InvalidOperationException("Character to edit not found. Try restarting OMniX");


        var newJsonCustomCharacter = JsonCustomCharacter.FromCharacter(customCharacter);

        _settings.CustomCharacters[customCharacter.InternalName.Id] = newJsonCustomCharacter;

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    // TODO: Not happy with this, should consider reworking custom characters and overrides
    internal async Task SetCustomCharacterImageAsync(ICharacter customCharacter, Uri? newImage)
    {
        await ReadSettingsAsync().ConfigureAwait(false);

        if (!_settings.CustomCharacters.TryGetValue(customCharacter.InternalName, out var jsonCustomCharacter))
            throw new InvalidOperationException("Character to edit not found. Try restarting OMniX");


        if (customCharacter.ImageUri is null && newImage is null)
            return;

        // Remove existing image first
        {
            var existingImagePath = GetCustomCharacterImagePath(customCharacter.InternalName);

            if (existingImagePath is not null && File.Exists(existingImagePath.LocalPath))
                File.Delete(existingImagePath.LocalPath);

            jsonCustomCharacter.Image = null;
        }

        // Only delete existing image
        if (newImage == null)
        {
            customCharacter.ImageUri = null;
            await SaveSettingsAsync().ConfigureAwait(false);
            return;
        }

        // Add new image
        var newImageFile = new FileInfo(newImage.LocalPath);
        if (!newImageFile.Exists)
            throw new InvalidOperationException("Image file does not exist");

        var fileExtension = newImageFile.Extension;

        var copiedImagePath = CreateCustomCharacterImagePath(customCharacter.InternalName, fileExtension);


        var copiedFile = newImageFile.CopyTo(copiedImagePath.LocalPath, true);

        jsonCustomCharacter.Image = copiedFile.FullName;
        await SaveSettingsAsync().ConfigureAwait(false);
        customCharacter.ImageUri = copiedImagePath;
    }

    internal async Task DeleteCustomCharacterAsync(InternalName internalName)
    {
        await ReadSettingsAsync(useCache: false).ConfigureAwait(false);

        if (!_settings.CustomCharacters.Remove(internalName, out var customCharacter))
            throw new InvalidOperationException("Character to delete not found. Try restarting OMniX");

        if (!customCharacter.Image.IsNullOrEmpty())
        {
            var imageFilePath = customCharacter.Image;

            if (File.Exists(imageFilePath))
                File.Delete(imageFilePath);
            else
                _logger.Warning("Image file for {InternalName} not found at {ImageFilePath}", internalName, imageFilePath);
        }


        await SaveSettingsAsync().ConfigureAwait(false);
    }
}

internal class GameServiceRoot
{
    public Dictionary<string, JsonCharacterOverride> CharacterOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonCustomCharacter> CustomCharacters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    internal GameServiceRootUntyped ToJson()
    {
        var characterOverrides = CharacterOverrides.ToDictionary(kv => kv.Key, kv => (object)kv.Value, comparer: StringComparer.OrdinalIgnoreCase);

        var customModObjects = new Dictionary<string, Dictionary<string, object>>
        {
            {
                Category.CreateForCharacter().InternalName,
                CustomCharacters.ToDictionary(kv => kv.Key, kv => (object)kv.Value, comparer: StringComparer.OrdinalIgnoreCase)
            }
        };


        return new GameServiceRootUntyped
        {
            CharacterOverrides = characterOverrides,
            CustomModObjects = customModObjects
        };
    }
}

internal class GameServiceRootUntyped
{
    public Dictionary<string, object> CharacterOverrides { get; set; } = new();
    public Dictionary<string, Dictionary<string, object>> CustomModObjects { get; set; } = new();
}

internal class JsonCharacterOverride
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? DisplayName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsDisabled { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string[]? Keys { get; set; }

    /// <summary>
    /// Full image path
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsMultiMod { get; set; }

    /// <summary>
    ///     二开：所属属性。空字符串表示「其他」（无属性）。
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Element { get; set; }
}

internal class JsonCustomCharacter
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? DisplayName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]

    public string[]? Keys { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]

    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Full image path
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? Rarity { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Element { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Class { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string[]? Region { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? ModFilesName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsMultiMod { get; set; }


    public static JsonCustomCharacter FromCharacter(ICharacter customCharacter)
    {
        var jsonCustomCharacter = new JsonCustomCharacter
        {
            DisplayName = customCharacter.DisplayName,
            Keys = customCharacter.Keys.ToArray(),
            ReleaseDate = customCharacter.ReleaseDate == null || customCharacter.ReleaseDate == DateTime.MinValue ||
                          customCharacter.ReleaseDate == DateTime.MaxValue
                ? null
                : customCharacter.ReleaseDate.Value.ToString("O"),
            Image = customCharacter.ImageUri != null ? customCharacter.ImageUri.LocalPath : null,
            Rarity = customCharacter.Rarity,
            Element = customCharacter.Element.Equals(Models.Element.NoneElement()) ? null : customCharacter.Element.InternalName.ToString(),
            Class = customCharacter.Class.Equals(Models.Class.NoneClass()) ? null : customCharacter.Class.InternalName.ToString(),
            Region = customCharacter.Regions.Count == 0 ? null : customCharacter.Regions.Select(r => r.InternalName.ToString()).ToArray(),
            ModFilesName = customCharacter.ModFilesName == "" ? null : customCharacter.ModFilesName,
            IsMultiMod = customCharacter.IsMultiMod == false ? null : customCharacter.IsMultiMod
        };

        return jsonCustomCharacter;
    }


    //[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    //public JsonCustomCharacterSkin[]? InGameSkins { get; set; }
}

//internal class JsonCustomCharacterSkin : JsonBaseNameable
//{
//    public string? ModFilesName { get; set; }

//    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
//    public string? Image { get; set; }

//    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
//    public string? ReleaseDate { get; set; }

//    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
//    public int? Rarity { get; set; }
//}