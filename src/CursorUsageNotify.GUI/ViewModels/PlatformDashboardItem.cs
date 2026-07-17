using CommunityToolkit.Mvvm.ComponentModel;
using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels
{
    /// <summary>
    /// 单个平台在数据大屏上的显示项。每个 <see cref="PlatformType"/> 对应一个实例，
    /// MainViewModel 持有多个实例并按启用状态控制可见性。
    /// </summary>
    /// <remarks>
    /// 该类是 GUI 专用 DTO，不参与持久化或服务层逻辑；
    /// 所有属性均可空为默认占位文本（"-"），避免空引用异常。
    /// </remarks>
    public sealed partial class PlatformDashboardItem : ObservableObject
    {
        /// <summary>
        /// 平台类型标识。
        /// </summary>
        public PlatformType Platform { get; init; }

        /// <summary>
        /// 平台显示名称（"Cursor" / "DeepSeek"）。
        /// </summary>
        public string PlatformName { get; init; } = string.Empty;

        /// <summary>
        /// 是否显示缓存写入列（仅 Cursor 有，DeepSeek 无此概念）。
        /// </summary>
        public bool HasCacheWrite { get; init; } = true;

        /// <summary>
        /// 是否显示订阅周期/状态（仅 Cursor 有 Stripe 订阅）。
        /// </summary>
        public bool HasSubscription { get; init; } = true;

        /// <summary>
        /// 是否显示账户摘要（余额/累计消费/总请求，DeepSeek 使用）。
        /// </summary>
        public bool HasAccountSummary { get; init; }

        /// <summary>
        /// 货币符号（Cursor: $，DeepSeek: ¥）。
        /// </summary>
        public string CurrencySymbol { get; init; } = "$";

        /// <summary>
        /// 是否启用（控制大屏该列是否显示，绑定到 IsVisible）。
        /// </summary>
        [ObservableProperty]
        private bool _isEnabled = true;

        // ──────────────────────────────────────────────────────────────
        // 账户信息
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 用户显示名。
        /// </summary>
        [ObservableProperty]
        private string _displayName = "-";

        /// <summary>
        /// 用户邮箱（如有）。
        /// </summary>
        [ObservableProperty]
        private string _email = "-";

        /// <summary>
        /// 订阅计划名（Cursor）/ 充值类型（DeepSeek "pay-as-you-go"）。
        /// </summary>
        [ObservableProperty]
        private string _planName = "-";

        /// <summary>
        /// 订阅周期文本（Cursor 起止日期）。
        /// </summary>
        [ObservableProperty]
        private string _periodRange = "-";

        /// <summary>
        /// 订阅状态文本（仅 Cursor 有意义）。
        /// </summary>
        [ObservableProperty]
        private string _subStatus = "-";

        /// <summary>
        /// 账户充值余额（DeepSeek，元）。
        /// </summary>
        [ObservableProperty]
        private decimal _accountBalance;

        /// <summary>
        /// 累计消费金额（DeepSeek，元）。
        /// </summary>
        [ObservableProperty]
        private decimal _cumulativeSpend;

        /// <summary>
        /// 总请求次数（DeepSeek 为本月 REQUEST 合计）。
        /// </summary>
        [ObservableProperty]
        private long _totalRequestCount;

        /// <summary>
        /// 按 API Key × 模型的用量明细（hover 展示）。
        /// </summary>
        [ObservableProperty]
        private string _usageBreakdownTooltip = string.Empty;

        // ──────────────────────────────────────────────────────────────
        // 当天用量
        // ──────────────────────────────────────────────────────────────

        [ObservableProperty] private long _todayInputTokens;
        [ObservableProperty] private long _todayOutputTokens;
        [ObservableProperty] private long _todayCacheReadTokens;
        [ObservableProperty] private long _todayCacheWriteTokens;
        [ObservableProperty] private long _todayTotalTokens;
        [ObservableProperty] private decimal _todaySpend;

        // ──────────────────────────────────────────────────────────────
        // 本周用量
        // ──────────────────────────────────────────────────────────────

        [ObservableProperty] private long _weekInputTokens;
        [ObservableProperty] private long _weekOutputTokens;
        [ObservableProperty] private long _weekCacheReadTokens;
        [ObservableProperty] private long _weekCacheWriteTokens;
        [ObservableProperty] private long _weekTotalTokens;
        [ObservableProperty] private decimal _weekSpend;

        // ──────────────────────────────────────────────────────────────
        // 本周期用量（Cursor=订阅周期，DeepSeek=本月）
        // ──────────────────────────────────────────────────────────────

        [ObservableProperty] private long _periodInputTokens;
        [ObservableProperty] private long _periodOutputTokens;
        [ObservableProperty] private long _periodCacheReadTokens;
        [ObservableProperty] private long _periodCacheWriteTokens;
        [ObservableProperty] private long _periodTotalTokens;
        [ObservableProperty] private decimal _periodSpend;

        // ──────────────────────────────────────────────────────────────
        // 同步状态（用于 tooltip）
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 最近一次成功同步时间文本。
        /// </summary>
        [ObservableProperty]
        private string _lastSyncTime = "-";

        /// <summary>
        /// 同步状态文本（等待中/同步中/已同步/失败）。
        /// </summary>
        [ObservableProperty]
        private string _statusText = "等待中";

        /// <summary>
        /// 同步状态颜色（灰=未启动，黄=同步中，绿=已同步，红=失败）。
        /// </summary>
        [ObservableProperty]
        private string _statusColor = "#888888";

        /// <summary>
        /// 是否正在同步（用于状态灯 tooltip 区分）。
        /// </summary>
        [ObservableProperty]
        private bool _isSyncing;

        /// <summary>
        /// 重抛所有 Token 用量属性，触发大屏 Binding 重新求值。
        /// 用于全局切换 Token 显示格式时强制刷新所有平台列。
        /// </summary>
        public void RefreshTokenBindings()
        {
            OnPropertyChanged(nameof(TodayInputTokens));
            OnPropertyChanged(nameof(TodayOutputTokens));
            OnPropertyChanged(nameof(TodayCacheReadTokens));
            OnPropertyChanged(nameof(TodayCacheWriteTokens));
            OnPropertyChanged(nameof(TodayTotalTokens));
            OnPropertyChanged(nameof(WeekInputTokens));
            OnPropertyChanged(nameof(WeekOutputTokens));
            OnPropertyChanged(nameof(WeekCacheReadTokens));
            OnPropertyChanged(nameof(WeekCacheWriteTokens));
            OnPropertyChanged(nameof(WeekTotalTokens));
            OnPropertyChanged(nameof(PeriodInputTokens));
            OnPropertyChanged(nameof(PeriodOutputTokens));
            OnPropertyChanged(nameof(PeriodCacheReadTokens));
            OnPropertyChanged(nameof(PeriodCacheWriteTokens));
            OnPropertyChanged(nameof(PeriodTotalTokens));
        }
    }
}
