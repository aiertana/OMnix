using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.Views.Dialogs;
using Windows.UI;

namespace OMniX.WinUI.ViewModels;

/// <summary>
///     功能专区（原「插件专区」）。插件是独立于管理器的一份东西：
///     放在插件文件夹里，管理器只是扫一眼、显示出来，删掉插件文件管理器照常跑，
///     加载失败也只影响卡片本身，不会波及 Mod 管理和游戏启动。
/// </summary>
public partial class FeatureAreaViewModel : ObservableRecipient, INavigationAware
{
    /// <summary>「全局」头像的 Id：选它看所有插件（不分游戏），放全局通用的配置和插件。</summary>
    public const string GlobalId = "Global";
    // 两张开着用的插件卡统一用主色红（图标底色更淡一档），未上线的用更淡的灰。
    // 之前 Mod 加载那张是中性灰，和旁边 DLSS5 的红不一样，看着像没开，按你的要求统一了。
    private static readonly Color ActiveTint = Color.FromArgb(0x24, 0xE5, 0x48, 0x4D);
    private static readonly Color ActiveText = Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D);
    private static readonly Color MutedTint = Color.FromArgb(0x1A, 0x9A, 0xA0, 0xAE);
    private static readonly Color MutedText = Color.FromArgb(0xFF, 0x9A, 0xA0, 0xAE);

    private readonly PluginDependencyService _dependencies = App.GetService<PluginDependencyService>();
    private readonly Dlss5PluginService _dlss5 = App.GetService<Dlss5PluginService>();
    private readonly IWindowManagerService _windowManager = App.GetService<IWindowManagerService>();
    private readonly NotificationManager _notifications = App.GetService<NotificationManager>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private readonly GameKernelService _kernel = App.GetService<GameKernelService>();
    private readonly PluginCatalogService _catalog = App.GetService<PluginCatalogService>();
    private readonly GameDataRootService _dataRoot = App.GetService<GameDataRootService>();
    private readonly SelectedGameService _selectedGameService = App.GetService<SelectedGameService>();

    private PluginCard? _dlss5Card;
    private PluginCard? _modCard;
    private PluginCard? _fixerCard;

    /// <summary>顶部那一排游戏头像。点哪个，下面的资源列表就换成哪个游戏的。</summary>
    public ObservableCollection<GameAvatarItem> Games { get; } = new();

    [ObservableProperty] private GameAvatarItem? _selectedGame;
    [ObservableProperty] private string _kernelStatusText = string.Empty;
    [ObservableProperty] private bool _canInstallKernel;

    public Visibility CanInstallKernelVisibility =>
        CanInstallKernel ? Visibility.Visible : Visibility.Collapsed;

    partial void OnCanInstallKernelChanged(bool value) =>
        OnPropertyChanged(nameof(CanInstallKernelVisibility));

    /// <summary>当前在看的那个游戏内核</summary>
    private GameDefinition CurrentGame =>
        GameCatalog.ById(SelectedGame?.Id) ?? GameCatalog.Fallback;

    /// <summary>现在是不是在「全局」视图（看所有插件、管全局配置）</summary>
    public bool IsGlobalView =>
        string.Equals(SelectedGame?.Id, GlobalId, StringComparison.OrdinalIgnoreCase);

    public FeatureAreaViewModel()
    {
        // 二开：这里一行都别加。
        // 以前在构造函数里用 .GetAwaiter().GetResult() 去取游戏信息和当前游戏 ——
        // 那是在 UI 线程上干等异步，结果就是「一点功能专区整个界面卡死」。
        // 加载一律挪到 OnNavigatedToAsync 里 await。
    }

    /// <summary>
    ///     插件目录：全局一份，GamesData\Plugins。
    ///     插件适用于哪些游戏由 plugin.json 的 games 字段定，不靠放的位置。
    /// </summary>
    public string PluginsFolderPath => _catalog.PluginsDirectory();

    partial void OnSelectedGameChanged(GameAvatarItem? value) =>
        OnPropertyChanged(nameof(PluginsFolderPath));

    public ObservableCollection<PluginCard> Plugins { get; } = new();

    // 二开：导航框架调的是同步的 OnNavigatedTo，真正的加载丢给后台跑。
    // 不能在这里 .GetAwaiter().GetResult() —— 那会占住 UI 线程，页面直接卡死。
    public void OnNavigatedTo(object parameter) => _ = OnNavigatedToAsync(parameter);

    /// <summary>导航进来时才加载。全程 await，不占着 UI 线程。</summary>
    private async Task OnNavigatedToAsync(object parameter)
    {
        // 首页点灰态游戏跳过来时会带上游戏 Id，直接把头像栏选中它
        if (parameter is string gameId)
        {
            var match = Games.FirstOrDefault(game =>
                string.Equals(game.Id, gameId, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                SelectedGame = match;
        }

        // 旧版插件放在各游戏目录下，进来先搬一次到全局目录（幂等，搬过就跳过）
        await Task.Run(() => _catalog.EnsureMigrated());

        await LoadGamesAsync();
        Reload();
    }

    /// <summary>头像栏：四个内核，没下载的画成灰的。</summary>
    private async Task LoadGamesAsync()
    {
        var current = SelectedGame?.Id;
        var currentCore = await _selectedGameService.GetSelectedGameAsync();
        var items = new List<GameAvatarItem>();

        foreach (var game in GameCatalog.All)
        {
            // 图标直接从包目录读，不初始化游戏 —— 以前没加载的游戏全显示成白块
            var iconPath = GameAssetsLocator.ResolveIconPath(_dataRoot, game);

            items.Add(new GameAvatarItem
            {
                Id = game.Id,
                Name = _localizer.GetLocalizedStringOrDefault("/HomePreset/" + game.NameKey, game.FallbackName),
                IconPath = iconPath,
                Icon = TryLoadImage(iconPath),
                IsInstalled = _kernel.IsInstalled(game),
                IsCurrent = string.Equals(game.Core.ToString(), currentCore, StringComparison.OrdinalIgnoreCase)
            });
        }

        // 「全局」放最前面：看所有插件、管全局配置（GamesData\Plugins）
        items.Insert(0, new GameAvatarItem
        {
            Id = GlobalId,
            Name = "全局",
            IsInstalled = true
        });

        Games.Clear();

        foreach (var item in items)
            Games.Add(item);

        SelectedGame = Games.FirstOrDefault(game => game.Id == current)
                       ?? Games.FirstOrDefault(game => game.IsCurrent)
                       ?? Games.FirstOrDefault();

        UpdateKernelStatus();
    }

    private void UpdateKernelStatus()
    {
        // 全局视图：不装内核这回事，状态行说明这里是干嘛的
        if (IsGlobalView)
        {
            CanInstallKernel = false;
            KernelStatusText = "全局配置 · 所有插件存放在 GamesData\\Plugins，适用于哪个游戏由插件自己声明";
            return;
        }

        var game = CurrentGame;

        CanInstallKernel = !_kernel.IsInstalled(game);
        KernelStatusText = CanInstallKernel
            ? string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_KernelMissing",
                "「{0}」的管理功能还没装到本机"), game.FallbackName)
            : string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_KernelReady",
                "「{0}」的管理功能已就绪"), game.FallbackName);
    }

    /// <summary>切换头像栏选中的游戏</summary>
    [RelayCommand]
    private void SelectGame(GameAvatarItem? target)
    {
        if (target is null)
            return;

        SelectedGame = target;
        UpdateKernelStatus();
        Reload();
    }

    /// <summary>按 Id 切换（页面里头像按钮把 Id 塞在 Tag 上，走这个入口）</summary>
    [RelayCommand]
    private void SelectGameById(string? gameId)
    {
        var match = Games.FirstOrDefault(game =>
            string.Equals(game.Id, gameId, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            SelectGame(match);
    }

    private static ImageSource? TryLoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     装内核。资源本来就打在包里，这里做的是登记 ——
    ///     登记完首页下拉和头像栏就不再把它当未下载了。
    /// </summary>
    [RelayCommand]
    private async Task InstallKernelAsync()
    {
        var game = CurrentGame;

        // 二开：装内核 = 把资源包（角色头像、Languages、那几个 json）装到 GamesData\<游戏>\ 下，
        // 管理功能仍在 Core。所以「安装」这一步是真的把资源搬过去了。
        await Task.Run(() => _kernel.Install(game, "local"));

        _notifications.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_KernelInstalled", "内核已安装"),
            string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_KernelInstalledBody",
                "「{0}」已经可以用了，回首页切换就能看到。"), game.FallbackName),
            TimeSpan.FromSeconds(6));

        await LoadGamesAsync();
        Reload();
    }

    /// <summary>
    ///     导入离线功能包。不是所有人都能连 GitHub，所以 zip 这条路必须通：
    ///     包里带 plugin.json 就认，根目录有就当一个插件，子目录有就分别算。
    /// </summary>
    [RelayCommand]
    private async Task ImportOfflinePackageAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();

            if (file is null)
                return;

            var imported = await Task.Run(() => _catalog.ImportOfflinePackage(file.Path, CurrentGame.Id));

            _notifications.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ImportDone", "导入完成"),
                string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ImportDoneBody",
                    "导入了 {0} 个功能包，放在：\n{1}"), imported.Count, _catalog.PluginsDirectory(CurrentGame.Id)),
                TimeSpan.FromSeconds(8));

            Reload();
        }
        catch (Exception e)
        {
            _notifications.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ImportFailed", "导入失败"),
                e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>给想自己写插件的人一个能照着填的模板目录</summary>
    [RelayCommand]
    private void CreatePluginTemplate()
    {
        try
        {
            var directory = _catalog.WriteTemplate(CurrentGame.Id, "my-plugin");

            _notifications.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_TemplateCreated", "模板已生成"),
                directory, TimeSpan.FromSeconds(8));

            Reload();
        }
        catch (Exception e)
        {
            _notifications.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ImportFailed", "导入失败"),
                e.Message, TimeSpan.FromSeconds(8));
        }
    }

    public void OnNavigatedFrom()
    {
    }

    private void Reload()
    {
        Plugins.Clear();

        _dlss5Card = new PluginCard
        {
            Name = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5CardName", "DLSS5 渲染链"),
            Glyph = "\uE7FC",
            BadgeBackground = new SolidColorBrush(ActiveTint),
            BadgeForeground = new SolidColorBrush(ActiveText),
            SecondaryText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_SourceLabel", "说明"),
            SecondaryCommand = OpenDlss5SourceCommand,
            PrimaryCommand = Dlss5PrimaryCommand,
            ToggleCommand = ToggleDlss5Command
        };

        _modCard = new PluginCard
        {
            Name = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModCardName", "Mod 加载"),
            Glyph = "\uE74C",
            BadgeBackground = new SolidColorBrush(ActiveTint),
            BadgeForeground = new SolidColorBrush(ActiveText),
            SecondaryText = "XXMI",
            SecondaryCommand = OpenXxmiCommand,
            PrimaryCommand = ModPrimaryCommand,
            ToggleCommand = ToggleModCommand
        };

        _fixerCard = new PluginCard
        {
            Name = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_FixerCardName", "Mod 修复器"),
            Glyph = "\uE90F",
            BadgeBackground = new SolidColorBrush(ActiveTint),
            BadgeForeground = new SolidColorBrush(ActiveText),
            SecondaryText = "GitHub",
            SecondaryCommand = OpenFixerSourceCommand,
            PrimaryCommand = FixerPrimaryCommand
        };

        var upcoming = new PluginCard
        {
            Name = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_UpcomingCardName", "GameBanana 同步"),
            Glyph = "\uE753",
            BadgeBackground = new SolidColorBrush(MutedTint),
            BadgeForeground = new SolidColorBrush(MutedText),
            Description = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_UpcomingDescription", "自动检查已安装 Mod 的更新，一条列表看完全部待更新项。"),
            Status = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_UpcomingStatus", "开发中"),
            PrimaryText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_UpcomingPrimaryText", "敬请期待"),
            PrimaryEnabled = false,
            SecondaryText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DetailsLabel", "详情"),
            SecondaryCommand = OpenPluginsFolderCommand
        };

        Plugins.Add(_dlss5Card);
        Plugins.Add(_modCard);
        Plugins.Add(_fixerCard);
        Plugins.Add(upcoming);

        AddDiscoveredPlugins();
        RefreshCards();
    }

    /// <summary>
    ///     扫插件目录，把第三方功能也列出来。
    ///     谁写的都行 —— 只要按约定在目录里放一个 plugin.json 就认，
    ///     不用改管理器、也不用走我们的编译流程。这就是「加功能不用重写管理器」的入口。
    /// </summary>
    private void AddDiscoveredPlugins()
    {
        IReadOnlyList<PluginManifest> found;

        try
        {
            // 全局视图全列出来（各自标注适用范围），游戏视图只列适用的
            found = IsGlobalView ? _catalog.ScanAll() : _catalog.Scan(CurrentGame.Id);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var plugin in found)
        {
            var captured = plugin;

            // 适用范围标出来：通用的不标，限定了游戏的列一下，
            // 不然玩家不知道这个插件换个游戏还在不在
            var description = string.IsNullOrWhiteSpace(plugin.Description)
                ? plugin.DirectoryPath
                : plugin.Description;

            if (!string.IsNullOrWhiteSpace(plugin.GamesText))
                description += $"\n适用：{plugin.GamesText}";

            var card = new PluginCard
            {
                Name = plugin.Name,
                Glyph = string.IsNullOrWhiteSpace(plugin.Glyph) ? "\uE9F0" : plugin.Glyph,
                BadgeBackground = new SolidColorBrush(MutedTint),
                BadgeForeground = new SolidColorBrush(MutedText),
                Description = description,
                Status = plugin.IsToggle
                    ? _localizer.GetLocalizedStringOrDefault(plugin.Enabled
                        ? "/HomePreset/Feature_StatusDeployed"
                        : "/HomePreset/Feature_StatusNotDeployed", plugin.Enabled ? "已部署" : "未部署")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusDiscovered", "已识别"),
                PrimaryText = plugin.IsAction
                    ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_RunPlugin", "运行")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DetailsLabel", "详情"),
                SecondaryText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_OpenFolderButton", "打开文件夹"),
                SecondaryCommand = OpenPluginsFolderCommand,
                SwitchVisible = plugin.IsToggle,
                SwitchOn = plugin.Enabled,
                Manifest = plugin
            };

            card.ToggleCommand = new AsyncRelayCommand(() => ToggleDiscoveredAsync(card, captured));
            card.PrimaryCommand = new AsyncRelayCommand(() => RunDiscoveredAsync(captured));

            Plugins.Add(card);
        }
    }

    private async Task ToggleDiscoveredAsync(PluginCard card, PluginManifest plugin)
    {
        await Task.Run(() => _catalog.SetEnabled(plugin, !plugin.Enabled));

        card.SwitchOn = plugin.Enabled;
        card.Status = plugin.Enabled
            ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusDeployed", "已部署")
            : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusNotDeployed", "未部署");
    }

    private async Task RunDiscoveredAsync(PluginManifest plugin)
    {
        if (plugin.IsAction && !string.IsNullOrWhiteSpace(plugin.Entry))
        {
            var entry = Path.Combine(plugin.DirectoryPath, plugin.Entry);

            if (File.Exists(entry))
            {
                Process.Start(new ProcessStartInfo(entry)
                {
                    WorkingDirectory = plugin.DirectoryPath,
                    UseShellExecute = true
                });
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(plugin.SourceUrl))
        {
            _dependencies.OpenUrl(plugin.SourceUrl);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(plugin.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            _notifications.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ImportFailed", "导入失败"),
                e.Message, TimeSpan.FromSeconds(6));
        }
    }

    /// <summary>把检测结果写回卡片：状态、描述、按钮文案、开关位置。</summary>
    private void RefreshCards()
    {
        var state = _dlss5.Detect();
        var deps = _dependencies.Detect();

        if (_dlss5Card is not null)
        {
            _dlss5Card.IsAvailable = state.Deployed;
            _dlss5Card.Status = state.Deployed
                ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusDeployed", "已部署")
                : deps.KitReady
                    ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusNotDeployed", "未部署")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusNoKit", "未下载部署包");

            _dlss5Card.Description = state.Deployed
                ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5DescDeployed", "加载器已铺进游戏目录，开关可以随时卸载。")
                : deps.KitReady
                    ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5DescKitReady", "部署包已就位，点下方开关把它铺进游戏目录（需要管理员权限）。")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5DescNoKit", "还没有部署包，点「下载部署包」自动拉取最新版，下完会自动部署。");

            _dlss5Card.PrimaryText = deps.KitReady ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5PrimaryConfig", "配置") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5PrimaryDownload", "下载部署包");

            // 没部署包时只留「下载部署包」，别摆一个点了还要先配路径的开关
            _dlss5Card.SwitchVisible = state.Deployed || deps.KitReady;
            _dlss5Card.SwitchEnabled = !_dlss5Card.IsDownloading;
            _dlss5Card.SwitchOn = state.Deployed;
            _dlss5Card.SwitchToolTip = state.Deployed
                ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5SwitchTipUninstall", "把加载链从游戏目录卸掉")
                : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5SwitchTipDeploy", "把加载链部署进游戏目录（需要管理员权限）");
            _dlss5Card.ToggleText = state.Deployed ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5ToggleUninstall", "卸载") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Dlss5ToggleDeploy", "部署");
        }

        if (_modCard is not null)
        {
            _modCard.IsAvailable = deps.ModReady;
            _modCard.Status = !deps.ModReady
                ? deps.XxmiReady
                    ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusMissingWwmi", "缺 WWMI")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusMissingXxmi", "缺 XXMI")
                : state.ModLoaderActive
                    ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusModEnabled", "Mod 已启用")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusModDisabled", "Mod 已停用");

            _modCard.Description = !deps.ModReady
                ? deps.XxmiReady
                    ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModDescXxmiReady", "XXMI 里设好游戏路径后，它会自动把 WWMI 下载下来。")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModDescNoXxmi", "还没有 XXMI，点「下载 XXMI」拉取便携版，解压即可用。")
                : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModDescReady", "WWMI 已就位，开关控制这次启动带不带 Mod。");

            _modCard.PrimaryText = deps.ModReady ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModPrimaryModsDir", "Mods 目录") : deps.XxmiReady ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModPrimaryOpenXxmi", "打开 XXMI") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModPrimaryDownloadXxmi", "下载 XXMI");

            // 依赖不齐就先别摆开关，点了也没用
            _modCard.SwitchVisible = deps.ModReady;
            _modCard.SwitchEnabled = !_modCard.IsDownloading;
            _modCard.SwitchOn = state.ModLoaderActive;
            _modCard.SwitchToolTip = !state.Deployed
                ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModSwitchTipNotDeployed", "加载链还没部署，现在启动游戏不加载 Mod；打开会顺手把加载链装上")
                : state.ModLoaderActive
                    ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModSwitchTipDisabled", "关掉后启动游戏不加载 Mod")
                    : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModSwitchTipEnabled", "打开后启动游戏会加载 Mod");
            _modCard.ToggleText = state.ModLoaderActive ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModToggleDisable", "停用") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModToggleEnable", "启用");
        }

        if (_fixerCard is not null)
        {
            // 用户自己从 GitHub 下好丢进插件文件夹的也算数
            var localExe = ModFixerService.Instance.FindLocalExe();

            _fixerCard.IsAvailable = localExe is not null;
            _fixerCard.Status = localExe is null ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusFixerNotDownloaded", "未下载") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_StatusFixerReady", "已就绪");
            _fixerCard.Description = localExe is null
                ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_FixerDescNotDownloaded", "Wuwa_Mod_Fixer 独立运行。也可以自己下好丢进插件文件夹，点修复时直接认。")
                : string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_FixerDescReady", "用的是 {0}，点角色的修复按钮就会跑它。"), Path.GetFileName(localExe));
            _fixerCard.PrimaryText = localExe is null ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_FixerPrimaryDownload", "下载修复器") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_FixerPrimaryOpenFolder", "打开文件夹");
        }
    }

    /// <summary>
    ///     DLSS5 卡主按钮：没包就下载（下完自动部署），有包就打开配置。
    /// </summary>
    [RelayCommand]
    private async Task Dlss5PrimaryAsync()
    {
        if (_dlss5Card is null)
            return;

        if (_dependencies.Detect().KitReady)
        {
            await OpenDlss5SetupAsync();
            return;
        }

        await DownloadKitAsync(_dlss5Card);
    }

    /// <summary>
    ///     Mod 卡主按钮：没 XXMI 就下载，有就打开对应目录。
    /// </summary>
    [RelayCommand]
    private async Task ModPrimaryAsync()
    {
        if (_modCard is null)
            return;

        var deps = _dependencies.Detect();

        if (deps.ModReady)
        {
            await OpenWwmiModsAsync();
            return;
        }

        if (deps.XxmiReady)
        {
            await OpenXxmiAsync();
            return;
        }

        await DownloadXxmiAsync(_modCard);
    }

    private async Task DownloadKitAsync(PluginCard card)
    {
        if (card.IsDownloading)
            return;

        card.IsDownloading = true;
        card.PrimaryEnabled = false;
        card.SwitchEnabled = false;
        card.DownloadIndeterminate = true;
        card.DownloadPercent = 0;
        card.DownloadText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DownloadPreparing", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/HomePreset/Feature_Preparing", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/HomePreset/Feature_Preparing", "正在准备…")));

        var progress = new Progress<DownloadProgress>(p =>
        {
            card.DownloadIndeterminate = p.Percent < 0;
            card.DownloadPercent = p.Percent < 0 ? 0 : p.Percent;
            card.DownloadText = p.Text;
        });

        try
        {
            var result = await _dependencies.DownloadLatestKitAsync(PluginDependencyService.PluginsRoot, progress,
                CancellationToken.None);

            if (!result.Success)
            {
                _notifications.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationDownloadFailed", "下载失败"), result.Message, TimeSpan.FromSeconds(10));
                return;
            }

            _notifications.ShowNotification(result.Message, _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationAutoDeploying", "正在自动部署到游戏目录…"), TimeSpan.FromSeconds(6));

            card.DownloadText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DeployingText", "正在部署…");
            card.DownloadIndeterminate = true;

            await AutoDeployAsync();
        }
        finally
        {
            card.IsDownloading = false;
            card.PrimaryEnabled = true;
            card.DownloadIndeterminate = false;
            RefreshCards();
        }
    }

    private async Task DownloadXxmiAsync(PluginCard card)
    {
        if (card.IsDownloading)
            return;

        card.IsDownloading = true;
        card.PrimaryEnabled = false;
        card.DownloadIndeterminate = true;
        card.DownloadPercent = 0;
        card.DownloadText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DownloadPreparing", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/HomePreset/Feature_Preparing", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/HomePreset/Feature_Preparing", "正在准备…")));

        var progress = new Progress<DownloadProgress>(p =>
        {
            card.DownloadIndeterminate = p.Percent < 0;
            card.DownloadPercent = p.Percent < 0 ? 0 : p.Percent;
            card.DownloadText = p.Text;
        });

        try
        {
            var result = await _dependencies.DownloadLatestXxmiAsync(progress, CancellationToken.None);

            _notifications.ShowNotification(result.Success ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationXxmiDownloaded", "XXMI 已下载") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationDownloadFailed", "下载失败"), result.Message,
                TimeSpan.FromSeconds(10));

            if (!result.Success)
                return;

            await OpenFolderAsync(Path.GetDirectoryName(result.LauncherPath) ?? result.XxmiRoot);
        }
        finally
        {
            card.IsDownloading = false;
            card.PrimaryEnabled = true;
            card.DownloadIndeterminate = false;
            RefreshCards();
        }
    }

    /// <summary>下载完顺手把加载链铺进游戏目录，省得再点一次。</summary>
    private async Task AutoDeployAsync()
    {
        var state = _dlss5.Detect();

        if (!state.Ready)
        {
            _notifications.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationAutoDeployFailedTitle", "还没法自动部署"),
                _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationAutoDeployFailedBody", "游戏可执行文件或部署包目录不对，点「配置」手动补一下。"), TimeSpan.FromSeconds(10));
            return;
        }

        var result = await _dlss5.SetEnabledAsync(true);

        _notifications.ShowNotification(
            result.Success && !result.NeedsSetup && !result.UserCancelled ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationChainDeployed", "渲染链已部署") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationAutoDeployNotSuccess", "自动部署未成功"),
            result.Message, TimeSpan.FromSeconds(10));
    }

    /// <summary>DLSS5 卡开关：部署 / 卸载加载链。</summary>
    [RelayCommand]
    private async Task ToggleDlss5Async()
    {
        if (_dlss5Card is null)
            return;

        var state = _dlss5.Detect();

        // 按钮上写的是「卸载 / 部署」，也就是下一步要做的动作，跟当前状态相反
        var enable = !state.Deployed;

        if (enable == state.Deployed)
            return;

        _dlss5Card.SwitchEnabled = false;

        try
        {
            if (!enable)
            {
                var confirm = new ContentDialog
                {
                    Title = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_UninstallConfirmTitle", "卸载 DLSS5 渲染链？"),
                    Content = string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_UninstallConfirmContent", "会删掉 {0} 下的加载链文件（d3d11.dll、scripts 等）。\n\n注意：被替换掉的 NVIDIA DLSS 库不会自动还原，需要跑一次游戏修复或重新部署。"), state.GameDir),
                    PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_UninstallConfirmPrimary", "确认卸载"),
                    CloseButtonText = _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_Cancel", "取消"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };

                if (await _windowManager.ShowDialogAsync(confirm) != ContentDialogResult.Primary)
                    return;
            }

            var result = await _dlss5.SetEnabledAsync(enable);

            _notifications.ShowNotification(enable ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationChainDeployed", "渲染链已部署") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationChainUninstalled", "渲染链已卸载"), result.Message,
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            _dlss5Card.SwitchEnabled = true;
            RefreshCards();
        }
    }

    /// <summary>Mod 卡开关：启用 / 停用 Mod 加载（改 d3d11.asi 的文件名）。</summary>
    [RelayCommand]
    private async Task ToggleModAsync()
    {
        if (_modCard is null)
            return;

        var state = _dlss5.Detect();
        var enable = !state.ModLoaderActive;
        _modCard.SwitchEnabled = false;

        try
        {
            // 加载链没部署时，两个开关就是同时关着的状态。
            // 只是关：本来就是关的，直接回正；想开：顺手把加载链装上。
            if (!state.Deployed)
            {
                if (enable)
                    await AutoDeployAsync();

                return;
            }

            var ok = await _dlss5.SetModLoaderAsync(enable);

            _notifications.ShowNotification(enable ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationModEnabled", "Mod 已启用") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationModDisabled", "Mod 已停用"),
                ok ? _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationTakeEffectNextLaunch", "下次启动游戏生效") : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationRenameFailed", "改文件名失败，检查一下游戏目录的写入权限"), TimeSpan.FromSeconds(6));
        }
        finally
        {
            _modCard.SwitchEnabled = true;
            RefreshCards();
        }
    }

    [RelayCommand]
    private async Task OpenPluginsFolderAsync() => await OpenFolderAsync(PluginsFolderPath);

    private async Task OpenDlss5SetupAsync()
    {
        await _windowManager.ShowDialogAsync(new Dlss5SetupDialog(_dlss5));
        RefreshCards();
    }

    [RelayCommand]
    private void OpenDlss5Source() => _dependencies.OpenUrl(PluginDependencyService.KitRepoUrl);

    /// <summary>Mod 修复器卡主按钮：本地没有就下载最新版，已经有了就打开插件文件夹。</summary>
    [RelayCommand]
    private async Task FixerPrimaryAsync()
    {
        if (_fixerCard is null)
            return;

        if (ModFixerService.Instance.FindLocalExe() is not null)
        {
            await OpenFolderAsync(PluginsFolderPath);
            return;
        }

        await DownloadFixerAsync(_fixerCard);
    }

    private async Task DownloadFixerAsync(PluginCard card)
    {
        if (card.IsDownloading)
            return;

        card.IsDownloading = true;
        card.PrimaryEnabled = false;
        card.DownloadIndeterminate = true;
        card.DownloadPercent = 0;
        card.DownloadText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/HomePreset/Feature_Preparing", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/HomePreset/Feature_Preparing", "正在准备…"));

        var progress = new Progress<(double Percent, long Received, long Total)>(p =>
        {
            card.DownloadIndeterminate = p.Percent < 0;
            card.DownloadPercent = p.Percent < 0 ? 0 : p.Percent;
            card.DownloadText = p.Total > 0
                ? $"{p.Received / 1024.0 / 1024.0:F1} / {p.Total / 1024.0 / 1024.0:F1} MB"
                : _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DownloadInProgress", "下载中…");
        });

        try
        {
            var service = ModFixerService.Instance;
            var release = service.Latest ?? await service.GetLatestReleaseAsync();

            if (release is null)
            {
                _notifications.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationFixerNoVersionTitle", "查不到修复器版本"),
                    _localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationFixerNoVersionBody", "GitHub 打不开或者网络不通。也可以自己下好丢进插件文件夹，效果一样。"), TimeSpan.FromSeconds(10));
                return;
            }

            var path = await service.DownloadAsync(release, progress);

            _notifications.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationFixerReadyTitle", "修复器已就位"),
                string.Format(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationFixerReadyBody", "{0} 已放进插件文件夹，角色页点修复就会用它。"), Path.GetFileName(path)), TimeSpan.FromSeconds(8));
        }
        catch (Exception e)
        {
            _notifications.ShowNotification(_localizer.GetLocalizedStringOrDefault("/HomePreset/Feature_NotificationDownloadFailed", "下载失败"), e.Message, TimeSpan.FromSeconds(10));
        }
        finally
        {
            card.IsDownloading = false;
            card.PrimaryEnabled = true;
            card.DownloadIndeterminate = false;
            RefreshCards();
        }
    }

    [RelayCommand]
    private void OpenFixerSource() =>
        _dependencies.OpenUrl("https://github.com/Moonholder/Wuwa_Mod_Fixer/releases");

    [RelayCommand]
    private async Task OpenWwmiModsAsync()
    {
        var snapshot = _dependencies.Detect();
        var mods = Path.Combine(snapshot.WwmiRoot, "Mods");

        if (!Directory.Exists(mods))
        {
            await _windowManager.ShowDialogAsync(new PluginDependencyDialog(_dependencies));
            RefreshCards();
            return;
        }

        await OpenFolderAsync(mods);
    }

    [RelayCommand]
    private async Task OpenXxmiAsync()
    {
        var snapshot = _dependencies.Detect();

        if (!snapshot.XxmiReady)
        {
            await _windowManager.ShowDialogAsync(new PluginDependencyDialog(_dependencies));
            RefreshCards();
            return;
        }

        await OpenFolderAsync(snapshot.XxmiRoot);
    }

    private static async Task OpenFolderAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception)
        {
            // 打开文件夹失败时忽略
        }
    }
}

/// <summary>功能专区里的一张卡片。</summary>
public partial class PluginCard : ObservableObject
{
    // 二开：卡片是 new 出来的，不经过 DI，用 App.GetService 取 localizer。
    // 必须是 static —— C# 的字段初始值不能引用实例成员（CS0236），
    // 上面 FeatureAreaViewModel 那份是实例字段，这里的初始值用不了。
    private static readonly ILanguageLocalizer CardLocalizer = App.GetService<ILanguageLocalizer>();

    /// <summary>第三方插件的 manifest。内置卡片没有这个，靠它区分是不是扫出来的。</summary>
    public PluginManifest? Manifest { get; set; }

    public string Name { get; init; } = string.Empty;
    public string Glyph { get; init; } = "\uE8B7";

    public Brush BadgeBackground { get; init; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    public Brush BadgeForeground { get; init; } = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    [ObservableProperty] private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private bool _isAvailable;

    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryOpacity))]
    private bool _primaryEnabled = true;

    [ObservableProperty] private string _primaryText = CardLocalizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DefaultPrimaryText", "打开");
    [ObservableProperty] private string _secondaryText = CardLocalizer.GetLocalizedStringOrDefault("/HomePreset/Feature_DetailsLabel", "详情");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchVisibility))]
    private bool _switchVisible;

    [ObservableProperty] private bool _switchOn;
    [ObservableProperty] private bool _switchEnabled = true;
    [ObservableProperty] private string _switchToolTip = string.Empty;
    [ObservableProperty] private string _toggleText = CardLocalizer.GetLocalizedStringOrDefault("/HomePreset/Feature_ModToggleEnable", "启用");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadVisibility))]
    [NotifyPropertyChangedFor(nameof(DescriptionVisibility))]
    private bool _isDownloading;

    [ObservableProperty] private bool _downloadIndeterminate;
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private string _downloadText = string.Empty;

    // 用 set 而不是 init：扫出来的插件卡要先 new 再挂命令（命令闭包里要用卡片自己）
    public System.Windows.Input.ICommand? PrimaryCommand { get; set; }
    public System.Windows.Input.ICommand? SecondaryCommand { get; set; }
    public System.Windows.Input.ICommand? ToggleCommand { get; set; }

    public Brush StatusBrush => new SolidColorBrush(IsAvailable
        ? Color.FromArgb(0xFF, 0x5F, 0xC9, 0x7C)
        : Color.FromArgb(0xFF, 0x6B, 0x70, 0x7A));

    /// <summary>主按钮文字的不透明度：禁用时跟着变淡，不然红字看着还能点。</summary>
    public double PrimaryOpacity => PrimaryEnabled ? 1 : 0.38;

    /// <summary>下载中不显示描述，改显示进度条。</summary>
    public Visibility DownloadVisibility => IsDownloading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DescriptionVisibility => IsDownloading ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SwitchVisibility => SwitchVisible ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
///     功能专区顶部那一排游戏头像。点哪个就切换下面整片资源列表。
/// </summary>
public class GameAvatarItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;

    /// <summary>头像图。XAML 不做 string → ImageSource 的转换，这里直接给图。</summary>
    public ImageSource? Icon { get; init; }

    /// <summary>内核有没有装到本机</summary>
    public bool IsInstalled { get; init; }

    /// <summary>是不是当前正在用的游戏</summary>
    public bool IsCurrent { get; init; }

    public double ItemOpacity => IsInstalled ? 1.0 : 0.45;

    public Visibility NotInstalledVisibility => IsInstalled ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>没有头像图的（比如「全局」那颗）就显示一个通用图标顶着。</summary>
    public Visibility FallbackIconVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
}
