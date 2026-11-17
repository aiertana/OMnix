using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.UI.Controls;
using OMniX.WinUI.Models.Options;
using OMniX.WinUI.Services.ModHandling;

namespace OMniX.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel
{
    [ObservableProperty] private bool _isSingleSelectEnabled;
    [ObservableProperty] private bool _isModFolderNameColumnVisible;
    [ObservableProperty] private bool _isSingleModSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddModArchiveCommand), nameof(AddModFolderCommand),
        nameof(InstallModsArchiveCommand), nameof(InstallModsFolderCommand))]
    private bool _isAddingModFolder;


    private async Task InitToolbarAsync()
    {
        var settings = await ReadSettingsAsync();
        IsSingleSelectEnabled = settings.SingleSelect;
        ModGridVM.GridSelectionMode = IsSingleSelectEnabled ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended;
        IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;
    }

    [RelayCommand]
    private async Task OpenGIMIRootFolderAsync()
    {
        var options = await _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section) ??
                      new ModManagerOptions();
        if (string.IsNullOrWhiteSpace(options.GimiRootFolderPath)) return;
        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(options.GimiRootFolderPath));
    }

    [RelayCommand]
    private async Task OpenCharacterFolderAsync()
    {
        var directoryToOpen = new DirectoryInfo(_modList.AbsModsFolderPath);
        if (!directoryToOpen.Exists)
        {
            _modList.InstantiateCharacterFolder();
            directoryToOpen.Refresh();

            if (!directoryToOpen.Exists)
            {
                var parentDir = directoryToOpen.Parent;

                if (parentDir is null)
                {
                    _logger.Error("Could not find parent directory of {Directory}", directoryToOpen.FullName);
                    return;
                }

                directoryToOpen = parentDir;
            }
        }

        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(directoryToOpen.FullName));
    }


    [RelayCommand]
    private async Task OpenModFolderAsync()
    {
        if (ModGridVM.SelectedMods.Count != 1) return;

        var mod = ModGridVM.SelectedMods.First();
        var directoryToOpen = new DirectoryInfo(mod.AbsFolderPath);
        if (!directoryToOpen.Exists)
        {
            _logger.Error("Could not find directory {Directory}", directoryToOpen.FullName);
            return;
        }

        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(directoryToOpen.FullName));
    }

    [RelayCommand(CanExecute = nameof(IsNotHardBusy))]
    private async Task RefreshAllModsAsync()
    {
        await CommandWrapperAsync(true,
            () => ModGridVM.ReloadAllModsAsync(minimumWaitTime: TimeSpan.FromMilliseconds(300))).ConfigureAwait(false);
    }


    [RelayCommand(CanExecute = nameof(IsNotHardBusy))]
    private async Task ToggleSingleSelectAsync()
    {
        await CommandWrapperAsync(false, async () =>
        {
            var settings = await ReadSettingsAsync();
            settings.SingleSelect = !IsSingleSelectEnabled;
            await SaveSettingsAsync(settings);

            IsSingleSelectEnabled = settings.SingleSelect;

            var firstModSelected = ModGridVM.SelectedMods.FirstOrDefault();
            ModGridVM.GridSelectionMode = IsSingleSelectEnabled ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended;
            if (firstModSelected is not null)
                ModGridVM.SetSelectedMod(firstModSelected.Id);
        });
    }


    [RelayCommand]
    private async Task ToggleHideModFolderColumnAsync()
    {
        await CommandWrapperAsync(false, async () =>
        {
            var settings = await ReadSettingsAsync();
            settings.ModFolderNameColumnVisible = !IsModFolderNameColumnVisible;
            await SaveSettingsAsync(settings);

            IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;
            ModGridVM.IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;
        });
    }


    private bool CanAddModFolder() => !IsAddingModFolder && IsNotHardBusy;

    [RelayCommand(CanExecute = nameof(CanAddModFolder))]
    private async Task AddModFolder()
    {
        await CommandWrapperAsync(true, async () =>
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                _logger.Debug("User cancelled folder picker.");
                return;
            }

            try
            {
                IsAddingModFolder = true;
                var result = await Task.Run(async () =>
                {
                    var installMonitor = await _modDragAndDropService.AddStorageItemFoldersAsync(_modList,
                        new ReadOnlyCollection<IStorageItem>([folder])).ConfigureAwait(false);

                    if (installMonitor is not null)
                        return await installMonitor.WaitForCloseAsync().ConfigureAwait(false);
                    return null;
                }, CancellationToken.None);


                if (result?.CloseReason == CloseRequestedArgs.CloseReasons.Success)
                    await ModGridVM.ReloadAllModsAsync();
            }
            finally
            {
                IsAddingModFolder = false;
            }
        });
    }

    private bool CanAddModArchive() => !IsAddingModFolder && IsNotHardBusy;

    [RelayCommand(CanExecute = nameof(CanAddModArchive))]
    private async Task AddModArchiveAsync()
    {
        await CommandWrapperAsync(true, async () =>
        {
            var filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(".zip");
            filePicker.FileTypeFilter.Add(".rar");
            filePicker.FileTypeFilter.Add(".7z");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
            var file = await filePicker.PickSingleFileAsync();
            if (file is null)
            {
                _logger.Debug("User cancelled file picker.");
                return;
            }

            try
            {
                IsAddingModFolder = true;
                var result = await Task.Run(async () =>
                    {
                        var installMonitor = await _modDragAndDropService.AddStorageItemFoldersAsync(_modList, [file]).ConfigureAwait(false);

                        if (installMonitor is not null)
                            return await installMonitor.WaitForCloseAsync().ConfigureAwait(false);
                        return null;
                    },
                    CancellationToken.None);

                if (result?.CloseReason == CloseRequestedArgs.CloseReasons.Success)
                    await ModGridVM.ReloadAllModsAsync();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error while adding archive.");
                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Toolbar_AddFileErrorTitle", "添加文件时出错"),
                    $"An error occurred while adding the storage items.\n{e.Message}",
                    TimeSpan.FromSeconds(5));
            }
            finally
            {
                IsAddingModFolder = false;
            }
        });
    }

    /// <summary>
    ///     二开：「添加 Mod」按钮（进度小窗路线）—— 选多个压缩包/文件夹，
    ///     全部按默认设置静默安装，只弹一个进度小窗，不逐个弹「安装 Mod」窗口。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddModArchive))]
    private async Task InstallModsArchiveAsync()
    {
        await CommandWrapperAsync(true, async () =>
        {
            var filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(".zip");
            filePicker.FileTypeFilter.Add(".rar");
            filePicker.FileTypeFilter.Add(".7z");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
            var files = await filePicker.PickMultipleFilesAsync();
            if (files is null || files.Count == 0)
            {
                _logger.Debug("User cancelled multi file picker.");
                return;
            }

            await BatchInstallProgress.RunAsync(
                _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Toolbar_BatchTitle", "批量添加 Mod"),
                new ReadOnlyCollection<IStorageItem>(new List<IStorageItem>(files)), _modList, _modDragAndDropService);

            if (ModGridVM.ModdableObjectHasAnyMods)
                await ModGridVM.ReloadAllModsAsync();
        });
    }

    /// <summary>
    ///     二开：「添加 Mod」按钮的文件夹路线 —— 选一个 Mod 文件夹按默认设置静默安装。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddModFolder))]
    private async Task InstallModsFolderAsync()
    {
        await CommandWrapperAsync(true, async () =>
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                _logger.Debug("User cancelled folder picker.");
                return;
            }

            await BatchInstallProgress.RunAsync(
                _localizer.GetLocalizedStringOrDefault("/CharacterDetails/Toolbar_BatchTitle", "批量添加 Mod"),
                new ReadOnlyCollection<IStorageItem>([folder]), _modList, _modDragAndDropService);

            if (ModGridVM.ModdableObjectHasAnyMods)
                await ModGridVM.ReloadAllModsAsync();
        });
    }
}