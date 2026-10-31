using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     value（int）等于 parameter（字符串形式的数字）时用 TrueValue。
///     悬浮窗排序 chips 的高亮用。
/// </summary>
public class IntEqualsToBrushConverter : IValueConverter
{
    public Brush? TrueValue { get; set; }

    public Brush? FalseValue { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value?.ToString() == parameter?.ToString()
            ? TrueValue ?? FalseValue
            : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
