using System.Diagnostics;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Entities;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OMniX.Core.Services;

// Extract process if archive file:
// 1. Copy archive to work folder in windows temp folder
// 2. Extract archive to windows temp folder
// 3. Move extracted files to work folder
// 4. Delete copied archive

public sealed class DragAndDropScanner
{
    private readonly ILogger _logger = Log.ForContext<DragAndDropScanner>();
    private readonly string _tmpFolder = Path.Combine(Path.GetTempPath(), "OMniX_TMP");

    // Extracts files to this folder
    private string _workFolder = Path.Combine(Path.GetTempPath(), "OMniX_TMP", Guid.NewGuid().ToString("N"));

    private ExtractTool _extractTool;

    public DragAndDropScanner()
    {
        _extractTool = GetExtractTool();
    }

    public DragAndDropScanResult ScanAndGetContents(string path, string? password = null)
    {
        PrepareWorkFolder();

        _workFolder = Path.Combine(_workFolder, Path.GetFileName(path));

        if (IsArchive(path))
        {
            // 检测压缩包是否加密，未提供密码时直接抛出，由界面层弹窗询问密码
            if (string.IsNullOrEmpty(password) && ArchiveEncryptionHelper.IsEncrypted(path, _logger))
                throw new ArchivePasswordRequiredException(path);

            var copiedArchive = new FileInfo(path);
            copiedArchive = copiedArchive.CopyTo(Path.Combine(_tmpFolder, Path.GetFileName(path)), true);

            CleanupWorkFolder();

            var result = Extractor(copiedArchive.FullName);

            result?.Invoke(copiedArchive.FullName, password);
        }
        else if (Directory.Exists(path)) // ModDragAndDropService handles loose folders, but this added just in case 
        {
            var modFolder = new Mod(new DirectoryInfo(path));
            modFolder.CopyTo(_workFolder);
        }
        else
            throw new Exception("No valid mod folder or archive found");


        return new DragAndDropScanResult()
        {
            ExtractedFolder = new Mod(new DirectoryInfo(_workFolder).Parent!)
        };
    }

    private void PrepareWorkFolder()
    {
        Directory.CreateDirectory(_tmpFolder);
        Directory.CreateDirectory(_workFolder);
    }

    /// <summary>
    ///     密码错误重试时清空上一次解压残留
    /// </summary>
    private void CleanupWorkFolder()
    {
        try
        {
            if (Directory.Exists(_workFolder))
                Directory.Delete(_workFolder, true);
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Could not cleanup work folder {Folder}", _workFolder);
        }

        Directory.CreateDirectory(_workFolder);
    }

    private bool IsArchive(string path)
    {
        return Path.GetExtension(path) switch
        {
            ".zip" => true,
            ".rar" => true,
            ".7z" => true,
            _ => false
        };
    }

    private Action<string, string?>? Extractor(string path)
    {
        Action<string, string?>? action = null;

        if (_extractTool == ExtractTool.Bundled7Zip)
            action = Extract7Z;
        else if (_extractTool == ExtractTool.SharpCompress)
            action = Path.GetExtension(path) switch
            {
                ".zip" => SharpExtractZip,
                ".rar" => SharpExtractRar,
                ".7z" => SharpExtract7z,
                _ => null
            };
        else if (_extractTool == ExtractTool.System7Zip) throw new NotImplementedException();

        return action;
    }

    private void ExtractEntries(IArchive archive)
    {
        _logger.Information("Extracting {ArchiveType} archive", archive.Type);
        foreach (var entry in archive.Entries)
        {
            _logger.Debug("Extracting {EntryName}", entry.Key);
            entry.WriteToDirectory(_workFolder, new ExtractionOptions()
            {
                ExtractFullPath = true,
                Overwrite = true,
                PreserveFileTime = false
            });
        }
    }

    private void SharpExtractZip(string path, string? password)
    {
        try
        {
            using var archive = ZipArchive.Open(path, CreateReaderOptions(password));
            ExtractEntries(archive);
        }
        catch (Exception e)
        {
            throw WrapPasswordException(path, password, e);
        }
    }


    private void SharpExtractRar(string path, string? password)
    {
        try
        {
            using var archive = RarArchive.Open(path, CreateReaderOptions(password));
            ExtractEntries(archive);
        }
        catch (Exception e)
        {
            throw WrapPasswordException(path, password, e);
        }
    }

    // ReSharper disable once InconsistentNaming
    private void SharpExtract7z(string path, string? password)
    {
        try
        {
            using var archive = ArchiveFactory.Open(path, CreateReaderOptions(password));
            ExtractEntries(archive);
        }
        catch (Exception e)
        {
            throw WrapPasswordException(path, password, e);
        }
    }

    private static ReaderOptions CreateReaderOptions(string? password) =>
        string.IsNullOrEmpty(password) ? new ReaderOptions() : new ReaderOptions { Password = password };

    private Exception WrapPasswordException(string archivePath, string? password, Exception exception)
    {
        if (!ArchiveEncryptionHelper.IsPasswordRelatedException(exception))
            return exception;

        _logger.Warning("Archive {Archive} is password protected (password provided: {Provided})", archivePath,
            !string.IsNullOrEmpty(password));

        return string.IsNullOrEmpty(password)
            ? new ArchivePasswordRequiredException(archivePath, exception)
            : new ArchivePasswordIncorrectException(archivePath, exception);
    }


    private enum ExtractTool
    {
        Bundled7Zip, // 7zip bundled with OMniX
        SharpCompress, // SharpCompress library
        System7Zip // 7zip installed on the system
    }

    private ExtractTool GetExtractTool()
    {
        var bundled7ZFolder = Path.Combine(AppContext.BaseDirectory, @"Assets\7z\");
        if (File.Exists(Path.Combine(bundled7ZFolder, "7z.exe")) &&
            File.Exists(Path.Combine(bundled7ZFolder, "7-zip.dll")) &&
            File.Exists(Path.Combine(bundled7ZFolder, "7z.dll")))
        {
            _logger.Debug("Using bundled 7zip");
            return ExtractTool.Bundled7Zip;
        }

        _logger.Information("Bundled 7zip not found, using SharpCompress library");
        return ExtractTool.SharpCompress;
    }


    private void Extract7Z(string path, string? password)
    {
        var sevenZipPath = Path.Combine(AppContext.BaseDirectory, @"Assets\7z\7z.exe");
        ArchiveEncryptionHelper.ExtractWith7Zip(sevenZipPath, path, _workFolder, password, _logger);
    }
}

public class DragAndDropScanResult
{
    public IMod ExtractedFolder { get; init; } = null!;
    public string[] IgnoredMods { get; init; } = Array.Empty<string>();
}