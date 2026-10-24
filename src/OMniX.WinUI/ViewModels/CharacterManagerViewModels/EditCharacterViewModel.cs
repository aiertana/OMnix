using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.GamesService.Requests;
using OMniX.Core.Helpers;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Services.Notifications;
using OMniX.WinUI.ViewModels.CharacterManagerViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Serilog;

namespace OMniX.WinUI.ViewModels;

public partial class EditCharacterViewModel : ObservableRecipient, INavigationAware
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILogger _logger;
    private readonly NotificationManager _notificationManager;
    private readonly ImageHandlerService _imageHandlerService;
    private readonly INavigationService _navigationService;
    private readonly IWindowManagerService _windowManagerService;

    private ICharacter _character = null!;


    [ObservableProperty] private Uri _modFolderUri = null!;
    [ObservableProperty] private string _modFolderString = "";
    [ObservableProperty] private string _modsCount;
    [ObservableProperty] private string _keyToAddInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCustomCharacter))]
    private bool _isCustomCharacter;

    public bool IsNotCustomCharacter => !IsCustomCharacter;

    public CharacterStatus CharacterStatus { get; } = new();

    public EditCharacterForm Form { get; } = new();

    public ObservableCollection<ModModel> Mods { get; } = new();


    public EditCharacterViewModel(IGameService gameService, ILogger logger, ISkinManagerService skinManagerService,
        ImageHandlerService imageHandlerService, NotificationManager notificationManager, INavigationService navigationService,
        IWindowManagerService windowManagerService)
    {
        _gameService = gameService;
        _logger = logger.ForContext<EditCharacterViewModel>();
        _skinManagerService = skinManagerService;
        _imageHandlerService = imageHandlerService;
        _notificationManager = notificationManager;
        _navigationService = navigationService;
        _windowManagerService = windowManagerService;
        Form.PropertyChanged += NotifyAllCommands;
    }


    public void OnNavigatedTo(object parameter)
    {
        if (parameter is not string internalName)
        {
            _logger.Error($"Invalid parameter type, {parameter}");
            internalName = _gameService.GetCharacters().First().InternalName;
        }

        if (parameter is InternalName id)
            internalName = id;


        var character = _gameService.GetCharacterByIdentifier(internalName, true);

        if (character is null)
        {
            _logger.Error($"Invalid character identifier, {internalName}");
            character = _gameService.GetCharacters().First();
        }

        _character = character;
        IsCustomCharacter = _character.IsCustomModObject;
        CharacterStatus.SetIsCustomCharacter(_character.IsCustomModObject);

        if (!_gameService.GetDisabledCharacters().Contains(character))
        {
            CharacterStatus.SetEnabled(true);
            var modList = _skinManagerService.GetCharacterModList(_character);
            ModFolderUri = new Uri(modList.AbsModsFolderPath);
            ModFolderString = ModFolderUri.LocalPath;
            ModsCount = modList.Mods.Count.ToString();
            // 二开：每行带上「修复 / 重命名 / 打开文件夹 / 删除」四个操作；已启用的排在最前面
            Mods.AddRange(modList.Mods
                .OrderByDescending(entry => entry.IsEnabled)
                .Select(entry => ModModel.FromEntry(entry, OpenModFolderCommand, DeleteModCommand,
                    RepairModCommand)));
        }
        else
        {
            CharacterStatus.SetEnabled(false);
            ModFolderUri = new Uri(_skinManagerService.ActiveModsFolderPath);
            ModFolderString = ModFolderUri.LocalPath;
            ModsCount = "-";
        }


        var allModObjects = _gameService.GetAllModdableObjects(GetOnly.Both);

        Form.Initialize(character, allModObjects);
        NotifyAllCommands();
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void AddKey()
    {
        KeyToAddInput = KeyToAddInput.Trim().ToLowerInvariant();
        var existingKeys = Form.Keys.Items.ToList();

        if (string.IsNullOrWhiteSpace(KeyToAddInput))
            return;

        if (existingKeys.Contains(KeyToAddInput, StringComparer.OrdinalIgnoreCase))
            return;

        Form.Keys.Items.Add(KeyToAddInput);
        KeyToAddInput = string.Empty;
        NotifyAllCommands();
    }

    [RelayCommand]
    private void RemoveKey(string key)
    {
        Form.Keys.Items.Remove(key);
        NotifyAllCommands();
    }

    #region Mod 行操作（重命名 / 打开文件夹 / 删除）

    /// <summary>
    ///     二开：进入就地改名状态（不弹窗，直接在列表里编辑名字）。
    /// </summary>
    [RelayCommand]
    private void RenameMod(ModModel? modModel) => modModel?.BeginEdit();

    /// <summary>
    ///     二开：点圆点切换启用 / 禁用，切换后按「已启用优先」重排。
    /// </summary>
    public void ToggleMod(ModModel? modModel)
    {
        if (modModel is null)
            return;

        try
        {
            var modList = _skinManagerService.GetCharacterModList(_character);

            modModel.IsEnabled = modList.ToggleMod(modModel.ModId);

            ReorderMods();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to toggle mod {ModId}", modModel.ModId);
            _notificationManager.ShowNotification("切换启用状态失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：已启用的排前面，禁用之后自动沉下去。
    /// </summary>
    private void ReorderMods()
    {
        var ordered = Mods.OrderByDescending(mod => mod.IsEnabled).ToList();

        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var currentIndex = Mods.IndexOf(ordered[targetIndex]);

            if (currentIndex != targetIndex)
                Mods.Move(currentIndex, targetIndex);
        }
    }

    /// <summary>
    ///     二开：用 Wuwa_Mod_Fixer 修这个 Mod。启动前把修复器的「MOD 目录」预填成它的路径。
    /// </summary>
    [RelayCommand]
    private async Task RepairMod(ModModel? modModel)
    {
        if (modModel is null)
            return;

        var service = ModFixerService.Instance;

        var release = service.Latest ?? await service.GetLatestReleaseAsync();

        if (release is null)
        {
            _notificationManager.ShowNotification("连不上修复器",
                "访问 GitHub 失败，可以先到项目主页手动下载 Wuwa_Mod_Fixer", TimeSpan.FromSeconds(8));
            return;
        }

        try
        {
            if (!service.IsDownloaded(release))
                _notificationManager.ShowNotification("正在下载修复器",
                    $"{release.Tag}（{release.SizeText}），第一次会慢一点", TimeSpan.FromSeconds(6));

            await service.LaunchForModAsync(modModel.FullPath);

            _notificationManager.ShowNotification("已打开修复器",
                $"MOD 目录已填成「{modModel.DisplayName}」", TimeSpan.FromSeconds(6));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to launch mod fixer for {Path}", modModel.FullPath);
            _notificationManager.ShowNotification("打开修复器失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：提交就地改名（回车或失焦时调用）。
    /// </summary>
    public Task CommitModRenameAsync(ModModel? modModel)
    {
        if (modModel is null || !modModel.IsEditing)
            return Task.CompletedTask;

        modModel.EndEdit();

        var newName = modModel.EditName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(newName) ||
            string.Equals(newName, modModel.DisplayName, StringComparison.Ordinal))
        {
            modModel.EditName = modModel.DisplayName;
            return Task.CompletedTask;
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _notificationManager.ShowNotification("名称里有不允许的字符", "换一个名字试试", TimeSpan.FromSeconds(5));
            modModel.EditName = modModel.DisplayName;
            return Task.CompletedTask;
        }

        var parent = Path.GetDirectoryName(modModel.FullPath);

        if (string.IsNullOrEmpty(parent))
            return Task.CompletedTask;

        var targetPath = Path.Combine(parent, newName);

        if (Directory.Exists(targetPath))
        {
            _notificationManager.ShowNotification("名字重复", $"「{newName}」已经存在了", TimeSpan.FromSeconds(5));
            modModel.EditName = modModel.DisplayName;
            return Task.CompletedTask;
        }

        try
        {
            Directory.Move(modModel.FullPath, targetPath);

            modModel.DisplayName = newName;
            modModel.FullPath = targetPath;

            _notificationManager.ShowNotification("已重命名", $"「{newName}」", TimeSpan.FromSeconds(4));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to rename mod folder {Path}", modModel.FullPath);
            _notificationManager.ShowNotification("重命名失败", e.Message, TimeSpan.FromSeconds(8));
            modModel.EditName = modModel.DisplayName;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     二开：取消就地改名（Esc）。
    /// </summary>
    public void CancelModRename(ModModel? modModel)
    {
        if (modModel is null)
            return;

        modModel.EditName = modModel.DisplayName;
        modModel.EndEdit();
    }

    /// <summary>
    ///     二开：在资源管理器里打开这个 Mod 的文件夹。
    /// </summary>
    [RelayCommand]
    private async Task OpenModFolder(ModModel? modModel)
    {
        if (modModel is null)
            return;

        try
        {
            if (!Directory.Exists(modModel.FullPath))
            {
                _notificationManager.ShowNotification("文件夹不存在", modModel.FullPath, TimeSpan.FromSeconds(6));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = modModel.FullPath,
                UseShellExecute = true
            });

            await Task.CompletedTask;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to open mod folder {Path}", modModel.FullPath);
            _notificationManager.ShowNotification("打开文件夹失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：从磁盘删除这个 Mod（先弹窗二次确认）。
    /// </summary>
    [RelayCommand]
    private async Task DeleteMod(ModModel? modModel)
    {
        if (modModel is null)
            return;

        var dialog = new ContentDialog
        {
            Title = "删除 Mod",
            Content = $"确定要删除「{modModel.DisplayName}」吗？\n\n" +
                      $"整个文件夹会从磁盘上删除：\n{modModel.FullPath}\n\n" +
                      "这个操作无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _windowManagerService.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
            return;

        try
        {
            if (Directory.Exists(modModel.FullPath))
                Directory.Delete(modModel.FullPath, true);

            Mods.Remove(modModel);
            ModsCount = Mods.Count.ToString();

            _notificationManager.ShowNotification("Mod 已删除",
                $"「{modModel.DisplayName}」已从磁盘删除", TimeSpan.FromSeconds(5));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to delete mod folder {Path}", modModel.FullPath);
            _notificationManager.ShowNotification("删除失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    #endregion


    #region ImageCommands

    [RelayCommand]
    private async Task PickImage()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: false);

        if (image is null || !File.Exists(image.Path))
            return;

        Form.Image.Value = new Uri(image.Path);
    }

    [RelayCommand]
    private async Task PasteImageAsync()
    {
        try
        {
            var image = await _imageHandlerService.GetImageFromClipboardAsync();
            if (image is not null)
                Form.Image.Value = image;
            else
                _notificationManager.ShowNotification("粘贴图片失败", "剪贴板里没有图片", null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to paste image");
            _notificationManager.ShowNotification("粘贴图片失败", ex.Message, null);
        }
    }

    [RelayCommand]
    private void ClearImage() => Form.Image.Value = _character.DefaultCharacter.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri;

    [RelayCommand]
    private async Task SelectImageAsync()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: false);
        if (image is not null && Uri.TryCreate(image.Path, UriKind.Absolute, out var imagePath))
            Form.Image.Value = imagePath;
    }

    #endregion

    private bool CanDisableCharacter() => !_character.IsCustomModObject && CharacterStatus.IsEnabled && !AnyChanges();

    [RelayCommand(CanExecute = nameof(CanDisableCharacter))]
    private async Task DisableCharacter()
    {
        var deleteFolderCheckBox = new CheckBox()
        {
            Content = "同时删除这个角色的文件夹和里面的 Mod？\n" +
                      "文件会被永久删除，无法找回！",
            IsChecked = false
        };

        var dialogContent = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text =
                        "确定要禁用这个角色吗？" +
                        "禁用不会删除角色，但 OMniX 将不再识别它。" +
                        "之后可以随时重新启用。" +
                        "点「确定」会立即执行。",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                deleteFolderCheckBox
            }
        };


        var disableDialog = new ContentDialog
        {
            Title = "禁用角色",
            Content = dialogContent,
            PrimaryButtonText = "确定禁用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await disableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        _logger.Information(
            $"Disabling character {_character.InternalName} with deleteFolder={deleteFolderCheckBox.IsChecked}");
        var deleteFolder = deleteFolderCheckBox.IsChecked ?? false;
        await Task.Run(async () =>
        {
            await _gameService.DisableCharacterAsync(_character).ConfigureAwait(false);
            await _skinManagerService.DisableModListAsync(_character, deleteFolder).ConfigureAwait(false);
        });
        ResetState();
    }


    private bool CanDeleteCustomCharacter() => _character.IsCustomModObject && CharacterStatus.IsEnabled && !Form.AnyFieldDirty;

    [RelayCommand(CanExecute = nameof(CanDeleteCustomCharacter))]
    private async Task DeleteCustomCharacterAsync()
    {
        var deleteFolderCheckBox = new CheckBox()
        {
            Content = "同时删除这个自定义角色的文件夹和里面的 Mod？\n" +
                      "文件会被永久删除，无法找回！",
            IsChecked = false
        };

        var dialogContent = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text =
                        "确定要删除这个自定义角色吗？" +
                        "角色会被移除，OMniX 将不再识别它。" +
                        "点「确定」会立即执行。",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                deleteFolderCheckBox
            }
        };


        var disableDialog = new ContentDialog
        {
            Title = "删除自定义角色",
            Content = dialogContent,
            PrimaryButtonText = "确定删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await disableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;


        _logger.Information($"Deleting custom character {_character.InternalName}");
        var deleteFolder = deleteFolderCheckBox.IsChecked ?? false;
        await Task.Run(async () =>
        {
            await _gameService.DeleteCustomCharacterAsync(_character.InternalName).ConfigureAwait(false);
            await _skinManagerService.DisableModListAsync(_character, deleteFolder).ConfigureAwait(false);
        });

        _navigationService.NavigateTo(typeof(CharacterManagerViewModel).FullName!, new object(), clearNavigation: true);
        _notificationManager.ShowNotification("自定义角色已删除", $"「{_character.DisplayName}」已删除", null);
    }

    private bool CanEnableCharacter() => !_character.IsCustomModObject && CharacterStatus.IsDisabled && !AnyChanges();

    [RelayCommand(CanExecute = nameof(CanEnableCharacter))]
    private async Task EnableCharacter()
    {
        var dialogContent = new TextBlock()
        {
            Text =
                "确定要启用这个角色吗？" +
                "点「确定」会立即执行。",
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var enableDialog = new ContentDialog
        {
            Title = "启用角色",
            Content = dialogContent,
            PrimaryButtonText = "确定启用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await enableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        _logger.Information($"Enabling character {_character.InternalName}");

        await _gameService.EnableCharacterAsync(_character);
        await _skinManagerService.EnableModListAsync(_character);
        ResetState();
    }

    [RelayCommand]
    private void GoToCharacter()
    {
        _navigationService.NavigateToCharacterDetails(_character.InternalName);
    }

    private bool CanSaveChanges() => Form.AnyFieldDirty && Form.IsValid;

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private void DummySave()
    {
    }

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private async Task SaveChangesAsync()
    {
        _logger.Debug($"Saving changes to character {_character.InternalName}");
        Form.ValidateAllFields();

        if (!AnyChanges() || !Form.IsValid)
            return;

        try
        {
            await InternalSaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to save changes to character");
            _notificationManager.ShowNotification("保存角色失败", e.Message, null);
            return;
        }


        ResetState();

        return;

        async Task InternalSaveChangesAsync()
        {
            if (_character.IsCustomModObject)
            {
                var editRequest = new EditCustomCharacterRequest();

                if (Form.DisplayName.IsDirty)
                {
                    Debug.Assert(Form.DisplayName.IsValid);
                    editRequest.DisplayName = NewValue<string>.Set(Form.DisplayName.Value.Trim());
                }

                if (Form.IsMultiMod.IsDirty)
                {
                    Debug.Assert(Form.IsMultiMod.IsValid);
                    editRequest.IsMultiMod = NewValue<bool>.Set(Form.IsMultiMod.Value);
                }

                if (Form.Image.IsDirty)
                {
                    Debug.Assert(Form.Image.IsValid);
                    var image = Form.Image.Value == ImageHandlerService.StaticPlaceholderImageUri ? null : Form.Image.Value;
                    editRequest.Image = NewValue<Uri?>.Set(image);
                }

                if (Form.Keys.IsDirty)
                {
                    Debug.Assert(Form.Keys.IsValid);
                    editRequest.Keys = NewValue<string[]>.Set(Form.Keys.Items.ToArray());
                }


                Debug.Assert(editRequest.AnyValuesSet);
                await Task.Run(() => _gameService.EditCustomCharacterAsync(_character.InternalName, editRequest)).ConfigureAwait(false);
            }
            else
            {
                var overrideRequest = new OverrideCharacterRequest();

                if (Form.DisplayName.IsDirty)
                {
                    Debug.Assert(Form.DisplayName.IsValid);
                    overrideRequest.DisplayName = NewValue<string>.Set(Form.DisplayName.Value.Trim());
                }

                if (Form.IsMultiMod.IsDirty)
                {
                    Debug.Assert(Form.IsMultiMod.IsValid);
                    overrideRequest.IsMultiMod = NewValue<bool>.Set(Form.IsMultiMod.Value);
                }

                if (Form.Image.IsDirty)
                {
                    Debug.Assert(Form.Image.IsValid);
                    var image = Form.Image.Value == ImageHandlerService.StaticPlaceholderImageUri
                                || Form.Image.Value == _character.DefaultCharacter.ImageUri
                        ? null
                        : Form.Image.Value;
                    overrideRequest.Image = NewValue<Uri?>.Set(image);
                }

                if (Form.Keys.IsDirty)
                {
                    Debug.Assert(Form.Keys.IsValid);
                    overrideRequest.Keys = NewValue<string[]>.Set(Form.Keys.Items.ToArray());
                }

                Debug.Assert(overrideRequest.AnyValuesSet);
                await Task.Run(() => _gameService.SetCharacterOverrideAsync(_character, overrideRequest)).ConfigureAwait(false);
            }
        }
    }


    private bool CanRevertChanges() => AnyChanges();

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private void RevertChanges()
    {
        _logger.Debug($"Reverting draft changes to character {_character.InternalName}");
        ResetState();
    }

    private bool CanResetCharacterToDefault() => !_character.IsCustomModObject;

    [RelayCommand(CanExecute = nameof(CanResetCharacterToDefault))]
    private async Task ResetCharacterToDefaultAsync()
    {
        _logger.Information($"Resetting character {_character.InternalName} to default");
        await _gameService.ResetOverrideForCharacterAsync(_character);
        ResetState();
    }

    private void ResetState()
    {
        Mods.Clear();
        OnNavigatedTo(_character.InternalName.Id);
        NotifyAllCommands();
    }

    private bool AnyChanges() => Form.AnyFieldDirty;

    private void NotifyAllCommands(object? sender = null, PropertyChangedEventArgs? propertyChangedEventArgs = null)
    {
        DummySaveCommand.NotifyCanExecuteChanged();
        DisableCharacterCommand.NotifyCanExecuteChanged();
        EnableCharacterCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        RevertChangesCommand.NotifyCanExecuteChanged();
        ResetCharacterToDefaultCommand.NotifyCanExecuteChanged();
        DeleteCustomCharacterCommand.NotifyCanExecuteChanged();
    }


    [RelayCommand]
    private async Task ShowCharacterModelAsync()
    {
        var json = JsonConvert.SerializeObject(_character, Formatting.Indented);


        var content = new ScrollViewer()
        {
            Content = new TextBlock()
            {
                Text = json,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(4)
            }
        };


        var dialogHeight = App.MainWindow.Height * 0.5;
        var dialogWidth = App.MainWindow.Width * 0.7;
        var contentWrapper = new Grid()
        {
            MinHeight = dialogHeight,
            MinWidth = dialogWidth,
            Children =
            {
                content
            }
        };

        var characterModelDialog = new ContentDialog
        {
            Title = "角色数据",
            Content = contentWrapper,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Resources =
            {
                ["ContentDialogMaxWidth"] = 8000,
                ["ContentDialogMaxHeight"] = 4000
            }
        };

        await characterModelDialog.ShowAsync();
    }
}

public class CharacterStatus : ObservableObject
{
    private bool _isCustomCharacter;
    public bool IsCustomCharacter => _isCustomCharacter;
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    private bool _isDisabled;

    public bool IsDisabled => _isDisabled;

    public bool IsEnabledAndNotCustomCharacter => IsEnabled && !IsCustomCharacter;

    public bool IsDisabledAndNotCustomCharacter => IsDisabled && !IsCustomCharacter;


    public void SetIsCustomCharacter(bool isCustomCharacter)
    {
        SetProperty(ref _isCustomCharacter, isCustomCharacter, nameof(IsCustomCharacter));
        OnPropertyChanged(nameof(IsDisabledAndNotCustomCharacter));
        OnPropertyChanged(nameof(IsEnabledAndNotCustomCharacter));
    }

    public void SetEnabled(bool enabled)
    {
        SetProperty(ref _isEnabled, enabled, nameof(IsEnabled));
        SetProperty(ref _isDisabled, !enabled, nameof(IsDisabled));
        OnPropertyChanged(nameof(IsDisabledAndNotCustomCharacter));
        OnPropertyChanged(nameof(IsEnabledAndNotCustomCharacter));
    }
}