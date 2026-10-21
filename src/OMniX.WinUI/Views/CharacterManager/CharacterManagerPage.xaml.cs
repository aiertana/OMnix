using OMniX.WinUI.ViewModels;
using OMniX.WinUI.Views.CharacterManager;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OMniX.WinUI.Views;

public sealed partial class CharacterManagerPage : Page
{
    public CharacterManagerViewModel ViewModel { get; set; }

    public CharacterManagerPage()
    {
        ViewModel = App.GetService<CharacterManagerViewModel>();
        InitializeComponent();
        ViewModel.SetSelection += CharacterSelected;
        Loaded += (sender, args) => CharacterSearchBox.Focus(FocusState.Programmatic);
    }

    private void CharacterSelected(object? sender, CharacterManagerViewModel.SetSelectionArgs e)
    {
        if (e.Character is not null)
            EditFrame.Navigate(typeof(EditCharacterPage), e.Character.InternalName.Id);
        else if (e.NewCharacter)
            EditFrame.Navigate(typeof(CreateCharacterPage));
        else
            EditFrame.Content = null;
    }

    private void CharacterSearchBox_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.OnSearchTextChanged(sender.Text);
    }

    private void CharacterSearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SelectCharacterCommand.Execute(args.ChosenSuggestion);
    }

    private void CharacterSearchBox_OnSuggestionChosen(AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
    }

    private void CharacterSearchBox_Ctrl_F(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CharacterSearchBox.Focus(FocusState.Keyboard);
    }

    /// <summary>
    ///     二开：点头像墙里的某一个 → 打开它的编辑页。
    /// </summary>
    private void CharacterGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterSearchResult result)
            ViewModel.OpenCharacterCommand.Execute(result);
    }

    /// <summary>
    ///     二开：编辑页左上角的「返回角色列表」→ 回到头像墙。
    /// </summary>
    private void BackToGrid_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.BackToGridCommand.Execute(null);
    }
}