using OMniX.WinUI.ViewModels;
using OMniX.WinUI.ViewModels.SubVms;
using OMniX.WinUI.Views.Controls;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.Views;

public sealed partial class StartupPage : Page
{
    public StartupViewModel ViewModel { get; }

    public StartupPage()
    {
        ViewModel = App.GetService<StartupViewModel>();
        InitializeComponent();
    }

    // 二开：这里选择的是启动器根文件夹（原 3Dmigoto 文件夹）
    private void LauncherFolder_OnPathChangedEvent(object? sender, FolderSelector.StringEventArgs e)
        => ViewModel.OnLauncherRootPathChanged(e.Value);


    private void ModsFolder_OnPathChangedEvent(object? sender, FolderSelector.StringEventArgs e)
        => ViewModel.PathToModsFolderPicker.Validate(e.Value);

    // 二开：点击游戏图标卡片后进入该游戏的文件夹配置
    private void GameCard_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameComboBoxEntryVM game)
            ViewModel.SelectGameCommand.Execute(game);
    }
}
