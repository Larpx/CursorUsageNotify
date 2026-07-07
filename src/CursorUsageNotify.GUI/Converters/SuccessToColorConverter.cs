using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.Converters
{
    /// <summary>
    /// 布尔 → 颜色转换器（true=绿色，false=红色，null=灰色）。
    /// 用于测试结果显示颜色切换。
    /// </summary>
    public sealed class SuccessToColorConverter : IValueConverter
    {
        /// <summary>
        /// 单例实例，避免 XAML 重复实例化。
        /// </summary>
        public static readonly SuccessToColorConverter Instance = new();

        /// <summary>
        /// 将布尔值转换为颜色画刷：true=绿色，false=红色，非布尔值=灰色。
        /// </summary>
        /// <param name="value">
        /// 原始值（应为 bool）。
        /// </param>
        /// <param name="targetType">
        /// 目标类型。
        /// </param>
        /// <param name="parameter">
        /// 转换参数（未使用）。
        /// </param>
        /// <param name="culture">
        /// 区域信息（未使用）。
        /// </param>
        /// <returns>
        /// 对应颜色画刷。
        /// </returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool ok)
            {
                return ok ? Brushes.Green : Brushes.Red;
            }
            return Brushes.Gray;
        }

        /// <summary>
        /// 反向转换不支持。
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// 始终抛出。
        /// </exception>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
