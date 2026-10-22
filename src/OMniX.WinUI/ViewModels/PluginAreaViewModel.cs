using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.Plugins;
using OMniX.WinUI.Views.Dialogs;
using Windows.UI;

namespace OMniX.WinUI.ViewModels;

/// <summary>
///     插件专区。插件是独立于管理器的一份东西：
///     放在插件文件夹里，管理器只是扫一眼、显示出来，删掉插件文件管理器照常跑，
///     加载失败也只影响卡片本身，不会波及 Mod 管理和游戏启动。
/// </summary>
public partial class PluginAreaViewModel : ObservableRecipient, INavigationAware
{
    // 一面玻璃上只该有一两种主色：DLSS5 用主色红，Mod 用中性灰，未上线的用更淡的灰
    private static readonly Color Dlss5Tint = Color.FromArgb(0x24, 0xE5, 0x48, 0x4D);
    private static readonly Color Dlss5Text = Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D);
    private static readonly Color ModTint = Color.FromArgb(0x24, 0x2C, 0x2C, 0x34);
    private static readonly Color ModText = Color.FromArgb(0xFF, 0x5A, 0x5A, 0x66);
    private static readonly Color MutedTint = Color.FromArgb(0x1A, 0x9A, 0xA0, 0xAE);
    private static readonly Color MutedText = Color.FromArgb(0xFF, 0x9A, 0xA0, 0xAE);

    private readonly PluginDependencyService _dependencies = App.GetService<PluginDependencyService>();
    private readonly Dlss5PluginService _dlss5 = App.GetService<Dlss5PluginService>();
    private readonly IWindowManagerService _windowManager = App.GetService<IWindowManagerService>();
    private readonly NotificationManager _notifications = App.GetService<NotificationManager>();

    private PluginCard? _dlss5Card;
    private PluginCard? _modCard;

    public PluginAreaViewModel()
    {
        Reload();
    }

    public string PluginsFolderPath => PluginDependencyService.PluginsRoot;

    public ObservableCollection<PluginCard> Plugins { get; } = new();

    public void OnNavigatedTo(object parameter) => Reload();

    public void OnNavigatedFrom()
    {
    }

    private void Reload()
    {
        Plugins.Clear();

        _dlss5Card = new PluginCard
        {
            Name = "DLSS5 渲染链",
            Glyph = "\uE7FC",
            BadgeBackground = new SolidColorBrush(Dlss5Tint),
            BadgeForeground = new SolidColorBrush(Dlss5Text),
            SecondaryText = "说明",
            SecondaryCommand = OpenDlss5SourceCommand,
            PrimaryCommand = Dlss5PrimaryCommand,
            ToggleCommand = ToggleDlss5Command
        };

        _modCard = new PluginCard
        {
            Name = "Mod 加载",
            Glyph = "\uE74C",
            BadgeBackground = new SolidColorBrush(ModTint),
            BadgeForeground = new SolidColorBrush(ModText),
            SecondaryText = "XXMI",
            SecondaryCommand = OpenXxmiCommand,
            PrimaryCommand = ModPrimaryCommand,
            ToggleCommand = ToggleModCommand
        };

        var upcoming = new PluginCard
        {
            Name = "GameBanana 同步",
            Glyph = "\uE753",
            BadgeBackground = new SolidColorBrush(MutedTint),
            BadgeForeground = new SolidColorBrush(MutedText),
            Description = "自动检查已安装 Mod 的更新，一条列表看完全部待更新项。",
            Status = "开发中",
            PrimaryText = "敬请期待",
            PrimaryEnabled = false,
            SecondaryText = "详情",
            SecondaryCommand = OpenPluginsFolderCommand
        };

        Plugins.Add(_dlss5Card);
        Plugins.Add(_modCard);
        Plugins.Add(upcoming);

        RefreshCards();
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
                ? "已部署"
                : deps.KitReady
                    ? "未部署"
                    : "未下载部署包";

            _dlss5Card.Description = state.Deployed
                ? "加载器已铺进游戏目录，开关可以随时卸载。"
                : deps.KitReady
                    ? "部署包已就位，点下方开关把它铺进游戏目录（需要管理员权限）。"
                    : "还没有部署包，点「下载部署包」自动拉取最新版，下完会自动部署。";

            _dlss5Card.PrimaryText = deps.KitReady ? "配置" : "下载部署包";

            // 没部署包时只留「下载部署包」，别摆一个点了还要先配路径的开关
            _dlss5Card.SwitchVisible = state.Deployed || deps.KitReady;
            _dlss5Card.SwitchEnabled = !_dlss5Card.IsDownloading;
            _dlss5Card.SwitchOn = state.Deployed;
            _dlss5Card.SwitchToolTip = state.Deployed
                ? "把加载链从游戏目录卸掉"
                : "把加载链部署进游戏目录（需要管理员权限）";
            _dlss5Card.ToggleText = state.Deployed ? "卸载" : "部署";
        }

        if (_modCard is not null)
        {
            _modCard.IsAvailable = deps.ModReady;
            _modCard.Status = !deps.ModReady
                ? deps.XxmiReady
                    ? "缺 WWMI"
                    : "缺 XXMI"
                : state.ModLoaderActive
                    ? "Mod 已启用"
                    : "Mod 已停用";

            _modCard.Description = !deps.ModReady
                ? deps.XxmiReady
                    ? "XXMI 里设好游戏路径后，它会自动把 WWMI 下载下来。"
                    : "还没有 XXMI，点「下载 XXMI」拉取便携版，解压即可用。"
                : "WWMI 已就位，开关控制这次启动带不带 Mod。";

            _modCard.PrimaryText = deps.ModReady ? "Mods 目录" : deps.XxmiReady ? "打开 XXMI" : "下载 XXMI";

            // 依赖不齐就先别摆开关，点了也没用
            _modCard.SwitchVisible = deps.ModReady;
            _modCard.SwitchEnabled = !_modCard.IsDownloading;
            _modCard.SwitchOn = state.ModLoaderActive;
            _modCard.SwitchToolTip = !state.Deployed
                ? "加载链还没部署，现在启动游戏不加载 Mod；打开会顺手把加载链装上"
                : state.ModLoaderActive
                    ? "关掉后启动游戏不加载 Mod"
                    : "打开后启动游戏会加载 Mod";
            _modCard.ToggleText = state.ModLoaderActive ? "停用" : "启用";
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
        card.DownloadText = "正在准备…";

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
                _notifications.ShowNotification("下载失败", result.Message, TimeSpan.FromSeconds(10));
                return;
            }

            _notifications.ShowNotification(result.Message, "正在自动部署到游戏目录…", TimeSpan.FromSeconds(6));

            card.DownloadText = "正在部署…";
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
        card.DownloadText = "正在准备…";

        var progress = new Progress<DownloadProgress>(p =>
        {
            card.DownloadIndeterminate = p.Percent < 0;
            card.DownloadPercent = p.Percent < 0 ? 0 : p.Percent;
            card.DownloadText = p.Text;
        });

        try
        {
            var result = await _dependencies.DownloadLatestXxmiAsync(progress, CancellationToken.None);

            _notifications.ShowNotification(result.Success ? "XXMI 已下载" : "下载失败", result.Message,
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
            _notifications.ShowNotification("还没法自动部署",
                "游戏可执行文件或部署包目录不对，点「配置」手动补一下。", TimeSpan.FromSeconds(10));
            return;
        }

        var result = await _dlss5.SetEnabledAsync(true);

        _notifications.ShowNotification(
            result.Success && !result.NeedsSetup && !result.UserCancelled ? "渲染链已部署" : "自动部署未成功",
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
                    Title = "卸载 DLSS5 渲染链？",
                    Content = $"会删掉 {state.GameDir} 下的加载链文件（d3d11.dll、scripts 等）。\n\n" +
                              "注意：被替换掉的 NVIDIA DLSS 库不会自动还原，需要跑一次游戏修复或重新部署。",
                    PrimaryButtonText = "确认卸载",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };

                if (await _windowManager.ShowDialogAsync(confirm) != ContentDialogResult.Primary)
                    return;
            }

            var result = await _dlss5.SetEnabledAsync(enable);

            _notifications.ShowNotification(enable ? "渲染链已部署" : "渲染链已卸载", result.Message,
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

            _notifications.ShowNotification(enable ? "Mod 已启用" : "Mod 已停用",
                ok ? "下次启动游戏生效" : "改文件名失败，检查一下游戏目录的写入权限", TimeSpan.FromSeconds(6));
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

/// <summary>插件专区里的一张卡片。</summary>
public partial class PluginCard : ObservableObject
{
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

    [ObservableProperty] private string _primaryText = "打开";
    [ObservableProperty] private string _secondaryText = "详情";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchVisibility))]
    private bool _switchVisible;

    [ObservableProperty] private bool _switchOn;
    [ObservableProperty] private bool _switchEnabled = true;
    [ObservableProperty] private string _switchToolTip = string.Empty;
    [ObservableProperty] private string _toggleText = "启用";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadVisibility))]
    [NotifyPropertyChangedFor(nameof(DescriptionVisibility))]
    private bool _isDownloading;

    [ObservableProperty] private bool _downloadIndeterminate;
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private string _downloadText = string.Empty;

    public System.Windows.Input.ICommand? PrimaryCommand { get; init; }
    public System.Windows.Input.ICommand? SecondaryCommand { get; init; }
    public System.Windows.Input.ICommand? ToggleCommand { get; init; }

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
