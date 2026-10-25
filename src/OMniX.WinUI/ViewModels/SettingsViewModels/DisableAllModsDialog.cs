using System.Text;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace OMniX.WinUI.ViewModels.SettingsViewModels;

public class DisableAllModsDialog
{
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly IGameService _gameService = App.GetService<IGameService>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<DisableAllModsDialog>();

    public async Task ShowDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_Title", "禁用 Mod"),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_Primary", "禁用所选分类的 Mod"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_Cancel", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };


        var categories = _gameService.GetCategories();

        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_PickPrompt", "选一下要给哪些分类关掉 Mod："),
            IsTextSelectionEnabled = true
        });


        foreach (var category in categories)
        {
            var checkBox = new CheckBox
            {
                Content = category.DisplayNamePlural,
                IsChecked = true
            };

            stackPanel.Children.Add(checkBox);
        }


        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_Hint",
                "开的 Mod 多的话，建议先存个预设（或者备份）再关。\n\n" +
                "所选分类里，只会关掉 OMniX 记录在案的 Mod"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 10, 0, 0)
        });


        dialog.Content = stackPanel;

        var result = await _windowManagerService.ShowDialogAsync(dialog);


        if (result != ContentDialogResult.Primary)
        {
            return;
        }


        var selectedCategories = stackPanel.Children
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => categories.First(cat => cat.DisplayNamePlural.Equals(c.Content)))
            .ToList();

        if (selectedCategories.Count == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_NoneTitle", "没选分类"),
                _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_NoneMsg", "一个分类都没选，所以什么都没动。"),
                TimeSpan.FromSeconds(5));
            return;
        }


        var modLists = _skinManagerService.CharacterModLists.Where(m => selectedCategories.Contains(m.Character.ModCategory)).ToList();

        var modListDisableTask = new List<Task<List<string>>>();


        foreach (var modList in modLists)
        {
            var task = Task.Run(() =>
            {
                var modsToDisable = modList.Mods.Where(m => m.IsEnabled).ToArray();
                var errors = new List<string>();
                foreach (var modEntry in modsToDisable)
                {
                    try
                    {
                        modList.DisableMod(modEntry.Id);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error while disabling mod.");
                        errors.Add($"{modEntry.Mod.FullPath}: {e.Message}");
                    }
                }

                return errors;
            });

            modListDisableTask.Add(task);
        }

        var errorsList = await Task.WhenAll(modListDisableTask);
        var errors = errorsList.SelectMany(e => e).ToArray();

        if (errors.Length == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_DoneTitle", "已禁用 Mod"),
                string.Format(
                    _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_DoneMsgFmt", "所选分类里由 OMniX 管理的 Mod 都关掉了：{0}"),
                    string.Join("、", selectedCategories.Select(c => c.DisplayNamePlural))),
                TimeSpan.FromSeconds(5));
            return;
        }


        var sb = new StringBuilder();
        sb.AppendLine(_localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_ErrorsHeader", "下面这些 Mod 出错了："));

        foreach (var error in errors)
        {
            sb.AppendLine(error);
        }


        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("/Dialogs/DisableAll_ErrTitle", "禁用 Mod 时出错"),
            sb.ToString(), TimeSpan.FromSeconds(10));
    }
}
