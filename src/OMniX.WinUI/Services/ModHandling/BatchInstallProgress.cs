using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using Serilog;
using Windows.Storage;

namespace OMniX.WinUI.Services.ModHandling;

/// <summary>
///     二开：批量「全部按默认添加」导入的进度小窗。
///     一次选多个压缩包/文件夹时不再逐个弹「安装 Mod」窗口，
///     改成一个小窗口显示「正在安装 x/y：名字」，装完自动关掉并汇总结果。
///     注意：同一个主窗口同时只能开一个 ContentDialog —— 途中遇到加密压缩包时，
///     密码输入框要弹出来，所以进度窗先 Hide、拿到密码后再重建显示
///     （ContentDialog 实例只能 ShowAsync 一次，恢复时必须 new 一个新的）。
///     安装任务全程在后台跑，不受进度窗显示/隐藏影响。
/// </summary>
public static class BatchInstallProgress
{
    private static Action? _suspendHandler;
    private static Action? _resumeHandler;

    /// <summary>
    ///     密码提示窗弹出前调用：把进度窗藏起来，腾出 ContentDialog 的坑位。
    /// </summary>
    internal static void SuspendProgressForDialog() => _suspendHandler?.Invoke();

    /// <summary>
    ///     密码提示窗关闭后调用：重建并重新显示进度窗。
    /// </summary>
    internal static void ResumeProgressAfterDialog() => _resumeHandler?.Invoke();

    public static async Task<(int OkCount, int FailedCount)> RunAsync(
        string title, IReadOnlyList<IStorageItem> items, ICharacterModList modList,
        ModDragAndDropService service)
    {
        var localizer = App.GetService<ILanguageLocalizer>();
        var notificationManager = App.GetService<Notifications.NotificationManager>();
        var logger = App.GetService<Serilog.ILogger>().ForContext(typeof(BatchInstallProgress));
        var dispatcher = App.MainWindow.DispatcherQueue;

        ContentDialog? dialog = null;
        TextBlock? currentText = null;
        TextBlock? subText = null;
        // 密码窗借用期间进度窗会被销毁重建，用这两个变量记住显示到哪了
        var lastCurrent = "…";
        var lastSub = localizer.GetLocalizedStringOrDefault("/ServicesMsg/Batch_Sub",
            "全部按默认设置添加，重名 Mod 自动改名");

        void ShowProgress()
        {
            var stack = new StackPanel { Spacing = 12, MinWidth = 320 };
            stack.Children.Add(new ProgressRing { IsActive = true, Width = 40, Height = 40 });
            currentText = new TextBlock { Text = lastCurrent };
            subText = new TextBlock
            {
                Opacity = 0.7,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Text = lastSub
            };
            stack.Children.Add(currentText);
            stack.Children.Add(subText);

            var d = new ContentDialog
            {
                Title = title,
                XamlRoot = App.MainWindow.Content.XamlRoot,
                IsPrimaryButtonEnabled = false,
                IsSecondaryButtonEnabled = false,
                // 静默安装期间不允许手动关闭，装完自动收
                Content = stack
            };
            dialog = d;

            try
            {
                _ = d.ShowAsync();
            }
            catch (Exception e)
            {
                // XamlRoot 不在屏幕上（用户切走了页面）等场景：dialog 弹不出来，
                // 安装继续跑，等完成就行。
                logger.Warning(e, "Batch install progress dialog failed to show");
                dialog = null;
            }
        }

        void HideProgress()
        {
            try
            {
                dialog?.Hide();
            }
            catch
            {
                // 窗口已经关了，无所谓
            }

            dialog = null;
            if (currentText is not null)
                lastCurrent = currentText.Text;
            if (subText is not null)
                lastSub = subText.Text;
        }

        // 挂给密码提示服务用（同命名空间内部协作）
        _suspendHandler = HideProgress;
        _resumeHandler = ShowProgress;

        ShowProgress();

        try
        {
            var result = await Task.Run(async () =>
            {
                return await service.InstallAllDefaultsAsync(modList, items,
                    (index, total, name) =>
                    {
                        // currentText 引用会被密码窗借还过程替换，闭包读的是变量本身，安全
                        dispatcher.TryEnqueue(() =>
                        {
                            lastCurrent = string.Format(
                                localizer.GetLocalizedStringOrDefault("/ServicesMsg/Batch_Current", "({0}/{1})  {2}"),
                                index + 1, total, name);
                            if (currentText is not null)
                                currentText.Text = lastCurrent;
                        });
                    }).ConfigureAwait(false);
            }).ConfigureAwait(false);

            var summary = result.FailedCount == 0
                ? string.Format(localizer.GetLocalizedStringOrDefault("/ServicesMsg/Batch_DoneOk", "已按默认设置添加 {0} 个 Mod"), result.OkCount)
                : string.Format(localizer.GetLocalizedStringOrDefault("/ServicesMsg/Batch_DoneMixed", "已添加 {0} 个 Mod，{1} 个失败（详见日志）"), result.OkCount, result.FailedCount);
            notificationManager.ShowNotification(
                localizer.GetLocalizedStringOrDefault("/ServicesMsg/Batch_DoneTitle", "批量导入完成"),
                summary, System.TimeSpan.FromSeconds(6));

            return result;
        }
        finally
        {
            HideProgress();
            _suspendHandler = null;
            _resumeHandler = null;
        }
    }
}
