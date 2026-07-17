
namespace Larpx.PersonalTools.CursorUsageNotify.Models.Dtos
{
    /// <summary>
    /// 按 API Key × 模型聚合的用量明细（DeepSeek 大屏 hover 展示）。
    /// </summary>
    public sealed class ApiKeyModelUsageBreakdown
    {
        /// <summary>
        /// API Key 显示名。
        /// </summary>
        public string ApiKeyName { get; set; } = string.Empty;

        /// <summary>
        /// API Key 追踪 ID（稳定标识）。
        /// </summary>
        public string ApiKeyTrackingId { get; set; } = string.Empty;

        /// <summary>
        /// 模型名。
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// 输入 token。
        /// </summary>
        public long InputTokens { get; set; }

        /// <summary>
        /// 输出 token。
        /// </summary>
        public long OutputTokens { get; set; }

        /// <summary>
        /// 缓存读取 token。
        /// </summary>
        public long CacheReadTokens { get; set; }

        /// <summary>
        /// 总 token（输入+输出+缓存读+缓存写）。
        /// </summary>
        public long TotalTokens { get; set; }

        /// <summary>
        /// 请求次数（DeepSeek usage.REQUEST 累计）。
        /// </summary>
        public long RequestCount { get; set; }

        /// <summary>
        /// 费用（分）。
        /// </summary>
        public decimal SpendCents { get; set; }
    }
}
