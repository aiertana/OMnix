using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OMniX.WinUI.Contracts.Services;
using Windows.UI;

namespace OMniX.WinUI.Services;

/// <summary>
///     三套配色。和"浅色 / 深色"是两回事：那个是明暗模式，这个是色系，
///     两套正交 —— 每套配色都要各给一份亮色值和暗色值。
///     取值严格照 OMNIX 紫蓝二次元版 / 蓝色版 / 深色版 三个 HTML 里的 CSS 变量。
/// </summary>
public enum OmniXPalette
{
    /// <summary>紫蓝二次元版：暖白 + 珊瑚红</summary>
    Warm,

    /// <summary>蓝色版：冷白 + 蓝</summary>
    Blue,

    /// <summary>深色版：近黑 + 白，整体就是深色，明暗模式对它不起作用</summary>
    Mono
}

/// <summary>
///     配色热切换：不重新加载资源字典，直接改已经解析出来的画刷实例。
///     界面引用的就是同一个 SolidColorBrush 对象，改它的 Color 立刻生效，
///     不需要重启，也不用重建页面。
/// </summary>
public sealed class PaletteService
{
    public const string PaletteKey = "App.Palette";

    private readonly ILocalSettingsService _localSettingsService;

    public static PaletteService? Instance { get; private set; }

    public OmniXPalette Current { get; private set; } = OmniXPalette.Warm;

    public event EventHandler<OmniXPalette>? Changed;

    public PaletteService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        Instance = this;
    }

    public string DisplayName => Current switch
    {
        OmniXPalette.Blue => "冷调蓝",
        OmniXPalette.Mono => "深黑白",
        _ => "暖白珊瑚"
    };

    public async Task InitializeAsync()
    {
        try
        {
            Current = Read(await _localSettingsService.ReadSettingAsync<string>(PaletteKey));
        }
        catch (Exception)
        {
            Current = OmniXPalette.Warm;
        }

        Apply();
    }

    public async Task SetAsync(OmniXPalette palette)
    {
        if (Current == palette)
            return;

        Current = palette;

        try
        {
            await _localSettingsService.SaveSettingAsync(PaletteKey, palette.ToString());
        }
        catch (Exception)
        {
            // 存不进去也要让界面先切过去
        }

        Apply();
        Refresh();
        Changed?.Invoke(this, palette);
    }

    private static OmniXPalette Read(string? value) => value switch
    {
        nameof(OmniXPalette.Blue) => OmniXPalette.Blue,
        nameof(OmniXPalette.Mono) => OmniXPalette.Mono,
        _ => OmniXPalette.Warm
    };

    private void Apply()
    {
        // 深浅两组都写，这样之后再切明暗模式时另一组已经是新配色了
        switch (Current)
        {
            case OmniXPalette.Blue:
                ApplyInto("Light", BlueLight, BlueGradient);
                ApplyInto("Dark", BlueDark, BlueGradient);
                break;

            case OmniXPalette.Mono:
                ApplyInto("Light", MonoColors, MonoGradient);
                ApplyInto("Dark", MonoColors, MonoGradient);
                break;

            default:
                ApplyInto("Light", WarmLight, WarmGradient);
                ApplyInto("Dark", WarmDark, WarmGradient);
                break;
        }
    }

    private static void ApplyInto(string section, IReadOnlyDictionary<string, string> colors, string[] gradient)
    {
        var dictionary = FindSection(section);

        foreach (var (key, hex) in colors)
        {
            var color = ToColor(hex);

            // 以 Brush 结尾的键本身就是画刷，只改颜色；
            // 其余是 Color 资源，改字典值（新建元素用），同时改同名画刷（已画出来的立刻变）
            var isBrush = key.EndsWith("Brush", StringComparison.Ordinal);
            var brushKey = isBrush ? key : key + "Brush";

            if (FindResource(brushKey) is SolidColorBrush brush)
                brush.Color = color;

            if (!isBrush && dictionary is not null)
                dictionary[key] = color;
        }

        if (FindResource("OmniXPageBackgroundBrush") is LinearGradientBrush page)
            SetStops(page, gradient);
    }

    private static void SetStops(LinearGradientBrush brush, string[] gradient)
    {
        var stops = brush.GradientStops;

        for (var i = 0; i < stops.Count && i < gradient.Length; i++)
            stops[i].Color = ToColor(gradient[i]);
    }

    /// <summary>
    ///     强制让所有 ThemeResource 重新解析一遍。
    ///     光改画刷能把大部分地方带过去，但直接写在 Color 上的（比如
    ///     Fill="{ThemeResource XxxColor}"）不会动，抖一下 RequestedTheme
    ///     能把这些重新求值，代价只是切换那一瞬间重解析一次。
    /// </summary>
    private static void Refresh()
    {
        try
        {
            if (App.MainWindow?.Content is not FrameworkElement root)
                return;

            var theme = root.RequestedTheme;
            root.RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            root.RequestedTheme = theme;
        }
        catch (Exception)
        {
            // 刷不动就算了，画刷层面已经改过一轮
        }
    }

    /// <summary>
    ///     资源都躺在 merged 的 OmniXTheme.xaml 里，顶层索引查不到，得递归下去找。
    /// </summary>
    private static object? FindResource(string key)
    {
        try
        {
            return FindResource(Application.Current.Resources, key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? FindResource(ResourceDictionary dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var value) && value is not null)
            return value;

        foreach (var merged in dictionary.MergedDictionaries)
        {
            var found = FindResource(merged, key);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static ResourceDictionary? FindSection(string section)
    {
        try
        {
            return FindSection(Application.Current.Resources, section);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ResourceDictionary? FindSection(ResourceDictionary dictionary, string section)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(section, out var value) && value is ResourceDictionary found)
            return found;

        foreach (var merged in dictionary.MergedDictionaries)
        {
            var nested = FindSection(merged, section);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static Color ToColor(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length == 6)
            text = "FF" + text;

        return Color.FromArgb(
            Convert.ToByte(text[..2], 16),
            Convert.ToByte(text.Substring(2, 2), 16),
            Convert.ToByte(text.Substring(4, 2), 16),
            Convert.ToByte(text.Substring(6, 2), 16));
    }

    // ========== 紫蓝二次元版（暖白 + 珊瑚红，默认） ==========

    private static readonly string[] WarmGradient = ["#FFFAF8F7", "#FFFCFAF9", "#FFF9F5F6"];

    private static readonly Dictionary<string, string> WarmLight = new()
    {
        // --accent #e84c6d / --accent-light #ff8aa0
        ["OmniXAccentColor"] = "#FFE84C6D",
        ["OmniXAccentStrongColor"] = "#FFFF8AA0",
        // --accent-bg rgba(232,76,109,.06) / strong .12
        ["OmniXAccentSoftColor"] = "#0FE84C6D",
        ["OmniXAccentSoftHoverColor"] = "#1FE84C6D",
        ["OmniXAccentSoftPressedColor"] = "#2EE84C6D",
        // --pink #ffb3d1 / --purple #b58bff / --blue #7ec8ff
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF7EC8FF",
        // --bg-card #ffffff / --bg-sidebar #ffffff / --bg-base #faf8f7
        ["OmniXCardAltBackgroundColor"] = "#FFFFFFFF",
        ["OmniXCardBackgroundColor"] = "#FFFFFFFF",
        // --line #f0ebeb / --line-strong #e2d8d8
        ["OmniXCardStrokeColor"] = "#FFF0EBEB",
        ["OmniXCardStrokeHoverColor"] = "#FFE2D8D8",
        ["OmniXControlBackgroundColor"] = "#FFFFFFFF",
        ["OmniXControlBorderColor"] = "#FFF0EBEB",
        ["OmniXControlBorderPointerOverColor"] = "#FFE84C6D",
        ["OmniXHeroBackgroundColor"] = "#FFFDF9F8",
        ["OmniXNavItemPointerOverColor"] = "#FFF7F1F0",
        ["OmniXNavItemPressedColor"] = "#FFEFE7E5",
        ["OmniXNavItemSelectedColor"] = "#1AE84C6D",
        ["OmniXNavItemSelectedPointerOverColor"] = "#26E84C6D",
        ["OmniXNavItemSelectedPressedColor"] = "#33E84C6D",
        ["OmniXNavPaneBackgroundColor"] = "#FFFFFFFF",
        ["OmniXPageBackgroundColor"] = "#FFFAF8F7",
        ["OmniXSidebarBackgroundColor"] = "#FFFFFFFF",
        // --ink #2d2a2e / --ink-dim #7a7278
        ["OmniXTextPrimaryColor"] = "#FF2D2A2E",
        ["OmniXTextSecondaryColor"] = "#FF7A7278",
        ["OmniXDialogBackgroundColor"] = "#FFFFFFFF",
        ["OmniXDialogBorderColor"] = "#FFF0EBEB",
        ["SystemAccentColor"] = "#FFE84C6D",
        ["SystemAccentColorLight1"] = "#FFFF8AA0",
        ["SystemAccentColorLight2"] = "#FFFFB0BE",
        ["SystemAccentColorLight3"] = "#FFFFD5DC",
        ["SystemAccentColorDark1"] = "#FFD63A5C",
        ["SystemAccentColorDark2"] = "#FFB32C49",
        ["SystemAccentColorDark3"] = "#FF8F1F36",
        ["AccentFillColorDefaultBrush"] = "#FFE84C6D",
        ["AccentFillColorSecondaryBrush"] = "#E6E84C6D",
        ["AccentFillColorTertiaryBrush"] = "#CCE84C6D",
        ["AccentFillColorDisabledBrush"] = "#33E84C6D",
        ["AccentTextFillColorPrimaryBrush"] = "#FFE84C6D",
        ["AccentTextFillColorSecondaryBrush"] = "#FFE84C6D",
        ["AccentTextFillColorTertiaryBrush"] = "#FFE84C6D"
    };

    private static readonly Dictionary<string, string> WarmDark = new()
    {
        ["OmniXAccentColor"] = "#FFFF8AA0",
        ["OmniXAccentStrongColor"] = "#FFFFB0BE",
        ["OmniXAccentSoftColor"] = "#FF3A2128",
        ["OmniXAccentSoftHoverColor"] = "#FF4A2A33",
        ["OmniXAccentSoftPressedColor"] = "#FF55303B",
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF7EC8FF",
        ["OmniXCardAltBackgroundColor"] = "#12FFFFFF",
        ["OmniXCardBackgroundColor"] = "#1AFFFFFF",
        ["OmniXCardStrokeColor"] = "#2EFFFFFF",
        ["OmniXCardStrokeHoverColor"] = "#66FF8AA0",
        ["OmniXControlBackgroundColor"] = "#1FFFFFFF",
        ["OmniXControlBorderColor"] = "#2AFFFFFF",
        ["OmniXControlBorderPointerOverColor"] = "#66FF8AA0",
        ["OmniXHeroBackgroundColor"] = "#99202026",
        ["OmniXNavItemPointerOverColor"] = "#1FFFFFFF",
        ["OmniXNavItemPressedColor"] = "#2BFFFFFF",
        ["OmniXNavItemSelectedColor"] = "#33FF8AA0",
        ["OmniXNavItemSelectedPointerOverColor"] = "#45FF8AA0",
        ["OmniXNavItemSelectedPressedColor"] = "#57FF8AA0",
        ["OmniXNavPaneBackgroundColor"] = "#B3141418",
        ["OmniXPageBackgroundColor"] = "#990E0E11",
        ["OmniXSidebarBackgroundColor"] = "#B3141418",
        ["OmniXTextPrimaryColor"] = "#FFF5F2F3",
        ["OmniXTextSecondaryColor"] = "#FFB9AEB2",
        ["OmniXDialogBackgroundColor"] = "#B31A1A20",
        ["OmniXDialogBorderColor"] = "#59FFFFFF",
        ["SystemAccentColor"] = "#FFFF8AA0",
        ["SystemAccentColorLight1"] = "#FFFFB0BE",
        ["SystemAccentColorLight2"] = "#FFFFCFD6",
        ["SystemAccentColorLight3"] = "#FFFFE6EA",
        ["SystemAccentColorDark1"] = "#FFE85A78",
        ["SystemAccentColorDark2"] = "#FFC43A58",
        ["SystemAccentColorDark3"] = "#FF9E2440",
        ["AccentFillColorDefaultBrush"] = "#FFFF8AA0",
        ["AccentFillColorSecondaryBrush"] = "#E6FF8AA0",
        ["AccentFillColorTertiaryBrush"] = "#CCFF8AA0",
        ["AccentFillColorDisabledBrush"] = "#33FF8AA0",
        ["AccentTextFillColorPrimaryBrush"] = "#FFFF8AA0",
        ["AccentTextFillColorSecondaryBrush"] = "#FFFF8AA0",
        ["AccentTextFillColorTertiaryBrush"] = "#FFFF8AA0"
    };

    // ========== 蓝色版（冷白 + 蓝） ==========

    private static readonly string[] BlueGradient = ["#FFF5F9FF", "#FFEDF5FF", "#FFE8F0FF"];

    private static readonly Dictionary<string, string> BlueLight = new()
    {
        // --accent #4a9eff / --accent-light #7ec8ff
        ["OmniXAccentColor"] = "#FF4A9EFF",
        ["OmniXAccentStrongColor"] = "#FF7EC8FF",
        // --accent-bg .07 / strong .14
        ["OmniXAccentSoftColor"] = "#124A9EFF",
        ["OmniXAccentSoftHoverColor"] = "#244A9EFF",
        ["OmniXAccentSoftPressedColor"] = "#334A9EFF",
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF4A9EFF",
        // --bg-card #ffffff / --bg-base #f5f9ff / --bg-sidebar #ffffff
        ["OmniXCardAltBackgroundColor"] = "#FFFBFDFF",
        ["OmniXCardBackgroundColor"] = "#FFFFFFFF",
        // --line #e8eff8 / --line-strong #d4e0f0
        ["OmniXCardStrokeColor"] = "#FFE8EFF8",
        ["OmniXCardStrokeHoverColor"] = "#FFD4E0F0",
        ["OmniXControlBackgroundColor"] = "#FFFFFFFF",
        ["OmniXControlBorderColor"] = "#FFE8EFF8",
        ["OmniXControlBorderPointerOverColor"] = "#FF4A9EFF",
        ["OmniXHeroBackgroundColor"] = "#FFF0F6FF",
        ["OmniXNavItemPointerOverColor"] = "#FFE8EFF8",
        ["OmniXNavItemPressedColor"] = "#FFD4E0F0",
        ["OmniXNavItemSelectedColor"] = "#124A9EFF",
        ["OmniXNavItemSelectedPointerOverColor"] = "#244A9EFF",
        ["OmniXNavItemSelectedPressedColor"] = "#334A9EFF",
        ["OmniXNavPaneBackgroundColor"] = "#FFFFFFFF",
        ["OmniXPageBackgroundColor"] = "#FFF5F9FF",
        ["OmniXSidebarBackgroundColor"] = "#FFFFFFFF",
        // --ink #2a303d / --ink-dim #6b7a90
        ["OmniXTextPrimaryColor"] = "#FF2A303D",
        ["OmniXTextSecondaryColor"] = "#FF6B7A90",
        ["OmniXDialogBackgroundColor"] = "#FFFFFFFF",
        ["OmniXDialogBorderColor"] = "#FFE8EFF8",
        ["SystemAccentColor"] = "#FF4A9EFF",
        ["SystemAccentColorLight1"] = "#FF7EC8FF",
        ["SystemAccentColorLight2"] = "#FFA8D8FF",
        ["SystemAccentColorLight3"] = "#FFCDE8FF",
        ["SystemAccentColorDark1"] = "#FF3A8EEF",
        ["SystemAccentColorDark2"] = "#FF2A6ECC",
        ["SystemAccentColorDark3"] = "#FF1E4E99",
        ["AccentFillColorDefaultBrush"] = "#FF4A9EFF",
        ["AccentFillColorSecondaryBrush"] = "#E64A9EFF",
        ["AccentFillColorTertiaryBrush"] = "#CC4A9EFF",
        ["AccentFillColorDisabledBrush"] = "#334A9EFF",
        ["AccentTextFillColorPrimaryBrush"] = "#FF4A9EFF",
        ["AccentTextFillColorSecondaryBrush"] = "#FF4A9EFF",
        ["AccentTextFillColorTertiaryBrush"] = "#FF4A9EFF"
    };

    private static readonly Dictionary<string, string> BlueDark = new()
    {
        ["OmniXAccentColor"] = "#FF7EC8FF",
        ["OmniXAccentStrongColor"] = "#FFA8D8FF",
        ["OmniXAccentSoftColor"] = "#1F4A9EFF",
        ["OmniXAccentSoftHoverColor"] = "#2E4A9EFF",
        ["OmniXAccentSoftPressedColor"] = "#3D4A9EFF",
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF7EC8FF",
        ["OmniXCardAltBackgroundColor"] = "#12FFFFFF",
        ["OmniXCardBackgroundColor"] = "#1AFFFFFF",
        ["OmniXCardStrokeColor"] = "#2EFFFFFF",
        ["OmniXCardStrokeHoverColor"] = "#667EC8FF",
        ["OmniXControlBackgroundColor"] = "#1FFFFFFF",
        ["OmniXControlBorderColor"] = "#2AFFFFFF",
        ["OmniXControlBorderPointerOverColor"] = "#667EC8FF",
        ["OmniXHeroBackgroundColor"] = "#99202633",
        ["OmniXNavItemPointerOverColor"] = "#1FFFFFFF",
        ["OmniXNavItemPressedColor"] = "#2BFFFFFF",
        ["OmniXNavItemSelectedColor"] = "#337EC8FF",
        ["OmniXNavItemSelectedPointerOverColor"] = "#457EC8FF",
        ["OmniXNavItemSelectedPressedColor"] = "#577EC8FF",
        ["OmniXNavPaneBackgroundColor"] = "#B30F1620",
        ["OmniXPageBackgroundColor"] = "#990D1119",
        ["OmniXSidebarBackgroundColor"] = "#B30F1620",
        ["OmniXTextPrimaryColor"] = "#FFE8F0FA",
        ["OmniXTextSecondaryColor"] = "#FFA8B8CC",
        ["OmniXDialogBackgroundColor"] = "#B31A222E",
        ["OmniXDialogBorderColor"] = "#59FFFFFF",
        ["SystemAccentColor"] = "#FF7EC8FF",
        ["SystemAccentColorLight1"] = "#FFA8D8FF",
        ["SystemAccentColorLight2"] = "#FFC2E4FF",
        ["SystemAccentColorLight3"] = "#FFDCEFFF",
        ["SystemAccentColorDark1"] = "#FF5FAEEA",
        ["SystemAccentColorDark2"] = "#FF3F88C4",
        ["SystemAccentColorDark3"] = "#FF2A6AA0",
        ["AccentFillColorDefaultBrush"] = "#FF7EC8FF",
        ["AccentFillColorSecondaryBrush"] = "#E67EC8FF",
        ["AccentFillColorTertiaryBrush"] = "#CC7EC8FF",
        ["AccentFillColorDisabledBrush"] = "#337EC8FF",
        ["AccentTextFillColorPrimaryBrush"] = "#FF7EC8FF",
        ["AccentTextFillColorSecondaryBrush"] = "#FF7EC8FF",
        ["AccentTextFillColorTertiaryBrush"] = "#FF7EC8FF"
    };

    // ========== 深色版（近黑 + 白），明暗两组共用 ==========

    private static readonly string[] MonoGradient = ["#FF0F0F11", "#FF141417", "#FF101014"];

    private static readonly Dictionary<string, string> MonoColors = new()
    {
        // --accent #ffffff / --accent-light #e0e0e5
        ["OmniXAccentColor"] = "#FFFFFFFF",
        ["OmniXAccentStrongColor"] = "#FFE0E0E5",
        // --accent-bg rgba(255,255,255,.06) / strong .12
        ["OmniXAccentSoftColor"] = "#0FFFFFFF",
        ["OmniXAccentSoftHoverColor"] = "#1FFFFFFF",
        ["OmniXAccentSoftPressedColor"] = "#2AFFFFFF",
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF7EC8FF",
        // --bg-card #1c1c20 / --bg-base #0f0f11 / --bg-sidebar #161618
        ["OmniXCardAltBackgroundColor"] = "#FF17171A",
        ["OmniXCardBackgroundColor"] = "#FF1C1C20",
        // --line #2a2a30 / --line-strong #3a3a42
        ["OmniXCardStrokeColor"] = "#FF2A2A30",
        ["OmniXCardStrokeHoverColor"] = "#FF3A3A42",
        ["OmniXControlBackgroundColor"] = "#FF222226",
        ["OmniXControlBorderColor"] = "#FF2A2A30",
        ["OmniXControlBorderPointerOverColor"] = "#FF8A8A92",
        ["OmniXHeroBackgroundColor"] = "#FF161618",
        ["OmniXNavItemPointerOverColor"] = "#14FFFFFF",
        ["OmniXNavItemPressedColor"] = "#1FFFFFFF",
        ["OmniXNavItemSelectedColor"] = "#1FFFFFFF",
        ["OmniXNavItemSelectedPointerOverColor"] = "#2AFFFFFF",
        ["OmniXNavItemSelectedPressedColor"] = "#35FFFFFF",
        ["OmniXNavPaneBackgroundColor"] = "#FF161618",
        ["OmniXPageBackgroundColor"] = "#FF0F0F11",
        ["OmniXSidebarBackgroundColor"] = "#FF161618",
        // --ink #f5f5f7 / --ink-dim #8a8a92
        ["OmniXTextPrimaryColor"] = "#FFF5F5F7",
        ["OmniXTextSecondaryColor"] = "#FF8A8A92",
        ["OmniXDialogBackgroundColor"] = "#FF1C1C20",
        ["OmniXDialogBorderColor"] = "#FF2A2A30",
        ["SystemAccentColor"] = "#FFFFFFFF",
        ["SystemAccentColorLight1"] = "#FFFFFFFF",
        ["SystemAccentColorLight2"] = "#FFFFFFFF",
        ["SystemAccentColorLight3"] = "#FFFFFFFF",
        ["SystemAccentColorDark1"] = "#FFE0E0E5",
        ["SystemAccentColorDark2"] = "#FFC0C0C8",
        ["SystemAccentColorDark3"] = "#FFA0A0A8",
        // 白底按钮，字用近黑，保证对比度
        ["AccentFillColorDefaultBrush"] = "#FFFFFFFF",
        ["AccentFillColorSecondaryBrush"] = "#E6FFFFFF",
        ["AccentFillColorTertiaryBrush"] = "#CCFFFFFF",
        ["AccentFillColorDisabledBrush"] = "#33FFFFFF",
        ["AccentTextFillColorPrimaryBrush"] = "#FFFFFFFF",
        ["AccentTextFillColorSecondaryBrush"] = "#FFFFFFFF",
        ["AccentTextFillColorTertiaryBrush"] = "#FFFFFFFF"
    };
}
