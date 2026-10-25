using Windows.System;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Helpers;
using Microsoft.UI.Xaml.Controls;
using OMniX.WinUI.Services.Notifications;
using Windows.Storage;
using OMniX.WinUI.Services;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Models.Settings;
using OMniX.Core.Contracts.Services;

namespace OMniX.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanOpenModFolder(ModGridItemVm? vm) =>
        vm is not null && !IsNavigating && !IsBusy && !vm.FolderPath.IsNullOrEmpty() && Directory.Exists(vm.FolderPath);

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task OpenModFolder(ModGridItemVm vm)
    {
        await Launcher.LaunchFolderPathAsync(vm.FolderPath);
    }


    private bool CanOpenModUrl(ModGridItemVm? vm) => vm is not null && !IsNavigating && !IsBusy && vm.HasModUrl;

    [RelayCommand(CanExecute = nameof(CanOpenModUrl))]
    private async Task OpenModUrl(ModGridItemVm vm)
    {
        await Launcher.LaunchUriAsync(vm.ModUrl);
    }

    /// <summary>
    /// return the result of the dialog and if the checkbox "Do not ask again" is checked
    /// </summary>
    private async Task<(ContentDialogResult, bool)> PromptDeleteDialog(ModGridItemVm vm)
    {
        var windowManager = App.GetService<IWindowManagerService>();

        var doNotAskAgainCheckBox = new CheckBox()
        {
            Content = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DontAskAgain", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DontAskAgain", "不再询问")),
            IsChecked = false,
        };
        var stackPanel = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text = string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DeleteConfirm", "确定要删除「{0}」吗？删除后可以在回收站里找回。"), vm.Name),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
                },
                doNotAskAgainCheckBox
            }
        };

        var dialog = new ContentDialog()
        {
            Title = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DeleteModTitle", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DeleteModTitle", "删除 Mod")),
            Content = stackPanel,
            PrimaryButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_Delete", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_Delete", "删除")),
            CloseButtonText = App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_Cancel", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_Cancel", "取消")),
            DefaultButton = ContentDialogButton.Primary,
        };

        // get result and check if checkbox is checked
        var result = await windowManager.ShowDialogAsync(dialog);
        var doNotAskAgain = doNotAskAgainCheckBox.IsChecked == true;

        return (result, doNotAskAgain);
    }

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task DeleteMod(ModGridItemVm vm)
    {
        if (_modList is null) { return; }

        var notificationManager = App.GetService<NotificationManager>();
        var settings =
            await _localSettingsService
                .ReadOrCreateSettingAsync<CharacterGallerySettings>(CharacterGallerySettings.Key);

        if (settings.CanDeleteDialogPrompt)
        {
            var (result, doNotAskAgainChecked) = await PromptDeleteDialog(vm);
            if (doNotAskAgainChecked)
            {
                settings.CanDeleteDialogPrompt = false;
                await _localSettingsService.SaveSettingAsync(CharacterGallerySettings.Key, settings);
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }
        }

        try
        {
            _modList.DeleteModBySkinEntryId(vm.Id);
            await ReloadModsAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to delete mod");
            notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DeleteFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DeleteFailed", "删除失败")), e.Message, TimeSpan.FromSeconds(10));
            return;
        }

        notificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_ModDeleted", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_ModDeleted", "已删除 Mod")), string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_MovedToBin", "「{0}」已移到回收站"), vm.Name), TimeSpan.FromSeconds(5));
    }
}