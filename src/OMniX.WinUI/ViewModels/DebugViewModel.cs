using CommunityToolkit.Mvvm.ComponentModel;
using OMniX.WinUI.Contracts.ViewModels;

namespace OMniX.WinUI.ViewModels;

public partial class DebugViewModel() : ObservableRecipient, INavigationAware
{
    public static bool UseNewModel = true;

    public void OnNavigatedTo(object parameter)
    {
    }

    public void OnNavigatedFrom()
    {
    }
}