using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OMniX.WinUI.ViewModels;

namespace OMniX.WinUI.Views;

/// <summary>
///     关于页。左右两侧的内容都在 XAML 里，这个文件只有「打开项目主页」一个事件。
/// </summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        ViewModel = App.GetService<AboutViewModel>();
        InitializeComponent();
    }

    public AboutViewModel ViewModel { get; }

    private async void OpenProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/aiertana/OMnix"));
        }
        catch (Exception)
        {
            // 打不开就算了，不影响别的
        }
    }
}
