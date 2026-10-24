using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Helpers;
using Microsoft.UI.Xaml.Media.Animation;

namespace OMniX.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanNavigateToModObject(SelectableModdableObjectVm? selectableModdableObject)
    {
        return selectableModdableObject is not null && !IsNavigating && !IsBusy && !selectableModdableObject.IsSelected;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToModObject))]
    private Task NavigateToModObject(SelectableModdableObjectVm selectableModdableObject)
    {
        IsNavigating = true;

        ModdableObjectVms.ForEach(m => m.IsSelected = false);
        selectableModdableObject.IsSelected = true;


        _navigationService.NavigateTo(typeof(CharacterGalleryViewModel).FullName!,
            selectableModdableObject.ModdableObject, transitionInfo: new SuppressNavigationTransitionInfo());

        IsNavigating = false;
        return Task.CompletedTask;
    }
}