using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.Views;

public sealed partial class FeatureAreaPage : Page
{
    public FeatureAreaViewModel ViewModel { get; }

    public FeatureAreaPage()
    {
        ViewModel = App.GetService<FeatureAreaViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void GameAvatar_OnClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string gameId })
            ViewModel.SelectGameByIdCommand.Execute(gameId);
    }
}
