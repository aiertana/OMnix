using System.Text;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotificationManager = OMniX.WinUI.Services.Notifications.NotificationManager;

namespace OMniX.WinUI.ViewModels.SettingsViewModels;

internal class ClearEmptyFoldersDialog
{
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();


    public async Task ShowDialogAsync()
    {
        var dialog = new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("/Dialogs/ClearFolders_Title", "清理空文件夹"),
            Content = new TextBlock()
            {
                Text = _localizer.GetLocalizedStringOrDefault("/Dialogs/ClearFolders_Content",
                    "会把角色 Mod 列表里的空文件夹删掉（完全空的，或者里面只剩 .OMniX_ 开头的文件/文件夹）。\n" +
                    "角色文件夹本身空了，也会一起删。\n" +
                    "Mods 根目录下的空文件夹同样会删。"),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/Dialogs/ClearFolders_Primary", "删除"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/Dialogs/ClearFolders_Cancel", "取消")
        };


        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            var deletedFolders = await Task.Run(() => _skinManagerService.CleanCharacterFolders());
            var sb = new StringBuilder();
            sb.AppendLine(_localizer.GetLocalizedStringOrDefault("/Dialogs/ClearFolders_DeletedHeader", "删掉的文件夹："));
            foreach (var folder in deletedFolders)
            {
                sb.AppendLine(folder.FullName);
            }

            var message = sb.ToString();

            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/Dialogs/ClearFolders_DoneTitle", "空文件夹已清理"),
                message, TimeSpan.FromSeconds(5));
        }
    }
}
