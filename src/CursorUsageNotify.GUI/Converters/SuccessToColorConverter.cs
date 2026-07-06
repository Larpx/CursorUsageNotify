using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CursorUsageNotify.GUI.Converters;

/// <summary>
/// 布尔 → 颜色转换器（true=绿色，false=红色，null=灰色）。
/// 用于测试结果显示颜色切换。
/// </summary>
public sealed class SuccessToColorConverter : IValueConverter
{
    public static readonly SuccessToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool ok)
        {
            return ok ? Brushes.Green : Brushes.Red;
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
