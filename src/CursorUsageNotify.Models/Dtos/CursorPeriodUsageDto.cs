using System.Text.Json.Serialization;
using System.Text.Json;


namespace Larpx.PersonalTools.CursorUsageNotify.Models.Dtos
{
    /// <summary>
    /// 当前计费周期用量汇总 DTO，对应 /api/dashboard/get-current-period-usage 响应。
    /// 字段结构基于 2026-07 实际抓包验证：周期为字符串 epoch 毫秒，用量在 planUsage 子对象。
    /// </summary>
    public sealed class CursorPeriodUsageDto
    {
        /// <summary>
        /// 计费周期开始时间（字符串 epoch 毫秒，API 实际返回字符串而非数字）。
        /// </summary>
        [JsonPropertyName("billingCycleStart")]
        public string? BillingCycleStart { get; set; }

        /// <summary>
        /// 计费周期结束时间（字符串 epoch 毫秒）。
        /// </summary>
        [JsonPropertyName("billingCycleEnd")]
        public string? BillingCycleEnd { get; set; }

        /// <summary>
        /// 计划用量与支出汇总。
        /// </summary>
        [JsonPropertyName("planUsage")]
        public CursorPlanUsageDto? PlanUsage { get; set; }

        /// <summary>
        /// 支出上限配置。
        /// </summary>
        [JsonPropertyName("spendLimitUsage")]
        public CursorSpendLimitUsageDto? SpendLimitUsage { get; set; }

        /// <summary>
        /// 未识别字段（保留原始数据便于后续扩展）。
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? ExtraFields { get; set; }

        /// <summary>
        /// 周期开始（epoch 毫秒），字符串安全转换为 long。
        /// </summary>
        [JsonIgnore]
        public long PeriodStartMs => ParseEpochMs(BillingCycleStart);

        /// <summary>
        /// 周期结束（epoch 毫秒）。
        /// </summary>
        [JsonIgnore]
        public long PeriodEndMs => ParseEpochMs(BillingCycleEnd);

        /// <summary>
        /// 本周期总支出（美分）。
        /// </summary>
        [JsonIgnore]
        public long TotalSpendCents => PlanUsage?.TotalSpendCents ?? 0;

        /// <summary>
        /// 剩余额度（美分）。
        /// </summary>
        [JsonIgnore]
        public long RemainingCents => PlanUsage?.RemainingCents ?? 0;

        /// <summary>
        /// 额度上限（美分）。
        /// </summary>
        [JsonIgnore]
        public long LimitCents => PlanUsage?.LimitCents ?? 0;

        private static long ParseEpochMs(string? value)
            => long.TryParse(value, out var v) ? v : 0;
    }

    /// <summary>
    /// 计划用量与支出（/api/dashboard/get-current-period-usage.planUsage）。
    /// 金额单位均为美分。
    /// </summary>
    public sealed class CursorPlanUsageDto
    {
        /// <summary>
        /// 本周期已用支出（美分）。
        /// </summary>
        [JsonPropertyName("totalSpend")]
        public long TotalSpendCents { get; set; }

        /// <summary>
        /// 已用包含额度（美分）。
        /// </summary>
        [JsonPropertyName("includedSpend")]
        public long IncludedSpendCents { get; set; }

        /// <summary>
        /// 剩余额度（美分）。
        /// </summary>
        [JsonPropertyName("remaining")]
        public long RemainingCents { get; set; }

        /// <summary>
        /// 额度上限（美分）。
        /// </summary>
        [JsonPropertyName("limit")]
        public long LimitCents { get; set; }

        /// <summary>
        /// Auto 模型已用百分比。
        /// </summary>
        [JsonPropertyName("autoPercentUsed")]
        public double AutoPercentUsed { get; set; }

        /// <summary>
        /// API 模型已用百分比。
        /// </summary>
        [JsonPropertyName("apiPercentUsed")]
        public double ApiPercentUsed { get; set; }

        /// <summary>
        /// 总已用百分比。
        /// </summary>
        [JsonPropertyName("totalPercentUsed")]
        public double TotalPercentUsed { get; set; }

        /// <summary>
        /// 未识别字段（保留原始数据便于后续扩展）。
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? ExtraFields { get; set; }
    }

    /// <summary>
    /// 支出上限配置（/api/dashboard/get-current-period-usage.spendLimitUsage）。
    /// </summary>
    public sealed class CursorSpendLimitUsageDto
    {
        /// <summary>
        /// 个人支出上限（美分）。
        /// </summary>
        [JsonPropertyName("individualLimit")]
        public long IndividualLimitCents { get; set; }

        /// <summary>
        /// 个人剩余额度（美分）。
        /// </summary>
        [JsonPropertyName("individualRemaining")]
        public long IndividualRemainingCents { get; set; }

        /// <summary>
        /// 限额类型。
        /// </summary>
        [JsonPropertyName("limitType")]
        public string? LimitType { get; set; }

        /// <summary>
        /// 未识别字段（保留原始数据便于后续扩展）。
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? ExtraFields { get; set; }
    }
}
