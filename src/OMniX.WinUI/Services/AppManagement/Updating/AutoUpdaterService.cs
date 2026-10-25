using System.Diagnostics;
using ErrorOr;
using Microsoft.UI.Xaml;
using OMniX.Core.Contracts.Services;
using Serilog;

namespace OMniX.WinUI.Services.AppManagement.Updating;

public class AutoUpdaterService
{
    private readonly ILogger _logger;
    private readonly UpdateChecker _updateChecker;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public const string AutoUpdaterFolder = "OMniX - Auto Updater";
    public const string NewAutoUpdaterFolder = "OMniX - Auto Updater_New";
    public const string AutoUpdaterExe = "OMniX - Auto Updater.exe";

    private readonly DirectoryInfo _oldAutoUpdaterFolder = new(Path.Combine(App.ROOT_DIR, AutoUpdaterFolder));

    private readonly DirectoryInfo _newAutoUpdaterFolder = new(Path.Combine(App.ROOT_DIR, NewAutoUpdaterFolder));

    private readonly DirectoryInfo _currentAutoUpdaterFolder = new(Path.Combine(App.ROOT_DIR, AutoUpdaterFolder));

    // Not really a proper lock, but it's good enough for this purposes.
    // Upper [RelayCommand]'s also stop the user from starting multiple update processes.
    private static bool HasStartedSelfUpdateProcess { get; set; }

    public bool AutoUpdaterExists =>
        _currentAutoUpdaterFolder.Exists && ContainsAutoUpdaterExe(_currentAutoUpdaterFolder);

    public AutoUpdaterService(ILogger logger, UpdateChecker updateChecker)
    {
        _logger = logger;
        _updateChecker = updateChecker;
    }


    public void UpdateAutoUpdater()
    {
        var isValidNewAutoUpdater = _newAutoUpdaterFolder.Exists &&
                                    ContainsAutoUpdaterExe(_newAutoUpdaterFolder);
        // There is no new auto updater, so we can't update the current one.
        if (!isValidNewAutoUpdater)
        {
            var isValidOldAutoUpdater = _oldAutoUpdaterFolder.Exists &&
                                        ContainsAutoUpdaterExe(_oldAutoUpdaterFolder);

            if (!isValidOldAutoUpdater)
            {
                _logger.Information("No auto updater found, auto updating is disabled.");
                return;
            }


            _logger.Debug("No new auto updater found, using current one.");
            return;
        }

        // 二开：原来是「删掉旧目录、把 _New 整个移过来」，这要求 _New 是一份完整的更新器。
        // 但增量补丁里只会带上真正变过的那几个文件，照原逻辑移过去，
        // 得到的就是一个缺了 .NET 运行时、双击直接报错的更新器。
        // 改成把 _New 里的文件盖到现有目录上，没带的就继续用旧的。
        try
        {
            App.OverrideShutdown = true;

            _logger.Information("Updating Auto Updater...");

            if (!_oldAutoUpdaterFolder.Exists)
                _oldAutoUpdaterFolder.Create();

            CopyFilesRecursively(_newAutoUpdaterFolder, _oldAutoUpdaterFolder);

            _newAutoUpdaterFolder.Delete(true);

            _logger.Information("Auto Updater updated successfully.");
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to update Auto Updater.");
        }
        finally
        {
            App.OverrideShutdown = false;
        }
    }

    private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.EnumerateDirectories())
            CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));

        foreach (var file in source.EnumerateFiles())
            file.CopyTo(Path.Combine(target.FullName, file.Name), true);
    }


    /// <summary>
    ///     启动更新器。
    ///     二开：<paramref name="localZipPath" /> 给了本地 zip 时，更新器会跳过
    ///     查版本和下载，直接拿这个包装 —— 设置页「导入本地更新包」走这条路。
    /// </summary>
    public Error[]? StartSelfUpdateProcess(string? localZipPath = null)
    {
        if (HasStartedSelfUpdateProcess)
        {
            _logger.Warning("Self update process already started.");
            return new[] { Error.Conflict(description: "Self update process already started.") };
        }

        HasStartedSelfUpdateProcess = true;
        _logger.Information("Starting self update process...");
        try
        {
            var errors = _startSelfUpdateProcess(localZipPath);
            return errors;
        }
        finally
        {
            HasStartedSelfUpdateProcess = false;
        }
    }

    private Error[]? _startSelfUpdateProcess(string? localZipPath = null)
    {
        // 二开：原来这几条报错是英文的，只丢一句「folder does not exist」，
        // 用户看到只会以为软件坏了。实际九成情况是：当前跑的是开发构建
        // （bin\...\Debug 下那份），那个目录里本来就没打包更新器。
        if (!_currentAutoUpdaterFolder.Exists)
        {
            _logger.Error("Current auto updater folder does not exist. Could not find the update folder: {Folder}",
                _currentAutoUpdaterFolder.FullName);
            return new[]
            {
                Error.NotFound(
                    description:
                    string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/AutoUpd_FolderMissing", "找不到更新器目录：{0}\n" +
                    "程序根目录下得有「OMniX - Auto Updater」这个文件夹。\n" +
                    "如果你跑的是源码里的开发构建（bin\\...\\Debug），那里不会带更新器——" +
                    "请改用 Releases 里解压出来的正式版目录。"), _currentAutoUpdaterFolder.FullName))
            };
        }

        if (!ContainsAutoUpdaterExe(_currentAutoUpdaterFolder))
        {
            _logger.Error(
                "Current auto updater folder does not contain the auto updater exe. Could not find {Exe} in {Folder}",
                AutoUpdaterExe, _currentAutoUpdaterFolder.FullName);

            return new[]
            {
                Error.NotFound(
                    description:
                    string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/AutoUpd_ExeMissing", "更新器目录在，但里面没有 {0}：{1}\n" +
                    "这个目录大概被清过，重新解压一份完整包覆盖即可。"), AutoUpdaterExe, _currentAutoUpdaterFolder.FullName))
            };
        }

        // 二开：光有 exe 不够。
        // 1.1.1 及更早的 UpdateAutoUpdater() 是「删掉旧目录、把 _New 整个移过来」，
        // 而增量补丁里的 _New 只带 exe/dll/pdb 三个文件 —— 移过去之后更新器目录就剩这三个，
        // .NET 运行时和 WinUI 资源全没了。之后每次点更新：窗口白一下、然后闪退，
        // 连日志都写不出来（创建日志的代码在 App 构造里，那时已经起不来了）。
        // 这里先把关，宁可明确报一句「请下整包覆盖」，也不去启动一个注定要崩的 exe。
        var missingFile = FindMissingAutoUpdaterFile(_currentAutoUpdaterFolder);
        if (missingFile is not null)
        {
            _logger.Error(
                "Auto updater folder is incomplete, missing {Missing}. Folder: {Folder}",
                missingFile, _currentAutoUpdaterFolder.FullName);

            return new[]
            {
                Error.Failure(
                    description:
                    string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/AutoUpd_Incomplete", "更新器目录不完整，缺 {0}。\n" +
                    "多半是之前某次增量更新把更新器覆盖坏了（旧逻辑会留下半套文件）。\n" +
                    "修法：去 GitHub Releases 下载最新的 OMniX 完整包，解压后把整个「OMniX - Auto Updater」" +
                    "文件夹覆盖到程序目录下，再点更新就正常了。"), missingFile))
            };
        }

        var isAutoUpdaterRunning = Process.GetProcessesByName(AutoUpdaterExe).Any();

        if (isAutoUpdaterRunning)
        {
            _logger.Error("Auto updater is already running.");
            return new[] { Error.Conflict(description: _localizer.GetLocalizedStringOrDefault("/ServicesMsg/AutoUpd_AlreadyRunning", "更新器已经在运行了，先把那个窗口关掉再试。")) };
        }

        try
        {
            _logger.Information("Starting Auto Updater...");
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(_currentAutoUpdaterFolder.FullName, AutoUpdaterExe),
                WorkingDirectory = _currentAutoUpdaterFolder.FullName,
                Arguments = BuildArguments(localZipPath),
                UseShellExecute = true
            });

            if (process is null || process.HasExited)
            {
                _logger.Error("Failed to start Auto Updater.");
                return new[] { Error.Unexpected(description: "Failed to start Auto Updater.") };
            }
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to start Auto Updater.");
            return new[]
            {
                Error.Unexpected(description: $"An error occurred while starting the Auto Updater. Reason: {e.Message}")
            };
        }

        _logger.Information("Self update process started successfully. Exiting...");

        Application.Current.Exit();
        return null;
    }

    /// <summary>
    ///     拼更新器的命令行参数：第一个是版本号，第二个（可选）是本地更新包路径。
    ///     路径必须加引号 —— 用户下载目录动不动就带空格，
    ///     不加引号更新器只能收到第一段，然后说文件不存在。
    /// </summary>
    private string BuildArguments(string? localZipPath)
    {
        var version = _updateChecker.CurrentVersion.ToString().Trim('v') ?? "";

        return string.IsNullOrWhiteSpace(localZipPath) ? version : $"{version} \"{localZipPath}\"";
    }

    private static bool ContainsAutoUpdaterExe(DirectoryInfo directoryInfo)
    {
        return directoryInfo.EnumerateFileSystemInfos().Any(f =>
            f.Name.Equals(AutoUpdaterExe, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    ///     更新器能起来所必需的文件，少一个都会白屏闪退（self-contained 的 .NET 运行时 / WinUI 资源没了）。
    ///     二开新增：用来识别被增量更新覆盖坏的更新器目录。
    /// </summary>
    private static readonly string[] RequiredAutoUpdaterFiles =
    {
        AutoUpdaterExe,
        "OMniX - Auto Updater.dll",
        "OMniX - Auto Updater.runtimeconfig.json",
        "OMniX - Auto Updater.deps.json",
        "resources.pri",
        "coreclr.dll",
        "hostfxr.dll"
    };

    private static string? FindMissingAutoUpdaterFile(DirectoryInfo folder)
    {
        foreach (var relative in RequiredAutoUpdaterFiles)
            if (!File.Exists(Path.Combine(folder.FullName, relative)))
                return relative;

        return null;
    }
}