using Microsoft.UI.Xaml.Controls;

namespace OMniX.AutoUpdater;

public sealed partial class MainPage : Page
{
    public MainPageVM ViewModel { get; }

    /// <param name="currentOmniXVersion">当前装着的 OMniX 版本号</param>
    /// <param name="localZipPath">
    ///     本地更新包（zip）的完整路径。给了这个就走「本地安装」，
    ///     不去 GitHub 查版本、也不下载 —— 用户在设置页手动导入时用。
    /// </param>
    public MainPage(string currentOmniXVersion, string localZipPath = "")
    {
        InitializeComponent();

        ViewModel = new MainPageVM(currentOmniXVersion, localZipPath);
    }
}
