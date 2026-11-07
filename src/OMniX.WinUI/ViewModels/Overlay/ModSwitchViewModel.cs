using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OMniX.Core.Contracts.Services;
using OMniX.Core.Entities;
using OMniX.Core.Entities.Mods.Helpers;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Services;
using ModPresetService = OMniX.Core.Services.ModPresetService.ModPresetService;
using OMniX.WinUI.Contracts.Services;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Services;
using OMniX.WinUI.ViewModels.CharacterDetailsViewModels;
using Microsoft.UI.Xaml;
using Serilog;

namespace OMniX.WinUI.ViewModels.Overlay;

/// <summary>
///     游戏内悬浮窗的「Mod 切换」面板数据源。
///     切换 Mod 走的是和主界面同一套逻辑（给文件夹加 / 去 DISABLED_ 前缀）。
///     切完要在游戏里生效，得回游戏按 F10 让 3Dmigoto 重载。
/// </summary>
public partial class ModSwitchViewModel : ObservableRecipient
{
    private readonly IGameService _gameService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;
    private readonly ModPresetService _modPresetService;
    private readonly ISkinManagerService _skinManagerService;

    /// <summary>
    ///     角色使用频率（点了多少次 tab），持久化到本地设置，"常用排序"用。
    /// </summary>
    private Dictionary<string, int> _characterUsage = new();

    public ModSwitchViewModel(IGameService gameService, ISkinManagerService skinManagerService,
        ModPresetService modPresetService,
        ILocalSettingsService localSettingsService, ILogger logger)
    {
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _modPresetService = modPresetService;
        _localSettingsService = localSettingsService;
        _logger = logger.ForContext<ModSwitchViewModel>();
    }

    /// <summary>
    ///     顶部角色标签行：名字 + 已启用/总数。
    /// </summary>
    public ObservableCollection<CharacterTabVm> CharacterTabs { get; } = new();

    public ObservableCollection<ModSwitchItemVm> Mods { get; } = new();

    public ObservableCollection<string> PresetNames { get; } = new();

    [ObservableProperty] private CharacterTabVm? _selectedTab;

    [ObservableProperty] private string _gameName = string.Empty;

    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>
    ///     角色使用频率的设置存储键。
    /// </summary>
    public const string CharacterUsageKey = "OverlayCharacterUsage";

    /// <summary>
    ///     0 = 自定义（Mod 内部顺序），1 = 名称，2 = 状态（启用的在前），3 = 时间（新改动的在前）。
    /// </summary>
    [ObservableProperty] private int _sortMode;

    /// <summary>
    ///     单选模式：启用一个 Mod 时自动停用同角色的其它 Mod。
    /// </summary>
    [ObservableProperty] private bool _isSingleSelect;

    /// <summary>
    ///     列表视图：true 用紧凑列表，false 用大封面卡片。
    /// </summary>
    [ObservableProperty] private bool _isListView;

    /// <summary>
    ///     界面缩放（0.85 ~ 1.3）。A- / A+ 调节。
    /// </summary>
    [ObservableProperty] private double _uiScale = 1.0;

    /// <summary>
    ///     角色行是否收起（汉堡按钮切换）。
    /// </summary>
    [ObservableProperty] private bool _isCharacterRowCollapsed;

    /// <summary>
    ///     每次打开面板都重新读一遍角色和 Mod，避免在游戏里删了 Mod 之后列表对不上。
    /// </summary>
    public void Reload()
    {
        GameName = _gameService.GameName;

        LoadPresets();
        LoadTabs();
    }

    private void LoadPresets()
    {
        PresetNames.Clear();
        foreach (var preset in _modPresetService.GetPresets())
        {
            PresetNames.Add(preset.Name);
        }
    }

    private void LoadTabs()
    {
        var previous = SelectedTab?.Character;

        // 最近改动优先：按角色 Mod 文件夹的最后修改时间降序，刚动过的角色排前面；
        // 没 Mod / 时间拿不到的角色保持游戏里的原顺序（OrderByDescending 排序稳定）
        var tabs = _gameService.GetCharacters()
            .Select(character =>
            {
                var modList = _skinManagerService.GetCharacterModListOrDefault(character.InternalName.Id);
                var lastModified = modList?.Mods.Count > 0
                    ? modList.Mods.Max(m => Directory.Exists(m.Mod.FullPath)
                        ? Directory.GetLastWriteTime(m.Mod.FullPath)
                        : DateTime.MinValue)
                    : DateTime.MinValue;
                return (Character: character, ModList: modList, LastModified: lastModified);
            })
            .OrderByDescending(x => x.LastModified)
            .ToList();

        CharacterTabs.Clear();

        foreach (var (character, modList, _) in tabs)
        {
            var total = modList?.Mods.Count ?? 0;
            var enabled = modList?.Mods.Count(m => m.IsEnabled) ?? 0;

            CharacterTabs.Add(new CharacterTabVm(character, $"{enabled}/{total}"));
        }

        // 保持之前选中的角色；没有就选第一个
        SelectedTab = previous is not null
            ? CharacterTabs.FirstOrDefault(t => ReferenceEquals(t.Character, previous)) ?? CharacterTabs.FirstOrDefault()
            : CharacterTabs.FirstOrDefault();

        if (SelectedTab is null)
        {
            Mods.Clear();
            StatusText = "当前没有可用角色";
        }
    }

    /// <summary>
    ///     打开面板前读一遍角色使用频率（异步读设置文件）。
    /// </summary>
    public async Task LoadCharacterUsageAsync()
    {
        try
        {
            _characterUsage = await _localSettingsService.ReadOrCreateSettingAsync<Dictionary<string, int>>(
                CharacterUsageKey, SettingScope.App) ?? new Dictionary<string, int>();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "读取角色使用频率失败，按默认顺序排");
            _characterUsage = new Dictionary<string, int>();
        }
    }

    private async Task BumpCharacterUsageAsync(ICharacter character)
    {
        try
        {
            var id = character.InternalName.Id;
            _characterUsage[id] = _characterUsage.GetValueOrDefault(id) + 1;
            await _localSettingsService.SaveSettingAsync(CharacterUsageKey, _characterUsage, SettingScope.App);
        }
        catch (Exception e)
        {
            _logger.Warning(e, "保存角色使用频率失败");
        }
    }

    partial void OnSelectedTabChanged(CharacterTabVm? value)
    {
        foreach (var tab in CharacterTabs)
        {
            tab.IsSelected = ReferenceEquals(tab, value);
        }

        OnPropertyChanged(nameof(CurrentCharacterName));
        LoadMods();
    }

    /// <summary>
    ///     窄条面板顶部显示的当前角色名。
    /// </summary>
    public string CurrentCharacterName => SelectedTab?.Name ?? "无角色";

    /// <summary>
    ///     窄条面板顶部显示的当前排序方式。
    /// </summary>
    public string SortModeName => SortMode switch
    {
        1 => "排序:名称",
        2 => "排序:状态",
        3 => "排序:时间",
        _ => "排序:自定义",
    };

    private void LoadMods()
    {
        Mods.Clear();

        if (SelectedTab is null)
        {
            return;
        }

        var character = SelectedTab.Character;
        var modList = _skinManagerService.GetCharacterModListOrDefault(character.InternalName.Id);

        if (modList is null)
        {
            StatusText = $"{character.DisplayName} 还没有 Mod 文件夹";
            return;
        }

        var items = modList.Mods.Select(m => new ModSwitchItemVm(character, m)).ToList();

        items = SortMode switch
        {
            1 => items.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            2 => items.OrderByDescending(m => m.IsEnabled).ThenBy(m => m.Name).ToList(),
            3 => items.OrderByDescending(m => m.ModifiedTime).ToList(),
            _ => items
        };

        foreach (var item in items)
        {
            Mods.Add(item);
        }

        StatusText = $"{character.DisplayName}：{items.Count(m => m.IsEnabled)}/{items.Count} 已启用";
    }

    partial void OnSortModeChanged(int value)
    {
        OnPropertyChanged(nameof(SortModeName));
        LoadMods();
    }

    [RelayCommand]
    private async Task SelectTabAsync(CharacterTabVm? tab)
    {
        if (tab is not null)
        {
            SelectedTab = tab;
            await BumpCharacterUsageAsync(tab.Character);
        }
    }

    [RelayCommand]
    private async Task ToggleModAsync(ModSwitchItemVm? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var modList = _skinManagerService.GetCharacterModList(item.Character);

            // 资源管理器窗口停在 Mod 文件夹里的话，改名必失败（目录句柄被握着）。
            // 先把它请走，用户不用自己找窗口。
            var released = FileLocks.CloseExplorerWindowsInside(GetModFolderPath(item));

            // 改文件夹名是 IO 操作，别卡 UI
            var enabled = await Task.Run(() => modList.ToggleMod(item.ModId));

            if (released > 0)
            {
                StatusText = $"已自动关闭 {released} 个占用该 Mod 文件夹的资源管理器窗口";
            }

            // 单选模式：开一个就自动把同角色其它启用的关掉
            if (enabled && IsSingleSelect)
            {
                await DisableOthersAsync(item.Character, item.ModId);
            }

            item.IsEnabled = enabled;

            UpdateTabCount(item.Character);
            StatusText = $"{(enabled ? "启用" : "停用")}：{item.Name}（回游戏按 F10 生效）";

            // 告诉主窗口的 ModGridVM / ModPaneVM 刷新对应行，不然主页显示的还是旧状态
            var entry = modList.Mods.FirstOrDefault(m => m.Id == item.ModId);
            if (entry is not null)
            {
                entry.Mod.Settings.TryGetSettings(out var settings);
                Messenger.Send(new ModChangedMessage(this, entry, settings));
            }
        }
        catch (UnauthorizedAccessException e)
        {
            // 游戏开着时 3DMigoto 锁住 Mod 文件，重命名失败。内存状态没被改过，
            // 把开关拨回真实状态，别让 UI 和磁盘各说各话。
            _logger.Warning(e, "悬浮窗切换 Mod 失败（文件被占用）：{ModName}", item.Name);
            item.IsEnabled = IsModActuallyEnabled(item);
            LoadMods();

            // 把真凶查出来给用户看，别让他对着 Access denied 干瞪眼
            StatusText = FileLocks.DescribeFileLock(GetModFolderPath(item));
        }
        catch (Exception e)
        {
            _logger.Error(e, "悬浮窗切换 Mod 失败：{ModName}", item.Name);
            item.IsEnabled = IsModActuallyEnabled(item);
            LoadMods();
            StatusText = $"切换失败：{e.Message}";
        }
    }

    /// <summary>
    ///     取 Mod 文件夹的绝对路径（查占用者时用）。拿不到返回 null。
    /// </summary>
    private string? GetModFolderPath(ModSwitchItemVm item)
    {
        try
        {
            return _skinManagerService.GetCharacterModListOrDefault(item.Character.InternalName.Id)?
                .Mods.FirstOrDefault(m => m.Id == item.ModId)?.Mod.FullPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     回读 ModList 内存里的真实启用状态（Rename 失败时它不会被改），用于把 UI 拨回去。
    /// </summary>
    private bool IsModActuallyEnabled(ModSwitchItemVm item)
    {
        try
        {
            var modList = _skinManagerService.GetCharacterModListOrDefault(item.Character.InternalName.Id);
            return modList?.Mods.FirstOrDefault(m => m.Id == item.ModId)?.IsEnabled ?? false;
        }
        catch
        {
            return item.IsEnabled;
        }
    }

    /// <summary>
    ///     橡皮擦按钮：停用该 Mod，恢复原版贴图。
    /// </summary>
    [RelayCommand]
    private async Task EraseModAsync(ModSwitchItemVm? item)
    {
        if (item is null || !item.IsEnabled)
        {
            return;
        }

        await ToggleModAsync(item);
    }

    /// <summary>
    ///     右下角刷新按钮：模拟按一次 F10，让 3DMigoto 重载 Mod，不用切出游戏。
    ///     游戏提权（管理员）而 OMniX 没提权时系统会丢掉模拟按键，GameKeySender
    ///     会把原因写进状态栏，不再悄悄失败。
    /// </summary>
    [RelayCommand]
    private async Task RefreshGameModsAsync()
    {
        NeedsAdminRestart = false;

        try
        {
            // 整个流程要卡 80ms 等待窗口切换，不能占着 UI 线程
            var (result, message, detail) = await Task.Run(() =>
                GameKeySender.SendF10ToGame(GameContext.Current.GameProcessNames));

            StatusText = message;
            NeedsAdminRestart = result == GameKeySender.F10SendResult.BlockedByUac;

            if (result == GameKeySender.F10SendResult.Sent)
            {
                _logger.Information("悬浮窗：F10 发送完成 —— {Detail}", detail);
            }
            else
            {
                _logger.Warning("悬浮窗：F10 发送失败（{Result}）：{Message} —— {Detail}", result, message, detail);
            }
        }
        catch (Exception e)
        {
            _logger.Error(e, "悬浮窗：发送 F10 失败");
            StatusText = $"F10 发送失败：{e.Message}";
        }
    }

    [ObservableProperty] private bool _needsAdminRestart;

    /// <summary>
    ///     游戏提权而 OMniX 没提权时的救场按钮：弹 UAC 重启自己成管理员。
    /// </summary>
    [RelayCommand]
    private void RestartAsAdmin()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            StatusText = "重启失败：拿不到程序路径";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            });

            _logger.Information("悬浮窗：已请求以管理员身份重启 OMniX，准备退出当前实例");
            Application.Current.Exit();
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // 1223 = ERROR_CANCELLED：用户在 UAC 弹窗里点了「否」
            StatusText = "已取消管理员授权，刷新需要 OMniX 也以管理员运行";
        }
        catch (Exception e)
        {
            _logger.Error(e, "悬浮窗：以管理员身份重启失败");
            StatusText = $"重启失败：{e.Message}";
        }
    }

    /// <summary>
    ///     单选模式用：把同角色除 excludeId 外所有启用的 Mod 停用。
    ///     停一个通知一次主页；浮窗里其它开关的显示也一起刷新。
    /// </summary>
    private async Task DisableOthersAsync(ICharacter character, Guid excludeId)
    {
        var modList = _skinManagerService.GetCharacterModListOrDefault(character.InternalName.Id);
        if (modList is null)
        {
            return;
        }

        var toDisable = modList.Mods.Where(m => m.IsEnabled && m.Id != excludeId).ToList();
        foreach (var entry in toDisable)
        {
            try
            {
                FileLocks.CloseExplorerWindowsInside(entry.Mod.FullPath);
                await Task.Run(() => modList.ToggleMod(entry.Id));
                entry.Mod.Settings.TryGetSettings(out var settings);
                Messenger.Send(new ModChangedMessage(this, entry, settings));
            }
            catch (Exception e)
            {
                _logger.Warning(e, "单选模式停用其它 Mod 失败：{Mod}", entry.Mod.Name);
            }
        }

        // 浮窗里本角色所有开关按 ModList 的真实状态回显
        foreach (var vm in Mods.Where(m => ReferenceEquals(m.Character, character)))
        {
            var entry = modList.Mods.FirstOrDefault(m => m.Id == vm.ModId);
            if (entry is not null)
            {
                vm.IsEnabled = entry.IsEnabled;
            }
        }
    }

    /// <summary>
    ///     开关之后让顶部角色标签上的 n/m 计数跟着变。
    /// </summary>
    private void UpdateTabCount(ICharacter character)
    {
        var tab = CharacterTabs.FirstOrDefault(t => ReferenceEquals(t.Character, character));
        if (tab is null)
        {
            return;
        }

        var modList = _skinManagerService.GetCharacterModListOrDefault(character.InternalName.Id);
        var total = modList?.Mods.Count ?? 0;
        var enabled = modList?.Mods.Count(m => m.IsEnabled) ?? 0;
        tab.CountText = $"{enabled}/{total}";
    }

    [RelayCommand]
    private async Task ApplyPresetAsync(string? presetName)
    {
        if (string.IsNullOrEmpty(presetName))
        {
            return;
        }

        try
        {
            StatusText = $"正在套用预设 {presetName}…";
            await Task.Run(() => _modPresetService.ApplyPresetAsync(presetName));
            StatusText = $"预设 {presetName} 已套用（按「刷新」或 F10 生效）";
            LoadTabs();
        }
        catch (Exception e)
        {
            _logger.Error(e, "悬浮窗套用预设失败：{Preset}", presetName);
            StatusText = $"套用失败：{e.Message}";
        }
    }

    /// <summary>
    ///     A- / A+：调界面缩放，范围 0.85 ~ 1.3。
    /// </summary>
    [RelayCommand]
    private void ZoomOut()
    {
        UiScale = Math.Max(0.85, Math.Round(UiScale - 0.15, 2));
    }

    [RelayCommand]
    private void ZoomIn()
    {
        UiScale = Math.Min(1.3, Math.Round(UiScale + 0.15, 2));
    }

    /// <summary>
    ///     汉堡按钮：收起 / 展开角色行。
    /// </summary>
    [RelayCommand]
    private void ToggleCharacterRow()
    {
        IsCharacterRowCollapsed = !IsCharacterRowCollapsed;
    }

    /// <summary>
    ///     单选模式 chip。
    /// </summary>
    [RelayCommand]
    private void ToggleSingleSelect()
    {
        IsSingleSelect = !IsSingleSelect;
    }

    /// <summary>
    ///     列表视图 chip。
    /// </summary>
    [RelayCommand]
    private void ToggleListView()
    {
        IsListView = !IsListView;
    }

    /// <summary>
    ///     右键菜单：重新读一遍 Mod 文件夹 —— 在别处（资源管理器 / 游戏里）动过 Mod
    ///     之后不用关掉面板再开一次。
    /// </summary>
    [RelayCommand]
    private void ReloadMods()
    {
        Reload();
        StatusText = "已重新载入 Mod 列表";
    }

    /// <summary>
    ///     右键菜单：用资源管理器打开当前角色的 Mod 文件夹。
    ///     某个 Mod 没生效想直接去翻文件时最快的一条路。
    /// </summary>
    [RelayCommand]
    private void OpenCharacterFolder()
    {
        var character = SelectedTab?.Character;
        if (character is null)
        {
            StatusText = "先选一个角色再打开它的 Mod 文件夹";
            return;
        }

        var path = _skinManagerService.GetCharacterModListOrDefault(character.InternalName.Id)?.AbsModsFolderPath;

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            StatusText = $"找不到 {character.DisplayName} 的 Mod 文件夹";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            StatusText = $"已打开 {character.DisplayName} 的 Mod 文件夹";
        }
        catch (Exception e)
        {
            _logger.Error(e, "悬浮窗：打开 Mod 文件夹失败 —— {Path}", path);
            StatusText = $"打开文件夹失败：{e.Message}";
        }
    }

    /// <summary>
    ///     顶栏缩放百分比显示。
    /// </summary>
    public string UiScaleText => $"{Math.Round(UiScale * 100)}%";

    partial void OnUiScaleChanged(double value)
    {
        OnPropertyChanged(nameof(UiScaleText));
    }
}

/// <summary>
///     顶部角色标签：头像 + 名字 + 已启用/总数 + 选中态。
///     头像直接用角色管理页同一张图（character.ImageUri），没有就退回占位图。
/// </summary>
public partial class CharacterTabVm : ObservableObject
{
    public CharacterTabVm(ICharacter character, string countText)
    {
        Character = character;
        Name = character.DisplayName;
        CountText = countText;
        IconUri = character.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri;
    }

    public ICharacter Character { get; }

    public string Name { get; }

    /// <summary>角色头像，和角色管理页同源，永远有值（缺图给占位图）。</summary>
    public Uri IconUri { get; }

    [ObservableProperty] private string _countText;

    [ObservableProperty] private bool _isSelected;
}

public partial class ModSwitchItemVm : ObservableObject
{
    public ModSwitchItemVm(ICharacter character, CharacterSkinEntry entry)
    {
        Character = character;
        ModId = entry.Id;
        Name = entry.Mod.GetDisplayName();
        IsEnabled = entry.IsEnabled;
        FullPath = entry.Mod.FullPath;
        ModifiedTime = Directory.Exists(FullPath)
            ? Directory.GetLastWriteTime(FullPath)
            : DateTime.MinValue;

        Uri? image = null;
        if (entry.Mod.Settings.TryGetSettings(out var settings))
        {
            image = settings.ImagePath;
        }

        image ??= SkinModHelpers.DetectModPreviewImages(entry.Mod.FullPath).FirstOrDefault();

        ImagePath = image ?? ModSwitchItemVm.Placeholder;
    }

    /// <summary>
    ///     Mod 没有封面时用的占位图。
    /// </summary>
    public static Uri Placeholder { get; } =
        new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets\\ModPanePlaceholder.webp"));

    public ICharacter Character { get; }

    public Guid ModId { get; }

    public string Name { get; }

    public string FullPath { get; }

    /// <summary>
    ///     Mod 文件夹最后修改时间，"按时间"排序用。
    /// </summary>
    public DateTime ModifiedTime { get; }

    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty] private Uri? _imagePath;
}
