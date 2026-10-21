using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;


namespace OMniX.WinUI.Views;

public sealed partial class NotificationsPage : Page
{
    public NotificationsViewModel ViewModel { get; }

    public NotificationsPage()
    {
        ViewModel = App.GetService<NotificationsViewModel>();
        InitializeComponent();
    }
}