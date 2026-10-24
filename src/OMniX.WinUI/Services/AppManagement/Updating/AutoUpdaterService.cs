using System.Diagnostics;
using ErrorOr;
using Microsoft.UI.Xaml;
using Serilog;

namespace OMniX.WinUI.Services.AppManagement.Updating;

public class AutoUpdaterService
{
    private readonly ILogger _logger;
    private readonly UpdateChecker _updateChecker;

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


    public Error[]? StartSelfUpdateProcess()
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
            var errors = _startSelfUpdateProcess();
            return errors;
        }
        finally
        {
            HasStartedSelfUpdateProcess = false;
        }
    }

    private Error[]? _startSelfUpdateProcess()
    {
        if (!_currentAutoUpdaterFolder.Exists)
        {
            _logger.Error("Current auto updater folder does not exist. Could not find the update folder: {Folder}",
                _currentAutoUpdaterFolder.FullName);
            return new[]
            {
                Error.NotFound(
                    description:
                    $"Current auto updater folder does not exist. Could not find the update folder: {_currentAutoUpdaterFolder.FullName}")
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
                    $"Current auto updater folder does not contain the auto updater exe. Could not find {AutoUpdaterExe} in {_currentAutoUpdaterFolder.FullName}")
            };
        }

        var isAutoUpdaterRunning = Process.GetProcessesByName(AutoUpdaterExe).Any();

        if (isAutoUpdaterRunning)
        {
            _logger.Error("Auto updater is already running.");
            return new[] { Error.Conflict(description: "Auto updater is already running.") };
        }

        try
        {
            _logger.Information("Starting Auto Updater...");
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(_currentAutoUpdaterFolder.FullName, AutoUpdaterExe),
                WorkingDirectory = _currentAutoUpdaterFolder.FullName,
                Arguments = _updateChecker.CurrentVersion.ToString().Trim('v') ?? "",
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

    private static bool ContainsAutoUpdaterExe(DirectoryInfo directoryInfo)
    {
        return directoryInfo.EnumerateFileSystemInfos().Any(f =>
            f.Name.Equals(AutoUpdaterExe, StringComparison.CurrentCultureIgnoreCase));
    }
}