using Windows.System;
using CommunityToolkitWrapper;
using OMniX.Core.GamesService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Helpers.Xaml;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OMniX.WinUI.Views;

public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel { get; }

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        NavigationViewControl.IsPaneOpen = true;


        // TODO: Set the title bar icon by updating /Assets/WindowIcon.ico.
        // A custom title bar is required for full window theme and Mica support.
        // https://docs.microsoft.com/windows/apps/develop/title-bar?tabs=winui3#full-customization
        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);
        App.MainWindow.Activated += MainWindow_Activated;
        AppTitleBarText.Text = ResourceExtensions.GetLocalized("AppDisplayName");
#if DEBUG
        AppTitleBarText.Text += " - DEBUG";
#endif
        KeyDown += GlobalKeyHandler_Invoked;
        PointerPressed += GlobalMouseHandler_Invoked;

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.IsNotFirstTimeStartupPage))
                UpdateTitleBarMargin();
        };
        UpdateTitleBarMargin();

        Loaded += (sender, args) =>
        {
            var bindings = new Binding()
            {
                Source = ViewModel,
                Path = new PropertyPath(nameof(ViewModel.SettingsInfoBadgeOpacity)),
                Mode = BindingMode.OneWay
            };

            var settingsItem = (NavigationViewItem)NavigationViewControl.SettingsItem;
            var infoBadge = new InfoBadge() { Opacity = 0, Value = 1 };
            settingsItem.InfoBadge = infoBadge;


            BindingOperations.SetBinding(settingsItem.InfoBadge, OpacityProperty, bindings);

            Bindings.Update();
        };

        ViewModel.GameService.Initialized += GameServiceOnInitialized;

#if RELEASE
        // Hide debug menu in release mode
        DebugItem.Visibility = Visibility.Collapsed;

#endif
    }

    private void GameServiceOnInitialized(object? sender, EventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            // 首页保持在侧栏第一项，角色分类从索引 1 开始插入
            var categories = ViewModel.GameService.GetCategories();
            var index = 1;
            foreach (var category in categories)
            {
                var categoryViewItem = new NavigationViewItem()
                {
                    Content = category.DisplayNamePlural,
                    Tag = category.InternalName.Id
                };
                NavigationHelper.SetNavigateToParameter(categoryViewItem, category);
                NavigationHelper.SetNavigateTo(categoryViewItem, typeof(CharactersViewModel).FullName!);


                switch (category.ModCategory)
                {
                    case ModCategory.Character:
                        categoryViewItem.Icon = new FontIcon() { Glyph = "\uE716" };

                        ViewModel.NavigationViewService.MenuItems!.Insert(index, categoryViewItem);
                        break;
                    case ModCategory.NPC:
                        categoryViewItem.Icon = new BitmapIcon()
                        {
                            UriSource = new Uri($"{App.ASSET_DIR}/NPC_Icon.png"),
                            ShowAsMonochrome = true
                        };

                        ViewModel.NavigationViewService.MenuItems!.Insert(index, categoryViewItem);
                        break;
                    case ModCategory.Object:
                        categoryViewItem.Icon = new FontIcon() { Glyph = "\uE8FC" };

                        ViewModel.NavigationViewService.MenuItems!.Insert(index, categoryViewItem);
                        break;
                    case ModCategory.Weapons:
                        categoryViewItem.Icon = new BitmapIcon()
                        {
                            UriSource = new Uri($"{App.ASSET_DIR}/Weapon_Icon.png"),
                            ShowAsMonochrome = false
                        };

                        ViewModel.NavigationViewService.MenuItems!.Insert(index, categoryViewItem);
                        break;


                    case ModCategory.Ui:
                    case ModCategory.Gliders:
                    case ModCategory.Custom:
                    default:
                        categoryViewItem.Icon = new FontIcon() { Glyph = "\uF142" };
                        ViewModel.NavigationViewService.MenuItems!.Insert(index, categoryViewItem);
                        break;
                }

                index++;

                //const string menuName = "Categories";
                //if (NavigationViewControl.MenuItems[1] is NavigationViewItem { Tag: not null } menuItem &&
                //    menuItem.Tag.Equals(menuName))
                //{
                //    menuItem.MenuItems.Add(categoryViewItem);
                //}
                //else
                //{
                //    var categoriesItem = new NavigationViewItem()
                //    {
                //        Content = menuName,
                //        Icon = new FontIcon() { Glyph = "\uE712" },
                //        Tag = menuName,
                //        SelectsOnInvoked = false
                //    };


                //    categoriesItem.MenuItems.Add(categoryViewItem);

                //    NavigationHelper.SetNavigateToParameter(categoryViewItem, category);
                //    NavigationViewControl.MenuItems.Insert(1, categoriesItem);
                //    categoriesItem.IsExpanded = true;
                //}
            }
        });

        App.MainWindow.DispatcherQueue.EnqueueAsync(async () =>
        {
            var notSelectedGame = await ViewModel.SelectedGameService.GetNotSelectedGameAsync();

            foreach (var game in notSelectedGame.Reverse())
            {
                var content = new StackPanel()
                {
                    Orientation = Orientation.Horizontal
                };

                var gameInfo = await GameService.GetGameInfoAsync(game);

                if (gameInfo is null)
                    return;

                content.Children.Add(new Grid()
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(8),
                    Children =
                {
                    new Image()
                    {
                        Source = new BitmapImage(new Uri(gameInfo.GameIcon)) { DecodePixelWidth = 20 }
                    }
                }
                });

                content.Children.Add(new TextBlock()
                {
                    Text = gameInfo.GameName,
                    Margin = new Thickness(16, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var navigationItem = new NavigationViewItem()
                {
                    Name = "SwitchGameButton",
                    Content = content,
                    Tag = $"{gameInfo.GameShortName}"
                };

                NavigationViewControl.FooterMenuItems.Insert(0, navigationItem);
                navigationItem.DoubleTapped += SwitchGameButtonOnDoubleTapped;

                var toolTip = new ToolTip
                {
                    Content = $"Double click to switch to {gameInfo.GameName}"
                };

                ToolTipService.SetToolTip(navigationItem, toolTip);
            }
        });
    }

    private void GlobalMouseHandler_Invoked(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled)
            return;

        // Check if mouse 4 or 5 is clicked
        var mouseButton = e.GetCurrentPoint(this).Properties.PointerUpdateKind;

        if (mouseButton is not (PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed)) return;

        var navigationService = App.GetService<INavigationService>();

        switch (mouseButton)
        {
            case PointerUpdateKind.XButton1Pressed when navigationService.CanGoBack:
                navigationService.GoBack();
                break;
            case PointerUpdateKind.XButton2Pressed when navigationService.CanGoForward:
                navigationService.GoForward();
                break;
        }
    }


    private Storyboard? _flowStoryboard;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleBarHelper.UpdateTitleBar(RequestedTheme);

        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));

        if (ThemeStyleService.Instance is not null)
            ThemeStyleService.Instance.Changed += OnThemeStyleChanged;

        ApplyFlowMotion();

        // 二开：切页面时新页面淡入放大（参考稿 panel.active 的 0.38s 过渡）
        NavigationFrame.Navigated += OnFrameNavigated;

        // 二开：点按音效 + 粒子挂在根上，全界面统一收 PointerPressed
        MotionFx.AttachClickFx(this, FxLayer);

        // 二开：背景樱花飘落，铺在最底层，跟界面风格同一套开关
        MotionFx.AttachSakura(ShellRoot);

        // 二开：看板娘跟着鼠标跑（默认趴着 / 按下张嘴）
        MotionFx.AttachCompanion(this, FxLayer);

        // 二开：侧边栏选中项的滑动指示条
        MotionFx.AttachNavIndicator(NavigationViewControl, FxLayer);

        await SyncDlss5ToggleAsync();
    }

    /// <summary>换风格时立刻启停流动光斑</summary>
    private void OnThemeStyleChanged(object? sender, OmniXThemeStyle style) => ApplyFlowMotion();

    /// <summary>
    ///     380ms、cubic-bezier(.22,1,.36,1) 的补间。
    ///     内置的 CubicEase 只能选现成曲线，要文档里这条自定义控制点就得用 KeySpline。
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Spline(DependencyObject target, string property, double to)
    {
        var animation = new DoubleAnimationUsingKeyFrames();

        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)),
            Value = to,
            KeySpline = new KeySpline
            {
                ControlPoint1 = new Windows.Foundation.Point(0.22, 1),
                ControlPoint2 = new Windows.Foundation.Point(0.36, 1)
            }
        });

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);

        return animation;
    }

    /// <summary>
    ///     二开：页面切换动画。参考稿的 .panel 是 opacity 0→1 配 scale 0.94→1，
    ///     0.38s 缓出。简易版直接跳过。
    /// </summary>
    private void OnFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (sender is not Frame frame)
            return;

        // 新页面是新的一批元素，LayoutUpdated 不一定会再触发，这里补扫一次，
        // 否则切到别的页面后那些按钮就没有悬停反馈。
        MotionFx.ScanTree(frame);

        // 切页面一声"咚"。点导航项的"嗒"是先响的，两个音错开一点，听上去是连贯的两下
        UiSoundService.Instance?.PlayPopup();

        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        // 用 CompositeTransform：缩放和位移要一起做
        var transform = frame.RenderTransform as CompositeTransform ?? new CompositeTransform();
        frame.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        frame.RenderTransform = transform;

        // 明确保底：万一动画没跑完，也要把页面恢复成完全可见
        void Restore()
        {
            frame.Opacity = 1;
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.TranslateY = 0;
        }

        try
        {
            // 参考稿：新页面从下方 16px 处淡入，scale 0.98 → 1，380ms
            transform.ScaleX = 0.98;
            transform.ScaleY = 0.98;
            transform.TranslateY = 16;
            frame.Opacity = 0;

            var storyboard = new Storyboard();

            storyboard.Children.Add(Spline(transform, nameof(CompositeTransform.ScaleX), 1.0));
            storyboard.Children.Add(Spline(transform, nameof(CompositeTransform.ScaleY), 1.0));
            storyboard.Children.Add(Spline(transform, nameof(CompositeTransform.TranslateY), 0.0));
            storyboard.Children.Add(Spline(frame, "Opacity", 1.0));

            storyboard.Completed += (_, _) => Restore();
            storyboard.Begin();
        }
        catch
        {
            Restore();
        }
    }

    private void ApplyFlowMotion()
    {
        var enabled = ThemeStyleService.Instance?.MotionEnabled ?? false;

        _flowStoryboard?.Stop();
        _flowStoryboard = null;

        if (!enabled)
            return;

        var storyboard = new Storyboard();

        // 两个光斑走不同周期，看起来才不像在原地晃
        storyboard.Children.Add(Drift(FlowWarmMove, "X", 120, 22));
        storyboard.Children.Add(Drift(FlowWarmMove, "Y", -70, 19));
        storyboard.Children.Add(Drift(FlowCoolMove, "X", -100, 25));
        storyboard.Children.Add(Drift(FlowCoolMove, "Y", 80, 17));

        storyboard.Begin();
        _flowStoryboard = storyboard;
    }

    private static DoubleAnimation Drift(TranslateTransform transform, string property, double to, int seconds)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromSeconds(seconds)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, property);

        return animation;
    }

    private bool _dlss5Syncing;

    /// <summary>
    ///     二开：把插件真实状态同步到开关上（不触发业务逻辑）。
    /// </summary>
    private async Task SyncDlss5ToggleAsync()
    {
        await ViewModel.RefreshDlss5Async();

        _dlss5Syncing = true;
        ModToggle.IsOn = ViewModel.ModEnabled;
        Dlss5Toggle.IsOn = ViewModel.Dlss5Enabled;
        _dlss5Syncing = false;
    }

    private async void ModToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        if (_dlss5Syncing || sender is not ToggleSwitch toggle)
            return;

        await ViewModel.SetModEnabledAsync(toggle.IsOn);

        _dlss5Syncing = true;
        toggle.IsOn = ViewModel.ModEnabled;
        _dlss5Syncing = false;
    }

    private async void Dlss5Toggle_OnToggled(object sender, RoutedEventArgs e)
    {
        if (_dlss5Syncing || sender is not ToggleSwitch toggle)
            return;

        await ViewModel.SetDlss5EnabledAsync(toggle.IsOn);

        _dlss5Syncing = true;
        toggle.IsOn = ViewModel.Dlss5Enabled;
        ModToggle.IsOn = ViewModel.ModEnabled;
        _dlss5Syncing = false;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        var resource = args.WindowActivationState == WindowActivationState.Deactivated
            ? "WindowCaptionForegroundDisabled"
            : "WindowCaptionForeground";

        AppTitleBarText.Foreground = (SolidColorBrush)Application.Current.Resources[resource];
        App.AppTitlebar = AppTitleBarText as UIElement;
    }

    private void NavigationViewControl_DisplayModeChanged(NavigationView sender,
        NavigationViewDisplayModeChangedEventArgs args) =>
        UpdateTitleBarMargin();

    /// <summary>
    ///     标题栏左侧留出侧栏宽度，侧栏隐藏时铺满整个窗口
    /// </summary>
    private void UpdateTitleBarMargin()
    {
        AppTitleBar.Margin = new Thickness
        {
            Left = ViewModel.IsNotFirstTimeStartupPage ? 216 : 0,
            Top = AppTitleBar.Margin.Top,
            Right = AppTitleBar.Margin.Right,
            Bottom = AppTitleBar.Margin.Bottom
        };
    }

    private async void TutorialButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/aiertana/OMnix#readme"));
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
    {
        var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

        if (modifiers.HasValue) keyboardAccelerator.Modifiers = modifiers.Value;

        keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

        return keyboardAccelerator;
    }

    private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        var navigationService = App.GetService<INavigationService>();

        var result = navigationService.GoBack();

        args.Handled = result;
    }

    private readonly VirtualKey[] _code = new[]
    {
        VirtualKey.Up, VirtualKey.Up, VirtualKey.Down, VirtualKey.Down, VirtualKey.Left, VirtualKey.Right,
        VirtualKey.Left, VirtualKey.Right, VirtualKey.B, VirtualKey.A, VirtualKey.Enter, VirtualKey.Space
    };

    private readonly List<VirtualKey> _codeKeys = new();

    private async void GlobalKeyHandler_Invoked(object sender, KeyRoutedEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (e.Key == VirtualKey.F10)
        {
            await ViewModel.RefreshGenshinMods();
            return;
        }


        if (_code.Contains(e.Key))
        {
            //_codeKeys.AddRange(_code[..^2].Append(VirtualKey.Space));
            _codeKeys.Add(e.Key);
        }
        else
            _codeKeys.Clear();


        if (_codeKeys.Count == _code.Length - 1 &&
            _codeKeys.SequenceEqual(_code.Take(_code.Length - 2).Append(VirtualKey.Space)) ||
            _codeKeys.SequenceEqual(_code.Take(_code.Length - 2).Append(VirtualKey.Enter)))
        {
            Perform_XD();
            _codeKeys.Clear();
        }
        else if (_code.Length < _codeKeys.Count - 1)
            _codeKeys.Clear();
    }

    private void Perform_XD()
    {
        App.GetService<INavigationService>().NavigateTo(typeof(EasterEggVM).FullName!);
    }

    private async void SwitchGameButtonOnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not NavigationViewItem)
            return;
        e.Handled = true;
        await Task.Delay(200);

        if (sender is FrameworkElement { Tag: string gameName })
        {
            await App.GetService<LifeCycleService>()
                .RestartAsync(notifyOnError: true,
                    postShutdownLogic: () => ViewModel.SelectedGameService.SetSelectedGame(gameName))
                    .ConfigureAwait(false);
        }
    }
}