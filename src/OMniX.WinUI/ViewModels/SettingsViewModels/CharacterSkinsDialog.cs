using OMniX.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.ViewModels.SettingsViewModels;

internal class CharacterSkinsDialog
{
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();

    public async Task<ContentDialogResult> ShowDialogAsync(bool isEnabled)
    {
        var dialog = new ContentDialog()
        {
            Title = isEnabled ? DisableTitle : EnableTitle,
            Content = new TextBlock()
            {
                Text = isEnabled ? DisableContent : EnableContent,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = isEnabled ? DisablePrimaryButtonText : EnablePrimaryButtonText,
            CloseButtonText = "取消"
        };


        return await _windowManagerService.ShowDialogAsync(dialog).ConfigureAwait(false);
    }

    private const string EnableTitle = "把皮肤当作独立角色？";

    private const string EnableContent =
        "开启后，游戏里的皮肤会在角色列表里单独列出来，当成一个角色。\n" +
        "以后这可能就是 OMniX 的默认设置。\n" +
        "OMniX 不会挪动、也不会删除你任何 Mod。\n\n" +
        "确定要开启吗？开启后 OMniX 会重启…";

    private const string EnablePrimaryButtonText = "开启";

    private const string DisableTitle = "不再把皮肤当作独立角色？";

    private const string DisableContent =
        "关闭后，游戏里的皮肤会归到本体角色下面，不再单独列出。\n" +
        "这是 OMniX 目前默认的处理方式。\n" +
        "OMniX 不会挪动、也不会删除你任何 Mod。\n\n" +
        "确定要关闭吗？关闭后 OMniX 会重启…";

    private const string DisablePrimaryButtonText = "关闭";
}