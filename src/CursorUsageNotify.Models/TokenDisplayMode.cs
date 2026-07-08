namespace Larpx.PersonalTools.CursorUsageNotify.Models
{
    /// <summary>
    /// Token 显示格式（用户可在数据大屏切换，全局生效）。
    /// </summary>
    public enum TokenDisplayMode
    {
        /// <summary>
        /// 显示全部数字（如 1,234,567）。
        /// </summary>
        FullNumber,

        /// <summary>
        /// 以万为单位保留一位小数（如 123.5万）。
        /// </summary>
        Wan,

        /// <summary>
        /// 以百万为单位保留一位小数（如 1.2百万）。
        /// </summary>
        Million
    }

    /// <summary>
    /// Token 数值格式化器：按 <see cref="TokenDisplayMode"/> 格式化 long 值，
    /// 四舍五入使用 <see cref="MidpointRounding.AwayFromZero"/>。
    /// </summary>
    public static class TokenFormatter
    {
        /// <summary>
        /// 按 <paramref name="mode"/> 格式化 token 数值。
        /// </summary>
        /// <param name="value">token 数值。</param>
        /// <param name="mode">显示格式。</param>
        /// <returns>格式化后的字符串。</returns>
        public static string Format(long value, TokenDisplayMode mode)
        {
            return mode switch
            {
                TokenDisplayMode.FullNumber => value.ToString("N0"),
                TokenDisplayMode.Wan => Math.Round(value / 10000.0, 1, MidpointRounding.AwayFromZero).ToString("F1") + "万",
                TokenDisplayMode.Million => Math.Round(value / 1000000.0, 1, MidpointRounding.AwayFromZero).ToString("F1") + "百万",
                _ => value.ToString("N0")
            };
        }
    }
}
