using System.Diagnostics.CodeAnalysis;
using CommunityToolkitWrapper;
using OMniX.Core.Contracts.Entities;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities.Mods.Contract;
using OMniX.Core.Entities.Mods.SkinMod;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;
using OMniX.Core.Services;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Models.Settings;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.ViewModels;
using OMniX.WinUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace OMniX.WinUI.Services.ModHandling;

public class ModInstallerService(
    IWindowManagerService windowManagerService,
    ILocalSettingsService localSettingsService)
{
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly IWindowManagerService _windowManagerService = windowManagerService;

    public async Task<InstallMonitor> StartModInstallationAsync(DirectoryInfo modFolder, ICharacterModList modList,
        ICharacterSkin? inGameSkin = null, Action<InstallOptions>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(modFolder);
        ArgumentNullException.ThrowIfNull(modList);


        if (inGameSkin is not null && modList.Character is not ICharacter)
            throw new ArgumentException("The mod list must be a character mod list if inGameSkin is not null");

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow.DispatcherQueue;

        var modOptions = new InstallOptions();
        setup?.Invoke(modOptions);


        var monitor =
            await dispatcherQueue.EnqueueAsync(() => InternalStartAsync(modFolder, modList, inGameSkin, modOptions));

        return monitor;
    }

    private async Task<InstallMonitor> InternalStartAsync(DirectoryInfo modFolder, ICharacterModList modList,
        ICharacterSkin? inGameSkin = null, InstallOptions? options = null)
    {
        var modTitle = Guid.TryParse(modFolder.Name, out _)
            ? modFolder.EnumerateDirectories().FirstOrDefault()?.Name
            : modFolder.Name;

        modTitle ??= modFolder.Name;

        var modInstallerSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModInstallerSettings>(ModInstallerSettings.Key);

        var modInstallPage = new ModInstallerPage(modList, modFolder, inGameSkin, options);
        var modInstallWindow = new WindowEx()
        {
            SystemBackdrop = new DesktopAcrylicBackdrop(),
            Title = $"安装 Mod - {modTitle}",
            Content = modInstallPage,
            Width = 1200,
            Height = 750,
            MinHeight = 415,
            MinWidth = 1024,
            IsAlwaysOnTop = modInstallerSettings.ModInstallerWindowOnTop
        };
        _windowManagerService.CreateWindow(modInstallWindow, modList);

        return new InstallMonitor(modInstallPage, modInstallWindow);
    }
}

public class InstallOptions
{
    public Uri? ModUrl { get; set; }
    public Guid? ExistingModIdToUpdate { get; set; }
}

public sealed class InstallMonitor : IDisposable
{
    private readonly TaskCompletionSource<CloseRequestedArgs> _taskCompletionSource = new();
    private readonly ModInstallerPage _modInstallerPage;
    private readonly WindowEx _modInstallerWindow;
    private CancellationTokenRegistration? _cancellationTokenRegistration;

    public Task Task => _taskCompletionSource.Task;

    /// <summary>
    ///     二开：批量导入时用来改窗口标题（显示当前是第几个）。
    /// </summary>
    public WindowEx Window => _modInstallerWindow;

    /// <summary>
    ///     二开：批量「全部按默认添加」需要直接驱动页面。
    /// </summary>
    public ModInstallerPage Page => _modInstallerPage;

    public InstallMonitor(ModInstallerPage modInstallerPage, WindowEx modInstallerWindow)
    {
        _modInstallerPage = modInstallerPage;
        _modInstallerWindow = modInstallerWindow;

        _modInstallerPage.CloseRequested += (_, e) =>
        {
            _taskCompletionSource.SetResult(e);
            _modInstallerWindow.Close();
        };

        _modInstallerWindow.Closed += (_, _) =>
        {
            if (!_taskCompletionSource.Task.IsCompleted)
                _taskCompletionSource.TrySetResult(new CloseRequestedArgs(CloseRequestedArgs.CloseReasons.Canceled));
        };
    }

    public Task<CloseRequestedArgs> WaitForCloseAsync(CancellationToken? cancellationToken = null)
    {
        if (cancellationToken is not null)
            _cancellationTokenRegistration = cancellationToken.Value.Register(() =>
            {
                if (!_taskCompletionSource.Task.IsCompleted)
                {
                    _taskCompletionSource.TrySetCanceled();
                    _modInstallerWindow.Close();
                }
            });

        return _taskCompletionSource.Task;
    }

    public void Dispose()
    {
        _cancellationTokenRegistration?.Dispose();
        _cancellationTokenRegistration = null;
    }
}

public sealed class ModInstallation : IDisposable
{
    private readonly ModCrawlerService _modCrawlerService = App.GetService<ModCrawlerService>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ICharacterModList _destinationModList;
    private readonly DirectoryInfo _originalModFolder;
    private readonly List<FileStream> _lockedFiles = new();

    private FileInfo? _omnixConfigFile;
    public DirectoryInfo ModFolder { get; private set; }
    private DirectoryInfo? _shaderFixesFolder;

    // TODO: Enable later
    private List<FileInfo> _shaderFixesFiles = new();


    private ModInstallation(DirectoryInfo originalModFolder, ICharacterModList destinationModList)
    {
        _originalModFolder = originalModFolder;
        _destinationModList = destinationModList;
        SetRootModFolder(originalModFolder);
        LockFiles();
    }

    private void LockFiles()
    {
        foreach (var fileInfo in _originalModFolder.GetFiles("*", SearchOption.AllDirectories))
        {
            var fileStream = fileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            _lockedFiles.Add(fileStream);
        }
    }

    // Lock mod folder
    public static ModInstallation Start(DirectoryInfo modFolder, ICharacterModList destinationModList)
    {
        return new ModInstallation(modFolder, destinationModList);
    }

    [MemberNotNull(nameof(ModFolder))]
    public void SetRootModFolder(DirectoryInfo newRootFolder)
    {
        if (newRootFolder.FullName == _shaderFixesFolder?.FullName)
            throw new ArgumentException("The new root folder is the same as the current shader fixes folder");

        if (!newRootFolder.Exists)
            throw new DirectoryNotFoundException($"The folder {newRootFolder.FullName} does not exist");

        ModFolder = new DirectoryInfo(newRootFolder.FullName);
        _omnixConfigFile = _modCrawlerService.GetFirstOmniXConfigFileAsync(ModFolder, false);
    }

    public void SetShaderFixesFolder(DirectoryInfo shaderFixesFolder)
    {
        // TODO: Enable later
        return;

        if (shaderFixesFolder.FullName == ModFolder.FullName)
            throw new ArgumentException("The new shader fixes folder is the same as the current root folder");

        if (!shaderFixesFolder.Exists)
            throw new DirectoryNotFoundException($"The folder {shaderFixesFolder.FullName} does not exist");

        _shaderFixesFiles.Clear();
        _shaderFixesFiles.AddRange(shaderFixesFolder.GetFiles("*.txt", SearchOption.TopDirectoryOnly));
        _shaderFixesFolder = shaderFixesFolder;
    }


    public DirectoryInfo? AutoSetModRootFolder()
    {
        var omnixConfigFile = _modCrawlerService.GetFirstOmniXConfigFileAsync(_originalModFolder);
        DirectoryInfo? modRootFolder = null;
        if (omnixConfigFile is not null)
        {
            modRootFolder = new DirectoryInfo(omnixConfigFile.DirectoryName!);
        }
        else
        {
            var mergedIniFile = _modCrawlerService.GetMergedIniFile(_originalModFolder);
            if (mergedIniFile is not null)
                modRootFolder = new DirectoryInfo(mergedIniFile.DirectoryName!);
        }

        modRootFolder ??= _originalModFolder.EnumerateDirectories().FirstOrDefault();


        if (modRootFolder is null)
            return null;

        SetRootModFolder(modRootFolder);
        return modRootFolder;
    }

    public DirectoryInfo? AutoSetShaderFixesFolder()
    {
        var shaderFixesFolder = _modCrawlerService.GetShaderFixesFolder(_originalModFolder);
        if (shaderFixesFolder is null)
            return null;

        SetShaderFixesFolder(shaderFixesFolder);
        return shaderFixesFolder;
    }

    public async Task<ModSettings?> TryReadModSettingsAsync()
    {
        if (_omnixConfigFile is null)
            return null;

        await RemoveOmniXConfigFileLockAsync().ConfigureAwait(false);
        try
        {
            return await SkinModSettingsManager.ReadSettingsAsync(_omnixConfigFile.FullName);
        }
        catch (Exception)
        {
            // ignored
        }

        LockOmniXConfigFile();
        return null;
    }

    public ISkinMod? AnyDuplicateName()
    {
        var skinEntries = _destinationModList.Mods;


        foreach (var skinEntry in skinEntries)
        {
            if (ModFolderHelpers.FolderNameEquals(skinEntry.Mod.Name, ModFolder.Name))
                return skinEntry.Mod;
        }

        return null;
    }

    public async Task<ISkinMod> RenameAndAddAsync(AddModOptions options, ISkinMod dupeMod,
        string dupeModNewFolderName, string? dupeModNewCustomName = null)
    {
        if (dupeModNewFolderName.IsNullOrEmpty() && options.NewModFolderName.IsNullOrEmpty())
            throw new ArgumentException("The new mod folder name and old folder name cannot be null or empty");

        if (ModFolderHelpers.FolderNameEquals(dupeModNewFolderName, options.NewModFolderName!))
            throw new ArgumentException("The new mod folder name and old folder name cannot be the same");

        ReleaseLockedFiles();

        var skinMod = await CreateSkinModWithOptionsAsync(options);
        var newModRenamed = false;
        if (!options.NewModFolderName.IsNullOrEmpty() &&
            !ModFolderHelpers.FolderNameEquals(skinMod.Name, options.NewModFolderName))
        {
            var tmpFolder = App.GetUniqueTmpFolder();
            skinMod = await SkinMod.CreateModAsync(skinMod.CopyTo(tmpFolder.FullName).FullPath).ConfigureAwait(false);
            skinMod.Rename(options.NewModFolderName);
            newModRenamed = true;
        }

        if (!dupeModNewFolderName.IsNullOrEmpty() &&
            !ModFolderHelpers.FolderNameEquals(dupeMod.Name, dupeModNewFolderName))
        {
            _destinationModList.RenameMod(dupeMod, dupeModNewFolderName);
        }

        // Set new custom name for dupe mod
        if (!dupeModNewCustomName.IsNullOrEmpty())
        {
            var dupeModSettings = await dupeMod.Settings.ReadSettingsAsync().ConfigureAwait(false);

            await dupeMod.Settings
                .SaveSettingsAsync(
                    dupeModSettings.DeepCopyWithProperties(customName: NewValue<string?>.Set(dupeModNewCustomName)))
                .ConfigureAwait(false);
        }

        _skinManagerService.AddMod(skinMod, _destinationModList, newModRenamed);
        return skinMod;
    }


    public async Task<ISkinMod> AddAndReplaceAsync(ISkinMod dupeMod, AddModOptions? options = null)
    {
        ReleaseLockedFiles();
        var skinMod = await CreateSkinModWithOptionsAsync(options).ConfigureAwait(false);
        try
        {
            _destinationModList.DeleteModBySkinEntryId(dupeMod.Id);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return _skinManagerService.AddMod(skinMod, _destinationModList);
    }

    public async Task<ISkinMod> AddModAsync(AddModOptions? options = null)
    {
        if (AnyDuplicateName() is not null)
            throw new InvalidOperationException("There is already a mod with the same name");

        ReleaseLockedFiles();
        var skinMod = await CreateSkinModWithOptionsAsync(options);

        return _skinManagerService.AddMod(skinMod, _destinationModList);
    }

    private async Task<ISkinMod> CreateSkinModWithOptionsAsync(AddModOptions? options = null)
    {
        await RemoveOmniXConfigFileLockAsync().ConfigureAwait(false);

        var skinMod = await SkinMod.CreateModAsync(ModFolder, true).ConfigureAwait(false);

        if (options is null)
            return skinMod;

        // 二开：快捷键存进 Preferences，不用改 ModSettings 的数据结构
        Dictionary<string, string>? preferences = null;

        if (!string.IsNullOrWhiteSpace(options.HotKey))
            preferences = new Dictionary<string, string> { ["HotKey"] = options.HotKey };

        var settings = new ModSettings(
            id: skinMod.Id,
            customName: options.ModName,
            imagePath: options.ModImage,
            author: options.Author,
            modUrl: Uri.TryCreate(options.ModUrl, UriKind.Absolute, out var modUrl) ? modUrl : null,
            description: options.Description,
            dateAdded: DateTime.Now,
            preferences: preferences
        );
        await skinMod.Settings.SaveSettingsAsync(settings, new SaveSettingsOptions { DeleteOldImage = false })
            .ConfigureAwait(false);
        return skinMod;
    }

    private async Task RemoveOmniXConfigFileLockAsync()
    {
        if (_omnixConfigFile is not null)
        {
            _omnixConfigFile.Refresh();
            var omnixFs = _lockedFiles.FirstOrDefault(file =>
                file.Name.Equals(_omnixConfigFile.FullName, StringComparison.OrdinalIgnoreCase));

            if (omnixFs is not null)
            {
                await omnixFs.DisposeAsync().ConfigureAwait(false);
                _lockedFiles.Remove(omnixFs);
            }
        }
    }

    private void LockOmniXConfigFile()
    {
        if (_omnixConfigFile is not null)
        {
            _omnixConfigFile.Refresh();
            if (!_omnixConfigFile.Exists) return;

            var omnixFs = _omnixConfigFile.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            _lockedFiles.Add(omnixFs);
        }
    }

    private void ReleaseLockedFiles()
    {
        foreach (var fileStream in _lockedFiles.ToArray())
        {
            fileStream.Dispose();
            _lockedFiles.Remove(fileStream);
        }

        Log.Debug("Released locked files, {time}", DateTime.Now);
    }

    public void Dispose()
    {
        ReleaseLockedFiles();
    }
}

public record AddModOptions
{
    public string? NewModFolderName { get; set; }
    public string? ModName { get; set; }
    public Uri? ModImage { get; set; }
    public string? ModUrl { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }

    /// <summary>
    ///     二开：快捷切换键（从 mod 的 ini 里认出来的，可以手改）。
    /// </summary>
    public string? HotKey { get; set; }
}