using Windows.Storage;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.Services.Notifications;

namespace OMniX.WinUI.ViewModels;

public partial class NotificationsViewModel : ObservableRecipient
{
    public readonly NotificationManager NotificationManager;

    private readonly ILanguageLocalizer _localizer;

    [ObservableProperty]
    private string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log.txt");

    public NotificationsViewModel(NotificationManager notificationManager, ILanguageLocalizer localizer)
    {
        NotificationManager = notificationManager;
        _localizer = localizer;
    }

    [RelayCommand]
    private async Task CopyLogFilePathAsync()
    {
        if (!File.Exists(LogFilePath))
        {
            NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/Notifications/LogNotFound", "找不到日志文件"), "", null);
            return;
        }

        var openResult = await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(LogFilePath));
        if (!openResult)
            NotificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/Notifications/LogOpenFailed", "日志文件打不开"), "", null);
    }
}