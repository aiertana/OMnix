using OMniX.Core.Services;
using Serilog;

namespace OMniX.WinUI.Services.ModHandling;

/// <summary>
///     带密码重试的压缩包解压辅助类
/// </summary>
public static class ArchiveExtractionHelper
{
    /// <summary>
    ///     解压压缩包，若压缩包加密则弹窗要求输入密码；密码错误时重新弹窗，直到成功或用户取消
    /// </summary>
    public static async Task<DirectoryInfo> ExtractWithPasswordPromptAsync(
        ArchiveService archiveService,
        IArchivePasswordPromptService passwordPromptService,
        ILogger logger,
        string archivePath,
        string destinationPath,
        WindowEx? window = null)
    {
        string? password = null;
        var isRetry = false;

        while (true)
        {
            try
            {
                return archiveService.ExtractArchive(archivePath, destinationPath, password: password);
            }
            catch (ArchivePasswordRequiredException)
            {
                CleanupFailedExtraction(destinationPath, archivePath, logger);
            }
            catch (ArchivePasswordIncorrectException)
            {
                CleanupFailedExtraction(destinationPath, archivePath, logger);
                isRetry = true;
            }

            var promptedPassword =
                await passwordPromptService.PromptForPasswordAsync(archivePath, isRetry, window)
                    .ConfigureAwait(false);

            if (string.IsNullOrEmpty(promptedPassword))
                throw new OperationCanceledException("已取消输入压缩包密码");

            password = promptedPassword;
        }
    }

    private static void CleanupFailedExtraction(string destinationPath, string archivePath, ILogger logger)
    {
        try
        {
            var extractedFolder = Path.Combine(destinationPath, Path.GetFileName(archivePath));
            if (Directory.Exists(extractedFolder))
                Directory.Delete(extractedFolder, true);
        }
        catch (Exception e)
        {
            logger.Warning(e, "Could not cleanup failed extraction folder for {Archive}", archivePath);
        }
    }
}
