using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;
using OMniX.Core.Services;
using OMniX.Core.Services.GameBanana;
using OMniX.Core.Services.GameBanana.Models;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using Microsoft.UI.Dispatching;
using Serilog;

namespace OMniX.WinUI.ViewModels.ModPageViewModels;

public partial class ModPageVM : ObservableRecipient
{
    private readonly GameBananaCoreService _gameBananaCoreService = App.GetService<GameBananaCoreService>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ModNotificationManager _modNotificationManager = App.GetService<ModNotificationManager>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly ModInstallerService _modInstallerService = App.GetService<ModInstallerService>();
    private readonly ArchiveService _archiveService = App.GetService<ArchiveService>();
    private readonly IArchivePasswordPromptService _archivePasswordPromptService =
        App.GetService<IArchivePasswordPromptService>();

    private readonly DispatcherQueue _dispatcherQueue;
    private ICharacterModList _characterModList = null!;
    private readonly IModdableObject _moddableObject;
    private readonly WindowEx _window;
    private List<ModFileInfo> _modFiles = new();
    private GbModId _gbModId = null!;
    private readonly CancellationToken _ct;

    private ModPageInfo? _modPageInfo;

    [ObservableProperty] private string _initializing = "true";

    [ObservableProperty] private string _modName = string.Empty;

    [ObservableProperty] private Uri _modPage = new("https://gamebanana.com/");

    [ObservableProperty] private Uri? _characterModListPath = null;

    [ObservableProperty] private bool _isOpenDownloadButtonEnabled = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand), nameof(StartDownloadCommand),
        nameof(StartInstallCommand))]
    private bool _isWindowBusy = false;

    public bool IsNotBusy => !IsWindowBusy;


    public readonly ObservableCollection<ModFileInfoVm> ModFileInfos = new();

    /// <summary>安装目标角色可选列表（当前游戏的全部可装角色/NPC/物件）。</summary>
    public ObservableCollection<IModdableObject> InstallTargets { get; } = new();

    [ObservableProperty] private IModdableObject? _selectedInstallTarget;

    /// <summary>识别状态提示（自动匹配成功 / 需手动选择）。</summary>
    [ObservableProperty] private string _detectStatusText = string.Empty;

    /// <summary>网页面包屑文本（如 Skins › Suoming），对齐 gamebanana.com 页头。</summary>
    public string BreadcrumbText =>
        string.IsNullOrWhiteSpace(SuperCategoryText)
            ? string.Empty
            : $"{SuperCategoryText} › {CategoryText}";

    [ObservableProperty] private string _superCategoryText = string.Empty;
    [ObservableProperty] private string _categoryText = string.Empty;

    partial void OnSuperCategoryTextChanged(string value) =>
        OnPropertyChanged(nameof(BreadcrumbText));

    partial void OnCategoryTextChanged(string value) =>
        OnPropertyChanged(nameof(BreadcrumbText));

    /// <summary>当前正在下载的文件名（无下载时为空）。</summary>
    [ObservableProperty] private string? _currentDownloadFile;

    /// <summary>当前下载进度（0-100）。</summary>
    [ObservableProperty] private int _currentDownloadProgress;

    /// <summary>底部全局下载进度条可见性。</summary>
    public Microsoft.UI.Xaml.Visibility DownloadingVisibility =>
        CurrentDownloadFile is null
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    partial void OnCurrentDownloadFileChanged(string? value) =>
        OnPropertyChanged(nameof(DownloadingVisibility));

    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<ModUpdateVM>();

    public ModPageVM(Uri modPage, IModdableObject moddableObject, WindowEx window, CancellationToken ctsToken)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _moddableObject = moddableObject;
        ModPage = modPage;
        _window = window;
        _window.Title = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_DownloadTitle", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_DownloadTitle", "下载 Mod 文件"));
        _ct = ctsToken;
        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            await InternalInitialize();
            Initializing = "false";
        }
        catch (OperationCanceledException)
        {
            _window.Close();
        }
        catch (Exception e)
        {
            await LogErrorAndClose(e);
        }
    }


    private async Task InternalInitialize()
    {
        IsWindowBusy = true;

        if (GameBananaUrlHelper.TryGetModIdFromUrl(ModPage, out var modId))
            _gbModId = modId;
        else
            await LogErrorAndClose(new InvalidGameBananaUrlException($"Invalid GameBanana url: {ModPage}"));

        PopulateInstallTargets();

        if (!await _gameBananaCoreService.HealthCheckAsync(_ct))
        {
            await LogErrorAndClose(
                new InvalidOperationException("Failed to get mod page info, GameBanana Api is not available"));
            return;
        }

        using (var _ = IgnorePollyLimiterScope.Ignore())
        {
            _modPageInfo = await _gameBananaCoreService.GetModProfileAsync(_gbModId, _ct);
        }

        if (_modPageInfo is null)
        {
            await LogErrorAndClose(new InvalidOperationException("Failed to get mod page info, mod does not exist"));
            return;
        }

        _window.Title = $"Downloads for: {_modPageInfo.ModName}";
        ModName = _modPageInfo.ModName ?? string.Empty;

        // 按网页面包屑的分类名自动识别角色（识别不到保持传入对象，可手动改）
        SuperCategoryText = _modPageInfo.SuperCategoryName ?? "";
        CategoryText = _modPageInfo.CategoryName ?? "";
        AutoDetectInstallTarget();

        _modFiles = _modPageInfo.Files.ToList();

        foreach (var modFile in _modFiles)
        {
            var vm = new ModFileInfoVm(modFile, StartDownloadCommand, StartInstallCommand)
            {
                IsBusy = true
            };
            ModFileInfos.Add(vm);
            await InitializeModFileVmAsync(vm);
        }

        IsWindowBusy = false;
    }

    /// <summary>装填当前游戏全部可安装目标（角色 / NPC / 物件）。</summary>
    private void PopulateInstallTargets()
    {
        try
        {
            var gameService = App.GetService<IGameService>();
            var targets = gameService.GetModdableObjects(_moddableObject.ModCategory, GetOnly.Both);
            foreach (var target in targets)
            {
                InstallTargets.Add(target);
            }
        }
        catch (Exception e)
        {
            _logger.Warning(e, "加载安装目标角色列表失败");
        }

        if (InstallTargets.Count == 0)
        {
            InstallTargets.Add(_moddableObject);
        }
    }

    /// <summary>
    ///     用网页分类名（如 Suoming）匹配安装目标：InternalName / Keys（json 别名）/
    ///     显示名三通道精确匹配（忽略大小写空格），退化前后缀；还不中就按
    ///     InternalName 全量查（含未启用角色，锁暝这类新角色常被禁用过滤掉）。
    /// </summary>
    private void AutoDetectInstallTarget()
    {
        var gbName = _modPageInfo?.CategoryName;
        if (string.IsNullOrWhiteSpace(gbName))
        {
            SelectedInstallTarget = _moddableObject;
            DetectStatusText = string.Empty;
            return;
        }

        var normalized = NormalizeName(gbName);
        var target = MatchTarget(normalized);

        if (target is null)
        {
            try
            {
                if (App.GetService<IGameService>()
                        .GetCharacterByIdentifier(gbName, includeDisabledCharacters: true) is { } character)
                {
                    if (!InstallTargets.Contains(character))
                    {
                        InstallTargets.Insert(0, character);
                    }

                    target = character;
                }
            }
            catch (Exception e)
            {
                _logger.Warning(e, "按 InternalName 兜底匹配角色失败");
            }
        }

        SelectedInstallTarget = target ?? _moddableObject;
        DetectStatusText = target is not null
            ? $"已按网页分类「{gbName}」识别到角色「{target.DisplayName}」，可手动更改"
            : $"网页分类「{gbName}」未匹配到角色，请手动选择";
    }

    private IModdableObject? MatchTarget(string normalized)
    {
        var exact = InstallTargets.FirstOrDefault(t =>
            NormalizeName(TargetName(t)) == normalized
            || (t is ICharacter character && character.Keys.Any(k => NormalizeName(k) == normalized))
            || NormalizeName(t.DisplayName) == normalized);
        if (exact is not null)
        {
            return exact;
        }

        // 前后缀退化：应对 GB 复合分类名（如 "Yangyang: Xuanling"）
        return InstallTargets.FirstOrDefault(t =>
            TargetName(t) is { Length: > 0 } name
            && (normalized.StartsWith(NormalizeName(name), StringComparison.Ordinal)
                || NormalizeName(name).StartsWith(normalized, StringComparison.Ordinal)));
    }

    private static string TargetName(IModdableObject target) =>
        target is ICharacter character ? character.InternalName.ToString() : target.ModFilesName;

    private static string NormalizeName(string name) =>
        name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

    partial void OnSelectedInstallTargetChanged(IModdableObject? value)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            _characterModList = _skinManagerService.GetCharacterModList(value);
            CharacterModListPath = new Uri(_characterModList.AbsModsFolderPath);
        }
        catch (Exception e)
        {
            _logger.Warning(e, "切换安装目标失败");
        }
    }

    private Task LogErrorAndClose(Exception e)
    {
        _logger.Error(e, App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_ReadUpdateFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_ReadUpdateFailed", "读取 Mod 更新信息失败")));
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.GetService<NotificationManager>().ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_ReadUpdateFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_ReadUpdateFailed", "读取 Mod 更新信息失败")),
                e.Message, TimeSpan.FromSeconds(10));
        });
        _window.Close();
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task CloseAsync()
    {
        _window.Close();
        return Task.CompletedTask;
    }


    private async Task InitializeModFileVmAsync(ModFileInfoVm fileInfoVm)
    {
        ArgumentNullException.ThrowIfNull(fileInfoVm);

        var existingArchive = await _gameBananaCoreService.GetLocalModArchiveByMd5HashAsync(fileInfoVm.Md5Hash, _ct);

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (existingArchive is not null)
            {
                fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
                fileInfoVm.DownloadProgress = 100;
                fileInfoVm.ArchiveFile = new FileInfo(existingArchive.FullName);
            }

            fileInfoVm.IsBusy = false;
        });
    }

    private bool CanStartDownload(ModFileInfoVm? fileInfoVm)
    {
        if (fileInfoVm is null)
            return false;

        var anyOtherDownloading = ModFileInfos.Any(x =>
            fileInfoVm.FileId != x.FileId && x.Status == ModFileInfoVm.InstallStatus.Downloading);

        var canDownload = IsNotBusy && !fileInfoVm.IsBusy && fileInfoVm.Status == ModFileInfoVm.InstallStatus.NotStarted
                          && !anyOtherDownloading && fileInfoVm.ArchiveFile is null &&
                          !fileInfoVm.ModId.IsNullOrEmpty() && !fileInfoVm.FileId.IsNullOrEmpty();


        return canDownload;
    }

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task StartDownload(ModFileInfoVm fileInfoVm)
    {
        try
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloading;
            fileInfoVm.IsBusy = true;

            var identifier = new GbModFileIdentifier(new GbModId(fileInfoVm.ModId), new GbModFileId(fileInfoVm.FileId));

            // 进度同时喂给卡片和底部全局进度条
            var combinedProgress = new Progress<int>(p =>
            {
                fileInfoVm.DownloadProgress = p;
                CurrentDownloadFile = fileInfoVm.FileName;
                CurrentDownloadProgress = p;
            });

            var archivePath =
                await Task.Run(() => _gameBananaCoreService.DownloadModAsync(identifier, combinedProgress, _ct),
                    _ct);

            fileInfoVm.ArchiveFile = new FileInfo(archivePath);
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        catch (Exception) when (_ct.IsCancellationRequested)
        {
            Reset();
        }
        catch (Exception e)
        {
            _logger.Error(e, App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_DownloadFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_DownloadFailed", "下载 Mod 文件失败")));

            _notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_DownloadFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_DownloadFailed", "下载 Mod 文件失败")),
                e.Message, TimeSpan.FromSeconds(10));

            Reset();
        }
        finally
        {
            fileInfoVm.IsBusy = false;
            CurrentDownloadFile = null;
        }

        void Reset()
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.NotStarted;
            fileInfoVm.ArchiveFile = null;
            fileInfoVm.DownloadProgress = 0;
        }
    }

    private bool CanInstall(ModFileInfoVm? fileInfoVm)
    {
        if (fileInfoVm is null)
            return false;

        var anyOtherInstalling = ModFileInfos.Any(x =>
            fileInfoVm.FileId != x.FileId && x.Status == ModFileInfoVm.InstallStatus.Installing);

        var canInstall = IsNotBusy && !fileInfoVm.IsBusy &&
                         fileInfoVm.Status == ModFileInfoVm.InstallStatus.Downloaded &&
                         !anyOtherInstalling && fileInfoVm.ArchiveFile is not null;

        return canInstall;
    }


    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task StartInstall(ModFileInfoVm fileInfoVm)
    {
        IsWindowBusy = true;
        fileInfoVm.IsBusy = true;

        fileInfoVm.Status = ModFileInfoVm.InstallStatus.Installing;

        try
        {
            var result = await Task.Run(async () =>
            {
                // 压缩包加密时会弹窗要求输入密码
                var modFolder = await ArchiveExtractionHelper.ExtractWithPasswordPromptAsync(
                    _archiveService,
                    _archivePasswordPromptService,
                    _logger,
                    fileInfoVm.ArchiveFile!.FullName,
                    App.GetUniqueTmpFolder().FullName,
                    _window);

                var archiveNameSections = Path.GetFileName(modFolder.Name).Split(ModArchiveRepository.Separator);
                if (archiveNameSections.Length != 4)
                    throw new InvalidArchiveNameFormatException();

                var modFolderName = archiveNameSections[0];
                var modFolderExt = Path.GetExtension(modFolder.Name);

                var modFolderParent = modFolder.Parent!;

                var zipRoot = Directory.CreateDirectory(Path.Combine(modFolderParent.FullName, "ArchiveRoot"));

                modFolder.MoveTo(Path.Combine(zipRoot.FullName, $"{modFolderName}{modFolderExt}"));

                var modUrl = _modPageInfo?.ModPageUrl;

                using var task = await _modInstallerService.StartModInstallationAsync(zipRoot, _characterModList,
                    setup: options =>
                    {
                        options.ModUrl = modUrl;
                    }).ConfigureAwait(false);

                return await task.WaitForCloseAsync(_ct).ConfigureAwait(false);
            }, _ct);

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Error)
            {
                if (result.Exception is not null)
                {
                    throw new Exception("An error occured during mod install, see logs and inner exception",
                        result.Exception);
                }

                throw new Exception("An error occured during mod install, see logs");
            }

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Canceled)
            {
                fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
                return;
            }

            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Installed;
        }
        catch (TaskCanceledException)
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        catch (Exception e)
        {
            _logger.Error(e, App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_InstallFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_InstallFailed", "安装 Mod 文件失败")));

            _notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_InstallFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mp_InstallFailed", "安装 Mod 文件失败")),
                e.InnerException?.Message ?? e.Message, TimeSpan.FromSeconds(10));

            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        finally
        {
            IsWindowBusy = false;
            fileInfoVm.IsBusy = false;
        }
    }
}