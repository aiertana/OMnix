using System.ComponentModel;
using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using OMniX.WinUI.ViewModels.CharacterManagerViewModels;
using Windows.System;

namespace OMniX.WinUI.Views.CharacterManager;

public sealed partial class EditCharacterPage : Page
{
    public EditCharacterViewModel ViewModel { get; }

    public EditCharacterPage()
    {
        ViewModel = App.GetService<EditCharacterViewModel>();
        InitializeComponent();

        ViewModel.CharacterStatus.PropertyChanged += CharacterStatus_PropertyChanged;
        SetBackground();
    }

    private void CharacterStatus_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.CharacterStatus.IsEnabled)
            or nameof(ViewModel.CharacterStatus.IsDisabled))
            SetBackground();
    }

    private void SetBackground()
    {
        var background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as SolidColorBrush;
        if (ViewModel.CharacterStatus.IsDisabled)
            background = Application.Current.Resources["SmokeFillColorDefaultBrush"] as SolidColorBrush;

        EditCharacterPageGrid.Background = background;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo(e.Parameter);
    }

    #region 二开：Mod 就地改名（不弹窗，直接在列表里编辑）

    /// <summary>
    ///     二开：点左边的圆点 → 启用 / 禁用这个 Mod。
    /// </summary>
    private void ModDot_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModModel mod })
            ViewModel.ToggleMod(mod);
    }

    /// <summary>
    ///     二开：点「重命名」按钮 → 把这一行切成输入框。
    /// </summary>
    private void ModRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ModModel mod })
            mod.BeginEdit();
    }

    /// <summary>
    ///     二开：输入框一显示出来就自动拿到焦点并全选，省得再点一下。
    /// </summary>
    private void ModRenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        box.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (box.Visibility != Visibility.Visible)
                return;

            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        });
    }

    /// <summary>
    ///     二开：回车保存、Esc 取消。
    /// </summary>
    private void ModRenameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ModModel mod })
            return;

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.CommitModRenameAsync(mod);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.CancelModRename(mod);
        }
    }

    /// <summary>
    ///     二开：点到别处也算保存。
    /// </summary>
    private void ModRenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ModModel mod } && mod.IsEditing)
            ViewModel.CommitModRenameAsync(mod);
    }

    #endregion
}