using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OMniX.WinUI.ViewModels;

/// <summary>
///     关于页。内容都是静态文案，直接写在 <c>AboutPage.xaml</c> 里，
///     这个 ViewModel 只是导航需要的壳，外加一个版本号。
/// </summary>
public partial class AboutViewModel : ObservableRecipient
{
    public AboutViewModel()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionText = version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>跟侧栏底部显示的是同一个版本号。</summary>
    public string AppVersionText { get; }
}
