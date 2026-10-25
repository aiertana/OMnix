using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.Services.Plugins;

namespace OMniX.WinUI.Views.Dialogs;

/// <summary>
///     二开：打开开关前先看一眼 XXMI / WWMI / DLSS5 部署包齐不齐。
/// </summary>
public sealed partial class PluginDependencyDialog : ContentDialog
{
    private readonly PluginDependencyService _dependencies;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private CancellationTokenSource? _cts;

    public PluginDependencyDialog(PluginDependencyService dependencies)
    {
        _dependencies = dependencies;
        InitializeComponent();

        Refresh();
    }

    /// <summary>下载完部署包后对外暴露新目录，调用方可以顺手把路径存下来</summary>
    public string? DownloadedKitDir { get; private set; }

    private void Refresh()
    {
        var snapshot = _dependencies.Detect();

        XxmiText.Text = snapshot.XxmiText;
        WwmiText.Text = snapshot.WwmiReady
            ? snapshot.WwmiRoot
            : snapshot.XxmiReady
                ? _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_WwmiHintSetPath", "在 XXMI Launcher 里设好游戏路径，它会自动把 WWMI 下载下来")
                : _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_WwmiHintNeedXxmi", "需要先装 XXMI Launcher");
        KitText.Text = snapshot.KitText;

        XxmiIcon.Glyph = snapshot.XxmiReady ? "\uE73E" : "\uE783";
        WwmiIcon.Glyph = snapshot.WwmiReady ? "\uE73E" : "\uE783";
        KitIcon.Glyph = snapshot.KitReady ? "\uE73E" : "\uE783";

        XxmiButton.Content = snapshot.XxmiReady
            ? "GitHub"
            : _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_BtnGoDownload", "去下载");
        WwmiButton.Content = snapshot.WwmiReady
            ? "GitHub"
            : _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_BtnShowInfo", "说明");
        KitButton.Content = snapshot.KitReady
            ? _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_BtnRedownload", "重新下载")
            : _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_BtnAutoDownload", "自动下载");

        // 至少要让 Mod 开关能用，才允许继续
        IsPrimaryButtonEnabled = snapshot.ModReady;

        if (!snapshot.ModReady)
            ShowTip(InfoBarSeverity.Warning,
                _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_TipMissingTitle", "还差东西"),
                _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_TipMissingMsg", "XXMI 和 WWMI 都就绪之后，两个开关才能真正控制加载链。"));
    }

    private void OpenXxmi_OnClick(object sender, RoutedEventArgs e) =>
        _dependencies.OpenUrl(PluginDependencyService.XxmiRepoUrl);

    private void OpenWwmi_OnClick(object sender, RoutedEventArgs e) =>
        _dependencies.OpenUrl(PluginDependencyService.WwmiRepoUrl);

    /// <summary>
    ///     二开：认不出来的时候让用户自己指。指定的位置会记下来，以后优先用它。
    /// </summary>
    private async void BrowseXxmi_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var folder = await folderPicker.PickSingleFolderAsync();

            if (folder is null)
                return;

            var error = _dependencies.SetXxmiRootManually(folder.Path);

            Refresh();

            if (error is null)
                ShowTip(InfoBarSeverity.Success,
                    _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_ManualOkTitle", "已记住"),
                    _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_ManualOkMsg",
                        "这个位置会优先使用，之后每次检测都先找它。"));
            else
                ShowTip(InfoBarSeverity.Error,
                    _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_ManualBadTitle", "这个位置不对"),
                    error);
        }
        catch (Exception ex)
        {
            ShowTip(InfoBarSeverity.Error,
                _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_ManualBadTitle", "这个位置不对"),
                ex.Message);
        }
    }

    private async void DownloadKit_OnClick(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        KitButton.IsEnabled = false;
        DownloadBar.Visibility = Visibility.Visible;
        ShowTip(InfoBarSeverity.Informational,
            _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_TipDownloadingTitle", "正在下载"),
            _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_TipDownloadingMsg", "从 GitHub 拉最新版部署包…"));

        var progress = new Progress<DownloadProgress>(p =>
        {
            DownloadBar.IsIndeterminate = p.Percent < 0;
            DownloadBar.Value = p.Percent < 0 ? 0 : p.Percent;
            ShowTip(InfoBarSeverity.Informational,
                _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_TipDownloadingTitle", "正在下载"),
                p.Text);
        });

        try
        {
            var targetRoot = PluginDependencyService.PluginsRoot;

            var result = await _dependencies.DownloadLatestKitAsync(targetRoot, progress, _cts.Token);

            if (result.Success)
            {
                DownloadedKitDir = result.KitDir;
                ShowTip(InfoBarSeverity.Success, result.Message,
                    string.Format(
                        _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_DonePath", "已经放到 {0}"),
                        result.KitDir));
            }
            else
            {
                ShowTip(InfoBarSeverity.Error,
                    _localizer.GetLocalizedStringOrDefault("/Dialogs/PluginDep_TipFailTitle", "下载失败"),
                    result.Message);
            }
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            KitButton.IsEnabled = true;
            DownloadBar.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }

    private void ShowTip(InfoBarSeverity severity, string title, string message)
    {
        TipBar.Severity = severity;
        TipBar.Title = title;
        TipBar.Message = message;
        TipBar.IsOpen = true;
    }
}
