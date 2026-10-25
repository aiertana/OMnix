using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Views;
using Serilog;
using static OMniX.WinUI.Services.ModHandling.ModDragAndDropService.DragAndDropFinishedArgs;

namespace OMniX.WinUI.Services.ModHandling;

public class ModDragAndDropService
{
    private readonly ILogger _logger;
    private readonly ModInstallerService _modInstallerService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly IArchivePasswordPromptService _archivePasswordPromptService;


    private readonly Notifications.NotificationManager _notificationManager;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public event EventHandler<DragAndDropFinishedArgs>? DragAndDropFinished;

    /// <summary>
    ///     二开：批量导入时是否把剩下的 Mod 也按默认值自动添加。
    ///     用户在第一个窗口里点过「全部按默认添加」之后置真。
    /// </summary>
    public static bool AutoAddRemaining { get; set; }

    /// <summary>
    ///     二开：批量导入的剩余队列（只存名字，安装页左侧会列出来）。
    /// </summary>
    public static ObservableCollection<string> BatchQueueNames { get; } = new();

    public ModDragAndDropService(ILogger logger, Notifications.NotificationManager notificationManager,
        ModInstallerService modInstallerService, IWindowManagerService windowManagerService,
        IArchivePasswordPromptService archivePasswordPromptService)
    {
        _notificationManager = notificationManager;
        _modInstallerService = modInstallerService;
        _windowManagerService = windowManagerService;
        _archivePasswordPromptService = archivePasswordPromptService;
        _logger = logger.ForContext<ModDragAndDropService>();
    }

    // Drag and drop directly from 7zip is REALLY STRANGE, I don't know why 7zip 'usually' deletes the files before we can copy them
    // Sometimes only a few folders are copied, sometimes only a single file is copied, but usually 7zip removes them and the app just crashes
    // This code is a mess, but it works.
    public async Task<InstallMonitor?> AddStorageItemFoldersAsync(
        ICharacterModList modList, IReadOnlyList<IStorageItem>? storageItems)
    {
        if (storageItems is null || !storageItems.Any())
        {
            _logger.Warning("Drag and drop files called with null/0 storage items.");
            return null;
        }


        if (_windowManagerService.GetWindow(modList) is { } window)
        {
            _notificationManager.ShowNotification(
                $"Please finish adding the mod for '{modList.Character.DisplayName}' first",
                $"OMniX does not support multiple mod installs for the same character",
                TimeSpan.FromSeconds(8));

            PInvoke.PlaySound("SystemAsterisk", null,
                SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_ALIAS | SND_FLAGS.SND_NODEFAULT);

            App.MainWindow.DispatcherQueue.TryEnqueue(() => window.Activate());
            return null;
        }

        // 二开：支持一次拖入多个压缩包 / Mod 文件夹，按顺序逐个安装。
        // 原来这里直接弹「more than one storage item」的错误，等于批量导入完全不可用。
        if (storageItems.Count > 1)
        {
            var queue = storageItems.ToList();

            _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/DragDrop_BatchTitle", "批量导入"),
                string.Format(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/DragDrop_BatchBody", "共 {0} 个 Mod，将逐个打开安装窗口：{1}"), queue.Count, string.Join("、" , queue.Take(3).Select(x => x.Name))) +
                (queue.Count > 3 ? " …" : ""), TimeSpan.FromSeconds(8));

            var firstMonitor = await InstallSingleAsync(modList, queue[0]);

            if (firstMonitor is null)
                return null;

            // 二开：把剩下的列到安装页左侧
            UpdateBatchQueue(() =>
            {
                BatchQueueNames.Clear();

                foreach (var pending in queue.Skip(1))
                    BatchQueueNames.Add(pending.Name);
            });

            SetBatchTitle(firstMonitor, queue[0].Name, 1, queue.Count);

            _ = ProcessBatchQueueAsync(modList, queue.Skip(1).ToList(), firstMonitor, queue.Count);

            return firstMonitor;
        }

        return await InstallSingleAsync(modList, storageItems[0]);
    }

    private static void SetBatchTitle(InstallMonitor monitor, string name, int index, int total)
    {
        // 二开：这个方法是在 Task.Run 的后台线程上被调用的
        // （AddStorageItemFoldersAsync 本身跑在 Task.Run 里），
        // 而 WinUI 的 Window 只能从 UI 线程碰。直接赋 Title 会抛
        //   COMException 0x8001010E (RPC_E_WRONG_THREAD)
        // 然后整个方法中断，界面弹出「An error occurred while adding the
        // storage items. Reason:」—— 而且原因是空的，因为这是 COM 层抛的，
        // 没有托管的消息文本，只能靠日志里的 HRESULT 才看得出来。
        //
        // 单个文件不会走到这里（只有批量才设标题），所以现象是
        // 「一次拖一个能装，拖两个以上就报错」，很容易误判成拖放没被接受。
        //
        // 用 DispatcherQueue 切回 UI 线程。标题只是个提示，拿不到队列就跳过，
        // 绝不能因为它把整批安装搞崩。
        var window = monitor.Window;

        if (window?.DispatcherQueue is not { } dispatcher)
        {
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            try
            {
                window.Title = string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/ServicesMsg/DragDrop_WindowTitle", "安装 Mod - {0}（{1}/{2}）"), name, index, total);
            }
            catch
            {
                // 窗口已经关了 —— 无所谓，标题不重要
            }
        });
    }

    /// <summary>
    ///     二开：BatchQueueNames 绑在安装页左侧的列表上（ObservableCollection），
    ///     属于 UI 对象，只能在 UI 线程改。批量导入整个跑在 Task.Run 里，
    ///     直接 Add / Remove 和 SetBatchTitle 是同一个坑：RPC_E_WRONG_THREAD。
    ///     这里统一切回 UI 线程执行。
    /// </summary>
    private static void UpdateBatchQueue(Action action)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;

        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcher.TryEnqueue(() => action());
    }

    /// <summary>
    ///     二开：批量队列。上一个安装窗口关掉之后再开下一个，避免同时弹一堆窗口。
    /// </summary>
    private async Task ProcessBatchQueueAsync(ICharacterModList modList, List<IStorageItem> queue,
        InstallMonitor current, int total)
    {
        for (var i = 0; i < queue.Count; i++)
        {
            try
            {
                await current.WaitForCloseAsync();
            }
            catch
            {
                // 被取消就停掉整个队列
                UpdateBatchQueue(() => BatchQueueNames.Clear());
                return;
            }

            // 这一个开始装了，从左侧队列里划掉
            UpdateBatchQueue(() =>
            {
                if (BatchQueueNames.Count > 0)
                    BatchQueueNames.RemoveAt(0);
            });

            var next = await InstallSingleAsync(modList, queue[i]);

            if (next is null)
                return;

            SetBatchTitle(next, queue[i].Name, i + 2, total);
            current = next;

            // 二开：用户点过「全部按默认添加」时，后续这几个也自动提交，不再逐个等确认
            if (!AutoAddRemaining)
                continue;

            await Task.Delay(2200);

            try
            {
                var viewModel = next.Page.ViewModel;

                if (!viewModel.AddModDialogCommand.CanExecute(null))
                    continue;

                await viewModel.AddModDialogCommand.ExecuteAsync(null);
                next.Window.Close();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Auto add failed for '{Name}', stopping batch", queue[i].Name);
                AutoAddRemaining = false;
                return;
            }
        }

        AutoAddRemaining = false;
        UpdateBatchQueue(() => BatchQueueNames.Clear());
    }

    private async Task<InstallMonitor?> InstallSingleAsync(ICharacterModList modList, IStorageItem storageItem)
    {

        InstallMonitor? installMonitor;
        if (storageItem is StorageFile storageFile)
        {
            var extractResult = await ScanArchiveWithPasswordAsync(storageFile.Path);
            if (extractResult is null)
                return null;

            installMonitor = await _modInstallerService.StartModInstallationAsync(
                new DirectoryInfo(extractResult.ExtractedFolder.FullPath), modList);

            return installMonitor;
        }

        if (storageItem is not StorageFolder sourceFolder)
        {
            _logger.Information("Unknown storage item type from drop: {StorageItemType}", storageItem.GetType());
            return null;
        }

        var destDirectoryInfo = App.GetUniqueTmpFolder();
        destDirectoryInfo.Create();
        destDirectoryInfo = new DirectoryInfo(Path.Combine(destDirectoryInfo.FullName, storageItem.Name));


        _logger.Debug("Source destination folder for drag and drop: {Source}", sourceFolder.Path);
        _logger.Debug("Copying folder {FolderName} to {DestinationFolder}", sourceFolder.Path,
            destDirectoryInfo.FullName);


        var sourceFolderPath = sourceFolder.Path;


        if (sourceFolderPath is null)
        {
            _logger.Warning("Source folder path is null, skipping.");
            return null;
        }

        var tmpFolder = Path.GetTempPath();

        Action<StorageFolder, StorageFolder> recursiveCopy = null!;

        if (sourceFolderPath.Contains(tmpFolder)) // Is 7zip
        {
            destDirectoryInfo = new DirectoryInfo(Path.Combine(destDirectoryInfo.FullName, sourceFolder.Name));
            recursiveCopy = RecursiveCopy7z;
        }
        else
        {
            destDirectoryInfo = new DirectoryInfo(Path.Combine(destDirectoryInfo.FullName, sourceFolder.Name));
            recursiveCopy = RecursiveCopy;
        }

        destDirectoryInfo.Create();

        try
        {
            recursiveCopy.Invoke(sourceFolder,
                await StorageFolder.GetFolderFromPathAsync(destDirectoryInfo.FullName));
        }
        catch (Exception)
        {
            Directory.Delete(destDirectoryInfo.FullName);
            throw;
        }

        installMonitor = await _modInstallerService.StartModInstallationAsync(destDirectoryInfo.Parent!, modList)
            .ConfigureAwait(false);
        DragAndDropFinished?.Invoke(this, new DragAndDropFinishedArgs(new List<ExtractPaths>()));
        return installMonitor;
    }

    /// <summary>
    ///     解压压缩包；若压缩包加密则弹窗要求输入密码，密码错误可重试，用户取消时返回 null
    /// </summary>
    private async Task<DragAndDropScanResult?> ScanArchiveWithPasswordAsync(string archivePath)
    {
        string? password = null;
        var isRetry = false;

        while (true)
        {
            var scanner = new DragAndDropScanner();
            try
            {
                return scanner.ScanAndGetContents(archivePath, password);
            }
            catch (ArchivePasswordRequiredException)
            {
                isRetry = false;
            }
            catch (ArchivePasswordIncorrectException)
            {
                isRetry = true;
            }

            password = await _archivePasswordPromptService.PromptForPasswordAsync(archivePath, isRetry);
            if (string.IsNullOrEmpty(password))
            {
                _notificationManager.ShowNotification(_localizer.GetLocalizedStringOrDefault("/ServicesMsg/DragDrop_CancelledTitle", "已取消导入"), _localizer.GetLocalizedStringOrDefault("/ServicesMsg/DragDrop_CancelledBody", "未输入压缩包密码，本次导入已取消"),
                    TimeSpan.FromSeconds(5));
                return null;
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    private void RecursiveCopy7z(StorageFolder sourceFolder, StorageFolder destinationFolder)
    {
        var tmpFolder = Path.GetTempPath();
        var parentDir = new DirectoryInfo(Path.GetDirectoryName(sourceFolder.Path)!);
        parentDir.MoveTo(Path.Combine(tmpFolder, "OMniX_TMP", Guid.NewGuid().ToString("N")));

        var modDir = parentDir.EnumerateDirectories().FirstOrDefault();

        if (modDir is null)
        {
            throw new DirectoryNotFoundException("No valid mod folder found in archive. Loose files are ignored");
        }

        RecursiveCopy(StorageFolder.GetFolderFromPathAsync(modDir.FullName).GetAwaiter().GetResult(),
            destinationFolder);
    }

    private void RecursiveCopy(StorageFolder sourceFolder, StorageFolder destinationFolder)
    {
        if (sourceFolder == null || destinationFolder == null)
            throw new ArgumentNullException("Source and destination folders cannot be null.");

        var sourceDir = new DirectoryInfo(sourceFolder.Path);

        // Copy files
        foreach (var file in sourceDir.GetFiles())
        {
            _logger.Debug("Copying file {FileName} to {DestinationFolder}", file.FullName, destinationFolder.Path);
            if (!File.Exists(file.FullName))
            {
                _logger.Warning("File {FileName} does not exist.", file.FullName);
                continue;
            }

            file.CopyTo(Path.Combine(destinationFolder.Path, file.Name), true);
        }
        // Recursively copy subfolders

        foreach (var subFolder in sourceDir.GetDirectories())
        {
            _logger.Debug("Copying subfolder {SubFolderName} to {DestinationFolder}", subFolder.FullName,
                destinationFolder.Path);
            if (!Directory.Exists(subFolder.FullName))
            {
                _logger.Warning("Subfolder {SubFolderName} does not exist.", subFolder.FullName);
                continue;
            }

            var newSubFolder = new DirectoryInfo(Path.Combine(destinationFolder.Path, subFolder.Name));
            newSubFolder.Create();
            RecursiveCopy(StorageFolder.GetFolderFromPathAsync(subFolder.FullName).GetAwaiter().GetResult(),
                StorageFolder.GetFolderFromPathAsync(newSubFolder.FullName).GetAwaiter().GetResult());
        }
    }


    public async Task AddModFromUrlAsync(ICharacterModList modList, Uri uri)
    {
        var windowKey = $"ModPage_{modList.Character.InternalName}";
        if (_windowManagerService.GetWindow(windowKey) is { } window)
        {
            PInvoke.PlaySound("SystemAsterisk", null,
                SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_ALIAS | SND_FLAGS.SND_NODEFAULT);

            App.MainWindow.DispatcherQueue.TryEnqueue(() => window.Activate());
            return;
        }


        var modWindow = new GbModPageWindow(uri, modList.Character);
        _windowManagerService.CreateWindow(modWindow, identifier: windowKey);
        await Task.Delay(100);
        modWindow.BringToFront();
    }

    public class DragAndDropFinishedArgs : EventArgs
    {
        public DragAndDropFinishedArgs(IReadOnlyCollection<ExtractPaths> extractResults)
        {
            ExtractResults = extractResults;
        }

        public IReadOnlyCollection<ExtractPaths> ExtractResults { get; }

        public record ExtractPaths
        {
            public ExtractPaths(string sourcePath, string extractedFolderPath)
            {
                SourcePath = sourcePath;
                ExtractedFolderPath = Path.EndsInDirectorySeparator(extractedFolderPath)
                    ? extractedFolderPath
                    : extractedFolderPath + Path.DirectorySeparatorChar;
            }

            public string SourcePath { get; init; }
            public string ExtractedFolderPath { get; init; }

            public void Deconstruct(out string SourcePath, out string ExtractedFolderPath)
            {
                SourcePath = this.SourcePath;
                ExtractedFolderPath = this.ExtractedFolderPath;
            }
        }
    }
}