using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.ViewModels;
using OMniX.WinUI.Views.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OMniX.WinUI.Views;

public sealed partial class PresetPage : Page
{
    public PresetViewModel ViewModel { get; } = App.GetService<PresetViewModel>();

    public PresetPage()
    {
        InitializeComponent();
        PresetsList.DragItemsCompleted += PresetsList_DragItemsCompleted;
    }

    private async void PresetsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Move && ViewModel.ReorderPresetsCommand.CanExecute(null))
        {
            await ViewModel.ReorderPresetsCommand.ExecuteAsync(null);
        }
    }

    private async void UIElement_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var presetVm = (ModPresetVm)((EditableTextBlock)sender).DataContext;

        if (e.Key == VirtualKey.Enter && ViewModel.RenamePresetCommand.CanExecute(presetVm))
        {
            await ViewModel.RenamePresetCommand.ExecuteAsync(presetVm);
        }
    }

    private TextBlock CreateTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        };
    }

    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "预设是怎么工作的",
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    CreateTextBlock(
                        "一个预设，就是一份「要开哪些 Mod、各自用什么设置」的清单。Mod 的设置本身存在 Mod 目录下的 .OMniX_ModConfig.json 里。"),
                    CreateTextBlock(
                        "新建预设时，OMniX 会把当下所有开着的 Mod 连同它们的设置记下来。以后套用这个预设，就只开这些 Mod，并把设置恢复成当时的样子。"),

                    CreateTextBlock(
                        "想让 OMniX 帮你刷新 3Dmigoto，可以先启动 Elevator，再勾上「自动同步」。也可以自己来：勾上「显示手动控制」，手动保存/读取设置，按 F10 刷新 3Dmigoto。"),

                    CreateTextBlock(
                        "你也可以完全不管预设，只用下面的手动控制来保存 Mod 的设置。"
                    )
                }
            }
        };

        await App.GetService<IWindowManagerService>().ShowDialogAsync(dialog).ConfigureAwait(false);
    }

    private void DragHandleIcon_OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);

    private void DragHandleIcon_OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
}