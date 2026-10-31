namespace OMniX.WinUI.Models;

/// <summary>
///     游戏内悬浮窗（Alt+C）的设置。-1 表示还没存过位置，首次显示时会自动居中到屏幕右上角。
/// </summary>
public class ModOverlaySettings
{
    public const string Key = nameof(ModOverlaySettings);

    /// <summary>
    ///     是否启用悬浮窗与全局热键。
    /// </summary>
    public bool Enabled { get; set; } = true;

    public double X { get; set; } = -1;

    public double Y { get; set; } = -1;
}
