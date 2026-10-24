using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.Views;

public sealed partial class PluginAreaPage : Page
{
    public PluginAreaViewModel ViewModel { get; }

    public PluginAreaPage()
    {
        ViewModel = App.GetService<PluginAreaViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
