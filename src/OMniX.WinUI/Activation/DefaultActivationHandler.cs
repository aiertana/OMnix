using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.ViewModels;
using Microsoft.UI.Xaml;

namespace OMniX.WinUI.Activation;

/// <summary>
/// DefaultActivationHandler is completely wrong name for this class. This is the first time startup handler.
/// </summary>
public class DefaultActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;
    public override string ActivationName { get; } = "FirstTimeStartup";

    public DefaultActivationHandler(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame?.Content == null;
    }

    /// <summary>
    ///     程序启动的默认落点是首页（OMniX Home）。首次启动配置向导不再强制弹出，
    ///     需要配置时由首页顶部的引导卡进入。
    /// </summary>
    protected override async Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        _navigationService.NavigateTo(typeof(HomeViewModel).FullName!, args.Arguments, true);

        await Task.CompletedTask;
    }
}