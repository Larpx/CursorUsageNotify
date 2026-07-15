using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;


namespace Larpx.PersonalTools.CursorUsageNotify.Models.Dtos
{
    /// <summary>
    /// DataGrid 显示模型：预计算所有格式化字符串，避免滚动时反复调用 IValueConverter 导致卡顿。
    /// 切换 Token 格式时仅需重新计算字符串字段后替换列表，无需销毁 DataGrid 视觉树。
    /// </summary>
    public sealed class UsageEventDisplayModel
    {
        /// <summary>
        /// 格式化后的时间字符串（yyyy-MM-dd HH:mm）。
        /// </summary>
        public string FormattedTimestamp { get; set; } = string.Empty;

        /// <summary>
        /// 平台名称（Cursor / DeepSeek）。
        /// </summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>
        /// 模型名称。
        /// </summary>
        public string Model { get; set; } = string.Empty;

        // ---- 原始 Token 数值（仅用于格式切换时重新计算，不绑定到 UI） ----

        /// <summary>
        /// 原始输入 Token 数。
        /// </summary>
        public long RawInputTokens { get; set; }

        /// <summary>
        /// 原始输出 Token 数。
        /// </summary>
        public long RawOutputTokens { get; set; }

        /// <summary>
        /// 原始缓存读取 Token 数。
        /// </summary>
        public long RawCacheReadTokens { get; set; }

        /// <summary>
        /// 原始缓存写入 Token 数。
        /// </summary>
        public long RawCacheWriteTokens { get; set; }

        // ---- 格式化字符串（绑定到 DataGrid 列） ----

        /// <summary>
        /// 格式化后的输入 Token 数量。
        /// </summary>
        public string FormattedInputTokens { get; set; } = string.Empty;

        /// <summary>
        /// 格式化后的输出 Token 数量。
        /// </summary>
        public string FormattedOutputTokens { get; set; } = string.Empty;

        /// <summary>
        /// 格式化后的缓存读取 Token 数量。
        /// </summary>
        public string FormattedCacheReadTokens { get; set; } = string.Empty;

        /// <summary>
        /// 格式化后的缓存写入 Token 数量。
        /// </summary>
        public string FormattedCacheWriteTokens { get; set; } = string.Empty;

        /// <summary>
        /// 格式化后的费用（美元）。
        /// </summary>
        public string FormattedCost { get; set; } = string.Empty;

        /// <summary>
        /// 是否 Max 模式。
        /// </summary>
        public bool MaxMode { get; set; }

        /// <summary>
        /// 是否 Headless 模式。
        /// </summary>
        public bool IsHeadless { get; set; }

        /// <summary>
        /// 根据指定格式重新计算 4 个 Token 显示字符串（其他字段不变）。
        /// </summary>
        /// <param name="mode">新的 Token 显示格式。</param>
        public void RefreshTokenFormat(TokenDisplayMode mode)
        {
            FormattedInputTokens = TokenFormatter.Format(RawInputTokens, mode);
            FormattedOutputTokens = TokenFormatter.Format(RawOutputTokens, mode);
            FormattedCacheReadTokens = TokenFormatter.Format(RawCacheReadTokens, mode);
            FormattedCacheWriteTokens = TokenFormatter.Format(RawCacheWriteTokens, mode);
        }

        /// <summary>
        /// 从 <see cref="UsageEventEntity"/> 创建显示模型，并预计算所有格式化字符串。
        /// </summary>
        /// <param name="entity">用量事件实体。</param>
        /// <param name="mode">Token 显示格式。</param>
        /// <returns>预计算字符串的显示模型。</returns>
        public static UsageEventDisplayModel FromEntity(UsageEventEntity entity, TokenDisplayMode mode)
        {
            // 时间：epoch 毫秒 → yyyy-MM-dd HH:mm
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(entity.Timestamp).LocalDateTime;
            var timestamp = dt.ToString("yyyy-MM-dd HH:mm");

            // 费用：美分 → 美元
            var cost = (entity.TotalCents / 100m).ToString("$0.00", System.Globalization.CultureInfo.InvariantCulture);

            return new UsageEventDisplayModel
            {
                FormattedTimestamp = timestamp,
                Platform = entity.Platform.ToString(),
                Model = entity.Model ?? string.Empty,
                RawInputTokens = entity.InputTokens,
                RawOutputTokens = entity.OutputTokens,
                RawCacheReadTokens = entity.CacheReadTokens,
                RawCacheWriteTokens = entity.CacheWriteTokens,
                FormattedInputTokens = TokenFormatter.Format(entity.InputTokens, mode),
                FormattedOutputTokens = TokenFormatter.Format(entity.OutputTokens, mode),
                FormattedCacheReadTokens = TokenFormatter.Format(entity.CacheReadTokens, mode),
                FormattedCacheWriteTokens = TokenFormatter.Format(entity.CacheWriteTokens, mode),
                FormattedCost = cost,
                MaxMode = entity.MaxMode,
                IsHeadless = entity.IsHeadless
            };
        }
    }
}
