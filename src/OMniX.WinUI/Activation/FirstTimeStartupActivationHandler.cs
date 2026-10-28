using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.Services;
using OMniX.Core.Services.CommandService;
using OMniX.Core.Services.GameBanana;
using OMniX.Core.Services.ModPresetService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml;

namespace OMniX.WinUI.Activation;

/// <summary>
/// FirstTimeStartupActivationHandler is completely wrong name for this class. This is the default startup handler.
/// </summary>
public class FirstTimeStartupActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly ModPresetService _modPresetService;
    private readonly UserPreferencesService _userPreferencesService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModArchiveRepository _modArchiveRepository;
    private readonly CommandService _commandService;

    /// <summary>二开：游戏资源包根目录（GamesData）</summary>
    private readonly GameDataRootService _dataRoot;

    public override string ActivationName { get; } = "RegularStartup";

    public FirstTimeStartupActivationHandler(INavigationService navigationService,
        ILocalSettingsService localSettingsService,
        ISkinManagerService skinManagerService, IGameService gameService, SelectedGameService selectedGameService,
        ModPresetService modPresetService, UserPreferencesService userPreferencesService,
        ModArchiveRepository modArchiveRepository, CommandService commandService,
        GameDataRootService dataRoot)
    {
        _navigationService = navigationService;
        _localSettingsService = localSettingsService;
        _skinManagerService = skinManagerService;
        _gameService = gameService;
        _selectedGameService = selectedGameService;
        _modPresetService = modPresetService;
        _userPreferencesService = userPreferencesService;
        _modArchiveRepository = modArchiveRepository;
        _commandService = commandService;
        _dataRoot = dataRoot;
    }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        var options = Task
            .Run(async () => await _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section))
            .GetAwaiter().GetResult();

        return Directory.Exists(options?.ModsFolderPath) && Directory.Exists(options?.GimiRootFolderPath);
    }

    protected override async Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        var modManagerOptions =
            await _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        // 二开：插件目录要在启动时就应用，不能等用户进设置页才生效 ——
        // 否则用户把插件挪到 D 盘、重启后直接去插件页下载，又装回 C 盘了。
        PluginDependencyService.SetPluginsRoot(modManagerOptions?.PluginsFolderPath);

        var gameServiceOptions = new InitializationOptions
        {
            // 二开：资源走 GamesData 里的包（管理功能仍在 Core）
            AssetsDirectory = GameAssetsLocator.Resolve(_dataRoot,
                await _selectedGameService.GetSelectedGameAsync()),
            LocalSettingsDirectory = _localSettingsService.ApplicationDataFolder,
            CharacterSkinsAsCharacters = modManagerOptions?.CharacterSkinsAsCharacters ?? false
        };

        await Task.Run(async () =>
        {
            var modArchiveSettings =
                await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

            await _gameService.InitializeAsync(gameServiceOptions).ConfigureAwait(false);

            await _skinManagerService.InitializeAsync(modManagerOptions!.ModsFolderPath!, null,
                modManagerOptions.GimiRootFolderPath).ConfigureAwait(false);

            var tasks = new List<Task>
            {
                _userPreferencesService.InitializeAsync(),
                _modPresetService.InitializeAsync(_localSettingsService.ApplicationDataFolder),
                _modArchiveRepository.InitializeAsync(_localSettingsService.ApplicationDataFolder,
                    o => o.MaxDirectorySizeGb = modArchiveSettings.MaxLocalArchiveCacheSizeGb),
                _commandService.InitializeAsync(_localSettingsService.ApplicationDataFolder)
            };

            await Task.WhenAll(tasks).ConfigureAwait(false);
        });


        _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!,
            _gameService.GetCategories().First(c => c.InternalNameEquals("Character")), true);
    }
}