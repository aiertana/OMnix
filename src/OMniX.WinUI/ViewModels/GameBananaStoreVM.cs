using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Services.GameBanana;
using OMniX.Core.Services.GameBanana.ApiModels;
using OMniX.Core.Services.GameBanana.Models;
using OMniX.WinUI.Helpers;
using OMniX.WinUI.Services.AppManagement;
using Serilog;

namespace OMniX.WinUI.ViewModels;

/// <summary>
///     GameBanana 商店（功能区插件）：浏览 / 搜索当前游戏的 Mod，
///     选中后打开既有的「从 GameBanana 下载 Mod」窗口完成下载安装。
///     本插件只负责逛，下载安装全部复用 GameBananaCoreService + ModPageVM 的现成链路。
/// </summary>
public partial class GameBananaStoreVM : ObservableRecipient
{
    private readonly ILogger _logger = Log.ForContext<GameBananaStoreVM>();
    private readonly GameBananaCoreService _gameBanana = App.GetService<GameBananaCoreService>();
    private readonly IGameService _gameService = App.GetService<IGameService>();
    private readonly IWindowManagerService _windowManager = App.GetService<IWindowManagerService>();

    /// <summary>GameBanana 的游戏编号。WuWa 已确认，其余游戏待补（补了就自动支持）。</summary>
    private static readonly Dictionary<string, int> GameIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WuWa"] = 20357,
    };

    private const int PerPage = 24;
    private int _page = 1;
    private bool _hasMore = true;
    private string? _lastQuery;
    private string? _lastSort;

    public ObservableCollection<StoreModCardVm> Results { get; } = new();
    public ObservableCollection<CharacterOptionVm> Characters { get; } = new();
    public ObservableCollection<CategoryOptionVm> RootCategories { get; } = new();
    public ObservableCollection<CategoryOptionVm> SubCategories { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedSortIndex;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasMoreItems;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private CategoryOptionVm? _selectedRootCategory;
    [ObservableProperty] private CategoryOptionVm? _selectedSubCategory;
    [ObservableProperty] private bool _hasSubCategories;

    partial void OnSelectedSubCategoryChanged(CategoryOptionVm? value) =>
        OnPropertyChanged(nameof(SelectedSubCategoryLabel));

    /// <summary>角色按钮上显示的当前选择（未选时只显示「角色」）。</summary>
    public string SelectedSubCategoryLabel =>
        SelectedSubCategory is null ? "角色" : $"角色: {SelectedSubCategory.DisplayName}";

    /// <summary>角色选择面板的筛选词（网页版「筛选...」输入框）。</summary>
    [ObservableProperty] private string _subCategoryFilter = string.Empty;

    /// <summary>角色选择面板里显示的（过滤后）列表。</summary>
    public ObservableCollection<CategoryOptionVm> FilteredSubCategories { get; } = new();

    /// <summary>当前一级下的全部子分类（过滤数据源）。</summary>
    private readonly List<CategoryOptionVm> _allSubCategories = new();

    /// <summary>加载代次：搜索条件变化时 +1，过期结果（旧代次）直接丢弃，防并发写串。</summary>
    private int _loadGeneration;

    partial void OnSubCategoryFilterChanged(string value) => RebuildFilteredSubCategories();

    private void RebuildFilteredSubCategories()
    {
        FilteredSubCategories.Clear();
        var filter = SubCategoryFilter?.Trim() ?? "";
        IEnumerable<CategoryOptionVm> query = _allSubCategories;
        if (filter.Length > 0)
        {
            query = query.Where(c => c.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var c in query)
        {
            FilteredSubCategories.Add(c);
        }
    }

    /// <summary>
    ///     点一级分类 pill（全部 Mod / 皮肤 / …）：互斥选中、预载二级角色列表，
    ///     然后重刷列表。子分类加载是异步的，所以搜索放在它完成后。
    /// </summary>
    [RelayCommand]
    private async Task SelectRootCategoryAsync(CategoryOptionVm? root)
    {
        if (root is null || root == SelectedRootCategory)
        {
            return;
        }

        foreach (var c in RootCategories)
        {
            c.IsSelected = c == root;
        }

        SelectedRootCategory = root;
        SelectedSubCategory = null;

        _allSubCategories.Clear();
        SubCategories.Clear();
        HasSubCategories = false;
        SubCategoryFilter = string.Empty;

        if (root.Id is { } rootId)
        {
            try
            {
                var subs = await _gameBanana.GetSubCategoriesAsync(rootId).ConfigureAwait(true);
                if (subs.Count > 0)
                {
                    // 首项「全部」= 该根分类整体，后面才是各角色
                    var allItem = new CategoryOptionVm("全部", new GbCategoryItem(rootId, root.DisplayName), isSelected: true);
                    _allSubCategories.Add(allItem);
                    SubCategories.Add(allItem);
                    foreach (var sub in subs)
                    {
                        var vm = new CategoryOptionVm(sub.Name, sub);
                        _allSubCategories.Add(vm);
                        SubCategories.Add(vm);
                    }

                    HasSubCategories = true;
                    SelectedSubCategory = allItem;
                    RebuildFilteredSubCategories();
                }
            }
            catch (Exception e)
            {
                _logger.Warning(e, "商店加载子分类失败");
            }
        }

        // 无子分类的一级（Other/Misc、UI）也要刷新列表
        SearchCommand.Execute(null);
    }

    /// <summary>角色面板里点了一个角色：互斥选中并重刷。</summary>
    [RelayCommand]
    private void SelectSubCategory(CategoryOptionVm? sub)
    {
        if (sub is null)
        {
            return;
        }

        foreach (var c in SubCategories)
        {
            c.IsSelected = c == sub;
        }

        SelectedSubCategory = sub;
        SearchCommand.Execute(null);
    }

    /// <summary>顶部角色面板对应的根分类 Id（Skins），面板打开时懒加载其子分类。</summary>
    private int? _pickerRootCategoryId;

    /// <summary>名字规范化：去空格/连字符/下划线 + 小写，用于角色中英名匹配。</summary>
    private static string NormalizeName(string name) =>
        name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

    /// <summary>
    ///     打开顶部角色面板前确保数据已就绪：面板数据独立于 pill 二级，
    ///     首次打开时按 Skins 根分类拉一次子分类（失败静默，面板为空列表）。
    /// </summary>
    [RelayCommand]
    private async Task EnsureCharacterPickerAsync()
    {
        if (_allSubCategories.Count > 0)
        {
            return;
        }

        var skinsRoot = RootCategories.FirstOrDefault(
            c => c.DisplayName.Equals("Skins", StringComparison.OrdinalIgnoreCase));
        if (skinsRoot?.Id is not { } rootId)
        {
            return;
        }

        _pickerRootCategoryId = rootId;
        try
        {
            var subs = await _gameBanana.GetSubCategoriesAsync(rootId).ConfigureAwait(true);
            _allSubCategories.Clear();
            SubCategories.Clear();
            // 首项「全部」= Skins 整体
            _allSubCategories.Add(new CategoryOptionVm("全部", new GbCategoryItem(rootId, skinsRoot.DisplayName)));
            foreach (var sub in subs)
            {
                _allSubCategories.Add(new CategoryOptionVm(sub.Name, sub));
            }

            RebuildFilteredSubCategories();
            RebuildPillItems();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "角色面板加载子分类失败");
        }
    }

    /// <summary>
    ///     顶部角色面板里选中一个角色：同步 pill 行选中态 + 切 Skins + 搜索。
    /// </summary>
    public void SelectCharacterFromPicker(CategoryOptionVm sub)
    {
        // 面板里的角色项与 PillItems 是同一实例（面板数据喂给 pill 行）
        SelectPillItem(PillItems.FirstOrDefault(p => p == sub) ?? sub);
    }

    /// <summary>x:Bind 在 Window 根上不支持带 Converter，可见性直接给现成的。</summary>
    public Microsoft.UI.Xaml.Visibility LoadMoreVisibility =>
        HasMoreItems ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>二级分类下拉的可见性（仅「皮肤」这类有子分类的一级才显示）。</summary>
    public Microsoft.UI.Xaml.Visibility SubCategoriesVisibility =>
        HasSubCategories ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnHasMoreItemsChanged(bool value) =>
        OnPropertyChanged(nameof(LoadMoreVisibility));

    partial void OnHasSubCategoriesChanged(bool value) =>
        OnPropertyChanged(nameof(SubCategoriesVisibility));
    [ObservableProperty] private CharacterOptionVm? _selectedCharacter;
    [ObservableProperty] private string _gameName = GameContext.Current.FallbackName;

    public GameBananaStoreVM()
    {
        LoadCharacters();
    }

    /// <summary>
    ///     窗口打开后自动调：拉分类 + 预载角色 pill 行，然后自动定位到
    ///     默认安装角色（底部「安装到角色」选中的那个）并加载其 mod；
    ///     匹配不到就退回加载最新发布。
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadCategoriesAsync().ConfigureAwait(true);
        await EnsureCharacterPickerAsync().ConfigureAwait(true);
        RebuildPillItems();

        var defaultCharacter = SelectedCharacter?.Moddable;
        if (defaultCharacter is not null)
        {
            var englishName = defaultCharacter is ICharacter character
                ? character.InternalName.ToString()
                : defaultCharacter.ModFilesName;
            var normalized = NormalizeName(englishName);
            var match = PillItems.FirstOrDefault(p =>
                p.Id is not null && p.Id != _pickerRootCategoryId &&
                NormalizeName(p.DisplayName) == normalized);
            if (match is not null)
            {
                SelectPillItem(match);
                return;
            }
        }

        await SearchAsync().ConfigureAwait(true);
    }

    /// <summary>
    ///     pill 行内容（横向角色列表）：「全部 Mod」+ 全部角色 + Other/Misc + UI。
    ///     角色项来自面板数据（EnsureCharacterPickerAsync 预载），
    ///     Other/Misc 与 UI 直接复用 RootCategories 实例以保持选中联动。
    /// </summary>
    public ObservableCollection<CategoryOptionVm> PillItems { get; } = new();

    private void RebuildPillItems()
    {
        var selectedId = PillItems.FirstOrDefault(p => p.IsSelected)?.Id;
        PillItems.Clear();

        var allModsItem = new CategoryOptionVm("全部 Mod", null, isSelected: selectedId is null);
        PillItems.Add(allModsItem);
        foreach (var sub in _allSubCategories)
        {
            // 跳过面板首项「全部」(Skins 整体)，pill 行用「全部 Mod」兜底全量
            PillItems.Add(sub);
        }

        foreach (var root in RootCategories.Where(r =>
                     r.Id is not null && r.Id != _pickerRootCategoryId))
        {
            PillItems.Add(root);
        }
    }

    /// <summary>
    ///     pill 行点击：全部 Mod → 按游戏浏览；角色 → Skins + 该角色；
    ///     Other/Misc、UI → 对应根分类。统一维护两组集合的选中态。
    /// </summary>
    public void SelectPillItem(CategoryOptionVm? item)
    {
        if (item is null)
        {
            return;
        }

        var skinsRoot = _pickerRootCategoryId is { } rootId
            ? RootCategories.FirstOrDefault(c => c.Id == rootId)
            : null;
        var isRootCategory = RootCategories.Contains(item);

        CategoryOptionVm? rootSelection;
        CategoryOptionVm? subSelection = null;

        if (item.Id is null)
        {
            rootSelection = RootCategories.FirstOrDefault(c => c.Id is null);
        }
        else if (isRootCategory)
        {
            rootSelection = item;
        }
        else
        {
            rootSelection = skinsRoot;
            subSelection = item;
        }

        foreach (var c in PillItems)
        {
            c.IsSelected = c == item;
        }

        foreach (var c in RootCategories)
        {
            c.IsSelected = c == rootSelection;
        }

        SelectedRootCategory = rootSelection;
        SelectedSubCategory = subSelection;
        SearchCommand.Execute(null);
    }

    /// <summary>一级分类：「全部 Mod」+ 根分类（皮肤 / Other / UI）。选中「皮肤」时二级出角色列表。</summary>
    private async Task LoadCategoriesAsync()
    {
        if (!GameIds.TryGetValue(GameContext.Current.Id, out var gameId))
        {
            return;
        }

        try
        {
            var rootCats = await _gameBanana.GetRootCategoriesAsync(gameId).ConfigureAwait(true);

            RootCategories.Clear();
            var allItem = new CategoryOptionVm("全部 Mod", null, isSelected: true);
            RootCategories.Add(allItem);
            foreach (var root in rootCats)
            {
                RootCategories.Add(new CategoryOptionVm(root.Name, root));
            }

            SelectedRootCategory = allItem;
            SubCategories.Clear();
            HasSubCategories = false;
        }
        catch (Exception e)
        {
            _logger.Warning(e, "商店加载分类失败");
        }
    }

    /// <summary>排序下拉的下标 → apiv11 的 _sSort。0 是默认（最新发布动态）。</summary>
    private static readonly string[] Sorts = ["", "Generic_LatestModified", "Generic_MostDownloaded", "Generic_MostLiked"];

    public void LoadCharacters()
    {
        Characters.Clear();

        try
        {
            var category = _gameService.GetCategories()
                .FirstOrDefault(c => c.ModCategory == ModCategory.Character);
            if (category is null)
            {
                return;
            }

            foreach (var moddable in _gameService.GetModdableObjects(category, GetOnly.Both)
                         .OrderBy(c => c.DisplayName, StringComparer.CurrentCulture))
            {
                Characters.Add(new CharacterOptionVm(moddable.DisplayName, moddable));
            }

            SelectedCharacter ??= Characters.FirstOrDefault();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "商店加载角色列表失败");
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _page = 1;
        _hasMore = true;
        Results.Clear();
        var generation = ++_loadGeneration;
        await LoadPageAsync(generation).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!_hasMore || IsLoading)
        {
            return;
        }

        _page++;
        await LoadPageAsync(_loadGeneration).ConfigureAwait(true);
    }

    private async Task LoadPageAsync(int generation)
    {
        if (!GameIds.TryGetValue(GameContext.Current.Id, out var gameId))
        {
            StatusText = $"暂未支持 {GameName} 的在线商店（缺 GameBanana 游戏 Id）";
            return;
        }

        IsLoading = true;
        StatusText = "加载中…";
        _lastQuery = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        _lastSort = SelectedSortIndex > 0 ? Sorts[SelectedSortIndex] : null;
        // 分类优先：二级（角色）> 一级（皮肤等）；有关键词时才忽略分类走全局搜索
        var categoryId = _lastQuery is null
            ? (SelectedSubCategory?.Id ?? SelectedRootCategory?.Id)
            : null;

        try
        {
            var result = await _gameBanana
                .SearchModsAsync(gameId, _lastQuery, _page, PerPage, _lastSort, categoryId)
                .ConfigureAwait(true);

            // 搜索条件又变了 → 这页是过期结果，丢弃
            if (generation != _loadGeneration)
            {
                return;
            }

            if (result is null)
            {
                StatusText = "GameBanana 没有返回数据";
                _hasMore = false;
                return;
            }

            foreach (var record in result.Records.Where(r => r.ModelName == "Mod" && r.Id > 0))
            {
                Results.Add(new StoreModCardVm(record));
            }

            // Mod/Index 不给总页数，但给总记录数；搜索接口反过来。两种都兜住。
            var recordCount = result.Metadata?.RecordCount ?? 0;
            var totalPages = result.Metadata?.TotalPages ?? 0;
            if (totalPages > 0)
            {
                _hasMore = _page < totalPages;
            }
            else if (recordCount > 0)
            {
                _hasMore = Results.Count < recordCount;
            }
            else
            {
                _hasMore = result.Records.Count >= PerPage;
            }

            HasMoreItems = _hasMore;
            StatusText = Results.Count == 0
                ? "没有找到相关 Mod"
                : recordCount > 0
                    ? $"共 {recordCount} 个，已加载 {Results.Count} 个"
                    : $"共 {Results.Count} 个";
        }
        catch (Exception e)
        {
            _logger.Warning(e, "GameBanana 搜索失败");
            StatusText = $"加载失败：{e.Message}";
            _hasMore = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     点卡片 → 打开既有的「从 GameBanana 下载 Mod」窗口（带所选角色），
    ///     下载和安装由那条现成链路完成。
    /// </summary>
    [RelayCommand]
    private void OpenMod(StoreModCardVm? card)
    {
        if (card?.ProfileUrl is null)
        {
            return;
        }

        if (SelectedCharacter is null)
        {
            StatusText = "先在底部选择要安装到的角色";
            return;
        }

        try
        {
            var uri = new Uri(card.ProfileUrl);
            var modPageWindow = new Views.GbModPageWindow(uri, SelectedCharacter.Moddable);
            _windowManager.CreateWindow(modPageWindow, identifier: $"GbModPage_{card.Id}");
            modPageWindow.BringToFront();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "打开 GameBanana 下载页失败：{Url}", card.ProfileUrl);
            StatusText = $"打开下载页失败：{e.Message}";
        }
    }
}

/// <summary>商店网格里的一张 Mod 卡。</summary>
public partial class StoreModCardVm : ObservableObject
{
    /// <summary>图片字节进程内缓存（缩略图/头像共用）。</summary>
    internal static readonly ConcurrentDictionary<string, byte[]> ImageCache = new();

    private static readonly GameBananaCoreService ImageDownloader = App.GetService<GameBananaCoreService>();

    public StoreModCardVm(ApiModSearchRecord record)
    {
        Id = record.Id;
        Name = record.Name;
        Author = record.Author?.AuthorName ?? "未知作者";
        ProfileUrl = record.ProfileUrl;
        Likes = record.LikeCount ?? 0;
        DateText = record.DateAdded is { } ts
            ? DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString("yyyy-MM-dd")
            : "";

        if (record.ThumbnailUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            _ = LoadImageInto(url, v => Thumbnail = v, 440);
        }
        else
        {
            Log.Debug("商店卡片 {Id} 没有可用的缩略图地址", record.Id);
        }

        // 作者头像（_aSubmitter._sAvatarUrl，没有就显示首字母占位）
        if (record.Author?.AvatarImageUrl is { } avatar && Uri.TryCreate(avatar, UriKind.Absolute, out _))
        {
            _ = LoadImageInto(avatar, v => AuthorAvatar = v, 48);
        }
    }

    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private ImageSource? _authorAvatar;

    partial void OnAuthorAvatarChanged(ImageSource? value) =>
        OnPropertyChanged(nameof(ShowAvatarPlaceholder));

    public int Id { get; }
    public string Name { get; }
    public string Author { get; }
    public string? ProfileUrl { get; }
    public int Likes { get; }
    public string DateText { get; }

    /// <summary>作者名首字母（头像没加载出来时的占位）。</summary>
    public string AuthorInitial => string.IsNullOrEmpty(Author) ? "?" : Author[..1].ToUpperInvariant();

    /// <summary>头像未就绪时显示首字母占位。</summary>
    public bool ShowAvatarPlaceholder => AuthorAvatar is null;

    /// <summary>
    ///     图片统一从 CoreService 下载（带上代理与限流重试；本机 XAML
    ///     内建网络图下载不通），进程内缓存后解码。
    /// </summary>
    internal static async Task<byte[]> DownloadImageAsync(string url)
    {
        if (ImageCache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        var bytes = await ImageDownloader.DownloadImageAsync(url).ConfigureAwait(true);
        ImageCache[url] = bytes;
        return bytes;
    }

    private static async Task LoadImageInto(string url, Action<ImageSource> setter, int decodeWidth)
    {
        try
        {
            var bytes = await DownloadImageAsync(url);
            var image = new BitmapImage { DecodePixelWidth = decodeWidth };
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            await image.SetSourceAsync(stream);
            setter(image);
        }
        catch (Exception e)
        {
            Log.Warning("商店图片下载失败 {Url}：{Message}", url, e.Message);
        }
    }
}

/// <summary>安装目标角色下拉项。</summary>
public sealed class CharacterOptionVm(string displayName, IModdableObject moddable)
{
    public string DisplayName { get; } = displayName;
    public IModdableObject Moddable { get; } = moddable;
}

/// <summary>GameBanana 分类 pill 项：null = 按游戏浏览全部。</summary>
public partial class CategoryOptionVm : ObservableObject
{
    public CategoryOptionVm(string displayName, GbCategoryItem? category, bool isSelected = false)
    {
        DisplayName = displayName;
        Id = category?.Id; // 分类编号：搜索 / 子分类加载全靠它（漏赋值会让一切分类筛选失效）
        CountText = category?.Count is { } n ? $"({n})" : "";
        _isSelected = isSelected;

        // 网络图标一律走 HttpClient 下载（这台机器 XAML 内建下载不通）
        if (category?.IconUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            _ = LoadIconAsync(url);
        }
    }

    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private bool _isSelected;

    partial void OnIconChanged(ImageSource? value) =>
        OnPropertyChanged(nameof(HasIcon));

    public bool HasIcon => Icon is not null;

    public string DisplayName { get; }
    public int? Id { get; }
    public string CountText { get; }

    private async Task LoadIconAsync(string url)
    {
        try
        {
            var bytes = await StoreModCardVm.DownloadImageAsync(url);
            var image = new BitmapImage { DecodePixelWidth = 40 };
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            await image.SetSourceAsync(stream);
            Icon = image;
        }
        catch (Exception e)
        {
            Log.Debug("商店分类图标下载失败 {Url}：{Message}", url, e.Message);
        }
    }
}
