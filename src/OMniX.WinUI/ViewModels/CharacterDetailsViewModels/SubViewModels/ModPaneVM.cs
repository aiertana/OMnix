using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkitWrapper;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.Entities.Mods.Contract;
using OMniX.Core.GamesService;
using OMniX.Core.Helpers;
// 二开：用别名避免和 Core 的 Constants 撞名
using WinUIHelpers = OMniX.WinUI.Helpers;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using Microsoft.UI.Dispatching;
using Serilog;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public sealed partial class ModPaneVM(
    ISkinManagerService skinManagerService,
    NotificationManager notificationService,
    ModSettingsService modSettingsService,
    ImageHandlerService imageHandlerService,
    ILanguageLocalizer localizer)
    : ObservableRecipient, IRecipient<ModChangedMessage>
{
    private readonly ILogger _logger = Log.ForContext<ModPaneVM>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly NotificationManager _notificationService = notificationService;
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly ImageHandlerService _imageHandlerService = imageHandlerService;
    private readonly ILanguageLocalizer _localizer = localizer;

    private readonly AsyncLock _loadModLock = new();
    private CancellationToken _cancellationToken = new();
    private DispatcherQueue _dispatcherQueue = null!;
    public bool IsInitialized { get; private set; }
    public BusySetter BusySetter { get; set; } = null!;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotReadOnly))]
    private bool _isReadOnly = true;

    [ObservableProperty] private bool _isEditingModName;

    public bool IsNotReadOnly => !IsReadOnly;

    private Guid? _loadedModId;
    private CharacterSkinEntry? _loadedMod;

    [MemberNotNullWhen(true, nameof(_loadedModId), nameof(_loadedMod))]
    public bool IsModLoaded => _loadedModId != null && ModModel.IsLoaded && _loadedMod != null;

    [ObservableProperty] private ModPaneFieldsVm _modModel = new();


    public bool QueueLoadMod(Guid? modId, bool force = false) => _channel.Writer.TryWrite(new LoadModMessage { ModId = modId, Force = force });


    private readonly Channel<LoadModMessage> _channel = Channel.CreateBounded<LoadModMessage>(
        new BoundedChannelOptions(1)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private async Task ModLoaderLoopAsync()
    {
        // Runs on the UI thread
        await foreach (var loadModMessage in _channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (_cancellationToken.IsCancellationRequested)
                break;
            // 二开：这里不能用 ConfigureAwait(false)，循环是 UI 线程上跑的，
            // 加了之后整个循环体落到后台线程，后面给界面属性赋值就会跨线程崩
            using var _ = await LockAsync();
            IsReadOnly = true;
            IsEditingModName = false;
            try
            {
                if (loadModMessage.ModId is null)
                {
                    await UnloadModAsync();
                    NotifyAllCommands();
                    OnPropertyChanged(nameof(IsModLoaded));
                    continue;
                }

                await LoadModAsync(loadModMessage.ModId.Value, loadModMessage.Force);
                NotifyAllCommands();
                OnPropertyChanged(nameof(IsModLoaded));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_LoadError", "加载 Mod 出错"), e.Message, null);
            }
        }
    }

    private async Task LoadModAsync(Guid modId, bool force)
    {
        if (modId == _loadedModId && force == false)
            return;

        var modPaneData = await Task.Run(async () =>
        {
            var modEntry = _skinManagerService.GetModEntryById(modId);
            if (modEntry == null)
                return null;

            var mod = modEntry.Mod;

            var modSettings =
                await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken: _cancellationToken);

            if (modSettings is null)
                return null;

            ICollection<KeySwapSection>? keySwaps = null;
            try
            {
                if (mod.KeySwaps is not null)
                    keySwaps = (await mod.KeySwaps.ReadKeySwapConfiguration(_cancellationToken)).ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _notificationService.ShowNotification($"Failed to load keyswaps for mod {mod.GetDisplayName()}", e.Message, null);
            }

            return new { modEntry, modSettings, keySwaps };
        }, _cancellationToken);

        if (modPaneData is null)
            return;


        _loadedMod = modPaneData.modEntry;

        // 二开：加载循环跑在后台线程（用了 ConfigureAwait(false)），
        // 而 ModModel 整个绑定到界面上，直接赋值会让界面的集合更新跨线程，
        // 触发 InvalidOperationException → 进程直接崩（0xc000027b）。
        // 这里统一切回 UI 线程再赋值。
        var newVm = ModPaneFieldsVm.FromModEntry(modPaneData.modEntry, modPaneData.modSettings,
            modPaneData.keySwaps ?? []);

        // 二开：按键列表同样交给 UI 队列
        newVm.KeySwaps.DispatcherQueue = _dispatcherQueue;
        newVm.UiDispatcher = _dispatcherQueue;

        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ModModel = newVm;
                ModModel.PropertyChanged += ModModel_PropertyChanged;
                _loadedModId = modId;
                IsReadOnly = false;
            });
            return;
        }

        ModModel = newVm;
        ModModel.PropertyChanged += ModModel_PropertyChanged;
        _loadedModId = modId;
        IsReadOnly = false;
    }

    private void ModModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) => SaveModSettingsCommand.NotifyCanExecuteChanged();
    private void BusySetter_HardBusyChanged(object? sender, EventArgs eventArgs) => NotifyAllCommands();

    private Task UnloadModAsync()
    {
        _loadedModId = null;
        _loadedMod = null;
        if (ModModel.IsLoaded)
            ModModel.PropertyChanged -= ModModel_PropertyChanged;
        ModModel = new ModPaneFieldsVm();
        return Task.CompletedTask;
    }


    private readonly record struct LoadModMessage
    {
        public Guid? ModId { get; init; }
        public bool Force { get; init; }
    }

    public void Receive(ModChangedMessage message)
    {
        if (!IsModLoaded)
            return;

        if (message.SkinEntry.Id != _loadedModId)
            return;

        if (message.sender == this)
            return;

        QueueLoadMod(message.SkinEntry.Id, true);
    }

    public Task OnNavigatedToAsync(DispatcherQueue dispatcherQueue, CancellationToken navigationCt)
    {
        _dispatcherQueue = dispatcherQueue;
        _cancellationToken = navigationCt;
        _ = _dispatcherQueue.EnqueueAsync(ModLoaderLoopAsync);
        Messenger.RegisterAll(this);
        BusySetter.HardBusyChanged += BusySetter_HardBusyChanged;
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public void OnNavigatedFrom()
    {
        _channel.Writer.TryComplete();
        Messenger.UnregisterAll(this);
        try
        {
            _loadModLock.Dispose();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Failed to dispose of load mod lock");
        }
    }

    private bool DefaultCanExecute => IsModLoaded && IsNotReadOnly && BusySetter.IsNotHardBusy;

    #region Commands

    private bool CanSetModIniFile() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanSetModIniFile))]
    private async Task SetModIniFileAsync()
    {
        if (!IsModLoaded) return;
        try
        {
            var modFolderPath = _loadedMod.Mod.FullPath;

            var dataPackage = new DataPackage();
            dataPackage.SetText(modFolderPath);
            Clipboard.SetContent(dataPackage);

            _notificationService.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_PathCopied", "Mod 文件夹路径已复制"), "", TimeSpan.FromSeconds(3));
        }
        catch (Exception e)
        {
            _logger.Error(e, "An error occured while trying to copy mod folder path to clipboard when setting .ini");
        }


        var filePicker = new FileOpenPicker();
        filePicker.SettingsIdentifier = "IniFilerPicker";
        filePicker.FileTypeFilter.Add(".ini");
        filePicker.CommitButtonText = "Set";
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
        var file = await filePicker.PickSingleFileAsync();

        if (file is null)
        {
            _logger.Debug("User cancelled file picker.");
            return;
        }

        var result = await Task.Run(() => _modSettingsService.SetModIniAsync(_loadedMod.Id, file.Path));


        if (result.Notification is not null)
            _notificationService.ShowNotification(result.Notification);

        _loadedMod.Mod.ClearCache();

        Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
        QueueLoadMod(_loadedModId, true);
    }

    private bool CanClearSetModIniFile() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanClearSetModIniFile))]
    private async Task ClearSetModIniFileAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded) return;

            var autoDetect = ModModel.IgnoreMergedIni;
            var result = await Task.Run(() =>
                _modSettingsService.SetModIniAsync(_loadedModId.Value, string.Empty, autoDetect), _cancellationToken);


            if (result.Notification is not null)
                _notificationService.ShowNotification(result.Notification);

            _loadedMod.Mod.ClearCache();

            Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
            QueueLoadMod(_loadedModId, true);
        }).ConfigureAwait(false);
    }

    private bool CanPickImageUri() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanPickImageUri))]
    private async Task PickImageUriAsync()
    {
        if (!IsModLoaded) return;
        var filePicker = new FileOpenPicker();
        filePicker.CommitButtonText = _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_PickImageCommit", "选这张图");
        filePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        filePicker.SettingsIdentifier = "ImagePicker";
        foreach (var supportedImageExtension in Constants.SupportedImageExtensions)
            filePicker.FileTypeFilter.Add(supportedImageExtension);


        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

        var file = await filePicker.PickSingleFileAsync();

        if (file == null) return;
        var imageUri = new Uri(file.Path);
        ModModel.ImageUri = imageUri;
    }

    private bool CanPasteImage() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanPasteImage))]
    private async Task PasteImageFromClipboardAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded) return;

            var clipboardHasValidImageResult = await _imageHandlerService.ClipboardContainsImageAsync();

            if (!clipboardHasValidImageResult.Result)
            {
                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_NoClipboardImage", "剪贴板里没有可用的图片"), "", null);
                return;
            }

            var imagePath = await _imageHandlerService.GetImageFromClipboardAsync(clipboardHasValidImageResult.DataPackage);

            if (imagePath == null)
            {
                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_GetClipboardImageFailed", "没能从剪贴板取到图片"), "", null);
                return;
            }

            ModModel.ImageUri = imagePath;
        }).ConfigureAwait(false);
    }

    private bool CanCopyImageToClipboard() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanCopyImageToClipboard))]
    private async Task CopyImageToClipboardAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!File.Exists(ModModel.ImageUri.LocalPath))
                return;

            var file = await StorageFile.GetFileFromPathAsync(ModModel.ImageUri.LocalPath);
            if (file is null)
                return;


            await ImageHandlerService.CopyImageToClipboardAsync(file).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }


    private bool CanClearImage() => DefaultCanExecute && ModModel.ImageUri != ImageHandlerService.StaticPlaceholderImageUri;

    [RelayCommand(CanExecute = nameof(CanClearImage))]
    private void ClearImage()
    {
        if (!IsModLoaded) return;
        ModModel.ImageUri = ImageHandlerService.StaticPlaceholderImageUri;
    }

    private bool CanSaveModSettings() => DefaultCanExecute && ModModel.AnyChanges;

    [RelayCommand(CanExecute = nameof(CanSaveModSettings))]
    private async Task SaveModSettingsAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!CanSaveModSettings()) return;

            var existingModSettings = await _loadedMod!.Mod.Settings.ReadSettingsAsync();

            var updateRequest = new UpdateSettingsRequest();

            if (ModModel.IsImageUriChanged)
                updateRequest.SetImagePath = ModModel.ImageUri;

            if (ModModel.IsModDisplayNameChanged)
                updateRequest.SetCustomName = ModModel.ModDisplayName;

            if (ModModel.IsModUrlChanged)
                updateRequest.SetModUrl = Uri.TryCreate(ModModel.ModUrl, UriKind.Absolute, out var url) ? url : null;

            // 二开：保存备注
            if (ModModel.IsNotesChanged)
                updateRequest.SetDescription = ModModel.Notes;

            Result<ModSettings>? result = null;
            Exception? savingKeySwapException = null;

            await Task.Run(async () =>
            {
                if (updateRequest.AnyUpdates)
                {
                    result = await _modSettingsService.SaveSettingsAsync(_loadedModId.Value, updateRequest).ConfigureAwait(false);
                }

                if (!_loadedMod.Mod.Settings.HasMergedIni && !ModModel.KeySwaps.Any()
                    || _loadedMod.Mod.KeySwaps is null ||
                    !ModModel.IsKeySwapsChanged ||
                    existingModSettings.IgnoreMergedIni)
                    return;

                // TODO: Will need to redo keyswap handling at some point doing a quick solution here
                var keySwapSections = new List<KeySwapSection>();

                foreach (var modModelSkinModKeySwap in ModModel.KeySwaps)
                {
                    var variants = int.TryParse(modModelSkinModKeySwap.VariationsCount, out var variantsCount)
                        ? variantsCount
                        : -1;

                    var keySwapSection = new KeySwapSection()
                    {
                        SectionName = modModelSkinModKeySwap.SectionKey,
                        ForwardKey = modModelSkinModKeySwap.ForwardHotkey,
                        BackwardKey = modModelSkinModKeySwap.BackwardHotkey,
                        Variants = variants == -1 ? null : variants,
                        Type = modModelSkinModKeySwap.Type ?? "Unknown"
                    };

                    keySwapSections.Add(keySwapSection);
                }

                try
                {
                    await _loadedMod.Mod.KeySwaps.SaveKeySwapConfiguration(keySwapSections).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    savingKeySwapException = e;
                    _logger.Error(e, "An error occured trying to save keyswaps for mod {ModPath}", _loadedMod.Mod.FullPath);
                }
            });

            if (result?.Notification is not null && savingKeySwapException is null)
                _notificationService.ShowNotification(result.Notification);

            if (savingKeySwapException is not null)
                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_SaveKeyswapsFailed", "保存按键替换失败"), savingKeySwapException.Message, null);

            _cancellationToken.ThrowIfCancellationRequested();


            Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
            QueueLoadMod(_loadedModId, true);
        }).ConfigureAwait(false);
    }

    private bool CanOpenModFolder() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task OpenModFolderAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded) return;
            await Windows.System.Launcher.LaunchFolderAsync(
                await StorageFolder.GetFolderFromPathAsync(_loadedMod.Mod.FullPath));
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    private void ToggleEditingModName()
    {
        IsEditingModName = !IsEditingModName;
    }

    /// <summary>
    ///     二开：删除当前 Mod（移到回收站，不做永久删除）
    /// </summary>
    [RelayCommand]
    private async Task DeleteModAsync()
    {
        if (_loadedMod is null)
            return;

        var modName = _loadedMod.Mod.Name;
        var modPath = _loadedMod.Mod.FullPath;

        try
        {
            await Task.Run(() =>
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    modPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));

            _notificationService.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DeletedTitle", "已删除 Mod"),
                string.Format(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DeletedBody", "{0} 已移到回收站"),
                    modName),
                TimeSpan.FromSeconds(6));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not delete mod {ModName}", modName);
            _notificationService.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DeleteFailed", "删除失败"), e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：在详情面板里直接启用 / 停用当前 Mod（改文件夹名的 DISABLED_ 前缀）
    /// </summary>
    [RelayCommand]
    private async Task ToggleModEnabledAsync()
    {
        if (_loadedMod is null)
            return;

        var enable = !ModModel.IsEnabled;

        try
        {
            var dir = new DirectoryInfo(_loadedMod.Mod.FullPath.TrimEnd(Path.DirectorySeparatorChar));
            if (!dir.Exists)
            {
                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_OperationFailed", "操作失败"),
                    string.Format(
                        _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_ModFolderNotFound", "找不到 Mod 文件夹：{0}"),
                        dir.FullName),
                    TimeSpan.FromSeconds(6));
                return;
            }

            // 已经是目标状态就直接同步界面
            if (ModFolderHelpers.FolderHasDisabledPrefix(dir.Name) == !enable)
            {
                ModModel.IsEnabled = enable;
                return;
            }

            var newName = enable
                ? ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(dir.Name)
                : ModFolderHelpers.GetFolderNameWithDisabledPrefix(dir.Name);

            var target = Path.Combine(dir.Parent!.FullName, newName);

            if (Directory.Exists(target) || File.Exists(target))
            {
                _notificationService.ShowNotification(
                    enable
                        ? _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_EnableFailed", "启用失败")
                        : _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DisableFailed", "停用失败"),
                    string.Format(
                        _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_FolderExistsBody", "已存在同名文件夹「{0}」"),
                        newName),
                    TimeSpan.FromSeconds(8));
                return;
            }

            // 不能 ConfigureAwait(false)，否则改界面状态会跨线程
            await Task.Run(() => dir.MoveTo(target));

            ModModel.IsEnabled = enable;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not toggle mod enabled state for {ModName}", _loadedMod.Mod.Name);
            _notificationService.ShowNotification(
                enable
                    ? _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_EnableFailed", "启用失败")
                    : _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Pane_DisableFailed", "停用失败"),
                e.Message,
                TimeSpan.FromSeconds(8));
        }
    }

    #endregion

    #region DragAndDropHandlers

    public bool CanSetImageFromDragDropWeb(Uri? url)
    {
        if (!DefaultCanExecute)
            return false;

        if (url is null || !url.IsAbsoluteUri)
            return false;

        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            return false;

        return Constants.SupportedImageExtensions.Contains(Path.GetExtension(url.AbsolutePath));
    }

    public async Task SetImageFromDragDropWeb(Uri uri)
    {
        await CommandWrapper(async () =>
        {
            var image = await _imageHandlerService.DownloadImageAsync(uri, _cancellationToken);
            ModModel.ImageUri = new Uri(image.Path);
        }, true, useDefaultExceptionHandler: true).ConfigureAwait(false);
    }

    public bool CanSetImageFromDragDropStorageItem(IReadOnlyList<IStorageItem> storageItems)
    {
        if (!DefaultCanExecute)
            return false;

        if (storageItems.Count != 1)
            return false;

        var file = storageItems.First();

        if (!Uri.TryCreate(file.Path, UriKind.Absolute, out _))
            return false;

        return Constants.SupportedImageExtensions.Contains(Path.GetExtension(file.Name));
    }

    public async Task SetImageFromDragDropFile(IReadOnlyList<IStorageItem> storageItems)
    {
        await CommandWrapper(() =>
        {
            var file = storageItems.First();

            var filePath = new Uri(file.Path);

            ModModel.ImageUri = filePath;
            return Task.CompletedTask;
        }, true, useDefaultExceptionHandler: true).ConfigureAwait(false);
    }

    #endregion

    private async Task CommandWrapper(Func<Task> command, bool hardBusy = false, Action<Exception>? uncaughtErrorHandler = null,
        bool useDefaultExceptionHandler = false, [CallerMemberName] string commandName = "")
    {
        try
        {
            using var _ = await LockAsync().ConfigureAwait(false);
            using var busy = hardBusy ? BusySetter.StartHardBusy() : BusySetter.StartSoftBusy();

            await command();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (useDefaultExceptionHandler)
            {
                _logger.Error(e, "An error occured while executing command {CommandName}", commandName);
                _notificationService.ShowNotification($"An error occured running command {commandName}", e.Message, null);
                return;
            }

            if (uncaughtErrorHandler is null) throw;

            var ex = ExceptionDispatchInfo.Capture(e);
            uncaughtErrorHandler(ex.SourceException);
        }
    }

    private async Task CommandWrapper(Action command, bool hardBusy = false)
    {
        try
        {
            using var _ = await LockAsync().ConfigureAwait(false);
            using var busy = hardBusy ? BusySetter.StartHardBusy() : BusySetter.StartSoftBusy();
            command();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<IDisposable> LockAsync() =>
        await _loadModLock.LockAsync(cancellationToken: _cancellationToken).ConfigureAwait(false);

    private IRelayCommand[]? _viewModelCommands;

    private void NotifyAllCommands()
    {
        if (_viewModelCommands is null)
        {
            var commands = new List<IRelayCommand>();
            foreach (var propertyInfo in GetType()
                         .GetProperties()
                         .Where(p => p.PropertyType.IsAssignableTo(typeof(IRelayCommand))))
            {
                var value = propertyInfo.GetValue(this);

                if (value is IRelayCommand relayCommand)
                    commands.Add(relayCommand);
            }

            _viewModelCommands = commands.ToArray();
        }

        _viewModelCommands.ForEach(c => c.NotifyCanExecuteChanged());
    }
}

public partial class ModPaneFieldsVm : ObservableObject
{
    public bool IsLoaded { get; private init; }
    public ModPaneFieldsVm? UnchangedValue { get; private init; }

    // 二开：本类由 new 创建，不经 DI，统一在这里取 localizer 处理兜底文案
    private static string UnknownFallback =>
        App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/CharacterDetails/Pane_Unknown", "未知") ?? "未知";

    /// <summary>
    ///     二开：UI 线程队列，由 ModPaneVM 注入
    /// </summary>
    public DispatcherQueue? UiDispatcher { get; set; }

    /// <summary>
    ///     二开：兜底。属性变更通知一律在 UI 线程发出，避免后台线程刷新时把进程搞崩
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        var queue = UiDispatcher;

        if (queue is not null && !queue.HasThreadAccess)
        {
            var name = e.PropertyName;
            queue.TryEnqueue(() => base.OnPropertyChanged(new PropertyChangedEventArgs(name)));
            return;
        }

        base.OnPropertyChanged(e);
    }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private Uri _imageUri = ImageHandlerService.StaticPlaceholderImageUri;
    public bool IsImageUriChanged => ImageUri != UnchangedValue?.ImageUri;
    [ObservableProperty] private string _modDisplayName = string.Empty;
    public bool IsModDisplayNameChanged => ModDisplayName != UnchangedValue?.ModDisplayName;
    [ObservableProperty] private string _modUrl = string.Empty;
    public bool IsModUrlChanged => ModUrl != UnchangedValue?.ModUrl;

    /// <summary>
    ///     二开：Mod 备注，存到 Mod 设置的 Description 里
    /// </summary>
    [ObservableProperty] private string? _notes;

    public bool IsNotesChanged => Notes != UnchangedValue?.Notes;

    /// <summary>
    ///     二开：所属角色的头像，显示在 Mod 大图下方
    /// </summary>
    [ObservableProperty] private Uri? _characterImageUri;

    /// <summary>
    ///     二开：所属角色名字
    /// </summary>
    [ObservableProperty] private string _characterDisplayName = string.Empty;

    [ObservableProperty] private string? _modIniPath = null;
    public bool IsModIniPathChanged => ModIniPath != UnchangedValue?.ModIniPath;
    [ObservableProperty] private bool _ignoreMergedIni = true;
    public bool IsIgnoreMergedIniChanged => IgnoreMergedIni != UnchangedValue?.IgnoreMergedIni;

    // 二开：改用线程安全集合
    public WinUIHelpers.UiObservableCollection<ModPaneFieldsKeySwapVm> KeySwaps { get; } = new();
    public bool IsKeySwapsChanged => AnyKeySwapChanges();

    public string IsKeySwapManagementEnabled => (!IgnoreMergedIni).ToString().ToLower();

    private ModPaneFieldsVm(CharacterSkinEntry modEntry, ModSettings modSettings, IEnumerable<KeySwapSection> keySwaps)
    {
        IsEnabled = modEntry.IsEnabled;
        ImageUri = modSettings.ImagePath ?? ImageHandlerService.StaticPlaceholderImageUri;
        ModDisplayName = modEntry.Mod.GetDisplayName();
        ModUrl = modSettings.ModUrl?.ToString() ?? "";
        // 二开：备注
        Notes = modSettings.Description;

        // 二开：所属角色的头像和名字
        CharacterImageUri = (modEntry.ModList.Character as IImageSupport)?.ImageUri;
        CharacterDisplayName = modEntry.ModList.Character.DisplayName;
        ModIniPath = modSettings.MergedIniPath?.ToString();
        IgnoreMergedIni = modSettings.IgnoreMergedIni;

        foreach (var keySwap in keySwaps)
        {
            KeySwaps.Add(new ModPaneFieldsKeySwapVm()
            {
                ForwardHotkey = keySwap.ForwardKey,
                BackwardHotkey = keySwap.BackwardKey,
                ForwardKeyDisplay = keySwap.ForwardKeyDisplay,
                BackwardKeyDisplay = keySwap.BackwardKeyDisplay,
                PrimaryLabel = keySwap.PrimaryLabel,
                ForwardKeyTokens = keySwap.ForwardKeyTokens,
                BackwardKeyTokens = keySwap.BackwardKeyTokens,
                HasBackwardKey = keySwap.HasBackwardKey,
                SectionKey = keySwap.SectionName,
                // 二开：显示美化后的部位名、说明，并标出菜单段
                DisplayName = keySwap.DisplayName,
                Description = keySwap.Description,
                IsMenu = keySwap.IsMenu,
                Type = keySwap.Type,
                VariationsCount = keySwap.Variants?.ToString() ?? UnknownFallback
            });

            KeySwaps.Last().PropertyChanged += (_, e) => { OnPropertyChanged(nameof(KeySwaps)); };
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AnyChanges))
                OnPropertyChanged(nameof(AnyChanges));
        };
    }

    public ModPaneFieldsVm()
    {
    }

    public static ModPaneFieldsVm FromModEntry(CharacterSkinEntry modEntry, ModSettings modSettings, ICollection<KeySwapSection> keySwaps)
    {
        return new ModPaneFieldsVm(modEntry, modSettings, keySwaps)
        {
            UnchangedValue = new ModPaneFieldsVm(modEntry, modSettings, keySwaps),
            IsLoaded = true
        };
    }

    public bool AnyChanges
    {
        get
        {
            var anyChanges = false;

            if (UnchangedValue is null)
                return false;

            anyChanges |= IsEnabled != UnchangedValue.IsEnabled;
            anyChanges |= ImageUri != UnchangedValue.ImageUri;
            anyChanges |= ModDisplayName != UnchangedValue.ModDisplayName;
            anyChanges |= ModUrl != UnchangedValue.ModUrl;
            anyChanges |= ModIniPath != UnchangedValue.ModIniPath;
            anyChanges |= IgnoreMergedIni != UnchangedValue.IgnoreMergedIni;

            if (KeySwaps.Count != UnchangedValue.KeySwaps.Count)
                return true;

            if (anyChanges)
                return true;

            anyChanges |= AnyKeySwapChanges();

            return anyChanges;
        }
    }

    private bool AnyKeySwapChanges()
    {
        var anyChanges = false;

        if (UnchangedValue is null)
            return false;

        if (UnchangedValue.IgnoreMergedIni)
            return false;

        if (KeySwaps.Count != UnchangedValue.KeySwaps.Count)
            return true;

        for (var i = 0; i < KeySwaps.Count; i++)
        {
            var oldKeySwap = UnchangedValue.KeySwaps[i];
            var newKeySwap = KeySwaps[i];

            anyChanges |= (oldKeySwap.ForwardHotkey ?? "") != (newKeySwap.ForwardHotkey ?? "");
            anyChanges |= (oldKeySwap.BackwardHotkey ?? "") != (newKeySwap.BackwardHotkey ?? "");
        }

        return anyChanges;
    }
}

[DebuggerDisplay("Section: {_sectionKey} - {_forwardHotkey} - {_backwardHotkey}")]
public partial class ModPaneFieldsKeySwapVm : ObservableObject
{
    // 二开：本类由 new 创建，不经 DI，统一在这里取 localizer 处理兜底文案
    private static string UnknownFallback =>
        App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/CharacterDetails/Pane_Unknown", "未知") ?? "未知";

    [ObservableProperty] private string _sectionKey = string.Empty;

    /// <summary>
    ///     二开：美化后的显示名（[KeyHair] → 头发）
    /// </summary>
    [ObservableProperty] private string _displayName = UnknownFallback;

    /// <summary>
    ///     二开：ini 注释里写的按键说明
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    /// <summary>
    ///     二开：是不是菜单段
    /// </summary>
    [ObservableProperty] private bool _isMenu;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [ObservableProperty] private string? _condition;
    [ObservableProperty] private string? _forwardHotkey;
    [ObservableProperty] private string? _backwardHotkey;

    /// <summary>
    ///     二开：美化后的按键显示（Alt + ←）
    /// </summary>
    [ObservableProperty] private string? _forwardKeyDisplay;

    [ObservableProperty] private string? _backwardKeyDisplay;

    /// <summary>
    ///     二开：主标签，优先显示变量名（switching / swapvar）
    /// </summary>
    [ObservableProperty] private string _primaryLabel = UnknownFallback;

    /// <summary>
    ///     二开：拆开后的键帽
    /// </summary>
    [ObservableProperty] private IReadOnlyList<string> _forwardKeyTokens = Array.Empty<string>();

    [ObservableProperty] private IReadOnlyList<string> _backwardKeyTokens = Array.Empty<string>();

    /// <summary>
    ///     二开：是否有后退键
    /// </summary>
    [ObservableProperty] private bool _hasBackwardKey;

    /// <summary>
    ///     二开：是否处于编辑状态（显示原始输入框）
    /// </summary>
    [ObservableProperty] private bool _isEditingKeys;
    [ObservableProperty] private string? _type;
    [ObservableProperty] private string _variationsCount = UnknownFallback;
}