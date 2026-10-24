using System.Collections.ObjectModel;
using System.Windows.Input;
using OMniX.WinUI.Models.CustomControlTemplates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OMniX.WinUI.Views.Controls;

public sealed partial class SelectCharacterFromGrid : UserControl
{
    public SelectCharacterFromGrid()
    {
        InitializeComponent();
    }


    public static readonly DependencyProperty GridSourceProperty = DependencyProperty.Register(
        nameof(GridSource), typeof(ObservableCollection<SelectCharacterTemplate>), typeof(SelectCharacterFromGrid),
        new PropertyMetadata(default(ObservableCollection<SelectCharacterTemplate>)));

    public ObservableCollection<SelectCharacterTemplate> GridSource
    {
        get => (ObservableCollection<SelectCharacterTemplate>)GetValue(GridSourceProperty);
        set => SetValue(GridSourceProperty, value);
    }

    public static readonly DependencyProperty ItemClickedCommandProperty = DependencyProperty.Register(
        nameof(ItemClickedCommand), typeof(ICommand),
        typeof(SelectCharacterFromGrid), new PropertyMetadata(default(ICommand)));

    public ICommand ItemClickedCommand
    {
        get => (ICommand)GetValue(ItemClickedCommandProperty);
        set => SetValue(ItemClickedCommandProperty, value);
    }

    private void ItemsView_OnItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        ItemClickedCommand?.Execute(args.InvokedItem);
    }
}