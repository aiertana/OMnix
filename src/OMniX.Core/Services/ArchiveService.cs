using System.Diagnostics;
using System.Security.Cryptography;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OMniX.Core.Services;

public class ArchiveService
{
    private readonly ILogger _logger;
    private readonly ExtractTool _extractTool;

    public ArchiveService(ILogger logger)
    {
        _logger = logger.ForContext<ArchiveService>();
        _extractTool = GetExtractTool();
    }

    public DirectoryInfo ExtractArchive(string archivePath, string destinationPath, bool overwritePath = false,
        string? password = null)
    {
        var archive = new FileInfo(archivePath);
        if (!archive.Exists)
            throw new FileNotFoundException("Archive not found", archivePath);

        if (!IsArchive(archivePath))
            throw new InvalidOperationException("File is not an archive");

        // 未提供密码时检测压缩包是否加密，加密则抛出，由界面层弹窗询问密码
        if (string.IsNullOrEmpty(password) && ArchiveEncryptionHelper.IsEncrypted(archivePath, _logger))
            throw new ArchivePasswordRequiredException(archivePath);

        var destinationDirectory = Directory.CreateDirectory(destinationPath);

        var extractedFolder = Path.Combine(destinationDirectory.FullName, archive.Name);

        if (Directory.Exists(extractedFolder))
            throw new InvalidOperationException("Destination folder already exists, could not extract folder");

        Directory.CreateDirectory(extractedFolder);

        var extractor = Extractor(archive.FullName);

        try
        {
            extractor?.Invoke(archive.FullName, extractedFolder, password);
        }
        catch (Exception e) when (ArchiveEncryptionHelper.IsPasswordRelatedException(e))
        {
            throw string.IsNullOrEmpty(password)
                ? new ArchivePasswordRequiredException(archivePath, e)
                : new ArchivePasswordIncorrectException(archivePath, e);
        }

        return new DirectoryInfo(extractedFolder);
    }

    // https://stackoverflow.com/a/31349703
    public async Task<string> CalculateFileMd5HashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists)
            throw new FileNotFoundException("File not found", filePath);


        using var md5 = MD5.Create();
        await using var stream = file.OpenRead();
        var hash = await md5.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        var convertedHash = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return convertedHash;
    }

    public bool IsHashEqual(byte[] hash1, byte[] hash2)
    {
        return hash1.SequenceEqual(hash2);
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

    private Action<string, string, string?>? Extractor(string archivePath)
    {
        Action<string, string, string?>? action = null;

        if (_extractTool == ExtractTool.Bundled7Zip)
            action = Extract7Z;
        else if (_extractTool == ExtractTool.SharpCompress)
            action = Path.GetExtension(archivePath) switch
            {
                ".zip" => SharpExtract,
                ".rar" => SharpExtract,
                ".7z" => SharpExtract,
                _ => null
            };
        else if (_extractTool == ExtractTool.System7Zip) throw new NotImplementedException();

        return action;
    }

    private void ExtractEntries(IArchive archive, string extractPath)
    {
        _logger.Information("Extracting {ArchiveType} archive", archive.Type);
        foreach (var entry in archive.Entries)
        {
            _logger.Debug("Extracting {EntryName}", entry.Key);
            entry.WriteToDirectory(extractPath, new ExtractionOptions()
            {
                ExtractFullPath = true,
                Overwrite = true,
                PreserveFileTime = false
            });
        }
    }

    private void SharpExtract(string archivePath, string extractPath, string? password)
    {
        using var archive = string.IsNullOrEmpty(password)
            ? ArchiveFactory.Open(archivePath)
            : ArchiveFactory.Open(archivePath, new ReaderOptions { Password = password });

        ExtractEntries(archive, extractPath);
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


    private void Extract7Z(string archivePath, string extractPath, string? password)
    {
        var sevenZipPath = Path.Combine(AppContext.BaseDirectory, @"Assets\7z\7z.exe");
        ArchiveEncryptionHelper.ExtractWith7Zip(sevenZipPath, archivePath, extractPath, password, _logger);
    }
}