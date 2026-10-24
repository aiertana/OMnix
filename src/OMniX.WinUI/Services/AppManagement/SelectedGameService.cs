using OMniX.Core.GamesService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models.Options;
using Newtonsoft.Json;
using Serilog;

namespace OMniX.WinUI.Services.AppManagement;

public class SelectedGameService
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;

    private const string _defaultApplicationDataFolder = "ApplicationData";

    private readonly string _omnixAppDataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX");

    private const string ConfigFile = "game.json";
    private readonly string _configPath;

    private const string Genshin = "Genshin";
    private const string Honkai = "Honkai";
    private const string WuWa = "WuWa";
    private const string ZZZ = "ZZZ";


    public SelectedGameService(ILocalSettingsService localSettingsService, ILogger logger)
    {
        _localSettingsService = localSettingsService;
        _logger = logger;
        Directory.CreateDirectory(_omnixAppDataPath);
        _configPath = Path.Combine(_omnixAppDataPath, ConfigFile);
    }

    private string GetGameSpecificSettingsFolderName(string game)
    {
        return Path.Combine(_defaultApplicationDataFolder + "_" + game);
    }

    public async Task SetSelectedGame(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");

        if (await GetSelectedGameAsync() == game)
            return;

        _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(game));
        await SaveSelectedGameAsync(game).ConfigureAwait(false);
    }

    public async Task InitializeAsync()
    {
        if (!File.Exists(_configPath))
        {
            await SaveSelectedGameAsync(WuWa);
        }

        var selectedGame = await GetSelectedGameAsync();

        _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(selectedGame));
    }

    public async Task<string> GetSelectedGameAsync()
    {
        if (!File.Exists(_configPath))
            return WuWa;

        var selectedGame = JsonConvert.DeserializeObject<SelectedGameModel>(await File.ReadAllTextAsync(_configPath));

        if (selectedGame == null || !IsValidGame(selectedGame.SelectedGame))
            return WuWa;

        // 二开：配置里存的是暂未开放的游戏（原神/星铁/绝区零）时，统一回落到鸣潮
        if (!Enum.TryParse<SupportedGames>(selectedGame.SelectedGame, true, out var parsed) ||
            !EnabledGames.IsEnabled(parsed))
        {
            await SaveSelectedGameAsync(WuWa).ConfigureAwait(false);
            return WuWa;
        }

        return selectedGame.SelectedGame;
    }

    public Task<SupportedGames[]> GetNotSelectedGameAsync()
    {
        // 二开：目前只开放鸣潮，没有其它可切换的游戏
        return Task.FromResult(Array.Empty<SupportedGames>());
    }


    public Task SaveSelectedGameAsync(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");


        var selectedGame = new SelectedGameModel
        {
            SelectedGame = game
        };

        return File.WriteAllTextAsync(_configPath, JsonConvert.SerializeObject(selectedGame, Formatting.Indented));
    }

    public async Task<bool> IsOmniXInitializedForGameAsync(string game)
    {
        if (!IsValidGame(game))
            throw new ArgumentException("Invalid game name.");

        string? oldGame = null;
        if (!_localSettingsService.GameScopedSettingsLocation.Equals(GetGameSpecificSettingsFolderName(game),
                StringComparison.OrdinalIgnoreCase))
        {
            oldGame = game;
            _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(game));
        }


        var modManagerOptions = await Task
            .Run(() => _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section));

        var ret = modManagerOptions is not null && !string.IsNullOrEmpty(modManagerOptions.GimiRootFolderPath) &&
                  !string.IsNullOrEmpty(modManagerOptions.ModsFolderPath);

        if (oldGame != null)
            _localSettingsService.SetApplicationDataFolderName(GetGameSpecificSettingsFolderName(oldGame));
        return ret;
    }

    private bool IsValidGame(string game)
    {
        if (Enum.TryParse<SupportedGames>(game, out _))
            return true;

        return game is Genshin or Honkai or WuWa;
    }


    private void CopyOldAppFolder(string game)
    {
        var oldAppSettingsFolder = new DirectoryInfo(Path.Combine(_omnixAppDataPath, _defaultApplicationDataFolder));

        if (!oldAppSettingsFolder.Exists)
        {
            _logger.Information("Could not find old app folder. Skipping copy.");
            return;
        }

        _logger.Information("Copying old app settings folder to new name.");

        var newAppSettingsFolder =
            new DirectoryInfo(Path.Combine(_omnixAppDataPath, GetGameSpecificSettingsFolderName(game)));
        newAppSettingsFolder.Create();

        if (newAppSettingsFolder.GetFiles().Any())
        {
            _logger.Information("New app settings folder is not empty. Skipping copy.");
            return;
        }

        foreach (var file in oldAppSettingsFolder.GetFiles())
        {
            file.CopyTo(Path.Combine(newAppSettingsFolder.FullName, file.Name));
        }
    }
}

public class SelectedGameModel
{
    public string SelectedGame { get; set; } = "Genshin";
}