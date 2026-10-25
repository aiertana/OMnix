using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using CommunityToolkit.WinUI;
using OMniX.WinUI.Helpers.Xaml;
using OMniX.WinUI.Models;
using OMniX.WinUI.ViewModels;
using OMniX.WinUI.ViewModels.SubVms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;
using OMniX.Core.Contracts.Services;

namespace OMniX.WinUI.Views;

public sealed partial class CharactersPage : Page
{
    public CharactersViewModel ViewModel { get; }

    public CharactersPage()
    {
        ViewModel = App.GetService<CharactersViewModel>();
        InitializeComponent();
        Loaded += (sender, args) =>
        {
            SearchBox.Focus(FocusState.Keyboard);
            ViewModel.SimpleSelectProcessDialogVM.Dialog = SelectProcessDialog;
        };
        ViewModel.OnScrollToCharacter += ViewModel_OnScrollToCharacter;
    }

    private async void ViewModel_OnScrollToCharacter(object? sender, ScrollToCharacterArgs e)
    {
        var character = e?.Character?.Character;
        if (character == null!) return;

        await CharactersGridView.AwaitUiElementLoaded(TimeSpan.FromMilliseconds(500));


        var item = CharactersGridView.Items.FirstOrDefault(x =>
            ((CharacterGridItemModel)x).Character.InternalNameEquals(character));

        if (item is null)
        {
            Debugger.Break();
        }

        await CharactersGridView.SmoothScrollIntoViewWithItemAsync(item, ScrollItemPlacement.Center, disableAnimation: true,
            scrollIfVisible: false);
    }


    private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.AutoSuggestBox_TextChanged(sender.Text);
    }


    private void CharacterSearchKeyShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
    }

    /// <summary>
    ///     二开：点App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_NewCharacter", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_NewCharacter", "新增角色"))卡片，输入名字后直接创建
    /// </summary>
    private async void AddCharacterCard_OnClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_SearchPlaceholder", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_SearchPlaceholder", "输入角色名字…")),
            MinWidth = 280
        };

        var dialog = new ContentDialog
        {
            Title = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_NewCharacter", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_NewCharacter", "新增角色")),
            Content = nameBox,
            PrimaryButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_Create", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_Create", "确认创建")),
            CloseButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_Cancel", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/CharVM_Cancel", "取消")),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await ViewModel.CreateCharacterAsync(nameBox.Text);
    }

    private async void SearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await ViewModel.SuggestionBox_Chosen((CharacterGridItemModel?)args.ChosenSuggestion);
    }

    private void ImageCommandsFlyout_OnOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menuFlyout)
            return;

        if (menuFlyout.Target.DataContext is not CharacterGridItemModel character)
            return;

        ViewModel.OnRightClickContext(character);
    }

    private void SetGridDropHereVisibility(Grid characterThumbnail, Visibility visibility)
    {
        var dropHereIcon = ((FontIcon)characterThumbnail.FindName("DropHereIcon"));
        dropHereIcon.Visibility = visibility;
        var dropHereBorder = ((Border)characterThumbnail.FindName("DropHereBorder"));
        dropHereBorder.Visibility = visibility;
    }

    private void CharacterThumbnail_OnDragEnter(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;

        var gridItem = ((Grid)sender);
        SetGridDropHereVisibility(gridItem, Visibility.Visible);
    }

    private void CharacterThumbnail_OnDragLeave(object sender, DragEventArgs e)
    {
        var gridItem = ((Grid)sender);
        SetGridDropHereVisibility(gridItem, Visibility.Collapsed);
    }

    private async void CharacterThumbnail_OnDrop(object sender, DragEventArgs e)
    {
        if (((Grid)sender).DataContext is CharacterGridItemModel characterGridItem)
        {
            var urlFormats = new[] { "Text", "UniformResourceLocatorW", "UniformResourceLocator" };
            if (urlFormats.All(format => e.DataView.Contains(format)))
            {
                try
                {
                    var uri = await e.DataView.GetWebLinkAsync();
                    await ViewModel.ModUrlDroppedOnCharacterAsync(characterGridItem, uri);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
            else
                await ViewModel.ModDroppedOnCharacterAsync(characterGridItem, await e.DataView.GetStorageItemsAsync());
        }

        var gridItem = ((Grid)sender);
        SetGridDropHereVisibility(gridItem, Visibility.Collapsed);
    }

    private void DragAndDropArea_OnDragEnter(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        Log.Information("DragEnter_DragAndDropArea_OnDragEnter");
    }

    private void DragAndDropArea_OnDrop(object sender, DragEventArgs e)
    {
        Log.Information("Drop_DragAndDropArea_OnDrop");
    }

    private void BitmapImage_OnImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Log.Error("Failed to load dock panel element icon. Reason: {e}", e.ErrorMessage);
    }

    private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.DockPanelVM.ElementSelectionChanged(e.AddedItems.OfType<ElementIcon>(),
            e.RemovedItems.OfType<ElementIcon>());
    }

    private void SortingComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SortByCommand.Execute(e.AddedItems.OfType<CharactersViewModel.GridItemSortingMethod>());
    }
}