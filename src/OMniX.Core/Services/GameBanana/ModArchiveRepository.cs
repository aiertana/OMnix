using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using OMniX.Core.Services.GameBanana.Models;
using Serilog;

namespace OMniX.Core.Services.GameBanana;

public sealed class ModArchiveRepository
{
    private readonly ILogger _logger;
    private readonly ArchiveService _archiveService;


    private readonly ConcurrentDictionary<string, ModArchiveHandle> _modArchives = new();

    private DirectoryInfo _settingsDirectory = null!;
    private DirectoryInfo _modArchiveDirectory = null!;
    private string _appDataFolder = string.Empty;
    private const string ModArchiveDirectoryName = "ModDownloads";

    private const string DownloadTempPrefix = ".TMP_DOWNLOAD";
    public const string Separator = "_!!_";

    private int _maxDirectorySizeGb;

    public ModArchiveRepository(ILogger logger, ArchiveService archiveService)
    {
        _archiveService = archiveService;
        _logger = logger.ForContext<ModArchiveRepository>();
    }

    public Uri ArchiveDirectory => new(EnsureArchiveDirectory().FullName);

    /// <summary>
    ///     二开：InitializeAsync 不一定跑得到。启动时真正干活的那个 handler
    ///     （叫 FirstTimeStartupActivationHandler，但它其实是默认处理器）
    ///     会先检查 Mods 和 GIMI 目录在不在，只要有一个不存在它就整个不执行，
    ///     改由 DefaultActivationHandler 接手 —— 而后者只负责导航到首页，
    ///     不初始化任何服务。于是 _modArchiveDirectory 一直保持 null，
    ///     用户一点设置页，SettingsViewModel 构造时读 ArchiveDirectory
    ///     就 NullReferenceException，界面弹「出错了，建议重启」。
    ///     对方电脑没配过目录（或者目录被挪走了）就正好走在这条路上。
    ///
    ///     读一个目录路径不该让页面崩掉。这里兜个底：没初始化过就按默认位置
    ///     现建一个，并记一条警告，方便以后从日志里看出来是谁没初始化。
    /// </summary>
    private DirectoryInfo EnsureArchiveDirectory()
    {
        if (_modArchiveDirectory is not null)
        {
            return _modArchiveDirectory;
        }

        var folder = string.IsNullOrWhiteSpace(_appDataFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX")
            : _appDataFolder;

        _modArchiveDirectory = new DirectoryInfo(Path.Combine(folder, ModArchiveDirectoryName));
        _modArchiveDirectory.Create();

        _logger.Warning(
            "ModArchiveRepository 尚未初始化就读取了 ArchiveDirectory，先用默认位置：{Path}",
            _modArchiveDirectory.FullName);

        return _modArchiveDirectory;
    }

    public async Task InitializeAsync(string appDataFolder, Action<SetupOptions>? setup = null)
    {
        ReadOptions(setup);

        // 记下来：万一之后没走完初始化就被读取，EnsureArchiveDirectory 能按这个位置兜底
        _appDataFolder = appDataFolder;

        _settingsDirectory = new DirectoryInfo(appDataFolder);
        _settingsDirectory.Create();

        _modArchiveDirectory = new DirectoryInfo(Path.Combine(appDataFolder, ModArchiveDirectoryName));
        _modArchiveDirectory.Create();

        var deleteTasks = new List<Task>();

        foreach (var archive in _modArchiveDirectory.EnumerateFiles().ToArray())
        {
            try
            {
                var archiveHandle = ModArchiveHandle.FromManagedFile(EnsureValidArchive(archive.FullName));

                if (_modArchives.ContainsKey(archiveHandle.FullName))
                {
                    _logger.Debug("Duplicate archive found, deleting: {ArchiveName}", archiveHandle.FullName);


                    deleteTasks.Add(Task.Run(archive.Delete));
                    continue;
                }

                TrackArchive(archiveHandle);
            }
            catch (Exception e)
            {
                _logger.Information("Invalid archive, deleting: {ArchiveName}", archive.FullName);
                deleteTasks.Add(Task.Run(archive.Delete));
            }
        }

        await Task.WhenAll(deleteTasks).ConfigureAwait(false);

        var _ = Task.Run(RemoveUntilUnderMaxSize);
    }

    private void ReadOptions(Action<SetupOptions>? setup = null)
    {
        var options = new SetupOptions();
        setup?.Invoke(options);
        _maxDirectorySizeGb = options.MaxDirectorySizeGb;
    }

    public async Task<ModArchiveHandle> CopyAndTrackModArchiveAsync(string archivePath,
        GbModFileIdentifier modFileIdentifier,
        CancellationToken cancellationToken = default)
    {
        var archiveFile = EnsureValidArchive(archivePath);

        await foreach (var modArchiveHandle in GetModArchivesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (modArchiveHandle.FullName.Equals(archiveFile.FullName, StringComparison.OrdinalIgnoreCase))
                return modArchiveHandle;
        }

        var archiveHash = await _archiveService.CalculateFileMd5HashAsync(archiveFile.FullName, cancellationToken)
            .ConfigureAwait(false);
        var newName = await GetManagedArchiveNameAsync(archiveFile.Name, modFileIdentifier, archiveHash)
            .ConfigureAwait(false);

        archiveFile = archiveFile.CopyTo(Path.Combine(_modArchiveDirectory.FullName, newName), true);

        var archiveHandle = ModArchiveHandle.FromManagedFile(archiveFile);
        TrackArchive(archiveHandle);
        return archiveHandle;
    }


    public async Task<ModArchiveHandle> CreateAndTrackModArchiveAsync(
        Func<FileStream, Task<string>> streamToWrite, GbModFileIdentifier modIdentifier,
        CancellationToken cancellationToken = default)
    {
        var newFile =
            new FileInfo(Path.Combine(_modArchiveDirectory.FullName, $"{DownloadTempPrefix}_{Guid.NewGuid()}"));

        if (newFile.Exists)
            newFile.Delete();


        try
        {
            var fileStream = newFile.Create();

            var archiveName = "";
            await using (fileStream.ConfigureAwait(false))
            {
                archiveName = await streamToWrite(fileStream).ConfigureAwait(false);
            }

            var archiveHash = await _archiveService.CalculateFileMd5HashAsync(newFile.FullName, cancellationToken)
                .ConfigureAwait(false);

            var newName = await GetManagedArchiveNameAsync(archiveName, modIdentifier, archiveHash)
                .ConfigureAwait(false);

            newFile.MoveTo(Path.Combine(_modArchiveDirectory.FullName, newName), true);
        }
        catch (Exception e)
        {
            newFile.Delete();

            _logger.Error(e, "Failed to download mod archive");
            throw;
        }


        var archiveHandle = ModArchiveHandle.FromManagedFile(newFile);
        TrackArchive(archiveHandle);
        return archiveHandle;
    }

    public async Task<ModArchiveHandle?> FirstOrDefaultAsync(Predicate<ModArchiveHandle> predicate,
        CancellationToken cancellationToken = default)
    {
        await foreach (var archiveHandle in GetModArchivesAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate(archiveHandle))
            {
                return archiveHandle;
            }
        }

        return null;
    }

    public async IAsyncEnumerable<ModArchiveHandle> GetModArchivesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var archives = _modArchives.ToArray();

        foreach (var archiveHandle in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            archiveHandle.Value.Refresh();

            if (!archiveHandle.Value.Exists)
            {
                _modArchives.TryRemove(archiveHandle.Key, out _);
                continue;
            }

            yield return archiveHandle.Value;
        }
    }


    private void TrackArchive(ModArchiveHandle archiveHandle)
    {
        ArgumentNullException.ThrowIfNull(archiveHandle);
        _modArchives.AddOrUpdate(archiveHandle.FullName, (_) => archiveHandle, (_, _) => archiveHandle);
    }

    private Task<string> GetManagedArchiveNameAsync(string archiveFileName, GbModFileIdentifier modFileIdentifier,
        string? archiveHash)
    {
        ArgumentNullException.ThrowIfNull(modFileIdentifier);
        var archiveName = Path.GetFileNameWithoutExtension(archiveFileName);
        var archiveExtension = Path.GetExtension(archiveFileName);

        var hash = archiveHash ?? "|REPLACE|";

        return Task.FromResult(
            $"{archiveName}{Separator}{modFileIdentifier.ModId}{Separator}{modFileIdentifier.ModFileId}{Separator}{hash}{archiveExtension}");
    }

    private Task<string> GetManagedArchiveNameAsync(ModArchiveHandle archiveHandle)
    {
        var modId = new GbModId(archiveHandle.ModId);
        var modFileId = new GbModFileId(archiveHandle.ModFileId);

        var archiveHash = archiveHandle.MD5Hash;

        return GetManagedArchiveNameAsync(archiveHandle.Name, new GbModFileIdentifier(modId, modFileId), archiveHash);
    }

    private static FileInfo EnsureValidArchive(string archivePath)
    {
        var archiveFileName = Path.GetFileNameWithoutExtension(archivePath);
        var archiveExtension = Path.GetExtension(archivePath);

        if (string.IsNullOrEmpty(archiveFileName) || string.IsNullOrEmpty(archiveExtension))
            throw new ArgumentException($"Invalid archive file path: {archivePath}", nameof(archivePath));

        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found", archivePath);

        var archiveFile = new FileInfo(archivePath);

        return archiveFile;
    }

    public double GetTotalCacheSizeInGB() => _modArchives.Sum(x => x.Value.SizeInGb);

    private void RemoveUntilUnderMaxSize()
    {
        // Remove archives until the directory is under the max size
        var currentSize = GetTotalCacheSizeInGB();

        if (currentSize > _maxDirectorySizeGb)
        {
            _logger.Information(
                "Mod archive directory is over the size limit, removing archives. Limit {MaxLimit} | CurrentSize: {CurrentSize}",
                _maxDirectorySizeGb, currentSize);
        }


        while (_modArchives.Sum(x => x.Value.SizeInGb) > _maxDirectorySizeGb)
        {
            var oldest = _modArchives.MinBy(x => x.Value.LastWriteTime);
            _modArchives.TryRemove(oldest.Key, out _);

            if (oldest.Value.Exists)
                File.Delete(oldest.Value.FullName);
            oldest.Value.Refresh();
        }
    }
}

public class ModArchiveHandle
{
    private readonly FileInfo _archiveFile;

    public string ModId { get; private set; }
    public string ModFileId { get; private set; }
    public string ModFileName { get; private set; }
    public string MD5Hash { get; private set; }

    public string FullName => _archiveFile.FullName.ToLowerInvariant();

    public string Name => _archiveFile.Name.ToLowerInvariant();

    public bool Exists => _archiveFile.Exists;

    public double SizeInGb => _archiveFile.Length / 1024D / 1024D / 1024D;

    public DateTime LastWriteTime => _archiveFile.LastWriteTime;

    public void Refresh() => _archiveFile.Refresh();

    private ModArchiveHandle(string modId, string modFileId, string modFileName, string md5Hash, FileInfo archiveFile)
    {
        ModId = modId;
        ModFileId = modFileId;
        ModFileName = modFileName;
        MD5Hash = md5Hash;
        _archiveFile = archiveFile;
    }

    internal static ModArchiveHandle FromManagedFile(FileInfo archiveFile)
    {
        var archiveFileName = Path.GetFileNameWithoutExtension(archiveFile.Name);

        var sections = archiveFileName.Split(ModArchiveRepository.Separator);

        if (sections.Length != 4)
            throw new InvalidArchiveNameFormatException();

        var modFileName = sections[0];
        var modId = sections[1];
        var modFileId = sections[2];
        var md5Hash = sections[3];

        if (string.IsNullOrEmpty(modFileName) || string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(modFileId) ||
            string.IsNullOrEmpty(md5Hash))
            throw new InvalidArchiveNameFormatException();


        return new ModArchiveHandle(modId, modFileId, modFileName, md5Hash, archiveFile);
    }
}

public class InvalidArchiveNameFormatException : Exception
{
    public InvalidArchiveNameFormatException(string message) : base(message)
    {
    }

    public InvalidArchiveNameFormatException() : base("Invalid archive name format")
    {
    }
}

public class SetupOptions
{
    private int _maxDirectorySizeGb = 10;

    public int MaxDirectorySizeGb
    {
        get => _maxDirectorySizeGb;
        set
        {
            if (value <= 0)
                _maxDirectorySizeGb = 0;

            _maxDirectorySizeGb = value;
        }
    }
}