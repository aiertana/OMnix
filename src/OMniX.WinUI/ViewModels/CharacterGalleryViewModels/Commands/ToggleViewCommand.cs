using CommunityToolkit.Mvvm.Input;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanToggleView()
    {
        return !IsNavigating && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanToggleView))]
    private async Task ToggleView()
    {
        var settings =
            await _localSettingsService
                .ReadOrCreateSettingAsync<CharacterDetailsSettings>(CharacterDetailsSettings.Key, SettingScope.App);

        settings.GalleryView = !settings.GalleryView;

        await _localSettingsService.SaveSettingAsync(CharacterDetailsSettings.Key, settings, SettingScope.App);

        _navigationService.NavigateToCharacterDetails(_moddableObject!.InternalName);
        _navigationService.ClearBackStack(1);
    }
}