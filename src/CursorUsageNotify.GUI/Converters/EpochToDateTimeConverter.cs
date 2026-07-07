using System.Globalization;
using Avalonia.Data.Converters;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.Converters
{
    /// <summary>
    /// Epoch 毫秒 → 本地时间字符串转换器（yyyy-MM-dd HH:mm:ss）。
    /// 用于 DataGrid 时间列和数据大屏最近拉取时间。
    /// </summary>
    public sealed class EpochToDateTimeConverter : IValueConverter
    {
        /// <summary>
        /// 单例实例，避免 XAML 重复实例化。
        /// </summary>
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
            if (value == null)
                return null;

            // 如果已经是数字（毫秒），直接返回（支持 long/int）
            if (value is long msValue)
            {
                return msValue;
            }

            if (value is int intValue)
            {
                return (long)intValue;
            }

            DateTime localDateTime;

            if (value is DateTime dt)
            {
                localDateTime = dt;
            }
            else if (value is string s)
            {
                s = s.Trim();
                if (s == "-" || s.Length == 0)
                    return null;

                // 先尝试精确解析到与 Convert 输出一致的格式，再退回到通用解析。
                if (!DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out localDateTime)
                    && !DateTime.TryParse(s, culture, DateTimeStyles.None, out localDateTime))
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            // Convert 输出为本地时间字符串，反向时将本地时间转换为 UTC 再计算 epoch 毫秒
            var utc = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local).ToUniversalTime();
            long epochMs = (long)(utc - DateTimeOffset.UnixEpoch).TotalMilliseconds;

            if (targetType == typeof(long) || targetType == typeof(long?))
                return epochMs;
            if (targetType == typeof(int) || targetType == typeof(int?))
                return (int)epochMs;
            if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
                return localDateTime;
            if (targetType == typeof(DateTimeOffset) || targetType == typeof(DateTimeOffset?))
                return new DateTimeOffset(localDateTime);

            // 默认返回 long 毫秒数
            return epochMs;
        }
    }
}
