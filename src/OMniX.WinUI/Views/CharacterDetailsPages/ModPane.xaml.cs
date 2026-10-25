using Windows.ApplicationModel.DataTransfer;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OMniX.WinUI.Views.CharacterDetailsPages;

public sealed partial class ModPane : UserControl
{
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public ModPane()
    {
        InitializeComponent();
    }


    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ModPaneVM), typeof(ModPane), new PropertyMetadata(default(ModPaneVM)));

    public ModPaneVM ViewModel
    {
        get { return (ModPaneVM)GetValue(ViewModelProperty); }
        set
        {
            SetValue(ViewModelProperty, value);
            OnViewModelSetHandler(ViewModel);
        }
    }


    private void OnViewModelSetHandler(ModPaneVM viewModel)
    {
    }


    /// <summary>
    ///     二开：删除 Mod（二次确认，移到回收站）
    /// </summary>
    private async void DeleteMod_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DeleteDialogTitle", "删除 Mod"),
            Content = string.Format(
                _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DeleteDialogContent", "确定要删除「{0}」吗？\n文件会被移到回收站，之后可以还原。"),
                ViewModel.ModModel.ModDisplayName),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DeleteDialogPrimary", "删除"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DeleteDialogClose", "取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.DeleteModCommand.Execute(null);
    }


    /// <summary>
    ///     二开：展开 / 收起按键的原始输入框
    /// </summary>
    private void EditKeys_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModPaneFieldsKeySwapVm keySwapVm })
            keySwapVm.IsEditingKeys = !keySwapVm.IsEditingKeys;
    }


    /// <summary>
    ///     二开：在详情面板里切换启用状态
    /// </summary>
    private void ModEnabledToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle)
            return;

        // 开关是单向绑定，真正的动作交给 ViewModel，失败会自动弹回来
        if (toggle.IsOn != ViewModel.ModModel.IsEnabled)
            ViewModel.ToggleModEnabledCommand.Execute(null);
    }


    private async void PaneImage_OnDragEnter(object sender, DragEventArgs e)
    {
        if (ViewModel.IsReadOnly || ViewModel.BusySetter.IsHardBusy)
            return;
        var deferral = e.GetDeferral();

        if (e.DataView.Contains(StandardDataFormats.WebLink))
        {
            var url = await e.DataView.GetWebLinkAsync();
            var isValidHttpLink = ViewModel.CanSetImageFromDragDropWeb(url);
            if (isValidHttpLink)
                e.AcceptedOperation = DataPackageOperation.Copy;
        }
        else if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var data = await e.DataView.GetStorageItemsAsync();
            if (ViewModel.CanSetImageFromDragDropStorageItem(data))
                e.AcceptedOperation = DataPackageOperation.Copy;
        }

        deferral.Complete();
    }

    private async void PaneImage_OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel.IsReadOnly || ViewModel.BusySetter.IsHardBusy)
            return;

        var deferral = e.GetDeferral();
        if (e.DataView.Contains(StandardDataFormats.Uri))
        {
            var uri = await e.DataView.GetUriAsync();
            await ViewModel.SetImageFromDragDropWeb(uri);
        }
        else if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            await ViewModel.SetImageFromDragDropFile(await e.DataView.GetStorageItemsAsync());
        }

        deferral.Complete();
    }
}