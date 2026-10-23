using OMniX.WinUI.Services;
using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;

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

    /// <summary>
    ///     右键「更换背景图」：选一张图，复制进应用目录后立刻换上。
    /// </summary>
    private async void ChangeHeroBackground_OnClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            CommitButtonText = "用作背景"
        };

        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
            picker.FileTypeFilter.Add(extension);

        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();

        if (file is null)
            return;

        await ViewModel.SetHeroBackgroundAsync(file.Path);
    }

    private async void ResetHeroBackground_OnClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ResetHeroBackgroundAsync();

    private void OpenHeroBackgroundFolder_OnClick(object sender, RoutedEventArgs e) =>
        ViewModel.OpenHeroBackgroundFolder();

}
