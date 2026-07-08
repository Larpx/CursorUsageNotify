using System.Globalization;
using Avalonia.Data.Converters;
using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.Converters
{
    /// <summary>
    /// Token 数值 → 格式化字符串转换器。
    /// 通过静态 <see cref="Mode"/> 读取当前全局格式，MainViewModel 切换时同步设置。
    /// </summary>
    public sealed class TokenFormatConverter : IValueConverter
    {
        /// <summary>
        /// 单例实例，避免 XAML 重复实例化。
        /// </summary>
        public static readonly TokenFormatConverter Instance = new();

        /// <summary>
        /// 当前全局显示格式（由 MainViewModel.CycleTokenFormatCommand 切换时同步设置）。
        /// </summary>
        public static TokenDisplayMode Mode { get; set; } = TokenDisplayMode.FullNumber;

        /// <inheritdoc/>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                long lng => TokenFormatter.Format(lng, Mode),
                int i => TokenFormatter.Format(i, Mode),
                double d => TokenFormatter.Format((long)d, Mode),
                _ => "-"
            };
        }

        /// <inheritdoc/>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
