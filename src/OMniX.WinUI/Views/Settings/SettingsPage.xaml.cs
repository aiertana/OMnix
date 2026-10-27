using OMniX.WinUI.ViewModels;
using OMniX.WinUI.Views.Controls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace OMniX.WinUI.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    private void GimiFolder_OnPathChangedEvent(object? sender, FolderSelector.StringEventArgs e)
        => ViewModel.PathToGIMIFolderPicker.Validate(e.Value);


    private void ModsFolder_OnPathChangedEvent(object? sender, FolderSelector.StringEventArgs e)
        => ViewModel.PathToModsFolderPicker.Validate(e.Value);

    // 二开：插件目录没有校验规则，直接赋值即可 —— 填错也不该拦住保存按钮
    private void PluginsFolder_OnPathChangedEvent(object? sender, FolderSelector.StringEventArgs e)
        => ViewModel.PathToPluginsFolderPicker.Path = e.Value;

    private async void LanguageSelectorComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        var item = (string)e.AddedItems[0];
        await ViewModel.SelectLanguageCommand.ExecuteAsync(item).ConfigureAwait(false);
    }

    private async void GameSelectorComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        var item = (string)e.AddedItems[0];
        await ViewModel.SelectGameCommand.ExecuteAsync(item).ConfigureAwait(false);
    }

    private void LaunchModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = ((ComboBox)sender).SelectedIndex;
        if (index < 0) return;

        if (ViewModel.SetLaunchModeCommand.CanExecute(index))
            ViewModel.SetLaunchModeCommand.ExecuteAsync(index);
    }

    private void LocalCacheSlider_OnValueChanged(object _, RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel.SetCacheLimitCommand.CanExecute((int)e.NewValue))
            ViewModel.SetCacheLimitCommand.ExecuteAsync((int)e.NewValue);
    }
}