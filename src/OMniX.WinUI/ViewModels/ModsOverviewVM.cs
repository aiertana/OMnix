using System.Collections.ObjectModel;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Helpers;
using OMniX.Core.Services.CommandService;
using OMniX.Core.Services.CommandService.Models;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Models;
using OMniX.WinUI.Services;
using OMniX.WinUI.Services.ModHandling;
using OMniX.WinUI.Services.Notifications;
using Serilog;
using ScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;

namespace OMniX.WinUI.ViewModels;

public partial class ModsOverviewVM(
    ILogger logger,
    ISkinManagerService skinManagerService,
    IGameService gameService,
    CommandService commandService,
    CommandHandlerService commandHandlerService,
    NotificationManager notificationManager,
    ModPresetHandlerService modPresetHandlerService)
    : ObservableRecipient, INavigationAware
{
    private readonly ILogger _logger = logger.ForContext<ModsOverviewVM>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly IGameService _gameService = gameService;
    private readonly CommandService _commandService = commandService;
    private readonly CommandHandlerService _commandHandlerService = commandHandlerService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ModPresetHandlerService _modPresetHandlerService = modPresetHandlerService;

    public static ModOverviewPageState PageState { get; } = new();
    public TaskCompletionSource<bool> ViewModelLoading { get; } = new();

    public ObservableCollection<CategoryNode> Categories { get; } = new();

    public ObservableCollection<ModOverviewCommandVM> CommandDefinitions { get; } = new();

    private string _searchText = string.Empty;
    public BaseNode? GoToNode { get; private set; }

    [ObservableProperty] private string _targetPath = string.Empty;

    /// <summary>二开：Hero 卡下面那行统计，刷新和批量操作后更新</summary>
    [ObservableProperty] private string _statText = "查看全部模组状态，批量开关模组资源";

    public async void OnNavigatedTo(object parameter)
    {
        await Refresh();
        await LoadCommands().ConfigureAwait(false);

        var goToId = default(InternalName);

        if (parameter is InternalName internalName)
        {
            goToId = internalName;
        }


        if (goToId is not null && GetAllNodes().FirstOrDefault(n => n.Id == goToId) is { } node)
        {
            if (node is ModModelNode modModelNode)
            {
                var moddableObjectNode = modModelNode.ParentModdableObject;
                var categoryNode = moddableObjectNode.ParentCategory;

                categoryNode.IsExpanded = true;
                moddableObjectNode.IsExpanded = true;
                GoToNode = modModelNode;
            }
            else if (node is ModdableObjectNode moddableObjectNode)
            {
                var categoryNode = moddableObjectNode.ParentCategory;

                categoryNode.IsExpanded = true;
                moddableObjectNode.IsExpanded = true;
                GoToNode = moddableObjectNode;
            }
            else if (node is CategoryNode categoryNode)
            {
                categoryNode.IsExpanded = true;
            }


            ViewModelLoading.SetResult(false);
        }
        else
        {
            ViewModelLoading.SetResult(true);
        }
    }

    public async Task RestoreState(ScrollViewer? scrollViewer)
    {
        SearchTextChangedHandler(PageState.SearchText);

        foreach (var node in GetAllNodes())
        {
            if (!PageState.NodesState.TryGetValue(node.Id, out var stateNode))
                continue;


            node.IsExpanded = stateNode.IsExpanded;
            node.IsVisible = stateNode.IsVisible;
        }

        if (scrollViewer is not null)
        {
            await Task.Delay(200);
            scrollViewer.ChangeView(null, PageState.ScrollPosition, null);
        }
    }

    public void OnNavigatedFrom()
    {
        PageState.SearchText = _searchText;

        PageState.NodesState = GetAllNodes().ToDictionary(node => node.Id, node => node);
    }

    private async Task LoadCommands()
    {
        var commandDefinitions =
            await _commandHandlerService.GetCommandsThatContainSpecialVariablesAsync(SpecialVariables.TargetPath);
        CommandDefinitions.Clear();

        foreach (var commandDefinition in commandDefinitions)
        {
            CommandDefinitions.Add(new ModOverviewCommandVM(commandDefinition)
            {
                RunCommand = RunCommandCommand,
                OpenFolder = OpenFolderCommand
            });
        }
    }

    [RelayCommand]
    private async Task RunCommandAsync(ModOverviewCommandVM? commandDefinitionVM)
    {
        if (commandDefinitionVM is null || TargetPath.IsNullOrEmpty())
            return;


        var result = await Task.Run(async () => await _commandHandlerService.RunCommandAsync(commandDefinitionVM.Id,
            SpecialVariablesInput.CreateWithTargetPath(TargetPath)).ConfigureAwait(false));

        if (result.HasNotification)
            _notificationManager.ShowNotification(result.Notification);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (TargetPath.IsNullOrEmpty() || !Directory.Exists(TargetPath))
            return;
        await Launcher.LaunchFolderPathAsync(TargetPath);
    }

    [RelayCommand]
    private async Task CloseOpenAll(string? option)
    {
        var categories = Categories.ToArray();
        Categories.Clear();
        await Task.Delay(100);

        if (option == "Close")
        {
            foreach (var baseNode in GetAllNodes(categories).Where(n => n is { IsVisible: true, IsExpanded: true })
                         .Reverse())
            {
                baseNode.IsExpanded = false;
            }
        }
        else
        {
            foreach (var baseNode in GetAllNodes(categories).Where(n => n is { IsVisible: true, IsExpanded: false }))
            {
                baseNode.IsExpanded = true;
            }
        }

        Categories.AddRange(categories);
    }

    private async Task Refresh()
    {
        Categories.Clear();

        var categories = _gameService.GetCategories();

        foreach (var category in categories)
        {
            var modObjects = _gameService.GetModdableObjects(category);
            var categoryNode = new CategoryNode(category,
                _skinManagerService.GetCategoryFolderPath(category).FullName);

            foreach (var moddableObject in modObjects)
            {
                var modList = _skinManagerService.GetCharacterModList(moddableObject);
                var moddableObjectNode =
                    new ModdableObjectNode(moddableObject, modList.AbsModsFolderPath, categoryNode);

                var mods = new List<(ModModel Model, CharacterSkinEntry Entry)>();
                foreach (var skinEntry in modList.Mods)
                {
                    var modModel = ModModel.FromMod(skinEntry);

                    var mod = skinEntry.Mod;

                    var skinSettings = mod.Settings.TryGetSettings(out var settings)
                        ? settings
                        : await mod.Settings.TryReadSettingsAsync();

                    if (skinSettings is not null)
                        modModel.WithModSettings(skinSettings);

                    mods.Add((modModel, skinEntry));
                }

                foreach (var (modModel, entry) in mods.OrderByDescending(x => x.Model.DateAdded))
                {
                    var modNode = new ModModelNode(modModel, moddableObjectNode)
                    {
                        ToggleCommand = ToggleModCommand,
                        SkinEntry = entry
                    };

                    // 二开：以磁盘上的文件夹名为准，避免列表缓存和实际状态不一致
                    modNode.IsEnabled = IsEnabledOnDisk(entry);

                    moddableObjectNode.Mods.Add(modNode);
                }


                if (moddableObjectNode.Mods.Count > 0)
                    categoryNode.ModdableObjects.Add(moddableObjectNode);
            }

            Categories.Add(categoryNode);
        }

        UpdateStats();
    }

    /// <summary>二开：统计栏文案</summary>
    private void UpdateStats()
    {
        var mods = GetAllNodes().OfType<ModModelNode>().ToList();
        var enabled = mods.Count(m => m.IsEnabled);
        StatText = mods.Count == 0
            ? "还没读到 Mod，检查一下游戏目录"
            : $"共 {mods.Count} 个 Mod · 已启用 {enabled} 个";
    }

    /// <summary>
    ///     二开：把某个 Mod 设成目标状态。单条勾选和批量操作共用这一份，
    ///     返回 false 表示列表里找不到对应的条目（通常是因为列表已经重建过）。
    /// </summary>
    private async Task<bool> ApplyModStateAsync(ModModelNode modNode, bool wantEnabled)
    {
        var modList = _skinManagerService.GetCharacterModList(modNode.ParentModdableObject.ModdableObject);
        var entry = modList.Mods.FirstOrDefault(m => m.Id == modNode.SkinEntry.Id);
        if (entry is null)
            return false;

        // 以磁盘上的真实状态为准：界面状态没同步时也不会做反向操作
        if (IsEnabledOnDisk(entry) != wantEnabled)
            await Task.Run(() => RenameModFolder(entry, wantEnabled));

        modNode.IsEnabled = wantEnabled;
        modNode.ParentModdableObject.RefreshEnabledCount();
        return true;
    }

    private async Task SetAllEnabledAsync(bool enable)
    {
        var mods = GetAllNodes().OfType<ModModelNode>().Where(m => m.IsEnabled != enable).ToList();

        if (mods.Count == 0)
        {
            _notificationManager.ShowNotification(enable ? "已经全部启用了" : "已经全部停用了",
                $"当前没有需要改动的 Mod", TimeSpan.FromSeconds(4));
            return;
        }

        var done = 0;
        var failed = 0;

        foreach (var mod in mods)
        {
            try
            {
                if (await ApplyModStateAsync(mod, enable))
                    done++;
                else
                    failed++;
            }
            catch (Exception e)
            {
                failed++;
                _logger.Debug(e, "Batch toggle failed for {ModName}", mod.Mod.Name);
            }
        }

        foreach (var moddableObject in GetAllNodes().OfType<ModdableObjectNode>())
            moddableObject.RefreshEnabledCount();

        UpdateStats();

        _notificationManager.ShowNotification(
            enable ? "已全部启用" : "已全部停用",
            failed == 0
                ? $"处理了 {done} 个 Mod"
                : $"成功 {done} 个，失败 {failed} 个（多半是文件夹被占用）",
            TimeSpan.FromSeconds(6));
    }

    /// <summary>
    ///     二开：启用预设。
    ///
    ///     原来这张卡是「全部启用」—— 一把把上百个 Mod 全打开，实际没人这么用
    ///     （同角色开一堆，游戏只认一个）。改成先列出所有预设，
    ///     选一个就把那个预设里的 Mod 组合启用上去。
    /// </summary>
    [RelayCommand]
    private async Task EnablePresetAsync()
    {
        var presets = (await _modPresetHandlerService.GetModPresetsAsync()).ToList();

        if (presets.Count == 0)
        {
            _notificationManager.ShowNotification("还没有预设",
                "先去「预设配置」把现在的 Mod 组合存成一个预设，这里就能一键切换",
                TimeSpan.FromSeconds(7));

            return;
        }

        // 每一项都带上这个预设里有多少个 Mod，选之前心里有数
        var list = new ListView
        {
            ItemsSource = presets
                .Select(preset => $"{preset.Name}（{preset.Mods.Count} 个 Mod）")
                .ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
            MinWidth = 320,
            MaxHeight = 360
        };

        var dialog = new ContentDialog
        {
            Title = "启用预设",
            Content = list,
            PrimaryButtonText = "启用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var index = list.SelectedIndex;

        if (index < 0 || index >= presets.Count)
            return;

        var preset = presets[index];

        // 3DMigoto 在跑、又开了自动同步的话，Apply 里会等几秒再回写偏好，
        // 先给个"正在启用"，不然这段时间界面一点反应都没有
        _notificationManager.ShowNotification("正在启用预设", $"「{preset.Name}」", TimeSpan.FromSeconds(4));

        var result = await _modPresetHandlerService.ApplyModPresetAsync(preset.Name);

        // 预设会改一堆 Mod 的开关状态，列表和顶上那行统计都得重读
        await Refresh();
        UpdateStats();

        if (result.HasNotification)
            _notificationManager.ShowNotification(result.Notification);
        else
            _notificationManager.ShowNotification("已启用预设",
                $"「{preset.Name}」里的 Mod 已经启用", TimeSpan.FromSeconds(6));
    }

    /// <summary>二开：一键停用全部</summary>
    [RelayCommand]
    private Task DisableAllAsync() => SetAllEnabledAsync(false);

    /// <summary>
    ///     二开：冲突检查。同一个角色下启用多个 Mod 时，游戏里通常只会生效一个，
    ///     这里只做提示，不替用户改任何东西。
    /// </summary>
    [RelayCommand]
    private void CheckConflicts()
    {
        var conflicts = new List<string>();
        var multiCount = 0;

        foreach (var moddableObject in GetAllNodes().OfType<ModdableObjectNode>())
        {
            var enabledCount = moddableObject.Mods.Count(m => m.IsEnabled);
            if (enabledCount <= 1)
                continue;

            multiCount += enabledCount;
            conflicts.Add($"{moddableObject.ModdableObject.DisplayName}（{enabledCount} 个）");
        }

        if (conflicts.Count == 0)
        {
            _notificationManager.ShowNotification("没有发现冲突", "每个角色最多只启用了一个 Mod",
                TimeSpan.FromSeconds(5));
            return;
        }

        var preview = string.Join("、", conflicts.Take(6));
        if (conflicts.Count > 6)
            preview += $" 等 {conflicts.Count} 个角色";

        _notificationManager.ShowNotification("发现可能的冲突",
            $"{preview} 同时启用了多个 Mod，游戏一般只会加载其中一个",
            TimeSpan.FromSeconds(10));
    }

    /// <summary>二开：刷新列表</summary>
    [RelayCommand]
    private async Task RefreshListAsync()
    {
        await Refresh();
        _notificationManager.ShowNotification("已刷新", StatText, TimeSpan.FromSeconds(4));
    }


    public void SearchTextChangedHandler(string? text)
    {
        _searchText = (text ?? string.Empty).Trim();

        if (_searchText.IsNullOrEmpty())
        {
            foreach (var category in Categories)
            {
                category.IsVisible = true;
                category.IsExpanded = false;

                foreach (var moddableObject in category.ModdableObjects)
                {
                    moddableObject.IsVisible = true;
                    moddableObject.IsExpanded = false;

                    foreach (var mod in moddableObject.Mods)
                    {
                        mod.IsVisible = true;
                    }
                }
            }

            return;
        }

        foreach (var category in Categories)
        {
            foreach (var moddableObject in category.ModdableObjects)
            {
                foreach (var mod in moddableObject.Mods)
                {
                    var isMatch = mod.Mod.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);

                    mod.IsVisible = isMatch;
                }

                var modChildMatch = moddableObject.Mods.Any(mod => mod.IsVisible);
                moddableObject.IsVisible = modChildMatch;
                moddableObject.IsExpanded = modChildMatch;
            }

            var modObjectMatch = category.ModdableObjects.Any(mod => mod.IsVisible);
            category.IsVisible = modObjectMatch;
            category.IsExpanded = modObjectMatch;
        }
    }


    private IEnumerable<BaseNode> GetAllNodes(IEnumerable<CategoryNode>? categoryNodes = null)
    {
        foreach (var categoryNode in categoryNodes ?? Categories)
        {
            yield return categoryNode;

            foreach (var moddableObjectNode in categoryNode.ModdableObjects)
            {
                yield return moddableObjectNode;

                foreach (var modModelNode in moddableObjectNode.Mods)
                {
                    yield return modModelNode;
                }
            }
        }
    }

    /// <summary>
    ///     二开：在总览里启用 / 停用某个 Mod。
    ///     按按钮上显示的动作执行（不是无脑 toggle），同一个角色下有多个 Mod 时只影响这一条。
    /// </summary>
    [RelayCommand]
    private async Task ToggleModAsync(ModModelNode? modNode)
    {
        if (modNode is null)
            return;

        // 按钮显示“启用”时 IsEnabled=false → 要执行启用；反之执行停用
        var wantEnabled = !modNode.IsEnabled;

        try
        {
            // 复用批量操作那一份：按 Id 重新取条目，按磁盘真实状态决定是否改名。
            // 注意：里面执行 Task.Run 时不能 ConfigureAwait(false)，
            // 否则回来时在线程池线程上改 UI 绑定会抛 0x8001010E
            if (!await ApplyModStateAsync(modNode, wantEnabled))
            {
                _notificationManager.ShowNotification("找不到这个 Mod",
                    "Mod 列表可能已刷新，请重新进入 Mod 总览", TimeSpan.FromSeconds(8));
                return;
            }
        }
        catch (InvalidOperationException e) when (
            e.Message.Contains("Cannot enable a enabled mod") ||
            e.Message.Contains("Cannot disable a disabled mod"))
        {
            // 文件夹实际已经是目标状态，只同步界面
            modNode.IsEnabled = wantEnabled;
            modNode.ParentModdableObject.RefreshEnabledCount();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not {Action} mod {ModName}", wantEnabled ? "enable" : "disable", modNode.Mod.Name);

            // 把具体原因显示出来，方便判断是文件占用、命名冲突还是别的
            _notificationManager.ShowNotification(
                wantEnabled ? "启用 Mod 失败" : "停用 Mod 失败",
                $"{modNode.Mod.Name}：{e.Message}",
                TimeSpan.FromSeconds(10));

            // 失败后把界面同步回真实状态，避免按钮一直显示错误的状态
            SyncActualState(modNode);
        }
    }

    /// <summary>
    ///     二开：重命名 Mod 文件夹（保留停用时的 DISABLED_ 前缀）
    /// </summary>
    public async Task RenameModAsync(ModModelNode? modNode, string? newName)
    {
        if (modNode is null)
            return;

        newName = newName?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        try
        {
            var dir = new DirectoryInfo(modNode.Mod.FolderPath.TrimEnd(Path.DirectorySeparatorChar));
            if (!dir.Exists)
            {
                _notificationManager.ShowNotification("重命名失败", $"找不到文件夹：{dir.FullName}",
                    TimeSpan.FromSeconds(6));
                return;
            }

            var hasDisabledPrefix = ModFolderHelpers.FolderHasDisabledPrefix(dir.Name);
            var targetName = hasDisabledPrefix
                ? ModFolderHelpers.GetFolderNameWithDisabledPrefix(newName)
                : newName;

            var target = Path.Combine(dir.Parent!.FullName, targetName);

            if (string.Equals(dir.FullName, target, StringComparison.OrdinalIgnoreCase))
                return;

            if (Directory.Exists(target) || File.Exists(target))
            {
                _notificationManager.ShowNotification("重命名失败", $"已存在同名文件夹「{targetName}」",
                    TimeSpan.FromSeconds(8));
                return;
            }

            await Task.Run(() => dir.MoveTo(target));

            await Refresh();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not rename mod {ModName}", modNode.Mod.Name);
            _notificationManager.ShowNotification("重命名失败", e.Message, TimeSpan.FromSeconds(8));
        }
    }

    /// <summary>
    ///     二开：按文件夹名判断 Mod 是否处于启用状态（停用的会被加上 DISABLED_ 前缀）
    /// </summary>
    private static bool IsEnabledOnDisk(CharacterSkinEntry entry)
    {
        var folderName = Path.GetFileName(entry.Mod.FullPath.TrimEnd(Path.DirectorySeparatorChar));
        return !ModFolderHelpers.FolderHasDisabledPrefix(folderName);
    }

    /// <summary>
    ///     二开：通过重命名文件夹来启用 / 停用 Mod
    /// </summary>
    private static void RenameModFolder(CharacterSkinEntry entry, bool enable)
    {
        var currentPath = entry.Mod.FullPath.TrimEnd(Path.DirectorySeparatorChar);
        var dir = new DirectoryInfo(currentPath);

        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Mod 文件夹不存在：{currentPath}");

        var hasDisabledPrefix = ModFolderHelpers.FolderHasDisabledPrefix(dir.Name);

        // 已经是目标状态，不用动
        if (hasDisabledPrefix == !enable)
            return;

        var newName = enable
            ? ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(dir.Name)
            : ModFolderHelpers.GetFolderNameWithDisabledPrefix(dir.Name);

        var target = Path.Combine(dir.Parent!.FullName, newName);

        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"已存在同名文件夹「{newName}」，请先处理它再操作");

        dir.MoveTo(target);
    }

    /// <summary>
    ///     二开：按 Mod 列表里的真实状态刷新界面
    /// </summary>
    private void SyncActualState(ModModelNode modNode)
    {
        try
        {
            var modList = _skinManagerService.GetCharacterModList(modNode.ParentModdableObject.ModdableObject);
            var entry = modList.Mods.FirstOrDefault(m => m.Id == modNode.SkinEntry.Id);
            if (entry is null)
                return;

            modNode.IsEnabled = entry.IsEnabled;
            modNode.ParentModdableObject.RefreshEnabledCount();
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Could not sync actual state for {ModName}", modNode.Mod.Name);
        }
    }

    [RelayCommand]
    private void GoToCharacter(object? characterObj)
    {
        if (characterObj is not IModdableObject character)
        {
            return;
        }

        App.GetService<INavigationService>().NavigateToCharacterDetails(character.InternalName);
    }
}

public partial class ModOverviewCommandVM : ObservableObject
{
    public ModOverviewCommandVM(CommandDefinition commandDefinition)
    {
        Id = commandDefinition.Id;
        DisplayName = commandDefinition.CommandDisplayName;

        var (fullCommand, workingDirectory) = commandDefinition.GetFullCommand(null);
        FullCommand = fullCommand;
        WorkingDirectory = workingDirectory ?? App.ROOT_DIR;
    }

    public Guid Id { get; set; }
    public string DisplayName { get; set; }

    public string FullCommand { get; set; }

    public string WorkingDirectory { get; set; }

    public required IAsyncRelayCommand RunCommand { get; init; }

    public required IAsyncRelayCommand OpenFolder { get; init; }

    [ObservableProperty] private string _targetPath = string.Empty;
}

public partial class BaseNode : ObservableObject
{
    public string Id { get; }

    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isExpanded;

    public BaseNode(string id)
    {
        Id = id;
    }
}

public interface IHasChildItems<out T> where T : BaseNode
{
    public IEnumerable<T> ChildItems { get; }
}

public partial class CategoryNode : BaseNode, IHasChildItems<ModdableObjectNode>
{
    public string DisplayName { get; init; }

    public ObservableCollection<ModdableObjectNode> ModdableObjects { get; } = new();
    public IEnumerable<ModdableObjectNode> ChildItems => ModdableObjects;

    public string FolderPath { get; }

    /// <summary>
    ///     二开：分类图标。取自 QAQM 那套分类图标，二改成 OMniX 主题色 + 透明底。
    ///     角色分类保持无图标（没有对应设计），其余按语义映射。
    /// </summary>
    public ImageSource? IconUri { get; init; }

    public bool HasIcon => IconUri is not null;

    public CategoryNode(ICategory category, string folderPath) : base(category.InternalName)
    {
        DisplayName = category.DisplayNamePlural;
        FolderPath = folderPath;
        IconUri = ResolveIcon(category.ModCategory);
    }

    private static ImageSource? ResolveIcon(ModCategory modCategory)
    {
        var file = modCategory switch
        {
            ModCategory.NPC => "npc.png",
            ModCategory.Ui => "ui.png",
            ModCategory.Object => "world.png",
            ModCategory.Custom => "utility.png",
            _ => null
        };

        if (file is null)
            return null;

        // 必须用绝对路径：ms-appx:/// 在 Debug（非打包）下解析不到。
        // 另外要返回 ImageSource，x:Bind 不做 string → ImageSource 的隐式转换，给 string 会直接抛异常。
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ModCategories", file);

        return File.Exists(path) ? new BitmapImage(new Uri(path)) : null;
    }
}

public partial class ModdableObjectNode : BaseNode, IHasChildItems<ModModelNode>
{
    [ObservableProperty] private IModdableObject _moddableObject;
    public Uri ImagePath { get; } = App.GetService<ImageHandlerService>().PlaceholderImageUri;

    public CategoryNode ParentCategory { get; }
    public ObservableCollection<ModModelNode> Mods { get; } = new();

    public IEnumerable<ModModelNode> ChildItems => Mods;

    public string FolderPath { get; }

    /// <summary>
    ///     二开：已启用的 Mod 数量
    /// </summary>
    public int EnabledModCount => Mods.Count(mod => mod.IsEnabled);

    /// <summary>
    ///     二开：角色行显示的统计，比如“3/5 已启用”
    /// </summary>
    public string ModSummaryText => $"{EnabledModCount}/{Mods.Count} 已启用";

    /// <summary>
    ///     二开：某个 Mod 的启用状态变化后刷新统计
    /// </summary>
    public void RefreshEnabledCount()
    {
        OnPropertyChanged(nameof(EnabledModCount));
        OnPropertyChanged(nameof(ModSummaryText));
    }

    public ModdableObjectNode(IModdableObject moddableObject, string folderPath, CategoryNode parentCategory) : base(
        moddableObject.InternalName)
    {
        FolderPath = folderPath;
        ParentCategory = parentCategory;
        ModdableObject = moddableObject;

        if (moddableObject is IImageSupport { ImageUri: not null } imageSupport)
            ImagePath = imageSupport.ImageUri;
    }
}

public partial class ModModelNode : BaseNode
{
    public ModModel Mod { get; }

    public ModdableObjectNode ParentModdableObject { get; }

    /// <summary>
    ///     二开：切换启用状态的命令（由 ModsOverviewVM 注入）
    /// </summary>
    public required IAsyncRelayCommand ToggleCommand { get; init; }

    /// <summary>
    ///     二开：对应的 Mod 条目（启用 / 停用需要它的 Id）
    /// </summary>
    public required CharacterSkinEntry SkinEntry { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    [NotifyPropertyChangedFor(nameof(EnabledText))]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _isEnabled;

    /// <summary>
    ///     按钮文字：已启用显示“不启用”，未启用显示“启用”
    /// </summary>
    public string ToggleButtonText => IsEnabled ? "不启用" : "启用";

    public string EnabledText => IsEnabled ? "已启用" : "未启用";

    public bool IsDisabled => !IsEnabled;

    public ModModelNode(ModModel mod, ModdableObjectNode parentModdableObject) : base(mod.Id.ToString())
    {
        Mod = mod;
        ParentModdableObject = parentModdableObject;
        _isEnabled = mod.IsEnabled;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        // 同步回 Mod，保证其它页面读到一致的状态
        Mod.IsEnabled = value;
    }
}

public class ModOverviewPageState : EventArgs
{
    public string SearchText { get; set; } = string.Empty;
    public double ScrollPosition { get; set; }
    public Dictionary<string, BaseNode> NodesState { get; set; } = new();
}