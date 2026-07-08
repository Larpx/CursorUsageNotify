
namespace Larpx.PersonalTools.CursorUsageNotify.Models.Dtos
{
    /// <summary>
    /// 用量聚合统计结果（从 usage_events 表按时间范围聚合）。
    /// </summary>
    public sealed class UsageAggregateStats
    {
        /// <summary>
        /// 总 token 数（输入 + 输出 + 缓存读 + 缓存写）。
        /// </summary>
        public long TotalTokens { get; set; }

        /// <summary>
        /// 总请求数（事件行数）。
        /// </summary>
        public int TotalRequests { get; set; }

        /// <summary>
        /// 总支出（美分）。
        /// </summary>
        public decimal TotalSpendCents { get; set; }

        /// <summary>
        /// 输入 token 总数。
        /// </summary>
        public long TotalInputTokens { get; set; }

        /// <summary>
        /// 输出 token 总数。
        /// </summary>
        public long TotalOutputTokens { get; set; }

        /// <summary>
        /// 缓存读取 token 总数。
        /// </summary>
        public long TotalCacheReadTokens { get; set; }

        /// <summary>
        /// 缓存写入 token 总数。
        /// </summary>
        public long TotalCacheWriteTokens { get; set; }

        /// <summary>
        /// 按 token 计费的请求数。
        /// </summary>
        public int TokenBasedRequests { get; set; }

        /// <summary>
        /// 按请求成本计费的请求数。
        /// </summary>
        public int CostBasedRequests { get; set; }

        /// <summary>
        /// 聚合起始时间（epoch 毫秒）。
        /// </summary>
        public long PeriodStart { get; set; }

        /// <summary>
        /// 聚合结束时间（epoch 毫秒）。
        /// </summary>
        public long PeriodEnd { get; set; }

        /// <summary>
        /// 聚合范围内的用户邮箱（去重后取第一条）。
        /// </summary>
        public string? UserEmail { get; set; }
    }
}
