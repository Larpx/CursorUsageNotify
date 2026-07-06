using System.Globalization;
using Avalonia.Data.Converters;

namespace CursorUsageNotify.GUI.Converters;

/// <summary>
/// 美分 → 美元字符串转换器（InvariantCulture，2 位小数）。
/// 用于数据大屏和 DataGrid 显示金额。
/// </summary>
public sealed class CentsToCurrencyConverter : IValueConverter
{
    /// <summary>单例实例，避免 XAML 重复实例化。</summary>
    public static readonly CentsToCurrencyConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal cents)
        {
            return (cents / 100m).ToString("C2", CultureInfo.InvariantCulture);
        }

        if (value is long centsLong)
        {
            return (centsLong / 100m).ToString("C2", CultureInfo.InvariantCulture);
        }

        if (value is int centsInt)
        {
            return (centsInt / 100m).ToString("C2", CultureInfo.InvariantCulture);
        }

        return "-";
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("CentsToCurrencyConverter 不支持反向转换。");
    }
}
