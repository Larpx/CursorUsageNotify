using System.Globalization;
using Avalonia.Data.Converters;

namespace CursorUsageNotify.GUI.Converters;

/// <summary>
/// Epoch 毫秒 → 本地时间字符串转换器（yyyy-MM-dd HH:mm:ss）。
/// 用于 DataGrid 时间列和数据大屏最近拉取时间。
/// </summary>
public sealed class EpochToDateTimeConverter : IValueConverter
{
    /// <summary>单例实例，避免 XAML 重复实例化。</summary>
    public static readonly EpochToDateTimeConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long ms && ms > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms)
                .LocalDateTime
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return "-";
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("EpochToDateTimeConverter 不支持反向转换。");
    }
}
