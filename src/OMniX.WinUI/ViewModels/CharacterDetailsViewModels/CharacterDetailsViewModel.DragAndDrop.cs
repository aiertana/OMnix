using Windows.Storage;
using OMniX.Core.Helpers;
using OMniX.Core.Services.GameBanana;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel
{
    public bool CanDragDropMod(IReadOnlyList<IStorageItem>? items)
    {
        if (IsHardBusy)
            return false;

        // 二开：这里原来是 items.Count != 1 —— 一次拖两个以上就直接判无效。
        // DragEnter 里拿到 false 就不会设置 AcceptedOperation，鼠标跟着变成禁止符，
        // 看着像「这个页面不支持批量拖入」。而角色列表页（CharactersPage 的头像）
        // 压根不做这个检查，所以那边反而能一次拖好几个进来，两个页面行为对不上。
        // 改成逐个校验：只要每一项都是合法的 Mod 文件夹或压缩包就放行。
        if (items is null || items.Count == 0)
            return false;

        foreach (var mod in items)
        {
            if (Directory.Exists(mod.Path))
                continue;

            if (!File.Exists(mod.Path))
                return false;

            var fileExt = Path.GetExtension(mod.Name);

            if (fileExt.IsNullOrEmpty() || !Constants.SupportedArchiveTypes.Contains(fileExt))
                return false;
        }

        return true;
    }

    public async Task DragDropModAsync(IReadOnlyList<IStorageItem> items)
    {
        if (!CanDragDropMod(items))
        {
            _notificationService.ShowNotification("拖拽操作失败",
                "The operation failed because the selected item is not a valid mod file or folder.",
                TimeSpan.FromSeconds(5));
            return;
        }

        await CommandWrapperAsync(true, async () =>
        {
            try
            {
                var installMonitor = await Task.Run(async () =>
                    await _modDragAndDropService.AddStorageItemFoldersAsync(_modList, items).ConfigureAwait(false), CancellationToken);

                if (installMonitor is not null)
                    _ = installMonitor.Task.ContinueWith((task) => ModGridVM.QueueModRefresh(), CancellationToken);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error while adding storage items.");
                _notificationService.ShowNotification("拖拽操作失败",
                    $"An error occurred while adding the storage items. Reason:\n{e.Message}",
                    TimeSpan.FromSeconds(5));
            }
        }).ConfigureAwait(false);
    }

    public bool CanDragDropModUrl(Uri? uri)
    {
        if (IsHardBusy)
            return false;

        if (uri is null)
            return false;

        if (!uri.IsAbsoluteUri)
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!GameBananaUrlHelper.TryGetModIdFromUrl(uri, out _))
            return false;

        return true;
    }

    public async Task DragDropModUrlAsync(Uri uri)
    {
        if (!CanDragDropModUrl(uri))
        {
            _notificationService.ShowNotification("拖拽操作失败",
                "The operation failed because the selected item is not a valid https GameBanana mod URL.",
                TimeSpan.FromSeconds(5));
            return;
        }

        await CommandWrapperAsync(true, async () =>
        {
            try
            {
                await _modDragAndDropService.AddModFromUrlAsync(_modList, uri);
            }
            catch (Exception e)
            {
                _logger.Error(e, "打开 Mod 页面失败");
                _notificationService.ShowNotification("打开 Mod 页面失败", e.Message, TimeSpan.FromSeconds(10));
            }
        }).ConfigureAwait(false);
    }
}