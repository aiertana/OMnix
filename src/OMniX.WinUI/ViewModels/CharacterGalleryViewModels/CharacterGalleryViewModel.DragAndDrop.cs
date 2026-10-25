using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using OMniX.WinUI.Services.Notifications;
using OMniX.Core.Contracts.Services;

namespace OMniX.WinUI.ViewModels.CharacterGalleryViewModels;

/// <summary>
///     二开：画廊视图的拖入支持。
///     详情页（详细视图）一直能拖 Mod 进来，但画廊视图是另一个页面，之前完全没有拖放逻辑，
///     所以一切换过去就拖不进去了。
/// </summary>
public partial class CharacterGalleryViewModel
{
    public bool CanDragDropMod(IReadOnlyList<IStorageItem>? items)
    {
        if (IsNavigating || IsBusy)
            return false;

        if (items is null || items.Count != 1)
            return false;

        return items[0] is IStorageFolder || items[0] is IStorageFile;
    }

    public async Task DragDropModAsync(IReadOnlyList<IStorageItem> items)
    {
        if (!CanDragDropMod(items) || _modList is null)
        {
            App.GetService<NotificationManager>().ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DropFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DropFailed", "拖入失败")),
                App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DropInvalid", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DropInvalid", "拖入的内容不是有效的 Mod 文件夹或压缩包。")), TimeSpan.FromSeconds(5));
            return;
        }

        try
        {
            var installMonitor = await Task.Run(async () =>
                await _modDragAndDropService.AddStorageItemFoldersAsync(_modList, items).ConfigureAwait(false));

            if (installMonitor is not null)
                await installMonitor.Task;

            await ReloadModsAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to drag and drop mod in gallery view");
            App.GetService<NotificationManager>().ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DropFailed", App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ModList/Mg_DropFailed", "拖入失败")), e.Message, TimeSpan.FromSeconds(8));
        }
    }
}
