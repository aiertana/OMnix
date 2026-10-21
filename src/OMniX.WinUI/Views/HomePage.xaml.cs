using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.GetService<HomeViewModel>();
        InitializeComponent();
    }

    /// <summary>
    ///     切换游戏会重启应用，所以这里走明确的交互确认：只有用户主动改选才触发。
    /// </summary>
    private async void GameSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;

        if (e.AddedItems[0] is GameSwitchItem item)
            await ViewModel.SelectGameCommand.ExecuteAsync(item);
    }
}
