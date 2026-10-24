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
                try
                {
                    result = await _windowManagerService.ShowDialogAsync(dialog, window);
                }
                catch (ArgumentException)
                {
                    // 指定的窗口未注册，退回主窗口显示
                    result = await _windowManagerService.ShowDialogAsync(dialog);
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
