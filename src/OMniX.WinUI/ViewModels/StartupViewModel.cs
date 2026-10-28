using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.WinUI.Helpers;
using OMniX.Core.Helpers;
using OMniX.Core.Services;
using OMniX.Core.Services.CommandService;
using OMniX.Core.Services.CommandService.Models;
using OMniX.Core.Services.GameBanana;
using OMniX.Core.Services.ModPresetService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Validators;
using OMniX.WinUI.Validators.PreConfigured;
using OMniX.WinUI.ViewModels.SubVms;
using Serilog;

namespace OMniX.WinUI.ViewModels;

public partial class StartupViewModel : ObservableRecipient, INavigationAware
{
    /// <summary>
    ///     二开：用户选择"跳过首次启动配置"后写入该标记，下次启动不再强制进入向导
    /// </summary>
    public const string SkipStartupWizardKey = "SkipStartupWizard";

    private readonly INavigationService _navigationService;
    private readonly ILogger _logger = Log.ForContext<StartupViewModel>();
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly ModPresetService _modPresetService;
    private readonly UserPreferencesService _userPreferencesService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModArchiveRepository _modArchiveRepository;
    private readonly CommandService _commandService;
    private readonly ThreeDMigtoProcessManager _modelImporterProcessManager;
    private readonly ILanguageLocalizer _localizer;

    /// <summary>
    ///     上次自动推导的 Mods 文件夹，用于判断是否可以继续自动填写
    /// </summary>
    private string? _suggestedModsFolder;

    /// <summary>
    ///     二开：已经提示过的 Mod 文件夹，避免同一个路径反复弹窗
    /// </summary>
    private string? _lastDetectedModsFolder;




    public PathPicker PathToGIMIFolderPicker { get; }

    public PathPicker PathToModsFolderPicker { get; }
    [ObservableProperty] private bool _reorganizeModsOnStartup;
    [ObservableProperty] private bool _disableMods;

    [ObservableProperty]
    private GameComboBoxEntryVM _selectedGame = new GameComboBoxEntryVM(EnabledGames.Default)
    {
        GameName = "Wuthering Waves",
        GameShortName = EnabledGames.Default.ToString(),
        GameIconPath = null!
    };

    [ObservableProperty] private string _modelImporterName = "Wuthering Waves Model Importer";
    [ObservableProperty] private string _modelImporterShortName = "WWMI";

    /// <summary>
    ///     是否已进入某个游戏的设置（false 时显示游戏图标选择界面）
    /// </summary>
    [ObservableProperty] private bool _isGameSetupVisible;

    /// <summary>
    ///     二开：在启动器文件夹（GIMI 根文件夹）中识别到的启动程序路径
    /// </summary>
    [ObservableProperty] private string? _launcherExecutablePath;

    [ObservableProperty] private bool _hasLauncherExecutable;

    /// <summary>
    ///     Mods 文件夹推导提示
    /// </summary>
    [ObservableProperty] private string _modsFolderHint = string.Empty;

    /// <summary>
    ///     是否显示游戏图标选择界面（取 <see cref="IsGameSetupVisible" /> 的反值）
    /// </summary>
    [ObservableProperty] private bool _isGameSelectionVisible = true;

    [ObservableProperty] private Uri _modelImporterUrl = new("https://github.com/SilentNightSound");

    public ObservableCollection<GameComboBoxEntryVM> Games { get; } = new();

    /// <summary>二开：游戏资源包根目录（GamesData）</summary>
    private readonly GameDataRootService _dataRoot;

    public StartupViewModel(INavigationService navigationService, ILocalSettingsService localSettingsService,
        IWindowManagerService windowManagerService, ISkinManagerService skinManagerService,
        SelectedGameService selectedGameService, IGameService gameService, ModPresetService modPresetService,
        UserPreferencesService userPreferencesService, ModArchiveRepository modArchiveRepository,
        CommandService commandService, ThreeDMigtoProcessManager modelImporterProcessManager,
        ILanguageLocalizer localizer, GameDataRootService dataRoot)
    {
        _navigationService = navigationService;
        _dataRoot = dataRoot;
        _localSettingsService = localSettingsService;
        _windowManagerService = windowManagerService;
        _skinManagerService = skinManagerService;
        _selectedGameService = selectedGameService;
        _gameService = gameService;
        _modPresetService = modPresetService;
        _userPreferencesService = userPreferencesService;
        _modArchiveRepository = modArchiveRepository;
        _commandService = commandService;
        _modelImporterProcessManager = modelImporterProcessManager;
        _localizer = localizer;

        PathToGIMIFolderPicker = new PathPicker([]);

        PathToModsFolderPicker =
            new PathPicker(ModsFolderValidator.Validators);

        PathToGIMIFolderPicker.IsValidChanged += (sender, args) => SaveStartupSettingsCommand.NotifyCanExecuteChanged();
        PathToModsFolderPicker.IsValidChanged +=
            (sender, args) => SaveStartupSettingsCommand.NotifyCanExecuteChanged();
    }


    private bool ValidStartupSettings() => PathToGIMIFolderPicker.IsValid && PathToModsFolderPicker.IsValid &&
                                           PathToGIMIFolderPicker.Path != PathToModsFolderPicker.Path;


    [RelayCommand(CanExecute = nameof(ValidStartupSettings))]
    private async Task SaveStartupSettings()
    {
        var modManagerOptions = new ModManagerOptions()
        {
            GimiRootFolderPath = PathToGIMIFolderPicker.Path,
            ModsFolderPath = PathToModsFolderPicker.Path,
            UnloadedModsFolderPath = null
        };

        await _selectedGameService.SetSelectedGame(SelectedGame.Value.ToString());

        // 二开：资源从 GamesData 的包里读（管理功能仍在 Core），包不存在才回内置
        await _gameService.InitializeAsync(
            GameAssetsLocator.Resolve(_dataRoot, await _selectedGameService.GetSelectedGameAsync()),
            _localSettingsService.ApplicationDataFolder);

        await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section,
            modManagerOptions);
        _logger.Information("Saved startup settings: {@ModManagerOptions}", modManagerOptions);

        await _skinManagerService.InitializeAsync(modManagerOptions.ModsFolderPath!, null,
            modManagerOptions.GimiRootFolderPath);

        var modArchiveSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

        var tasks = new List<Task>
        {
            _userPreferencesService.InitializeAsync(),
            _modPresetService.InitializeAsync(_localSettingsService.ApplicationDataFolder),
            _modArchiveRepository.InitializeAsync(_localSettingsService.ApplicationDataFolder,
                o => o.MaxDirectorySizeGb = modArchiveSettings.MaxLocalArchiveCacheSizeGb),
            _commandService.InitializeAsync(_localSettingsService.ApplicationDataFolder)
        };

        await Task.WhenAll(tasks);

        // 二开：将识别到的启动器 exe 注册为模型导入器启动命令，之后可在 OMniX 内直接启动
        await SaveLauncherCommandAsync();

        // 二开：配置已完成，清除跳过标记
        await _localSettingsService.SaveSettingAsync(SkipStartupWizardKey, false);

        await _localSettingsService.SaveSettingAsync(ActivationService.IgnoreNewFolderStructureKey, true);

        if (ReorganizeModsOnStartup)
        {
            await Task.Run(() => _skinManagerService.ReorganizeModsAsync(disableMods: DisableMods));
        }


        // 二开：配置完回首页。首页是外壳的中心，路径配好之后那边会自己显示状态。
        _navigationService.NavigateTo(typeof(HomeViewModel).FullName!, null, true);
        App.GetService<NotificationManager>().ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_NotificationSettingsSaved", "启动设置已保存"),
            $"启动设置已保存 successfully to '{_localSettingsService.GameScopedSettingsLocation}'",
            TimeSpan.FromSeconds(7));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        {
            await Task.Delay(TimeSpan.FromSeconds(7));
            App.GetService<NotificationManager>().ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_NotificationAlpha", "OMniX 还在 alpha 阶段"),
                "There will be bugs and things will most likely break. Anyway, hope you enjoy using OMniX!",
                TimeSpan.FromSeconds(20));
        });
    }


    /// <summary>
    ///     二开：把启动器根目录中识别到的 exe 保存为"启动模型导入器/启动器"命令
    /// </summary>
    private async Task SaveLauncherCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(LauncherExecutablePath) || !File.Exists(LauncherExecutablePath))
            return;

        try
        {
            var commandDefinition = new CommandDefinition
            {
                CommandDisplayName = $"Start {ModelImporterShortName}",
                KillOnMainAppExit = false,
                ExecutionOptions = new CommandExecutionOptions
                {
                    UseShellExecute = true,
                    RunAsAdmin = true,
                    Command = LauncherExecutablePath,
                    Arguments = null,
                    WorkingDirectory = Path.GetDirectoryName(LauncherExecutablePath),
                    CreateWindow = true
                }
            };

            await _modelImporterProcessManager.SetCommandAsync(commandDefinition);
            _logger.Information("Registered launcher command: {LauncherExecutable}", LauncherExecutablePath);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not register launcher command for {LauncherExecutable}", LauncherExecutablePath);
        }
    }

    [RelayCommand]
    private async Task BrowseLauncherFolderAsync()
    {
        await PathToGIMIFolderPicker.BrowseFolderPathAsync(App.MainWindow);

        // 二开：选择的文件夹为启动器根目录，自动识别启动程序与 Mods 文件夹
        PathToGIMIFolderPicker.Validate(PathToGIMIFolderPicker.Path);
        UpdateDerivedPaths();
        await DetectAndApplyModsFolderAsync();
    }


    [RelayCommand]
    private async Task BrowseModsFolderAsync()
        => await PathToModsFolderPicker.BrowseFolderPathAsync(App.MainWindow);

    public async void OnNavigatedTo(object parameter)
    {
        // 二开：进配置页不再自动缩放窗口，全程保持用户当前的窗口大小

        var settings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        await SetGameComboBoxValues();

        SetSelectedGame(await _selectedGameService.GetSelectedGameAsync());
        await SetGameInfo(SelectedGame.Value.ToString());
        SetPaths(settings);
        ReorganizeModsOnStartup = true;

        // 二开：只启用了一个游戏时，直接进入该游戏的设置，省掉"选择游戏"那一步
        IsGameSetupVisible = EnabledGames.All.Length <= 1;
    }

    [RelayCommand]
    private async Task SetGameAsync(string game)
    {
        if (game.IsNullOrEmpty())
            return;

        await _selectedGameService.SetSelectedGame(game);
        SetSelectedGame(game);

        var settings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        await SetGameInfo(game);
        SetPaths(settings);
        IsGameSetupVisible = true;
    }

    /// <summary>
    ///     二开：点击游戏图标卡片后进入该游戏的设置
    /// </summary>
    [RelayCommand]
    private async Task SelectGameAsync(GameComboBoxEntryVM game)
    {
        if (game is null)
            return;

        await SetGameAsync(game.Value.ToString());
    }

    /// <summary>
    ///     二开：返回游戏图标选择界面
    /// </summary>
    [RelayCommand]
    private void BackToGameSelection() => IsGameSetupVisible = false;

    /// <summary>
    ///     二开：返回首页（原"跳过"会直接跳设置页，未配置游戏时设置页会崩）。
    ///     首页顶部有引导卡，随时可以再回来配置。
    /// </summary>
    [RelayCommand]
    private void SkipStartupWizard()
    {
        // 二开：不改变窗口大小，直接回首页
        _navigationService.NavigateTo(typeof(HomeViewModel).FullName!);
    }

    /// <summary>
    ///     二开：启动器文件夹（原 3Dmigoto 文件夹）路径变化时，
    ///     重新识别启动程序路径并自动推导 Mods 文件夹
    /// </summary>
    public async void OnLauncherRootPathChanged(string? path)
    {
        PathToGIMIFolderPicker.Validate(path);
        UpdateDerivedPaths();
        await DetectAndApplyModsFolderAsync();
    }

    /// <summary>
    ///     二开：识别启动器目录里实际存在的 Mod 文件夹，弹窗提示并询问是否重命名为规范名字
    /// </summary>
    private async Task DetectAndApplyModsFolderAsync()
    {
        var launcherRootFolder = PathToGIMIFolderPicker.Path;
        if (string.IsNullOrWhiteSpace(launcherRootFolder))
            return;

        var detected = LauncherLocator.TryDetectExistingModsFolder(launcherRootFolder, ModelImporterShortName);
        if (detected is null)
            return;

        // 同一个路径只提示一次
        if (string.Equals(detected.FolderPath, _lastDetectedModsFolder, StringComparison.OrdinalIgnoreCase))
            return;

        var currentModsFolder = PathToModsFolderPicker.Path;

        // 已经识别到、名字也规范、和当前填写的一致 → 静默使用，不打扰
        if (detected.IsWellNamed &&
            string.Equals(detected.FolderPath, currentModsFolder, StringComparison.OrdinalIgnoreCase))
        {
            _lastDetectedModsFolder = detected.FolderPath;
            return;
        }

        _lastDetectedModsFolder = detected.FolderPath;

        var canRename = !detected.IsWellNamed && !Directory.Exists(detected.WellNamedPath);

        var message = canRename
            ? string.Format(
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_DetectedMessageRename", "在下面这个位置识别到 {0} 个 Mod：\n\n{1}\n\n已自动设为 Mods 文件夹。要不要顺便把文件夹名统一成「{2}」？\n改名后的位置：{3}\n（只在原地改名，不会挪到别的地方，Mod 内容会一起搬过去）"),
                detected.ModCount, detected.FolderPath, LauncherLocator.ModsFolderName, detected.WellNamedPath)
            : string.Format(
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_DetectedMessagePlain", "在下面这个位置识别到 {0} 个 Mod：\n\n{1}\n\n已自动设为 Mods 文件夹。"),
                detected.ModCount, detected.FolderPath);

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_DetectedDialogTitle", "识别到 Mod 文件夹"),
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = canRename ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_DetectedDialogPrimaryRename", "改名并迁移 Mod") : null,
            SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_DetectedDialogSecondary", "只用这个文件夹，不改名"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_DetectedDialogClose", "不用它"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            try
            {
                Directory.Move(detected.FolderPath, detected.WellNamedPath);
                SetModsFolder(detected.WellNamedPath);
                _logger.Information("Mods folder renamed from {Old} to {New}", detected.FolderPath,
                    detected.WellNamedPath);
                App.GetService<NotificationManager>().ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_NotificationFolderRenamed", "Mods 文件夹已改名"),
                    string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_NotificationFolderRenamedBody", "已迁移到：{0}"), detected.WellNamedPath), TimeSpan.FromSeconds(8));
            }
            catch (Exception e)
            {
                _logger.Error(e, "Could not rename mods folder from {Old} to {New}", detected.FolderPath,
                    detected.WellNamedPath);

                // 改名失败时退回：仍然使用原文件夹
                SetModsFolder(detected.FolderPath);
                App.GetService<NotificationManager>().ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_NotificationFolderRenameFailed", "Mods 文件夹改名失败"),
                    string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_NotificationFolderRenameFailedBody", "已改用原文件夹：{0}"), detected.FolderPath), TimeSpan.FromSeconds(8));
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            SetModsFolder(detected.FolderPath);
        }
    }

    /// <summary>
    ///     二开：写入 Mods 文件夹路径并校验
    /// </summary>
    private void SetModsFolder(string path)
    {
        _suggestedModsFolder = path;
        PathToModsFolderPicker.Path = path;
        PathToModsFolderPicker.Validate(path);
    }

    partial void OnIsGameSetupVisibleChanged(bool value) => IsGameSelectionVisible = !value;

    private void UpdateDerivedPaths()
    {
        var launcherRootFolder = PathToGIMIFolderPicker.Path;

        LauncherExecutablePath = LauncherLocator.FindLauncherExecutable(launcherRootFolder);
        HasLauncherExecutable = !string.IsNullOrEmpty(LauncherExecutablePath);

        if (string.IsNullOrWhiteSpace(launcherRootFolder))
        {
            _suggestedModsFolder = null;
            ModsFolderHint = string.Empty;
            return;
        }

        var suggestedModsFolder = LauncherLocator.GetModsFolder(launcherRootFolder, ModelImporterShortName);
        ModsFolderHint = string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Startup_DefaultModsFolderHint", "默认：{0}"), suggestedModsFolder);

        // 仅在 Mods 文件夹为空或仍是上次自动推导的路径时才自动填写，避免覆盖用户手动输入
        if (PathToModsFolderPicker.PathHasValue &&
            !string.Equals(PathToModsFolderPicker.Path, _suggestedModsFolder, StringComparison.OrdinalIgnoreCase))
            return;

        _suggestedModsFolder = suggestedModsFolder;
        PathToModsFolderPicker.Path = suggestedModsFolder;
        PathToModsFolderPicker.Validate(suggestedModsFolder);
    }


    private async Task SetGameInfo(string game)
    {
        var gameInfo = await GameService.GetGameInfoAsync(Enum.Parse<SupportedGames>(game));

        if (gameInfo is null)
        {
            _logger.Error("Game info for {Game} is null", game);
            return;
        }

        ModelImporterName = gameInfo.GameModelImporterName;
        ModelImporterShortName = gameInfo.GameModelImporterShortName;
        ModelImporterUrl = gameInfo.GameModelImporterUrl;
        // 二开：这里校验的是启动器根目录，而非旧的 3Dmigoto 文件夹
        PathToGIMIFolderPicker.SetValidators(LauncherRootValidators.Validators(() => ModelImporterShortName));
    }

    private async Task SetGameComboBoxValues()
    {
        // 二开：列出外壳认识的全部内核（鸣潮 / 绝区零 / 原神 / 崩铁）。
        // 没下载的内核也能选 —— 选了只是把配置写进那个游戏的档案里，资源到了就能用。
        foreach (var supportedGame in EnabledGames.All)
        {
            var gameInfo = await GameService.GetGameInfoAsync(supportedGame);
            if (gameInfo is null)
                continue;

            Games.Add(new GameComboBoxEntryVM(supportedGame)
            {
                GameIconPath = new Uri(gameInfo.GameIcon),
                GameName = gameInfo.GameName,
                GameShortName = gameInfo.GameShortName
            });
        }
    }

    private void SetSelectedGame(string game)
    {
        var selectedGame = Games.FirstOrDefault(g => g.Value.ToString() == game);
        if (selectedGame is not null)
            SelectedGame = selectedGame;
    }


    private void SetPaths(ModManagerOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.GimiRootFolderPath))
            PathToGIMIFolderPicker.Path = settings.GimiRootFolderPath;
        else
            PathToGIMIFolderPicker.Path = "";

        if (!string.IsNullOrWhiteSpace(settings.ModsFolderPath))
            PathToModsFolderPicker.Path = settings.ModsFolderPath;
        else
            PathToModsFolderPicker.Path = "";
    }


    public void OnNavigatedFrom()
    {
    }
}