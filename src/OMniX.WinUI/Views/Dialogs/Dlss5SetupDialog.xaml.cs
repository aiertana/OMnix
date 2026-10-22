using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OMniX.WinUI.Services.Plugins;
using Windows.Storage.Pickers;

namespace OMniX.WinUI.Views.Dialogs;

/// <summary>
///     二开：DLSS5 插件第一次开起来时的路径配置。
/// </summary>
public sealed partial class Dlss5SetupDialog : ContentDialog
{
    private readonly Dlss5PluginService _service;

    public Dlss5SetupDialog(Dlss5PluginService service)
    {
        _service = service;
        InitializeComponent();

        GameExeBox.Text = service.ResolveGameExePath();
        KitDirBox.Text = service.ResolveKitDirectory();

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var exePath = GameExeBox.Text.Trim().Trim('"');
        var kitDir = KitDirBox.Text.Trim().Trim('"');

        if (!File.Exists(exePath))
        {
            args.Cancel = true;
            GameExeBox.Focus(FocusState.Programmatic);
            await ShowTipAsync("没找到这个可执行文件", "路径要指到 Client-Win64-Shipping.exe 本体。");
            return;
        }

        if (!File.Exists(Path.Combine(kitDir, "install.ps1")))
        {
            args.Cancel = true;
            KitDirBox.Focus(FocusState.Programmatic);
            await ShowTipAsync("部署包目录不对", "这个目录里应该有 install.ps1 和 files 文件夹。");
            return;
        }

        await _service.SavePathsAsync(exePath, kitDir);
    }

    private async void BrowseGameExe_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                CommitButtonText = "选择"
            };
            picker.FileTypeFilter.Add(".exe");

            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var file = await picker.PickSingleFileAsync();

            if (file is not null)
                GameExeBox.Text = file.Path;
        }
        catch (Exception ex)
        {
            await ShowTipAsync("选文件失败", ex.Message);
        }
    }

    private async void BrowseKitDir_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                CommitButtonText = "选择文件夹"
            };
            picker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var folder = await picker.PickSingleFolderAsync();

            if (folder is not null)
                KitDirBox.Text = folder.Path;
        }
        catch (Exception ex)
        {
            await ShowTipAsync("选文件夹失败", ex.Message);
        }
    }

    private async Task ShowTipAsync(string title, string message)
    {
        var tip = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "知道了",
            XamlRoot = XamlRoot
        };

        await tip.ShowAsync();
    }
}
