using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OMniX.Core.Services.GameBanana;
using OMniX.WinUI.ViewModels;
using System.IO;
using Windows.System;
using WinUIEx;

namespace OMniX.WinUI.Views.Dialogs;

/// <summary>
///     GameBanana 窗口：浏览 / 搜索在线 Mod，点卡片打开既有的下载安装页。
///     分类用 pill 面包屑（一级分类 + 角色行），带代理设置（给没开 VPN 的用户）。
/// </summary>
public sealed partial class GameBananaStoreWindow : WindowEx
{
    public GameBananaStoreVM ViewModel { get; }

    public GameBananaStoreWindow()
    {
        ViewModel = new GameBananaStoreVM();
        InitializeComponent();

        Title = "GameBanana";
        Width = 1080;
        Height = 720;
        // OMniX 娘圆角图标，商店与下载窗口共用
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/GameBananaIcon.ico"));
    }

    private bool _initialized;

    /// <summary>打开即自动加载最新发布（先拉分类再刷第一页）。</summary>
    private async void RootPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private void ResultCard_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StoreModCardVm card)
        {
            ViewModel.OpenModCommand.Execute(card);
        }
    }

    private void RootCategoryList_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CategoryOptionVm item)
        {
            ViewModel.SelectPillItem(item);
        }
    }

    /// <summary>打开角色面板（顶部「角色 ▾」）：先确保数据就绪，再对齐按钮下方弹出。
    /// 面板右缘与按钮右缘对齐（按钮贴窗口右缘，按左缘对齐会溢出被裁），窄窗再往左收。</summary>
    private async void SubCategoryPicker_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.EnsureCharacterPickerCommand.ExecuteAsync(null);

        var transform = CharacterPickerButton.TransformToVisual(Content);
        var pos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

        const double panelWidth = 260;
        var rightAligned = pos.X + CharacterPickerButton.ActualWidth - panelWidth;
        SubCategoryPopup.HorizontalOffset = Math.Clamp(rightAligned, 4, Math.Max(4, pos.X));
        SubCategoryPopup.VerticalOffset = pos.Y + CharacterPickerButton.ActualHeight + 4;
        SubCategoryPopup.IsOpen = true;
    }

    private void SubCategoryList_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CategoryOptionVm sub)
        {
            ViewModel.SelectCharacterFromPicker(sub);
            SubCategoryPopup.IsOpen = false;
        }
    }

    /// <summary>pill 行滚轮横滚：竖直滚轮转成横向偏移，角色多时滚一滚就能看到后面的。</summary>
    private void PillList_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListView list)
        {
            var scrollViewer = FindScrollViewer(list);
            if (scrollViewer is not null)
            {
                var delta = e.GetCurrentPoint(list).Properties.MouseWheelDelta;
                scrollViewer.ChangeView(scrollViewer.HorizontalOffset - delta, null, null, true);
                e.Handled = true;
            }
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < children; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var found = FindScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>代理设置对话框：跟随系统 / 手动填地址 / 关闭。</summary>
    private async void ProxySettings_Click(object sender, RoutedEventArgs e)
    {
        var modeCombo = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (mode, label) in new[]
                 {
                     (GameBananaProxy.ProxyMode.None, "不使用代理"),
                     (GameBananaProxy.ProxyMode.System, "跟随系统代理"),
                     (GameBananaProxy.ProxyMode.Manual, "手动指定地址")
                 })
        {
            modeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = mode });
            if (GameBananaProxy.Mode == mode)
            {
                modeCombo.SelectedIndex = modeCombo.Items.Count - 1;
            }
        }

        var manualBox = new TextBox
        {
            Width = 280,
            PlaceholderText = "http://127.0.0.1:7890",
            Text = GameBananaProxy.ManualUrl,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        manualBox.Header = "手动代理地址（本地加速端口）";

        var tip = new TextBlock
        {
            Text = "上不去 GameBanana 时：开着加速软件的话选「跟随系统代理」，" +
                   "或者手动填本地端口（如 Clash 的 7890）。改完重新打开本窗口生效。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var stack = new StackPanel { Spacing = 12 };
        var modeLabel = new TextBlock { Text = "代理模式" };
        stack.Children.Add(modeLabel);
        stack.Children.Add(modeCombo);
        stack.Children.Add(manualBox);

        // 测试连接：按弹窗当前的选择（还没保存也能测）临时开一条通道试 GameBanana
        var testRow = new StackPanel { Spacing = 4 };
        var testButton = new Button { Content = "测试连接", HorizontalAlignment = HorizontalAlignment.Left };
        var testResult = new TextBlock { TextWrapping = TextWrapping.Wrap };
        testRow.Children.Add(testButton);
        testRow.Children.Add(testResult);
        stack.Children.Add(testRow);
        stack.Children.Add(tip);

        testButton.Click += async (s, e) =>
        {
            testButton.IsEnabled = false;
            testResult.Text = "测试中…";

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var mode = ((ComboBoxItem)modeCombo.SelectedItem!)?.Tag is GameBananaProxy.ProxyMode m
                    ? m
                    : GameBananaProxy.ProxyMode.None;
                var proxyUri = mode switch
                {
                    GameBananaProxy.ProxyMode.System => GameBananaProxy.GetSystemProxyUri(),
                    GameBananaProxy.ProxyMode.Manual when Uri.TryCreate(manualBox.Text.Trim(), UriKind.Absolute, out var u)
                        && u.Scheme is "http" or "https" => u,
                    _ => null
                };

                using var handler = new HttpClientHandler();
                if (proxyUri is not null)
                {
                    handler.UseProxy = true;
                    handler.Proxy = new System.Net.WebProxy(proxyUri);
                }

                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
                using var response = await client.GetAsync("https://gamebanana.com/apiv11").ConfigureAwait(true);
                response.EnsureSuccessStatusCode();
                testResult.Text = $"✓ 连接正常（{sw.ElapsedMilliseconds / 1000.0:F1} 秒，HTTP {(int)response.StatusCode}）";
            }
            catch (Exception ex)
            {
                testResult.Text = $"✗ 连不上：{ex.Message}\n检查加速器是否在跑、换节点，或改用手动代理地址";
            }

            testButton.IsEnabled = true;
        };

        var dialog = new ContentDialog
        {
            Title = "GameBanana 访问代理",
            Content = stack,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var selectedMode = ((ComboBoxItem)modeCombo.SelectedItem!)?.Tag is GameBananaProxy.ProxyMode m
            ? m
            : GameBananaProxy.ProxyMode.None;
        GameBananaProxy.Mode = selectedMode;
        GameBananaProxy.ManualUrl = manualBox.Text.Trim();
        GameBananaProxy.Save();

        ViewModel.StatusText =
            selectedMode == GameBananaProxy.ProxyMode.None ? "代理已关闭，重新打开窗口生效"
            : selectedMode == GameBananaProxy.ProxyMode.System ? "将跟随系统代理，重新打开窗口生效"
            : "将使用手动代理，重新打开窗口生效";
    }
}
