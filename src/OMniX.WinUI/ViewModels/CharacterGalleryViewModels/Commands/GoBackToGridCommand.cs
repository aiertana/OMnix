using CommunityToolkit.Mvvm.Input;
using OMniX.WinUI.Views;

namespace OMniX.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    [RelayCommand]
    private void GoBackToGrid()
    {
        var gridLastStack = _navigationService.GetBackStackItems().LastOrDefault();

        if (gridLastStack is not null && gridLastStack.SourcePageType == typeof(CharactersPage))
        {
            _navigationService.GoBack();
            return;
        }

        _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!, _category);
    }
}