using Windows.Storage;
using Windows.System;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.GamesService.Interfaces;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OMniX.Core.Contracts.Services;

namespace OMniX.WinUI.Views;

public sealed partial class ModInstallerPage : Page, IDisposable
{
    public event EventHandler<CloseRequestedArgs>? CloseRequested;
    public ModInstallerVM ViewModel { get; } = App.GetService<ModInstallerVM>();

    /// <summary>
    ///     二开：左侧导航「Mod 安装」走的是这个无参构造。
    ///     这个页面原本只有下面那个带参构造（要装什么包、装到哪个角色），
    ///     而 Frame.Navigate 激活实例时只会找无参构造 —— 找不到就抛
    ///     NullReferenceException，点一下导航直接崩（日志里那条
    ///     ActivateInstance 的 NRE 就是它）。
    ///     没有上下文就没法装东西，这里给一条指引，别把半个安装界面摆出来。
    /// </summary>
    public ModInstallerPage()
    {
        InitializeComponent();
        ShowStandaloneHint();
    }

    public ModInstallerPage(ICharacterModList characterModList, DirectoryInfo modToInstall,
        ICharacterSkin? inGameSkin = null, InstallOptions? options = null)
    {
        InitializeComponent();
        ViewModel.DuplicateModDialog += OnDuplicateModFound;
        ViewModel.InstallerFinished += (_, _) => { DispatcherQueue.TryEnqueue(() => { IsEnabled = false; }); };
        Loading += (_, _) =>
        {
            _ = ViewModel.InitializeAsync(characterModList, modToInstall, DispatcherQueue, inGameSkin, options);
        };

        ViewModel.CloseRequested += (_, e) => { CloseRequested?.Invoke(this, e); };
    }

    private void ShowStandaloneHint()
    {
        var title = new TextBlock
        {
            Text = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModInstaller/Mi_StandaloneTitle", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModInstaller/Mi_StandaloneTitle", "安装 Mod 需要先指定「装什么」和「装到谁身上」")),
            FontSize = 20,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        var body = new TextBlock
        {
            Margin = new Thickness(0, 14, 0, 0),
            LineHeight = 24,
            Text =
                App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModInstaller/Mi_StandaloneBody", "两种方式：\n\n· 把 zip 或 Mod 文件夹直接拖进主窗口 —— 一次拖几个就是批量导入，\n  程序会挨个问你要装到哪个角色\n\n· 去「角色列表」选中角色，用那边的安装按钮 —— 角色会自动带上，不用再选"),
            TextWrapping = TextWrapping.WrapWholeWords
        };

        var panel = new StackPanel
        {
            Padding = new Thickness(48, 56, 48, 48),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        panel.Children.Add(title);
        panel.Children.Add(body);

        Content = panel;
    }

    private async void OnDuplicateModFound(object? sender, EventArgs e)
    {
        await DuplicateModDialog.ShowAsync();
    }


    private async void RootFolder_DoubleClicked(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not TreeViewItem treeView) return;
        if (treeView.DataContext is not RootFolder rootFolder) return;
        if (!Directory.Exists(rootFolder.Path)) return;

        await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(rootFolder.Path));
    }

    private void FrameworkElement_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem treeView) return;
        if (treeView.DataContext is not FileSystemItem fileSystemItem) return;

        if (fileSystemItem.IsFolder)
            treeView.ContextFlyout = FolderFlyout;
        else if (fileSystemItem.IsFile)
            treeView.ContextFlyout = FileFlyout;
        else
            treeView.ContextFlyout = null;
    }

    private async void FileSystemItem_DoubleClicked(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not Grid grid) return;
        if (grid.DataContext is not FileSystemItem fileSystemItem || fileSystemItem.IsFile) return;
        if (!Directory.Exists(fileSystemItem.Path)) return;

        await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(fileSystemItem.Path));
    }

    /// <summary>
    ///     二开：点识别出来的快捷键，直接填进输入框。
    /// </summary>
    private void DetectedHotKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hotKey } && !string.IsNullOrWhiteSpace(hotKey))
            ViewModel.HotKey = hotKey;
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (sender is not TextBox textBox) return;
            ViewModel.ModUrl = textBox.Text.Trim();
        }
    }

    public void Dispose()
    {
        ViewModel.Dispose();
    }
}

class ExplorerItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate RootFolderTemplate { get; set; }
    public DataTemplate FileSystemItem { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return item switch
        {
            RootFolder => RootFolderTemplate,
            ViewModels.FileSystemItem => FileSystemItem,
            _ => throw new ArgumentOutOfRangeException(nameof(item))
        };
    }
}