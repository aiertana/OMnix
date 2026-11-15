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

/// <summary>mod 行：角色目录下的一个 mod 开关单元（一级子文件夹，含其全部可注入 ini）。
/// 开关在 mod 行上——注入/还原这个 mod 的全部 ini（教程 bat 同款逻辑，单位放大到 mod 文件夹）。</summary>
public partial class OilFxRowVm : ObservableObject
{
    /// <summary>匹配到的角色库角色；目录名认不出时为 null。</summary>
    public IModdableObject? Character { get; init; }

    /// <summary>角色目录名（玩家目录常见中文名）。</summary>
    public string FolderName { get; init; } = string.Empty;

    /// <summary>mod 开关单元（一个 mod / mod 合集 = 角色目录下的一个子文件夹）。</summary>
    public OilFxModInfo Mod { get; init; } = null!;

    public string DisplayName => Mod.FolderName;

    /// <summary>相对角色目录的子路径（区分同名 mod 文件夹）。</summary>
    public string ModFolder => Mod.SubPath;

    /// <summary>mod 文件夹带 DISABLED_ 前缀（被启动器禁用，不加载），行用不同颜色区分。</summary>
    public bool IsDisabled => Mod.Disabled;

    /// <summary>禁用标记可见性。</summary>
    public Visibility DisabledBadgeVisibility => IsDisabled ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>禁用行整体压暗一档。</summary>
    public double RowOpacity => IsDisabled ? 0.55 : 1.0;

    private static readonly Brush NormalNameBrush = LoadBrush("TextFillColorPrimaryBrush");
    private static readonly Brush DisabledNameBrush = LoadBrush("TextFillColorDisabledBrush");
    private static readonly Brush DisabledBadgeTextBrush = LoadBrush("SystemFillColorAttentionBrush");

    private static Brush LoadBrush(string key)
    {
        try
        {
            return (Brush)Application.Current.Resources[key];
        }
        catch
        {
            return new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
    }

    /// <summary>mod 名字颜色：禁用的 mod 灰掉，正常 mod 正常颜色。</summary>
    public Brush NameBrush => IsDisabled ? DisabledNameBrush : NormalNameBrush;

    /// <summary>「已禁用」角标颜色。</summary>
    public Brush DisabledBadgeBrush => DisabledBadgeTextBrush;

    /// <summary>ini 数量摘要。</summary>
    public string ModsSummary =>
        Mod.Inis.Count == 0
            ? "没有可注入的 ini"
            : $"{Mod.Inis.Count} 个 ini · 已注入 {Mod.InjectedCount}"
              + (Mod.InjectedCount > 0 && !HasBackup ? " · 有 ini 未备份" : "");

    public bool HasBackup => Mod.HasBackup;

    public Visibility NotInjectedTipVisibility =>
        Mod.InjectedCount > 0 && !Mod.HasBackup ? Visibility.Visible : Visibility.Collapsed;

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

/// <summary>头像网格卡：一个角色目录（或手动文件夹）。点它，下方列出该角色的 mod 行。</summary>
public partial class OilFxCharacterCardVm : ObservableObject
{
    /// <summary>匹配到的角色库角色；目录名认不出时为 null。</summary>
    public IModdableObject? Character { get; init; }

    /// <summary>mod 目录的一级目录名。</summary>
    public string FolderName { get; init; } = string.Empty;

    /// <summary>是否来自"手动选择文件夹"。</summary>
    public bool IsManual { get; init; }

    /// <summary>该角色的全部 mod 单元（含禁用的）。</summary>
    public List<OilFxModInfo> Mods { get; init; } = [];

    /// <summary>冲突提示（原作者标注该角色 @发 等部位可能冲突）。</summary>
    public string ConflictTip { get; init; } = string.Empty;

    public bool HasConflict => ConflictTip.Length > 0;

    public string DisplayName => Character?.DisplayName is { Length: > 0 } name ? name : FolderName;

    public Uri? Avatar => Character?.ImageUri;

    // Image.Source 要 ImageSource，不能直接绑 Uri，这里做一次加载
    public ImageSource? AvatarImage => Avatar is null ? null : new BitmapImage(Avatar) { DecodePixelWidth = 128 };

    public bool HasAvatar => Avatar is not null;

    /// <summary>没匹配到角色库（无头像）时显示名字首字占位。</summary>
    public bool ShowFallbackInitial => !HasAvatar;

    public int ModCount => Mods.Count;

    public int InjectedCount => Mods.Count(m => m.Injected);

    public int DisabledCount => Mods.Count(m => m.Disabled);

    public bool HasMods => ModCount > 0;

    /// <summary>卡下方摘要：mod 数 / 已注入 / 禁用。</summary>
    public string Summary =>
        ModCount == 0
            ? "没有可注入的 mod"
            : $"{ModCount} 个 mod · 已注入 {InjectedCount}"
              + (DisabledCount > 0 ? $" · {DisabledCount} 个禁用" : "");
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

    /// <summary>角色头像网格（点一张卡，下方列出该角色的 mod）。</summary>
    public ObservableCollection<OilFxCharacterCardVm> CharacterCards { get; } = [];

    /// <summary>当前选中角色的 mod 行。</summary>
    public ObservableCollection<OilFxRowVm> ModRows { get; } = [];

    /// <summary>GridView 选中项（TwoWay）。选中 = 进入该角色的 mod 列表视图。</summary>
    [ObservableProperty]
    private OilFxCharacterCardVm? _selectedCard;

    partial void OnSelectedCardChanged(OilFxCharacterCardVm? value)
    {
        OnPropertyChanged(nameof(GridVisibility));
        OnPropertyChanged(nameof(ModListVisibility));
        OnPropertyChanged(nameof(SelectedCardDisplayName));
        RebuildModRows();
    }

    /// <summary>重建期间忽略 ToggleSwitch 重入。</summary>
    public bool IsRebuilding { get; private set; }

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

        // 记住当前选中角色，重建后保持
        var previousKey = SelectedCard is null ? string.Empty : OilFxName.Normalize(SelectedCard.FolderName);

        CharacterCards.Clear();
        ModRows.Clear();

        // 自动：扫整个 Mods 根，一级目录 = 一个角色卡；已注入过油光的排在最前面
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
            AddCard(group.GroupName, group.Mods, conflicts, characterIndex, isManual: false);
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
            AddCard(groupName + "（手动）", mods, conflicts, characterIndex, isManual: true);
        }

        // 恢复选中（开关操作后重建列表时保持当前角色，不自动跳到第一张——
        // 没选中的话就停在角色网格，等用户点头像进 mod 列表）
        SelectedCard = CharacterCards.FirstOrDefault(c =>
            OilFxName.Normalize(c.FolderName) == previousKey);

        RebuildModRows();
        UpdateHookStatus();
    }

    private void AddCard(
        string groupName,
        List<OilFxModInfo> mods,
        HashSet<string> conflicts,
        Dictionary<string, IModdableObject> characterIndex,
        bool isManual)
    {
        var normalized = OilFxName.Normalize(groupName);
        var matched = normalized.Length > 0 && characterIndex.TryGetValue(normalized, out var c) ? c : null;

        CharacterCards.Add(new OilFxCharacterCardVm
        {
            Character = matched,
            FolderName = groupName,
            IsManual = isManual,
            Mods = mods,
            ConflictTip = normalized.Length > 0 && conflicts.Contains(normalized)
                ? "与 @发 部位 mod 可能冲突"
                : string.Empty
        });
    }

    /// <summary>按选中角色重建 mod 行（禁用的 mod 直接列出，颜色区分）。
    /// 选中 = 进入该角色的 mod 列表视图；清空 = 回角色网格。</summary>
    private void RebuildModRows()
    {
        ModRows.Clear();

        var card = SelectedCard;
        if (card is null)
        {
            EmptyModListVisibility = Visibility.Collapsed;
            SelectedConflictTip = string.Empty;
            OnPropertyChanged(nameof(SelectedConflictVisibility));
            return;
        }

        EmptyModListVisibility = card.Mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectedConflictTip = card.ConflictTip;
        OnPropertyChanged(nameof(SelectedConflictVisibility));

        foreach (var mod in card.Mods)
        {
            ModRows.Add(new OilFxRowVm
            {
                Character = card.Character,
                FolderName = card.FolderName,
                Mod = mod,
                IsEnabled = mod.Injected
            });
        }
    }

    /// <summary>角色网格视图可见性：没选中角色时显示网格。</summary>
    public Visibility GridVisibility =>
        SelectedCard is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>mod 列表视图可见性：选中角色后整个区域切成它的 mod 列表。</summary>
    public Visibility ModListVisibility =>
        SelectedCard is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>mod 列表视图标题：选中角色名。</summary>
    public string SelectedCardDisplayName => SelectedCard?.DisplayName ?? string.Empty;

    /// <summary>返回角色网格。</summary>
    [RelayCommand]
    private void BackToCharacters() => SelectedCard = null;

    /// <summary>没选中角色时显示占位提示。</summary>
    [ObservableProperty]
    private Visibility _emptyModListVisibility = Visibility.Collapsed;

    /// <summary>选中角色的冲突提示（原作者标注 @发 部位可能冲突）。</summary>
    [ObservableProperty]
    private string _selectedConflictTip = string.Empty;

    /// <summary>冲突提示可见性。</summary>
    public Visibility SelectedConflictVisibility =>
        string.IsNullOrEmpty(SelectedConflictTip) ? Visibility.Collapsed : Visibility.Visible;

    private void UpdateHookStatus()
    {
        var allMods = CharacterCards.SelectMany(c => c.Mods).ToList();
        var iniTotal = allMods.Sum(m => m.Inis.Count);
        var iniInjected = allMods.Sum(m => m.InjectedCount);
        HookStatus = allMods.Count == 0
            ? "没扫到可注入的 mod（先装角色 mod，或用「手动选择」指定文件夹）"
            : $"共 {CharacterCards.Count} 个角色 / {allMods.Count} 个 mod / {iniTotal} 个 ini，已注入 {iniInjected} 个 ini（灰色行 = 被启动器禁用）";
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

        if (row is null)
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

        if (row is null)
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
            foreach (var mod in CharacterCards.SelectMany(c => c.Mods).ToList())
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
