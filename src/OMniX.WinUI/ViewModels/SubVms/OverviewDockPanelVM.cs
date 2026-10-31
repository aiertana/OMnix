using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.WinUI.Services;
using Serilog;

namespace OMniX.WinUI.ViewModels.SubVms;

public partial class OverviewDockPanelVM : ObservableRecipient
{
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<OverviewDockPanelVM>();
    private readonly IGameService _gameService = App.GetService<IGameService>();

    public event EventHandler<FilterElementSelectedArgs>? FilterElementSelected;
    public ObservableCollection<ElementIcon> Elements { get; set; } = new();

    public ObservableCollection<ElementIcon> SelectedElements { get; set; } = new();

    [ObservableProperty] private bool _isInitialized;

    public void Initialize()
    {
        // 只在第一次填充：Initialize 每次导航都会被调用，不挡住会重复追加图标。
        if (IsInitialized)
            return;

        Elements.Clear();

        var elements = _gameService.GetElements().Where(e => !e.InternalNameEquals("None")).Reverse();

        foreach (var element in elements)
        {
            // 二开：属性图标换成新的一套。
            // 注意这里必须用「绝对文件路径」而不是 ms-appx:/// —— 原来的 svg 也是绝对路径，
            // 用 ms-appx 在 Debug 下取不到，界面上就只剩两三个图标。
            // 二开：终末地 / 异环这类新内核暂时没有属性图标素材，
            // 这里必须给个能用的兜底 —— 返回空字符串会让 x:Bind 把 "" 转成 ImageSource 时抛异常，
            // 整个角色页直接崩掉（实测报「The value cannot be converted to type ImageSource」）。
            var iconUri = ResolveElementIcon(element.InternalName.ToString())
                          ?? element.ImageUri?.ToString()
                          ?? ImageHandlerService.StaticPlaceholderImageUri.ToString();

            Elements.Add(new ElementIcon(element.DisplayName, iconUri, element.InternalName));
        }

        // 二开：游戏数据里没有「其他」这个属性，得自己补一个。
        // 非角色（Others、滑翔翼、武器…）和没指定属性的角色都归到这一类。
        if (File.Exists(OtherElementIconUri))
            Elements.Add(new ElementIcon(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/ElementOtherLabel", "其他"), OtherElementIconUri, OtherElementInternalName));

        IsInitialized = true;
    }

    /// <summary>
    ///     二开：属性 → 图标文件映射。
    /// </summary>
    /// <summary>
    ///     二开：「其他」属性。非角色、或者角色没有对应属性时归到这里，
    ///     图标用白色版的物理图标。
    /// </summary>
    public const string OtherElementInternalName = "Other";

    /// <summary>
    ///     二开：图标文件所在目录（运行时输出目录下）。
    /// </summary>
    public static string ElementIconFolder =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "GameElements");

    public static string OtherElementIconUri =>
        Path.Combine(ElementIconFolder, "other.png");

    public static string? ResolveElementIcon(string internalName)
    {
        var file = internalName.ToLowerInvariant() switch
        {
            "glacio" => "glacio",
            "electro" => "electro",
            "aero" => "aero",
            "havoc" => "havoc",
            "fusion" => "fusion",
            "spectro" => "spectro",
            "physical" => "physical",
            "other" => "other",
            "unknown" => "other",
            _ => null
        };

        if (file is null)
            return null;

        // 用绝对文件路径（和原来的 svg 一致），并优先用 png：
        // 原始素材是 webp，BitmapImage 解码不稳定，已统一转成 png。
        return Path.Combine(ElementIconFolder, file + ".png");
    }


    public void ElementSelectionChanged(IEnumerable<ElementIcon> newItems, IEnumerable<ElementIcon> removedItems)
    {
        foreach (var elementIcon in newItems)
        {
            SelectedElements.Add(elementIcon);
        }

        foreach (var elementIcon in removedItems)
        {
            SelectedElements.Remove(elementIcon);
        }

        FilterElementSelected?.Invoke(this,
            new FilterElementSelectedArgs(SelectedElements.Select(e => e.InternalElementName)));
    }
}

public sealed class FilterElementSelectedArgs : EventArgs
{
    public string[] InternalElementNames { get; }

    public FilterElementSelectedArgs(IEnumerable<string> internalElementName)
    {
        InternalElementNames = internalElementName.ToArray();
    }
}

[DebuggerDisplay("{Element}|{ImageUri}")]
public partial class ElementIcon : ObservableObject
{
    public string Name { get; set; }
    public string ImageUri { get; set; }
    public string InternalElementName { get; set; }

    [ObservableProperty] private bool _isSelected;

    public ElementIcon(string name, string imageUri, string internalElementName)
    {
        Name = name;
        ImageUri = imageUri;
        InternalElementName = internalElementName;
    }
}