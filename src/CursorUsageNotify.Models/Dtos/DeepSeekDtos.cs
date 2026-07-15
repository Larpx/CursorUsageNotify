using System.Text.Json.Serialization;


namespace Larpx.PersonalTools.CursorUsageNotify.Models.Dtos
{
    /// <summary>
    /// DeepSeek API 通用响应包装。
    /// DeepSeek 所有 API 均使用 code/msg/data 三层结构，data 内再嵌套 biz_code/biz_msg/biz_data。
    /// </summary>
    /// <typeparam name="T">biz_data 的具体类型。</typeparam>
    public sealed class DeepSeekResponse<T>
    {
        /// <summary>
        /// 外层状态码（0 表示成功）。
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// 外层消息。
        /// </summary>
        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        /// <summary>
        /// 内层业务数据包装。
        /// </summary>
        [JsonPropertyName("data")]
        public DeepSeekBizData<T>? Data { get; set; }

        /// <summary>
        /// 是否成功（外层 code=0 且内层 biz_code=0）。
        /// </summary>
        [JsonIgnore]
        public bool IsSuccess => Code == 0 && Data?.BizCode == 0;
    }

    /// <summary>
    /// DeepSeek 内层业务数据包装。
    /// </summary>
    /// <typeparam name="T">biz_data 的具体类型。</typeparam>
    public sealed class DeepSeekBizData<T>
    {
        /// <summary>
        /// 内层业务状态码。
        /// </summary>
        [JsonPropertyName("biz_code")]
        public int BizCode { get; set; }

        /// <summary>
        /// 内层业务消息。
        /// </summary>
        [JsonPropertyName("biz_msg")]
        public string? BizMsg { get; set; }

        /// <summary>
        /// 实际业务数据。
        /// </summary>
        [JsonPropertyName("biz_data")]
        public T? BizDataValue { get; set; }
    }

    /// <summary>
    /// 用户账户汇总 DTO，对应 /api/v0/users/get_user_summary 响应。
    /// 包含本月用量、账户余额、总费用等信息。
    /// </summary>
    public sealed class DeepSeekUserSummaryDto
    {
        /// <summary>
        /// 当前可用 token 额度（账户余额对应的 token 估算）。
        /// </summary>
        [JsonPropertyName("current_token")]
        public long CurrentToken { get; set; }

        /// <summary>
        /// 本月已用 token（字符串形式）。
        /// </summary>
        [JsonPropertyName("monthly_usage")]
        public string? MonthlyUsage { get; set; }

        /// <summary>
        /// 总用量 token（字符串形式，可能为 0）。
        /// </summary>
        [JsonPropertyName("total_usage")]
        public string? TotalUsage { get; set; }

        /// <summary>
        /// 正常充值钱包列表。
        /// </summary>
        [JsonPropertyName("normal_wallets")]
        public List<DeepSeekWalletDto>? NormalWallets { get; set; }

        /// <summary>
        /// 赠送钱包列表。
        /// </summary>
        [JsonPropertyName("bonus_wallets")]
        public List<DeepSeekWalletDto>? BonusWallets { get; set; }

        /// <summary>
        /// 总可用 token 估算（字符串形式）。
        /// </summary>
        [JsonPropertyName("total_available_token_estimation")]
        public string? TotalAvailableTokenEstimation { get; set; }

        /// <summary>
        /// 本月费用列表（按币种）。
        /// </summary>
        [JsonPropertyName("monthly_costs")]
        public List<DeepSeekCostDto>? MonthlyCosts { get; set; }

        /// <summary>
        /// 本月 token 用量（字符串形式，与 monthly_usage 相同）。
        /// </summary>
        [JsonPropertyName("monthly_token_usage")]
        public string? MonthlyTokenUsage { get; set; }

        /// <summary>
        /// 总费用列表（按币种）。
        /// </summary>
        [JsonPropertyName("total_costs")]
        public List<DeepSeekCostDto>? TotalCosts { get; set; }
    }

    /// <summary>
    /// 钱包余额信息。
    /// </summary>
    public sealed class DeepSeekWalletDto
    {
        /// <summary>
        /// 币种（如 CNY）。
        /// </summary>
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        /// <summary>
        /// 余额（字符串形式，单位元）。
        /// </summary>
        [JsonPropertyName("balance")]
        public string? Balance { get; set; }

        /// <summary>
        /// 余额对应的 token 估算（字符串形式）。
        /// </summary>
        [JsonPropertyName("token_estimation")]
        public string? TokenEstimation { get; set; }
    }

    /// <summary>
    /// 费用信息。
    /// </summary>
    public sealed class DeepSeekCostDto
    {
        /// <summary>
        /// 币种（如 CNY）。
        /// </summary>
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        /// <summary>
        /// 金额（字符串形式，单位元）。
        /// </summary>
        [JsonPropertyName("amount")]
        public string? Amount { get; set; }
    }

    /// <summary>
    /// 按 API Key 分组的用量数据 DTO，对应 /api/v0/usage/by_api_key/amount 响应。
    /// 返回每日 token 用量明细，按 api_key × model 分组。
    /// </summary>
    public sealed class DeepSeekUsageAmountDto
    {
        /// <summary>
        /// 查询起始时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("start")]
        public long Start { get; set; }

        /// <summary>
        /// 查询结束时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("end")]
        public long End { get; set; }

        /// <summary>
        /// 时间桶大小（秒，86400=按天）。
        /// </summary>
        [JsonPropertyName("bucket")]
        public long Bucket { get; set; }

        /// <summary>
        /// 涉及的模型列表。
        /// </summary>
        [JsonPropertyName("models")]
        public List<string>? Models { get; set; }

        /// <summary>
        /// 按 api_key × model 分组的用量序列。
        /// </summary>
        [JsonPropertyName("series")]
        public List<DeepSeekAmountSeriesDto>? Series { get; set; }
    }

    /// <summary>
    /// 单个 api_key × model 的用量序列。
    /// </summary>
    public sealed class DeepSeekAmountSeriesDto
    {
        /// <summary>
        /// API Key 信息。
        /// </summary>
        [JsonPropertyName("api_key")]
        public DeepSeekApiKeyDto? ApiKey { get; set; }

        /// <summary>
        /// 模型名。
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>
        /// 每日用量桶。
        /// </summary>
        [JsonPropertyName("buckets")]
        public List<DeepSeekAmountBucketDto>? Buckets { get; set; }
    }

    /// <summary>
    /// API Key 信息。
    /// </summary>
    public sealed class DeepSeekApiKeyDto
    {
        /// <summary>
        /// API Key 追踪 ID。
        /// </summary>
        [JsonPropertyName("tracking_id")]
        public string? TrackingId { get; set; }

        /// <summary>
        /// API Key 名称（可用作用户标识）。
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// 脱敏的 API Key。
        /// </summary>
        [JsonPropertyName("sensitive_id")]
        public string? SensitiveId { get; set; }

        /// <summary>
        /// 是否有效。
        /// </summary>
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }

    /// <summary>
    /// 单日用量桶。
    /// </summary>
    public sealed class DeepSeekAmountBucketDto
    {
        /// <summary>
        /// 桶起始时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("time")]
        public long Time { get; set; }

        /// <summary>
        /// token 用量明细。
        /// </summary>
        [JsonPropertyName("usage")]
        public DeepSeekTokenUsageDto? Usage { get; set; }
    }

    /// <summary>
    /// DeepSeek token 用量明细。
    /// 注意：DeepSeek 没有 CacheWriteTokens（仅 Cursor 有）。
    /// </summary>
    public sealed class DeepSeekTokenUsageDto
    {
        /// <summary>
        /// 输出 token（响应 token）。
        /// </summary>
        [JsonPropertyName("RESPONSE_TOKEN")]
        public long ResponseToken { get; set; }

        /// <summary>
        /// 请求数。
        /// </summary>
        [JsonPropertyName("REQUEST")]
        public long Request { get; set; }

        /// <summary>
        /// 缓存命中 token（输入命中缓存）。
        /// </summary>
        [JsonPropertyName("PROMPT_CACHE_HIT_TOKEN")]
        public long PromptCacheHitToken { get; set; }

        /// <summary>
        /// 缓存未命中 token（输入未命中缓存，即实际输入 token）。
        /// </summary>
        [JsonPropertyName("PROMPT_CACHE_MISS_TOKEN")]
        public long PromptCacheMissToken { get; set; }
    }

    /// <summary>
    /// 按 API Key 分组的费用数据 DTO，对应 /api/v0/usage/by_api_key/cost 响应。
    /// 返回每日费用明细，按币种 × api_key × model 分组。
    /// </summary>
    public sealed class DeepSeekUsageCostDto
    {
        /// <summary>
        /// 查询起始时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("start")]
        public long Start { get; set; }

        /// <summary>
        /// 查询结束时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("end")]
        public long End { get; set; }

        /// <summary>
        /// 时间桶大小（秒，86400=按天）。
        /// </summary>
        [JsonPropertyName("bucket")]
        public long Bucket { get; set; }

        /// <summary>
        /// 涉及的模型列表。
        /// </summary>
        [JsonPropertyName("models")]
        public List<string>? Models { get; set; }

        /// <summary>
        /// 按币种分组的费用序列。
        /// </summary>
        [JsonPropertyName("data")]
        public List<DeepSeekCostCurrencyDto>? Data { get; set; }
    }

    /// <summary>
    /// 单币种费用序列。
    /// </summary>
    public sealed class DeepSeekCostCurrencyDto
    {
        /// <summary>
        /// 币种（如 CNY）。
        /// </summary>
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        /// <summary>
        /// 按 api_key × model 分组的费用序列。
        /// </summary>
        [JsonPropertyName("series")]
        public List<DeepSeekCostSeriesDto>? Series { get; set; }
    }

    /// <summary>
    /// 单个 api_key × model 的费用序列。
    /// </summary>
    public sealed class DeepSeekCostSeriesDto
    {
        /// <summary>
        /// API Key 信息。
        /// </summary>
        [JsonPropertyName("api_key")]
        public DeepSeekApiKeyDto? ApiKey { get; set; }

        /// <summary>
        /// 模型名。
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>
        /// 每日费用桶。
        /// </summary>
        [JsonPropertyName("buckets")]
        public List<DeepSeekCostBucketDto>? Buckets { get; set; }
    }

    /// <summary>
    /// 单日费用桶。
    /// </summary>
    public sealed class DeepSeekCostBucketDto
    {
        /// <summary>
        /// 桶起始时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("time")]
        public long Time { get; set; }

        /// <summary>
        /// 费用（字符串形式，单位元）。
        /// </summary>
        [JsonPropertyName("cost")]
        public string? Cost { get; set; }
    }

    /// <summary>
    /// API Key 列表 DTO，对应 /api/v0/users/get_api_keys 响应。
    /// 用于推断用户标识（API Key 名称）。
    /// </summary>
    public sealed class DeepSeekApiKeysDto
    {
        /// <summary>
        /// API Key 列表。
        /// </summary>
        [JsonPropertyName("api_keys")]
        public List<DeepSeekApiKeyInfoDto>? ApiKeys { get; set; }
    }

    /// <summary>
    /// API Key 详细信息。
    /// </summary>
    public sealed class DeepSeekApiKeyInfoDto
    {
        /// <summary>
        /// 创建时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("created_at")]
        public long CreatedAt { get; set; }

        /// <summary>
        /// 最后使用时间（Unix 秒）。
        /// </summary>
        [JsonPropertyName("last_use")]
        public long LastUse { get; set; }

        /// <summary>
        /// API Key 追踪 ID。
        /// </summary>
        [JsonPropertyName("tracking_id")]
        public string? TrackingId { get; set; }

        /// <summary>
        /// 脱敏的 API Key。
        /// </summary>
        [JsonPropertyName("sensitive_id")]
        public string? SensitiveId { get; set; }

        /// <summary>
        /// API Key 名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
