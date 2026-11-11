using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Services.GameBanana;
using OMniX.Core.Services.GameBanana.OilFx;

using Serilog;

namespace OMniX.WinUI.ViewModels;

/// <summary>列表行：角色分组头 或 单个 mod（单个 .ini）。
/// 开关在 mod 行上——注入/还原这一个 ini（教程 bat 同款逻辑），角色头只做分组展示。</summary>
public partial class OilFxRowVm : ObservableObject
{
    /// <summary>true = 角色分组头；false = 单个 mod 行。</summary>
    public bool IsGroupHeader { get; init; }

    public bool IsModRow => !IsGroupHeader;

    /// <summary>匹配到的角色库角色；目录名认不出时为 null。</summary>
    public IModdableObject? Character { get; init; }

    /// <summary>mod 目录的一级目录名（玩家目录常见中文名）。</summary>
    public string FolderName { get; init; } = string.Empty;

    /// <summary>是否来自"手动选择文件夹"（分组头用）。</summary>
    public bool IsManual { get; init; }

    /// <summary>分组头：组下全部 mod 文件夹；mod 行：自身。</summary>
    public IReadOnlyList<OilFxModInfo> GroupMods { get; init; } = [];

    /// <summary>分组头：组内被启动器禁用（DISABLED_ 前缀）而未列出的 mod 数。</summary>
    public int DisabledModsCount { get; init; }

    /// <summary>mod 行的单个 mod 文件夹（一个 mod = 一个文件夹，含其全部可注入 ini）。</summary>
    public OilFxModInfo Mod { get; init; } = null!;

    public string DisplayName => Character?.DisplayName is { Length: > 0 } name && IsGroupHeader
        ? name
        : IsGroupHeader
            ? FolderName
            : Mod.FolderName;

    /// <summary>mod 行：相对角色目录的子路径（区分同名 mod 文件夹）。</summary>
    public string ModFolder => IsGroupHeader ? string.Empty : Mod.SubPath;

    public Uri? Avatar => Character?.ImageUri;

    // Image.Source 要 ImageSource，不能直接绑 Uri，这里做一次加载
    public ImageSource? AvatarImage => Avatar is null ? null : new BitmapImage(Avatar);

    public bool HasAvatar => Avatar is not null && IsGroupHeader;

    /// <summary>没匹配到角色库（无头像）时显示名字首字占位。</summary>
    public bool ShowFallbackInitial => IsGroupHeader && !HasAvatar;

    /// <summary>分组头：mod 数量摘要；mod 行：ini 数量摘要。</summary>
    public string ModsSummary =>
        IsGroupHeader
            ? GroupMods.Count == 0
                ? "没有可注入的 mod"
                : $"{GroupMods.Count} 个 mod · 已注入 {GroupMods.Count(m => m.Injected)}"
                  + (DisabledModsCount > 0 ? $" · 另有 {DisabledModsCount} 个禁用的 mod 未列出" : "")
            : $"{Mod.Inis.Count} 个 ini · 已注入 {Mod.InjectedCount}"
              + (Mod.InjectedCount > 0 && !HasBackup ? " · 有 ini 未备份" : "");

    public bool HasBackup => IsModRow && Mod.HasBackup;

    /// <summary>冲突提示（原作者标注该角色 @发 等部位可能冲突，分组头用）。</summary>
    public string ConflictTip { get; init; } = string.Empty;

    public bool HasConflict => ConflictTip.Length > 0;

    public Visibility NotInjectedTipVisibility =>
        IsModRow && Mod.InjectedCount > 0 && !Mod.HasBackup ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>mod 行所属的分组头（分组头自身为 null）。展开时子行才真正加入列表。</summary>
    public OilFxRowVm? Group { get; init; }

    /// <summary>分组头：是否展开。默认收起，点分组头切换（由 VM 增删子行实现）。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>分组头展开箭头：展开 ▾ / 收起 ▸。</summary>
    public string ChevronGlyph => IsExpanded ? "\uE76C" : "\uE76B";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ChevronGlyph));

    /// <summary>分组头持有的子 mod 行（mod 行此集合为空）。</summary>
    public List<OilFxRowVm> ChildRows { get; } = [];

    /// <summary>mod 行当前注入状态（驱动灯开关）。</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>mod 行操作中（注入/还原跑后台线程，行底显示进度条）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>进度条百分比 0-100。</summary>
    [ObservableProperty]
    private double _busyProgress;

    /// <summary>进度说明文字（"注入中 2/6…"）。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>操作中禁用开关和还原按钮，防止重复点击。</summary>
    public bool CanToggle => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(BusyVisibility));
        OnPropertyChanged(nameof(CanToggle));
        if (!value)
        {
            BusyProgress = 0;
            BusyText = string.Empty;
        }
    }
}

public partial class OilFXViewModel : ObservableObject
{
    private readonly IApiGameBananaClient _apiGameBanana;
    private readonly OMniX.Core.Services.ArchiveService _archiveService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly ILogger _logger = Log.ForContext<OilFXViewModel>();

    private OilFxService _oilFx = null!;

    /// <summary>手动添加的 mod 文件夹（刷新后仍保留）。</summary>
    private readonly List<string> _manualFolders = [];

    public OilFXViewModel(
        IApiGameBananaClient apiGameBanana,
        OMniX.Core.Services.ArchiveService archiveService,
        ISkinManagerService skinManagerService,
        IGameService gameService)
    {
        _apiGameBanana = apiGameBanana;
        _archiveService = archiveService;
        _skinManagerService = skinManagerService;
        _gameService = gameService;
        _ = InitializeAsync();
    }

    public string ModsRoot => _skinManagerService.ActiveModsFolderPath;

    /// <summary>试用提示：目前仅适用于鸣潮。</summary>
    public string TrialTip => "试用阶段：目前仅适用于鸣潮（WWMI），其他游戏暂不支持";

    [ObservableProperty] private string _frameworkStatus = "检查中…";

    [ObservableProperty] private string _versionStatus = string.Empty;

    [ObservableProperty] private bool _hasNewVersion;

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _busyText = string.Empty;

    [ObservableProperty] private double _installProgress;

    public bool CanInstall => !IsBusy;

    [ObservableProperty] private double _oilStrength = 0.7;

    [ObservableProperty] private bool _realisticOil;

    /// <summary>油光款式（Matcap.ini $oilSkin 0-6）：0 禁用 / 1 默认 / 2-5 四种油光 / 6 湿身。游戏内 Alt+0 可临时循环。</summary>
    [ObservableProperty] private int _oilSkin = 2;

    public string[] SkinOptions { get; } =
    [
        "0 · 禁用",
        "1 · 默认油光",
        "2 · 油光 A",
        "3 · 油光 B",
        "4 · 油光 C",
        "5 · 油光 D",
        "6 · 湿身",
    ];

    [ObservableProperty] private string _hookStatus = string.Empty;

    public string SourceUrl => OilFxHooks.SourceUrl;

    public ObservableCollection<OilFxRowVm> Rows { get; } = [];

    private async Task InitializeAsync()
    {
        try
        {
            _oilFx = new OilFxService(_apiGameBanana, _archiveService, _logger);

            await RefreshFrameworkAsync().ConfigureAwait(true);
            // 启动即应用一次参数 + Alt+小键盘热键（框架被外部覆盖/更新后也能自愈）
            ApplyParamsToFramework();
            BuildCharacterList();
            await CheckLatestAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            _logger.Error(e, "OilFX 窗口初始化失败");
            FrameworkStatus = $"初始化失败：{e.Message}";
        }
    }

    private void BuildCharacterList()
    {
        // 列表重建期间，灯开关的初始 IsOn 赋值触发 Toggled 事件必须被忽略
        IsRebuilding = true;
        try
        {
            BuildCharacterListCore();
        }
        finally
        {
            IsRebuilding = false;
        }
    }

    private void BuildCharacterListCore()
    {
        // 冲突提示表崩了也不能阻断扫描列表
        HashSet<string> conflicts;
        try
        {
            conflicts = OilFxHooks.ConflictTip.Split('+')
                .Select(OilFxName.Normalize)
                .ToHashSet();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "OilFX 冲突提示表解析失败，跳过冲突提示");
            conflicts = [];
        }

        // 角色库索引：Normalize 名 → 角色（目录名常见中文名，与 DisplayName/Keys 比对）
        var characterIndex = new Dictionary<string, IModdableObject>();
        foreach (var character in _gameService.GetCharacters(includeDisabled: true))
        {
            var names = new[] { character.DisplayName, character.InternalName.ToString(), character.ModFilesName }
                .Concat(character.Keys)
                .Select(OilFxName.Normalize)
                .Where(n => n.Length > 0);
            foreach (var n in names)
            {
                characterIndex.TryAdd(n, character);
            }
        }

        Rows.Clear();

        // 自动：扫整个 Mods 根，一级目录 = 一组；已注入过油光的组排在最前面
        var groups = _oilFx.ScanModsRoot(ModsRoot);
        groups = groups
            .OrderByDescending(g => g.Mods.Any(m => m.Injected))
            .ThenBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _logger.Information(
            "OilFX 自动扫描 {Root}: {Groups} 组 / {Count} 个 mod 文件夹",
            ModsRoot, groups.Count, groups.Sum(g => g.Mods.Count));
        foreach (var group in groups)
        {
            AddGroup(group.GroupName, group.Mods, conflicts, characterIndex, isManual: false, disabledCount: group.DisabledModsCount);
        }

        // 手动：用户选的文件夹
        foreach (var folder in _manualFolders)
        {
            var mods = _oilFx.ScanFolder(folder);
            if (mods.Count == 0)
            {
                continue;
            }

            var groupName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            AddGroup(groupName + "（手动）", mods, conflicts, characterIndex, isManual: true);
        }

        UpdateHookStatus();
    }

    private void AddGroup(
        string groupName,
        List<OilFxModInfo> mods,
        HashSet<string> conflicts,
        Dictionary<string, IModdableObject> characterIndex,
        bool isManual,
        int disabledCount = 0)
    {
        var normalized = OilFxName.Normalize(groupName);
        var matched = normalized.Length > 0 && characterIndex.TryGetValue(normalized, out var c) ? c : null;

        // 分组头行（默认收起，点击展开 mod 列表）
        var header = new OilFxRowVm
        {
            IsGroupHeader = true,
            Character = matched,
            FolderName = groupName,
            IsManual = isManual,
            GroupMods = mods,
            DisabledModsCount = disabledCount,
            ConflictTip = normalized.Length > 0 && conflicts.Contains(normalized)
                ? "与 @发 部位 mod 可能冲突"
                : string.Empty
        };
        Rows.Add(header);

        // 每个 mod 文件夹一行，开关管这一个 mod 的全部 ini。
        // mod 行不直接进列表——展开分组头时才插入（收起时列表里只有分组头，不占空间）
        foreach (var mod in mods)
        {
            var row = new OilFxRowVm
            {
                IsGroupHeader = false,
                Character = matched,
                FolderName = groupName,
                GroupMods = mods,
                Group = header,
                Mod = mod,
                IsEnabled = mod.Injected
            };
            header.ChildRows.Add(row);
        }

        // 刷新前已展开的组保持展开
        if (_expandedFolders.Contains(FolderKey(groupName)))
        {
            ExpandGroup(header);
        }
    }

    /// <summary>展开状态的组名集合（刷新/重建后保持展开）。</summary>
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);

    private static string FolderKey(string folderName) => OilFxName.Normalize(folderName);

    /// <summary>展开分组头：把该组所有 mod 行插到分组头之后。</summary>
    public void ExpandGroup(OilFxRowVm header)
    {
        if (!header.IsGroupHeader || header.IsExpanded)
        {
            return;
        }

        header.IsExpanded = true;
        _expandedFolders.Add(FolderKey(header.FolderName));
        var index = Rows.IndexOf(header);
        for (var i = 0; i < header.ChildRows.Count; i++)
        {
            Rows.Insert(index + 1 + i, header.ChildRows[i]);
        }
    }

    /// <summary>收起分组头：把该组所有 mod 行从列表移除（不占空间）。</summary>
    public void CollapseGroup(OilFxRowVm header)
    {
        if (!header.IsGroupHeader || !header.IsExpanded)
        {
            return;
        }

        header.IsExpanded = false;
        _expandedFolders.Remove(FolderKey(header.FolderName));
        foreach (var row in header.ChildRows)
        {
            Rows.Remove(row);
        }
    }

    /// <summary>点分组头：切换展开/收起。</summary>
    public void ToggleExpand(OilFxRowVm header)
    {
        if (header.IsExpanded)
        {
            CollapseGroup(header);
        }
        else
        {
            ExpandGroup(header);
        }
    }

    private void UpdateHookStatus()
    {
        var mods = Rows.Where(r => r.IsModRow).ToList();
        var modCount = mods.Count;
        var iniTotal = mods.Sum(m => m.Mod.Inis.Count);
        var iniInjected = mods.Sum(m => m.Mod.InjectedCount);
        HookStatus = modCount == 0
            ? "没扫到可注入的 mod（先装角色 mod，或用「手动选择」指定文件夹）"
            : $"共 {modCount} 个 mod / {iniTotal} 个 ini，已注入 {iniInjected} 个 ini";
    }

    /// <summary>查线上最新版本，与本地已装对比。</summary>
    [RelayCommand]
    private async Task CheckLatestAsync()
    {
        try
        {
            var latest = await _oilFx.GetLatestFileAsync().ConfigureAwait(true);
            if (latest is null)
            {
                VersionStatus = "GameBanana 查询失败（网络/代理）";
                return;
            }

            var installed = _oilFx.State.InstalledFileId > 0;
            HasNewVersion = installed && latest.FileId != _oilFx.State.InstalledFileId;
            VersionStatus = installed
                ? HasNewVersion
                    ? $"有新版本！本地 {_oilFx.State.FileName} → 线上 {latest.FileName}"
                    : $"已是最新（{_oilFx.State.FileName}）"
                : $"未安装，线上最新 {latest.FileName}";
        }
        catch (Exception e)
        {
            _logger.Warning(e, "OilFX 查询线上版本失败");
            VersionStatus = "GameBanana 查询失败（检查加速器/代理）";
        }
    }

    private async Task RefreshFrameworkAsync()
    {
        var installed = _oilFx.IsFrameworkInstalled(ModsRoot);
        FrameworkStatus = installed ? "框架已安装" : "框架未安装";
        if (installed)
        {
            (OilStrength, RealisticOil, OilSkin) = _oilFx.ReadParams(ModsRoot);
        }
    }

    [RelayCommand]
    private async Task InstallOrUpdateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = "下载中…";
        InstallProgress = 0;
        try
        {
            var fileName = await _oilFx.InstallOrUpdateAsync(
                ModsRoot,
                new Progress<int>(p =>
                {
                    InstallProgress = p;
                    BusyText = $"下载中… {p}%";
                })).ConfigureAwait(true);

            await RefreshFrameworkAsync().ConfigureAwait(true);
            await CheckLatestAsync().ConfigureAwait(true);
            ApplyParamsToFramework();
            FrameworkStatus = $"框架已安装（{fileName}）";
        }
        catch (Exception e)
        {
            _logger.Error(e, "OilFX 安装/更新失败");
            BusyText = string.Empty;
            FrameworkStatus = $"安装失败：{e.InnerException?.Message ?? e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>单个 mod 开关：开 = 注入这个 mod 文件夹的全部 ini（bat 模式 1，Component ≥ 3），关 = 全部从 .bak 还原。
    /// IO 跑后台线程，行底显示进度条。</summary>
    [RelayCommand]
    private async Task ToggleMod(OilFxRowVm? row)
    {
        // 列表重建时 ToggleSwitch 的初始 IsOn 赋值会触发 Toggled → 无限递归 → 栈溢出闪退
        // （窗口层还有 IsOn==IsEnabled 的一致性检查兜底）
        if (_isToggling || IsRebuilding)
        {
            return;
        }

        if (row is null || row.IsGroupHeader)
        {
            return;
        }

        if (!_oilFx.IsFrameworkInstalled(ModsRoot))
        {
            HookStatus = "请先安装框架";
            return;
        }

        _isToggling = true;
        row.IsBusy = true;
        try
        {
            var mod = row.Mod;
            var target = row.IsEnabled;
            var total = mod.Inis.Count;
            System.IProgress<int> progress = new Progress<int>(done =>
            {
                row.BusyProgress = total > 0 ? done * 100.0 / total : 0;
                row.BusyText = $"{(target ? "注入" : "还原")}中 {done}/{total}…";
            });

            var changed = await Task.Run(() =>
            {
                var n = 0;
                var done = 0;
                foreach (var ini in mod.Inis)
                {
                    if (target && !ini.Injected)
                    {
                        n += _oilFx.InjectIni(ini.IniPath);
                    }
                    else if (!target && ini.Injected && ini.HasBackup)
                    {
                        if (_oilFx.RestoreIni(ini.IniPath))
                        {
                            n++;
                        }
                    }

                    done++;
                    progress.Report(done);
                }

                return n;
            }).ConfigureAwait(true);

            _logger.Information("OilFX mod 开关 {Mod} → {Target}，{Count} 个 ini，改动 {Lines} 行",
                mod.FolderPath, target ? "注入" : "还原", total, changed);

            BuildCharacterList();
            ApplyParamsToFramework();

            if (changed == 0)
            {
                HookStatus = target
                    ? "该 mod 已经是注入状态，无需重复注入"
                    : "该 mod 没有已注入的 ini（没有 .bak 可还原）";
            }
        }
        catch (Exception e)
        {
            _logger.Error(e, "OilFX mod 开关操作失败");
            HookStatus = $"操作失败：{e.Message}";
            BuildCharacterList();
        }
        finally
        {
            row.IsBusy = false;
            _isToggling = false;
        }
    }

    /// <summary>单个 mod 行的还原按钮：无条件把该 mod 所有有 .bak 的 ini 还原。</summary>
    [RelayCommand]
    private async Task RestoreMod(OilFxRowVm? row)
    {
        if (_isToggling || IsRebuilding)
        {
            return;
        }

        if (row is null || row.IsGroupHeader)
        {
            return;
        }

        _isToggling = true;
        row.IsBusy = true;
        try
        {
            var total = row.Mod.Inis.Count;
            System.IProgress<int> progress = new Progress<int>(done =>
            {
                row.BusyProgress = total > 0 ? done * 100.0 / total : 0;
                row.BusyText = $"还原中 {done}/{total}…";
            });

            var restored = await Task.Run(() =>
            {
                var n = 0;
                var done = 0;
                foreach (var ini in row.Mod.Inis)
                {
                    if (_oilFx.RestoreIni(ini.IniPath))
                    {
                        n++;
                    }

                    done++;
                    progress.Report(done);
                }

                return n;
            }).ConfigureAwait(true);

            _logger.Information("OilFX 单行还原 {Mod}: {Count} 个 ini", row.Mod.FolderPath, restored);
            BuildCharacterList();
            HookStatus = restored > 0
                ? $"已还原 {restored} 个 ini"
                : "该 mod 没有 .bak 备份（从未注入过，无需还原）";
        }
        catch (Exception e)
        {
            _logger.Error(e, "OilFX 单行还原失败");
            HookStatus = $"还原失败：{e.Message}";
            BuildCharacterList();
        }
        finally
        {
            row.IsBusy = false;
            _isToggling = false;
        }
    }

    /// <summary>手动添加 mod 文件夹（窗口层弹 FolderPicker 后调这里）。</summary>
    public void AddManualFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        if (!_manualFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _manualFolders.Add(folder);
        }

        BuildCharacterList();
        ApplyParamsToFramework();
        _logger.Information("OilFX 手动添加文件夹: {Folder}", folder);
    }

    /// <summary>手动刷新扫描结果（新装了 mod 之后点）。</summary>
    [RelayCommand]
    private void Rescan()
    {
        BuildCharacterList();
        ApplyParamsToFramework();
    }

    /// <summary>一键还原：所有已注入且有 .bak 的 mod ini 全部还原。</summary>
    [RelayCommand]
    private void RestoreAll()
    {
        if (_isToggling || IsRebuilding)
        {
            return;
        }

        _isToggling = true;
        try
        {
            var restored = 0;
            foreach (var mod in Rows.Where(r => r.IsModRow).Select(r => r.Mod).ToList())
            {
                foreach (var ini in mod.Inis)
                {
                    if (_oilFx.RestoreIni(ini.IniPath))
                    {
                        restored++;
                    }
                }
            }

            _logger.Information("OilFX 一键还原 {Count} 个 mod ini", restored);
            HookStatus = $"已还原 {restored} 个 mod ini";
            BuildCharacterList();
        }
        catch (Exception e)
        {
            _logger.Error(e, "OilFX 一键还原失败");
            HookStatus = $"还原失败：{e.Message}";
            BuildCharacterList();
        }
        finally
        {
            _isToggling = false;
        }
    }

    /// <summary>重建列表期间为 true——灯开关被 ListView 重建时 Toggled 会重入，需忽略。</summary>
    public bool IsRebuilding { get; private set; }

    private bool _isToggling;

    private void ApplyParamsToFramework()
    {
        _oilFx.WriteParams(ModsRoot, OilStrength, RealisticOil, OilSkin);
    }

    partial void OnOilSkinChanged(int value) => ApplyParamsToFramework();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInstall));

    partial void OnOilStrengthChanged(double value) => ApplyParamsToFramework();

    partial void OnRealisticOilChanged(bool value) => ApplyParamsToFramework();
}
