using System;
using System.Linq;
using System.Threading.Tasks;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Services;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.Services.ModHandling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace OMniX.WinUI.Services;

public class ModRandomizationService
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly CharacterSkinService _characterSkinService;
    private readonly NotificationManager _notificationManager;
    private readonly ILogger _logger;
    private static readonly Random Random = new();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public ModRandomizationService(
        IGameService gameService,
        ISkinManagerService skinManagerService,
        IWindowManagerService windowManagerService,
        CharacterSkinService characterSkinService,
        NotificationManager notificationManager,
        ILogger logger)
    {
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _windowManagerService = windowManagerService;
        _characterSkinService = characterSkinService;
        _notificationManager = notificationManager;
        _logger = logger.ForContext<ModRandomizationService>();
    }

    public async Task ShowRandomizeModsDialog()
    {
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_Title", "随机换 Mod"),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_Button", "随机"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_Cancel", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

        var categories = _gameService.GetCategories();
        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_PickHint", "选一下要给哪些分类随机：")
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_Explain", "说明：只会随机那些「本来就只能开一个」的 Mod 文件夹，所以 Others 里的不会动。同一个角色的皮肤，每次只会开一个 Mod。"),
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 10)
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

        stackPanel.Children.Add(new CheckBox
        {
            Margin = new Thickness(0, 10, 0, 0),
            Content = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_AllowNone", "允许随机结果是一个都不开（有些 Mod 文件夹可能整个空着）"),
            IsChecked = false
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_BackupTip", "开的 Mod 多的话，建议先存个预设（或者备份）再随机"),
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
            .SkipLast(1)
            .Where(c => c.IsChecked == true)
            .Select(c => categories.First(cat => cat.DisplayNamePlural.Equals(c.Content)))
            .ToList();

        var allowNoMods = stackPanel.Children
            .OfType<CheckBox>()
            .Last()
            .IsChecked == true;

        if (selectedCategories.Count == 0)
        {
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_NoneTitle", "没选分类"), _localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_NoneBody", "一个分类都没选，什么都没换。"),
                TimeSpan.FromSeconds(5));
            return;
        }

        try
        {
            await Task.Run(async () =>
            {
                var modLists = _skinManagerService.CharacterModLists
                    .Where(modList => selectedCategories.Contains(modList.Character.ModCategory))
                    .Where(modList => !modList.Character.IsMultiMod)
                    .ToList();

                foreach (var modList in modLists)
                {
                    var mods = modList.Mods.ToList();

                    if (mods.Count == 0)
                        continue;

                    // Need special handling for characters because they have an in game skins
                    if (modList.Character is ICharacter { Skins.Count: > 1 } character)
                    {
                        var skinModMap = await _characterSkinService.GetAllModsBySkinAsync(character)
                            .ConfigureAwait(false);
                        if (skinModMap is null)
                            continue;

                        // Don't know what to do with undetectable mods
                        skinModMap.UndetectableMods.ForEach(mod => modList.DisableMod(mod.Id));

                        foreach (var (_, skinMods) in skinModMap.ModsBySkin)
                        {
                            if (skinMods.Count == 0)
                                continue;

                            foreach (var mod in skinMods.Where(mod => modList.IsModEnabled(mod)))
                            {
                                modList.DisableMod(mod.Id);
                            }

                            var randomModIndex = Random.Next(0, skinMods.Count + (allowNoMods ? 1 : 0));

                            if (randomModIndex == skinMods.Count)
                                continue;

                            modList.EnableMod(skinMods.ElementAt(randomModIndex).Id);
                        }

                        continue;
                    }

                    foreach (var characterSkinEntry in mods.Where(characterSkinEntry => characterSkinEntry.IsEnabled))
                    {
                        modList.DisableMod(characterSkinEntry.Id);
                    }

                    var randomIndex = Random.Next(0, mods.Count + (allowNoMods ? 1 : 0));
                    if (randomIndex == mods.Count)
                        continue;

                    modList.EnableMod(mods[randomIndex].Id);
                }
            });
        }
        catch (Exception e)
        {
            _logger.Error(e, "随机换 Mod 失败");
            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_Failed", "随机换 Mod 失败"), e.Message, TimeSpan.FromSeconds(5));
            return;
        }

        _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/Random_Done", "随机完成"),
            "Mods have been randomized for the categories: " +
            string.Join(", ", selectedCategories.Select(c => c.DisplayNamePlural)),
            TimeSpan.FromSeconds(5));
    }
}