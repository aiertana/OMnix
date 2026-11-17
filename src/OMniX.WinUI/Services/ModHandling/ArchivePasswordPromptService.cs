using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Views.Dialogs;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace OMniX.WinUI.Services.ModHandling;

public interface IArchivePasswordPromptService
{
    /// <summary>
    ///     弹窗要求用户输入压缩包密码，用户取消时返回 null
    /// </summary>
    Task<string?> PromptForPasswordAsync(string archivePath, bool isRetry = false, WindowEx? window = null);
}

public sealed class ArchivePasswordPromptService : IArchivePasswordPromptService
{
    private readonly ILogger _logger;
    private readonly IWindowManagerService _windowManagerService;

    public ArchivePasswordPromptService(ILogger logger, IWindowManagerService windowManagerService)
    {
        _logger = logger.ForContext<ArchivePasswordPromptService>();
        _windowManagerService = windowManagerService;
    }

    public Task<string?> PromptForPasswordAsync(string archivePath, bool isRetry = false, WindowEx? window = null)
    {
        var taskCompletionSource = new TaskCompletionSource<string?>();

        var enqueued = App.MainWindow.DispatcherQueue.TryEnqueue(async void () =>
        {
            try
            {
                var dialog = new ArchivePasswordDialog(Path.GetFileName(archivePath), isRetry);

                ContentDialogResult result;
                // 二开：静默批量导入的进度小窗也是 ContentDialog —— 同一个主窗口同时
                // 只能开一个 dialog，不先藏进度窗的话密码框 ShowAsync 会直接抛异常，
                // 加密压缩包就被静默跳过了。弹密码前先 Hide 进度窗，拿完密码恢复。
                BatchInstallProgress.SuspendProgressForDialog();
                try
                {
                    try
                    {
                        result = await _windowManagerService.ShowDialogAsync(dialog, window);
                    }
                    catch (ArgumentException)
                    {
                        // 指定的窗口未注册，退回主窗口显示
                        result = await _windowManagerService.ShowDialogAsync(dialog);
                    }
                }
                finally
                {
                    BatchInstallProgress.ResumeProgressAfterDialog();
                }

                taskCompletionSource.TrySetResult(
                    result == ContentDialogResult.Primary && !string.IsNullOrEmpty(dialog.Password)
                        ? dialog.Password
                        : null);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to show archive password dialog for {Archive}", archivePath);
                taskCompletionSource.TrySetResult(null);
            }
        });

        if (!enqueued)
        {
            _logger.Warning("Could not enqueue archive password dialog for {Archive}", archivePath);
            taskCompletionSource.TrySetResult(null);
        }

        return taskCompletionSource.Task;
    }
}
