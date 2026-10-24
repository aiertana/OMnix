namespace OMniX.WinUI.Services.Notifications;

// This is a static class to easily  launch a not implemented notification from different places in the app.
internal static class NotImplemented
{
    public static NotificationManager NotificationManager { get; set; } = null!;

    public static void Show(string? message = null, TimeSpan? time = null)
    {
        NotificationManager.ShowNotification("还没做", message ?? "This feature is not implemented yet.",
            time ?? TimeSpan.FromSeconds(2));
    }
}