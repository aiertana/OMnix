using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using CommunityToolkit.WinUI.UI.Animations;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers.Xaml;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Serilog;

namespace OMniX.WinUI.Views.CharacterDetailsPages;

public sealed partial class CharacterDetailsPage : Page
{
    public CharacterDetailsViewModel ViewModel { get; } = App.GetService<CharacterDetailsViewModel>();

    public CharacterDetailsPage()
    {
        InitializeComponent();
        CharacterCard.ViewModel = ViewModel;
        ModPane.ViewModel = ViewModel.ModPaneVM;
        ModGrid.ViewModel = ViewModel.ModGridVM;
        ModGrid.ViewModel.OnModsReloaded += OnModsReloaded;

        ViewModel.OnModObjectLoaded += OnModObjectLoaded;
        ViewModel.OnModsLoaded += OnModsLoaded;
        ViewModel.OnInitializingFinished += OnInitializingFinished;

        ViewModel.ContextMenuVM.CloseFlyout += ContextMenuVM_CloseFlyout;
    }


    /// <summary>
    ///     二开：切换外观分区
    /// </summary>
    private void SectionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: SectionItemVm section })
            ViewModel.SelectedSectionItem = section;
    }


    private void OnModObjectLoaded(object? sender, EventArgs e)
    {
        ViewModel.OnModObjectLoaded -= OnModObjectLoaded;
        ViewModel.GridLoadedAwaiter = () => ModGrid.DataGrid.AwaitItemsSourceLoaded(ViewModel.CancellationToken);

        if (!ViewModel.IsCharacter) return;

        // 二开：原来不管有几个皮肤都往整个面板上挂一条英文 ToolTip
        // （"This character only has one default in-game skin, so you can't change it."），
        // 只有默认皮肤时按钮就是灰的，用户看不出为什么点不了、也不知道怎么才能选。
        // 现在按皮肤数量给中文提示，并把「怎么才会出现更多皮肤」说清楚。
        var skins = ViewModel.Character.Skins;
        var skinButton = CharacterCard.SelectSkinButton;

        if (skins is not null && skins.Count > 1)
        {
            skinButton.Visibility = Visibility.Visible;
            CharacterCard.NoMoreSkinsText.Visibility = Visibility.Collapsed;

            skinButton.Content = "选择皮肤";
            ToolTipService.SetToolTip(skinButton, "切换游戏内显示的皮肤（快捷键 F1）");
            return;
        }

        // 只有一个皮肤：按钮直接藏起来，换一行灰字说明，避免出现一个点不动的灰按钮
        skinButton.Visibility = Visibility.Collapsed;
        CharacterCard.NoMoreSkinsText.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(CharacterCard.NoMoreSkinsText,
            "这个角色目前只有默认皮肤。\n安装带有多套皮肤的游戏内 Mod 后，这里会出现可选皮肤。");
    }


    private void OnModsLoaded(object? sender, EventArgs e)
    {
        ViewModel.OnModsLoaded -= OnModsLoaded;
        PageInitLoader.Visibility = Visibility.Collapsed;
        RightWorkingArea.Visibility = Visibility.Visible;

        if (ModGrid.ViewModel.ModdableObjectHasAnyMods) return;
        ShowNoModsElement();
    }

    private void OnModsReloaded(object? sender, EventArgs eventArgs)
    {
        if (ViewModel.ModGridVM.ModdableObjectHasAnyMods)
            HideNoModsElement();
        else
            ShowNoModsElement();
    }

    private void OnInitializingFinished(object? sender, EventArgs e)
    {
        ViewModel.OnInitializingFinished -= OnInitializingFinished;
        ModGrid.DataGrid.ContextFlyout = ModRowFlyout;
        ModGrid.DataGrid.Focus(FocusState.Programmatic);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (CharacterCard?.ItemHero != null) // Trying to fix an argument null exception
            this.RegisterElementForConnectedAnimation("animationKeyContentGrid", CharacterCard.ItemHero);
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (e.NavigationMode == NavigationMode.Back)
        {
            var navigationService = App.GetService<INavigationService>();
            if (ViewModel.ShownModObject != null!)
                navigationService.SetListDataItemForNextConnectedAnimation(ViewModel.ShownModObject);
        }
    }


    private void ShowNoModsElement()
    {
        var noModsElement = EnsureNoModsUIElementAdded();
        noModsElement.Visibility = Visibility.Visible;
        ModGrid.Visibility = Visibility.Collapsed;
        ModPane.Visibility = Visibility.Collapsed;
        ModPaneSplitter.Visibility = Visibility.Collapsed;
        SearchModsTextBox.Visibility = Visibility.Collapsed;

        ModListArea.AllowDrop = false;
        MainContentArea.AllowDrop = true;
    }

    private void HideNoModsElement()
    {
        var noModsElement = FindNoModsUIElement();
        if (noModsElement is not null)
            noModsElement.Visibility = Visibility.Collapsed;


        ModGrid.Visibility = Visibility.Visible;
        ModPane.Visibility = Visibility.Visible;
        ModPaneSplitter.Visibility = Visibility.Visible;
        SearchModsTextBox.Visibility = Visibility.Visible;

        ModListArea.AllowDrop = true;
        MainContentArea.AllowDrop = false;
    }

    private StackPanel? FindNoModsUIElement()
    {
        if (MainContentArea.FindName("NoModsStackPanel") is StackPanel existingStackPanel)
            return existingStackPanel;

        return null;
    }

    private StackPanel EnsureNoModsUIElementAdded()
    {
        var existingStackPanel = FindNoModsUIElement();
        if (existingStackPanel is not null)
            return existingStackPanel;

        // 二开：这里原来是 JASM 时期的英文空状态
        // （"No mods found for this character 😖" + 一个 1px 灰框的 "Drop Mods Here"），
        // 换成中文，并把颜色/圆角统一到项目主题，避免一眼看出前身。
        Brush Res(string key, Windows.UI.Color fallback)
        {
            return Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
                ? brush
                : new SolidColorBrush(fallback);
        }

        var stackPanel = new StackPanel
        {
            Name = "NoModsStackPanel",
            Visibility = Visibility.Collapsed,
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AllowDrop = false
        };

        var title = new TextBlock
        {
            Text = "这个角色还没有 Mod",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Res("OmniXTextPrimaryBrush", Colors.Black),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            AllowDrop = false
        };

        var subtitle = new TextBlock
        {
            Text = "把 Mod 文件夹拖进来，或从「Mod 安装」导入",
            FontSize = 13,
            Foreground = Res("OmniXTextSecondaryBrush", Colors.Gray),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
            AllowDrop = false
        };

        var backgroundGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AllowDrop = false,
            Background = new SolidColorBrush(Colors.Transparent)
        };

        var dropZone = new Border
        {
            BorderBrush = Res("OmniXCardStrokeHoverBrush", Colors.LightGray),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Background = Res("OmniXCardAltBackgroundBrush", Colors.Transparent),
            Width = 320,
            Height = 210,
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(16),
            AllowDrop = false
        };

        var dropContent = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            AllowDrop = false
        };

        dropContent.Children.Add(new FontIcon
        {
            Glyph = "\uE8E5",
            FontSize = 28,
            Foreground = Res("OmniXAccentBrush", Colors.Red),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            AllowDrop = false
        });

        dropContent.Children.Add(new TextBlock
        {
            Text = "拖放到这里",
            FontSize = 15,
            Foreground = Res("OmniXTextSecondaryBrush", Colors.Gray),
            HorizontalAlignment = HorizontalAlignment.Center,
            AllowDrop = false
        });

        dropZone.Child = dropContent;
        backgroundGrid.Children.Add(dropZone);

        stackPanel.Children.Add(title);
        stackPanel.Children.Add(subtitle);
        stackPanel.Children.Add(backgroundGrid);

        MainContentArea.Children.Add(stackPanel);

        return stackPanel;
    }


    private void KeyboardAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchModsTextBox.Focus(FocusState.Keyboard);
    }

    private void SearchModsTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchMods(SearchModsTextBox.Text);
    }

    private async void ModListArea_OnDragEnter(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                var uri = await e.DataView.GetWebLinkAsync();
                if (ViewModel.CanDragDropModUrl(uri))
                    e.AcceptedOperation = DataPackageOperation.Copy;
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                try
                {
                    var storageItems = await e.DataView.GetStorageItemsAsync();
                    if (ViewModel.CanDragDropMod(storageItems))
                        e.AcceptedOperation = DataPackageOperation.Copy;
                }
                catch (COMException exception)
                {
                    // When drag and dropping a folder from within an archive in WinRAR, GetStorageItemsAsync throws a COMException
                    // For this case, assume this is a valid drag and drop operation as the command itself will also check if the items are valid
                    // when (exception.HResult == -2147221404) HResult that is thrown specifically for WinRAR

                    e.AcceptedOperation = DataPackageOperation.Copy;

                    if (exception.HResult != -2147221404)
                    {
                        Log.Error(exception, "Error while checking if the dragged items are valid.");
                    }
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void ModListArea_OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                await ViewModel.DragDropModUrlAsync(await e.DataView.GetWebLinkAsync());
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                await ViewModel.DragDropModAsync(await e.DataView.GetStorageItemsAsync());
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ViewToggleSwitch_OnToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.GoToGalleryScreenCommand.CanExecute(null))
            ViewModel.GoToGalleryScreenCommand.ExecuteAsync(null);
    }

    #region ModRowFlyout

    private void ContextMenuVM_CloseFlyout(object? sender, EventArgs e) => ModRowFlyout.Hide();

    private void ModRowFlyout_OnOpening(object? sender, object e)
    {
        if (!ViewModel.ContextMenuVM.CanOpenContextMenu)
        {
            ModRowFlyout.Hide();
            return;
        }
    }

    private void ModRowFlyout_OnOpened(object? sender, object e) => MoveModSearchBox.Focus(FocusState.Programmatic);


    private void ModRowFlyout_OnClosing(FlyoutBase sender, FlyoutBaseClosingEventArgs args) => ViewModel.ContextMenuVM.OnFlyoutClosing();

    /// <summary>
    ///     二开：右键菜单里的「导出为压缩包…」，把选中的 Mod 交给导出面板（默认存到程序目录的 OMniX Exports）。
    /// </summary>
    private async void ExportSelectedMods_OnClick(object sender, RoutedEventArgs e)
    {
        ModRowFlyout.Hide();
        await ModGrid.ExportSelectedModsAsync();
    }

    private void OpenSelectedModFolder_OnClick(object sender, RoutedEventArgs e)
    {
        ModRowFlyout.Hide();
        ModGrid.OpenSelectedModFolder();
    }

    private void MoveModSearch_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.ContextMenuVM.SearchTextChanged(sender.Text);
    }

    private void MoveModSearch_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.ContextMenuVM.OnSuggestionChosen((SuggestedModObject)args.ChosenSuggestion);
        MoveModsButton.Focus(FocusState.Programmatic);
    }

    #endregion
}