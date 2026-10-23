using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OMniX.WinUI.Contracts.Services;
using Windows.UI;

namespace OMniX.WinUI.Services;

/// <summary>
///     三套配色。和"浅色 / 深色"是两回事：那个是明暗模式，这个是色系，
///     两套正交 —— 每套配色都要各给一份亮色值和暗色值。
/// </summary>
public enum OmniXPalette
{
    /// <summary>暖白 + 珊瑚红，1.0 版那套</summary>
    Warm,

    /// <summary>冷调蓝</summary>
    Blue,

    /// <summary>深黑白。整体就是深色，明暗模式对它不起作用</summary>
    Mono
}

/// <summary>
///     配色切换。这里不重新加载资源字典，而是直接改已经解析出来的画刷实例：
///     界面上引用的是同一个 SolidColorBrush 对象，改它的 Color 会立刻跟着变，
///     不需要重建页面；Color 资源本身也一起改，之后新建的元素才会用上新值。
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
        // 深浅两组都写进去，这样切换明暗模式时另一组已经是新配色了
        switch (Current)
        {
            case OmniXPalette.Blue:
                ApplyInto("Light", BlueLight, BlueLightGradient);
                ApplyInto("Dark", BlueDark, BlueDarkGradient);
                break;

            case OmniXPalette.Mono:
                // 深黑白整体就是深色，明暗两组用同一份
                ApplyInto("Light", MonoColors, MonoGradient);
                ApplyInto("Dark", MonoColors, MonoGradient);
                break;

            default:
                ApplyInto("Light", WarmLight, WarmLightGradient);
                ApplyInto("Dark", WarmDark, WarmDarkGradient);
                break;
        }
    }

    private static void ApplyInto(string section, IReadOnlyDictionary<string, string> colors, string[] gradient)
    {
        var dictionary = GetSection(section);

        foreach (var (key, hex) in colors)
        {
            var color = ToColor(hex);

            // 以 Brush 结尾的键本身就是画刷，只改它的颜色；
            // 其余是 Color 资源，改资源值，同时改同名画刷（让已经画出来的地方立刻跟上）
            var brushKey = key.EndsWith("Brush", StringComparison.Ordinal) ? key : key + "Brush";

            if (Find(brushKey) is SolidColorBrush brush)
                brush.Color = color;

            if (!key.EndsWith("Brush", StringComparison.Ordinal) && dictionary is not null)
                dictionary[key] = color;
        }

        if (dictionary is not null && Find("OmniXPageBackgroundBrush") is LinearGradientBrush page)
            SetStops(page, gradient);
    }

    private static void SetStops(LinearGradientBrush brush, string[] gradient)
    {
        var stops = brush.GradientStops;

        for (var i = 0; i < stops.Count && i < gradient.Length; i++)
            stops[i].Color = ToColor(gradient[i]);
    }

    private static ResourceDictionary? GetSection(string section)
    {
        try
        {
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(section, out var value)
                && value is ResourceDictionary dictionary)
                return dictionary;
        }
        catch (Exception)
        {
            // 主题字典拿不到就只改画刷，界面照样能换色
        }

        return null;
    }

    /// <summary>画刷都定义在主题字典外面，直接查顶层资源</summary>
    private static object? Find(string key)
    {
        try
        {
            return Application.Current.Resources.TryGetValue(key, out var value) ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
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

    // ========== 暖白珊瑚（1.0 版那套，当前默认） ==========

    private static readonly string[] WarmLightGradient = ["#FFEAE1F5", "#FFF2EDF8", "#FFEDEFFA"];

    private static readonly string[] WarmDarkGradient = ["#F216121C", "#F2121219", "#F20F1418"];

    private static readonly Dictionary<string, string> WarmLight = new()
    {
        ["OmniXAccentColor"] = "#FFE35B68",
        ["OmniXAccentStrongColor"] = "#FFE97F8D",
        ["OmniXAccentSoftColor"] = "#FFFDF1F3",
        ["OmniXAccentSoftHoverColor"] = "#FFFCE4E8",
        ["OmniXAccentSoftPressedColor"] = "#FFFAD8DE",
        ["OmniXAuxPinkColor"] = "#FFFF9EC7",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF8CCBFF",
        ["OmniXCardAltBackgroundColor"] = "#9EFFFFFF",
        ["OmniXCardBackgroundColor"] = "#B8FFFFFF",
        ["OmniXCardStrokeColor"] = "#29150E6E",
        ["OmniXCardStrokeHoverColor"] = "#52966ED8",
        ["OmniXControlBackgroundColor"] = "#B3FFFFFF",
        ["OmniXControlBorderColor"] = "#29150E6E",
        ["OmniXControlBorderPointerOverColor"] = "#668CCBFF",
        ["OmniXHeroBackgroundColor"] = "#D9FFFFFF",
        ["OmniXNavItemPointerOverColor"] = "#B8FFFFFF",
        ["OmniXNavItemPressedColor"] = "#CCFFFFFF",
        ["OmniXNavItemSelectedColor"] = "#2EE35B68",
        ["OmniXNavItemSelectedPointerOverColor"] = "#45E35B68",
        ["OmniXNavItemSelectedPressedColor"] = "#5CE35B68",
        ["OmniXNavPaneBackgroundColor"] = "#99FFFFFF",
        ["OmniXPageBackgroundColor"] = "#FFFFFFFF",
        ["OmniXSidebarBackgroundColor"] = "#9EFFFFFF",
        ["OmniXTextPrimaryColor"] = "#FF4B3D6B",
        ["OmniXTextSecondaryColor"] = "#FF8A7DA8",
        ["OmniXDialogBackgroundColor"] = "#EBFFFFFF",
        ["OmniXDialogBorderColor"] = "#29150E6E",
        ["SystemAccentColor"] = "#FFE35B68",
        ["SystemAccentColorLight1"] = "#FFE97F8D",
        ["SystemAccentColorLight2"] = "#FFF1ADB4",
        ["SystemAccentColorLight3"] = "#FFF7CCD1",
        ["SystemAccentColorDark1"] = "#FFC14D58",
        ["SystemAccentColorDark2"] = "#FFA3424B",
        ["SystemAccentColorDark3"] = "#FF88373E",
        ["AccentFillColorDefaultBrush"] = "#FFE35B68",
        ["AccentFillColorSecondaryBrush"] = "#E6E35B68",
        ["AccentFillColorTertiaryBrush"] = "#CCE35B68",
        ["AccentFillColorDisabledBrush"] = "#33E35B68",
        ["AccentTextFillColorPrimaryBrush"] = "#FFD1475A",
        ["AccentTextFillColorSecondaryBrush"] = "#FFD1475A",
        ["AccentTextFillColorTertiaryBrush"] = "#FFD1475A"
    };

    private static readonly Dictionary<string, string> WarmDark = new()
    {
        ["OmniXAccentColor"] = "#FFE97F8D",
        ["OmniXAccentStrongColor"] = "#FFFFB0C2",
        ["OmniXAccentSoftColor"] = "#FF3A2328",
        ["OmniXAccentSoftHoverColor"] = "#FF4A2D33",
        ["OmniXAccentSoftPressedColor"] = "#FF553138",
        ["OmniXAuxPinkColor"] = "#FFFF9EC7",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF8CCBFF",
        ["OmniXCardAltBackgroundColor"] = "#12FFFFFF",
        ["OmniXCardBackgroundColor"] = "#1AFFFFFF",
        ["OmniXCardStrokeColor"] = "#2EFFFFFF",
        ["OmniXCardStrokeHoverColor"] = "#66E97F8D",
        ["OmniXControlBackgroundColor"] = "#1FFFFFFF",
        ["OmniXControlBorderColor"] = "#2AFFFFFF",
        ["OmniXControlBorderPointerOverColor"] = "#668CCBFF",
        ["OmniXHeroBackgroundColor"] = "#99202026",
        ["OmniXNavItemPointerOverColor"] = "#1FFFFFFF",
        ["OmniXNavItemPressedColor"] = "#2BFFFFFF",
        ["OmniXNavItemSelectedColor"] = "#33E97F8D",
        ["OmniXNavItemSelectedPointerOverColor"] = "#45E97F8D",
        ["OmniXNavItemSelectedPressedColor"] = "#57E97F8D",
        ["OmniXNavPaneBackgroundColor"] = "#B3141418",
        ["OmniXPageBackgroundColor"] = "#990E0E11",
        ["OmniXSidebarBackgroundColor"] = "#B3141418",
        ["OmniXTextPrimaryColor"] = "#FFF3EFFA",
        ["OmniXTextSecondaryColor"] = "#FFB9AED0",
        ["OmniXDialogBackgroundColor"] = "#B31A1A20",
        ["OmniXDialogBorderColor"] = "#59FFFFFF",
        ["SystemAccentColor"] = "#FFFF6B75",
        ["SystemAccentColorLight1"] = "#FFFF8A92",
        ["SystemAccentColorLight2"] = "#FFFFA8AE",
        ["SystemAccentColorLight3"] = "#FFFFC6CA",
        ["SystemAccentColorDark1"] = "#FFE5484D",
        ["SystemAccentColorDark2"] = "#FFC9172A",
        ["SystemAccentColorDark3"] = "#FFA51020",
        ["AccentFillColorDefaultBrush"] = "#FFE97F8D",
        ["AccentFillColorSecondaryBrush"] = "#E6E97F8D",
        ["AccentFillColorTertiaryBrush"] = "#CCE97F8D",
        ["AccentFillColorDisabledBrush"] = "#33E97F8D",
        ["AccentTextFillColorPrimaryBrush"] = "#FFE9A0AB",
        ["AccentTextFillColorSecondaryBrush"] = "#FFE9A0AB",
        ["AccentTextFillColorTertiaryBrush"] = "#FFE9A0AB"
    };

    // ========== 冷调蓝 ==========

    private static readonly string[] BlueLightGradient = ["#FFF5F9FF", "#FFEDF5FF", "#FFE8F0FF"];

    private static readonly string[] BlueDarkGradient = ["#FF0F1420", "#FF111726", "#FF0D1220"];

    private static readonly Dictionary<string, string> BlueLight = new()
    {
        ["OmniXAccentColor"] = "#FF4A9EFF",
        ["OmniXAccentStrongColor"] = "#FF7EC8FF",
        ["OmniXAccentSoftColor"] = "#FFF2F8FF",
        ["OmniXAccentSoftHoverColor"] = "#FFE4F1FF",
        ["OmniXAccentSoftPressedColor"] = "#FFD4E6FF",
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF4A9EFF",
        ["OmniXCardAltBackgroundColor"] = "#FFFBFDFF",
        ["OmniXCardBackgroundColor"] = "#FFFFFFFF",
        ["OmniXCardStrokeColor"] = "#FFE8EFF8",
        ["OmniXCardStrokeHoverColor"] = "#FF7EC8FF",
        ["OmniXControlBackgroundColor"] = "#FFFFFFFF",
        ["OmniXControlBorderColor"] = "#FFE8EFF8",
        ["OmniXControlBorderPointerOverColor"] = "#FF4A9EFF",
        ["OmniXHeroBackgroundColor"] = "#FFF0F6FF",
        ["OmniXNavItemPointerOverColor"] = "#FFE8EFF8",
        ["OmniXNavItemPressedColor"] = "#FFD4E0F0",
        ["OmniXNavItemSelectedColor"] = "#124A9EFF",
        ["OmniXNavItemSelectedPointerOverColor"] = "#244A9EFF",
        ["OmniXNavItemSelectedPressedColor"] = "#3D4A9EFF",
        ["OmniXNavPaneBackgroundColor"] = "#FFF5F9FF",
        ["OmniXPageBackgroundColor"] = "#FFF5F9FF",
        ["OmniXSidebarBackgroundColor"] = "#FFFFFFFF",
        ["OmniXTextPrimaryColor"] = "#FF2A303D",
        ["OmniXTextSecondaryColor"] = "#FF6B7A90",
        ["OmniXDialogBackgroundColor"] = "#FFF5F9FF",
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
        ["AccentTextFillColorPrimaryBrush"] = "#FF2A6ECC",
        ["AccentTextFillColorSecondaryBrush"] = "#FF2A6ECC",
        ["AccentTextFillColorTertiaryBrush"] = "#FF2A6ECC"
    };

    private static readonly Dictionary<string, string> BlueDark = new()
    {
        ["OmniXAccentColor"] = "#FF7EC8FF",
        ["OmniXAccentStrongColor"] = "#FFA8D8FF",
        ["OmniXAccentSoftColor"] = "#FF1E2A38",
        ["OmniXAccentSoftHoverColor"] = "#FF26354A",
        ["OmniXAccentSoftPressedColor"] = "#FF2E4058",
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF4A9EFF",
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
        ["AccentTextFillColorPrimaryBrush"] = "#FFA8D8FF",
        ["AccentTextFillColorSecondaryBrush"] = "#FFA8D8FF",
        ["AccentTextFillColorTertiaryBrush"] = "#FFA8D8FF"
    };

    // ========== 深黑白：整体就是深色，明暗两组共用 ==========

    private static readonly string[] MonoGradient = ["#FF0F0F11", "#FF141417", "#FF101014"];

    private static readonly Dictionary<string, string> MonoColors = new()
    {
        // 深色版主色：交接文档给的是 #E63946（OMNIX logo 那抹红），
        // HTML 深色版那份写的是纯白，这里以文档为准
        ["OmniXAccentColor"] = "#FFE63946",
        ["OmniXAccentStrongColor"] = "#FFEF6B77",
        ["OmniXAccentSoftColor"] = "#FF1AE63946",
        ["OmniXAccentSoftHoverColor"] = "#FF26E63946",
        ["OmniXAccentSoftPressedColor"] = "#FF33E63946",
        ["OmniXAuxPinkColor"] = "#FFFFB3D1",
        ["OmniXAuxPurpleColor"] = "#FFB58BFF",
        ["OmniXAuxBlueColor"] = "#FF7EC8FF",
        ["OmniXCardAltBackgroundColor"] = "#FF141417",
        ["OmniXCardBackgroundColor"] = "#FF1C1C20",
        ["OmniXCardStrokeColor"] = "#FF2A2A30",
        ["OmniXCardStrokeHoverColor"] = "#FF3A3A42",
        ["OmniXControlBackgroundColor"] = "#FF222226",
        ["OmniXControlBorderColor"] = "#FF2A2A30",
        ["OmniXControlBorderPointerOverColor"] = "#FF8A8A92",
        ["OmniXHeroBackgroundColor"] = "#FF161618",
        ["OmniXNavItemPointerOverColor"] = "#FF1FFFFFFF",
        ["OmniXNavItemPressedColor"] = "#FF2AFFFFFF",
        ["OmniXNavItemSelectedColor"] = "#FF1AE63946",
        ["OmniXNavItemSelectedPointerOverColor"] = "#FF26E63946",
        ["OmniXNavItemSelectedPressedColor"] = "#FF33E63946",
        ["OmniXNavPaneBackgroundColor"] = "#FF161618",
        ["OmniXPageBackgroundColor"] = "#FF0F0F11",
        ["OmniXSidebarBackgroundColor"] = "#FF161618",
        ["OmniXTextPrimaryColor"] = "#FFF5F5F7",
        ["OmniXTextSecondaryColor"] = "#FF8A8A92",
        ["OmniXDialogBackgroundColor"] = "#FF1C1C20",
        ["OmniXDialogBorderColor"] = "#FF2A2A30",
        ["SystemAccentColor"] = "#FFE63946",
        ["SystemAccentColorLight1"] = "#FFEF6B77",
        ["SystemAccentColorLight2"] = "#FFF49AA4",
        ["SystemAccentColorLight3"] = "#FFF9C5CB",
        ["SystemAccentColorDark1"] = "#FFC42B38",
        ["SystemAccentColorDark2"] = "#FF9E202B",
        ["SystemAccentColorDark3"] = "#FF7A1821",
        ["AccentFillColorDefaultBrush"] = "#FFE63946",
        ["AccentFillColorSecondaryBrush"] = "#E6E63946",
        ["AccentFillColorTertiaryBrush"] = "#CCE63946",
        ["AccentFillColorDisabledBrush"] = "#33E63946",
        // 近黑底上用亮一点的红，纯 #E63946 当文字会糊
        ["AccentTextFillColorPrimaryBrush"] = "#FFFF6B77",
        ["AccentTextFillColorSecondaryBrush"] = "#FFFF6B77",
        ["AccentTextFillColorTertiaryBrush"] = "#FFFF6B77"
    };
}
