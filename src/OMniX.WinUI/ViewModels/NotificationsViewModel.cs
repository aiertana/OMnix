using Windows.Storage;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.WinUI.Services.Notifications;

namespace OMniX.WinUI.ViewModels;

public partial class NotificationsViewModel : ObservableRecipient
{
    public readonly NotificationManager NotificationManager;

    [ObservableProperty]
    private string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log.txt");

    public NotificationsViewModel(NotificationManager notificationManager)
    {
        NotificationManager = notificationManager;
    }

    [RelayCommand]
    private async Task CopyLogFilePathAsync()
    {
        if (!File.Exists(LogFilePath))
        {
            NotificationManager.ShowNotification("找不到日志文件", "", null);
            return;
        }

        var openResult = await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(LogFilePath));
        if (!openResult)
            NotificationManager.ShowNotification("日志文件打不开", "", null);
    }
}