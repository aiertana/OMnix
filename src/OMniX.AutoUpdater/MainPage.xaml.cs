using Microsoft.UI.Xaml.Controls;

namespace OMniX.AutoUpdater;

public sealed partial class MainPage : Page
{
    public MainPageVM ViewModel { get; }

    public MainPage(string currentOmniXVersion)
    {
        InitializeComponent();

        ViewModel = new MainPageVM(currentOmniXVersion);
    }
}