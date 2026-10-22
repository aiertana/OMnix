using OMniX.WinUI.Contracts.Services;

namespace OMniX.WinUI.Services;

/// <summary>
///     界面风格。配色始终只有一套（主色红 + 中性灰），两种风格差的只是动效：
///     Simple  = 1.3 那套静态玻璃，不动；
///     Standard= 流动光斑 + 悬停上浮 + 按压反馈 + 卡片扫光。
/// </summary>
public enum OmniXThemeStyle
{
    /// <summary>简易版：静态玻璃，没有动画，性能最好</summary>
    Simple,

    /// <summary>标准版：带流动与交互反馈</summary>
    Standard
}

public sealed class ThemeStyleService
{
    public const string ThemeStyleKey = "App.ThemeStyle";

    private readonly ILocalSettingsService _localSettingsService;

    public static ThemeStyleService? Instance { get; private set; }

    public OmniXThemeStyle Current { get; private set; } = OmniXThemeStyle.Standard;

    /// <summary>标准版才开动效</summary>
    public bool MotionEnabled => Current == OmniXThemeStyle.Standard;

    public event EventHandler<OmniXThemeStyle>? Changed;

    public ThemeStyleService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        Instance = this;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var saved = await _localSettingsService.ReadSettingAsync<string>(ThemeStyleKey);

            Current = string.Equals(saved, nameof(OmniXThemeStyle.Simple), StringComparison.OrdinalIgnoreCase)
                ? OmniXThemeStyle.Simple
                : OmniXThemeStyle.Standard;
        }
        catch (Exception)
        {
            Current = OmniXThemeStyle.Standard;
        }
    }

    public async Task SetAsync(OmniXThemeStyle style)
    {
        if (Current == style)
            return;

        Current = style;

        try
        {
            await _localSettingsService.SaveSettingAsync(ThemeStyleKey, style.ToString());
        }
        catch (Exception)
        {
            // 存不进去也要让界面先切过去
        }

        Changed?.Invoke(this, style);
    }

    public string DisplayName =>
        Current == OmniXThemeStyle.Standard ? "标准版" : "简易版";
}
