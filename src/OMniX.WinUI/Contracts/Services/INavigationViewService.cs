using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.Contracts.Services;

public interface INavigationViewService
{
    public bool IsEnabled { get; set; }
    IList<object>? MenuItems { get; }

    object? SettingsItem { get; }

    void Initialize(NavigationView navigationView);

    void UnregisterEvents();

    NavigationViewItem? GetSelectedItem(Type pageType, object? parameter = null);
}