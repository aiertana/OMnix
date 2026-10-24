using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace OMniX.Core.Services;

/// <summary>
///     压缩包已加密，需要用户输入密码
/// </summary>
public sealed class ArchivePasswordRequiredException : Exception
{
    public ArchivePasswordRequiredException(string archivePath, Exception? innerException = null)
        : base($"压缩包 \"{Path.GetFileName(archivePath)}\" 已加密，需要密码才能解压。", innerException)
    {
        ArchivePath = archivePath;
    }

    public string ArchivePath { get; }
}

/// <summary>
///     用户输入的密码不正确
/// </summary>
public sealed class ArchivePasswordIncorrectException : Exception
{
    public ArchivePasswordIncorrectException(string archivePath, Exception? innerException = null)
        : base($"压缩包 \"{Path.GetFileName(archivePath)}\" 的密码不正确。", innerException)
    {
        ArchivePath = archivePath;
    }

    public string ArchivePath { get; }
}

/// <summary>
///     其他解压错误
/// </summary>
public sealed class ArchiveExtractionException : Exception
{
    public ArchiveExtractionException(string message, Exception? innerException = null) : base(message,
        innerException)
    {
    }
}

/// <summary>
///     压缩包加密检测 & 7zip 命令行解压封装
/// </summary>
public static class ArchiveEncryptionHelper
{
    private static readonly string[] PasswordHints =
    {
        "password", "encrypted", "encrypt", "cryptographic", "密码", "加密"
    };

    /// <summary>
    ///     返回随程序分发的 7z.exe 路径，不存在时返回 null
    /// </summary>
    public static string? GetBundled7ZipPath()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Assets", "7z", "7z.exe");
        var dll = Path.Combine(AppContext.BaseDirectory, "Assets", "7z", "7z.dll");
        var zDll = Path.Combine(AppContext.BaseDirectory, "Assets", "7z", "7-zip.dll");

        return File.Exists(exe) && File.Exists(dll) && File.Exists(zDll) ? exe : null;
    }

    /// <summary>
    ///     判断压缩包是否需要密码
    /// </summary>
    public static bool IsEncrypted(string archivePath, ILogger? logger = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found", archivePath);

        var sevenZip = GetBundled7ZipPath();
        if (sevenZip is not null)
        {
            try
            {
                return IsEncryptedWith7Zip(sevenZip, archivePath, logger);
            }
            catch (Exception e)
            {
                logger?.Debug(e, "7zip encryption detection failed, falling back to SharpCompress");
            }
        }

        return IsEncryptedWithSharpCompress(archivePath, logger);
    }

    public static bool IsPasswordRelatedException(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is System.Security.Cryptography.CryptographicException
                or SharpCompress.Common.CryptographicException)
                return true;

            var message = e.Message;
            if (string.IsNullOrWhiteSpace(message))
                continue;

            if (PasswordHints.Any(hint => message.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     使用随包分发的 7z.exe 解压，失败时抛出 <see cref="ArchivePasswordRequiredException" /> 或
    ///     <see cref="ArchivePasswordIncorrectException" />
    /// </summary>
    public static void ExtractWith7Zip(string sevenZipPath, string archivePath, string destinationPath,
        string? password, ILogger? logger = null)
    {
        var arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y";
        if (!string.IsNullOrEmpty(password))
            arguments += $" -p\"{password}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        logger?.Information("Extracting archive with 7zip: {Arguments}", arguments);

        using var process = Process.Start(startInfo) ??
                            throw new ArchiveExtractionException("无法启动 7z.exe");

        // 关闭标准输入，避免 7zip 在等待密码输入时挂起
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception)
        {
            // ignored
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        var output = SafeResult(outputTask) + SafeResult(errorTask);
        logger?.Information("7zip extraction finished with exit code {ExitCode}", process.ExitCode);

        if (process.ExitCode == 0)
            return;

        logger?.Warning("7zip extraction failed: {Output}", output);

        if (IsPasswordRelatedException(new Exception(output)) ||
            output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Cannot open encrypted archive", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(password))
                throw new ArchivePasswordRequiredException(archivePath);

            throw new ArchivePasswordIncorrectException(archivePath);
        }

        throw new ArchiveExtractionException(
            $"解压失败（7zip 退出码 {process.ExitCode}）：{output.Trim()}");
    }

    private static string SafeResult(Task<string> task)
    {
        try
        {
            return task.Wait(TimeSpan.FromSeconds(5)) ? task.Result : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static bool IsEncryptedWith7Zip(string sevenZipPath, string archivePath, ILogger? logger)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            // -p"" 传空密码，避免 7zip 交互式等待输入
            Arguments = $"l -slt -p\"\" \"{archivePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ?? throw new ArchiveExtractionException("无法启动 7z.exe");

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception)
        {
            // ignored
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        var output = SafeResult(outputTask) + SafeResult(errorTask);

        if (output.Contains("Encrypted = +", StringComparison.OrdinalIgnoreCase))
            return true;

        if (process.ExitCode != 0 && IsPasswordRelatedException(new Exception(output)))
            return true;

        logger?.Debug("Archive {Archive} is not encrypted (7zip exit code {ExitCode})", archivePath,
            process.ExitCode);

        return false;
    }

    private static bool IsEncryptedWithSharpCompress(string archivePath, ILogger? logger)
    {
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory);
            if (entry is null)
                return false;

            using var stream = entry.OpenEntryStream();
            var buffer = new byte[64];
            _ = stream.Read(buffer, 0, buffer.Length);

            return false;
        }
        catch (Exception e) when (IsPasswordRelatedException(e))
        {
            return true;
        }
        catch (Exception e)
        {
            logger?.Debug(e, "Could not determine encryption state of archive {Archive}", archivePath);
            return false;
        }
    }
}
