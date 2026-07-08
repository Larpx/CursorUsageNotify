using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.GUI.Converters;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Services.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels
{
    /// <summary>
    /// 数据大屏 ViewModel：展示用户信息、订阅、用量、总 token 数等统计数据。
    /// 订阅 <see cref="UsageDataFetchedMessage"/> 自动刷新。
    /// </summary>
    public sealed partial class MainViewModel : ViewModelBase
    {
        private readonly IUsageRepository _repository;
        private readonly UsageSyncOptions _syncOptions;
        private readonly UserPreferences _userPrefs;

        /// <summary>
        /// 构造数据大屏 ViewModel，注入仓储与消息总线，
        /// 订阅同步事件并在初始化时拉取一次最新数据。
        /// </summary>
        /// <param name="repository">
        /// 用量数据仓储。
        /// </param>
        /// <param name="messenger">
        /// 消息总线。
        /// </param>
        /// <param name="syncOptions">
        /// 运行时同步选项（含 Token 显示格式共享状态）。
        /// </param>
        /// <param name="userPrefs">
        /// 用户偏好持久化配置。
        /// </param>
        public MainViewModel(IUsageRepository repository, IMessenger messenger, UsageSyncOptions syncOptions, UserPreferences userPrefs)
            : base(messenger)
        {
            _repository = repository;
            _syncOptions = syncOptions;
            _userPrefs = userPrefs;

            // 从持久化偏好恢复 Token 显示格式
            _tokenDisplayMode = userPrefs.TokenDisplayMode;
            _tokenFormatLabel = GetLabelForMode(userPrefs.TokenDisplayMode);
            _syncOptions.TokenDisplayMode = userPrefs.TokenDisplayMode;
            TokenFormatConverter.Mode = userPrefs.TokenDisplayMode;

            Messenger.Register<UsageDataFetchedMessage>(this, OnDataFetched);
            Messenger.Register<SyncFailedMessage>(this, OnSyncFailed);
            Messenger.Register<SyncStartedMessage>(this, OnSyncStarted);
            Messenger.Register<CookieExpiringSoonMessage>(this, OnCookieExpiring);
            Messenger.Register<EndpointDegradedMessage>(this, OnEndpointDegraded);
            Messenger.Register<TokenStorageUnavailableMessage>(this, OnTokenStorageUnavailable);
            _ = LoadAsync();
        }

        /// <summary>
        /// 根据 Token 显示格式返回对应的按钮标签文本。
        /// </summary>
        /// <param name="mode">Token 显示格式。</param>
        /// <returns>按钮标签。</returns>
        private static string GetLabelForMode(TokenDisplayMode mode)
        {
            return mode switch
            {
                TokenDisplayMode.Wan => "当前Token显示格式：万",
                TokenDisplayMode.Million => "当前Token显示格式：百万",
                _ => "当前Token显示格式：全部"
            };
        }

        /// <summary>
        /// 用户邮箱（来自登录账号）。
        /// </summary>
        [ObservableProperty]
        private string _userEmail = "未获取";

        /// <summary>
        /// 用户显示名（优先 Name，其次 Email）。
        /// </summary>
        [ObservableProperty]
        private string _userDisplayName = "未获取";

        /// <summary>
        /// 订阅计划名称（如 pro、free）。
        /// </summary>
        [ObservableProperty]
        private string _planName = "未获取";

        /// <summary>
        /// 当前订阅周期范围文本。
        /// </summary>
        [ObservableProperty]
        private string _periodRange = "-";

        // ---- 本订阅周期用量统计 ----

        /// <summary>
        /// 本订阅周期输入 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _periodInputTokens;

        /// <summary>
        /// 本订阅周期输出 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _periodOutputTokens;

        /// <summary>
        /// 本订阅周期缓存读取 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _periodCacheReadTokens;

        /// <summary>
        /// 本订阅周期缓存写入 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _periodCacheWriteTokens;

        /// <summary>
        /// 本订阅周期总支出（美元）。
        /// </summary>
        [ObservableProperty]
        private decimal _periodSpendDollars;

        /// <summary>
        /// 本订阅周期总 token 用量（输入+输出+缓存读+缓存写）。
        /// </summary>
        [ObservableProperty]
        private long _periodTotalTokens;

        // ---- 本周用量统计 ----

        /// <summary>
        /// 本周输入 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _weekInputTokens;

        /// <summary>
        /// 本周输出 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _weekOutputTokens;

        /// <summary>
        /// 本周缓存读取 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _weekCacheReadTokens;

        /// <summary>
        /// 本周缓存写入 token 总数。
        /// </summary>
        [ObservableProperty]
        private long _weekCacheWriteTokens;

        /// <summary>
        /// 本周总支出（美元）。
        /// </summary>
        [ObservableProperty]
        private decimal _weekSpendDollars;

        /// <summary>
        /// 本周总 token 用量（输入+输出+缓存读+缓存写）。
        /// </summary>
        [ObservableProperty]
        private long _weekTotalTokens;

        /// <summary>
        /// 最近一次成功拉取数据的时间文本。
        /// </summary>
        [ObservableProperty]
        private string _lastFetchTime = "-";

        /// <summary>
        /// 状态指示颜色（灰=未启动，黄=同步中，绿=已同步，红=失败）。
        /// </summary>
        [ObservableProperty]
        private string _statusColor = "#888888"; // 灰=未启动

        /// <summary>
        /// 状态文本（如：等待中、同步中、已同步、失败）。
        /// </summary>
        [ObservableProperty]
        private string _statusText = "等待中";

        /// <summary>
        /// 是否正在同步（控制顶部进度条显示，避免误以为卡顿）。
        /// </summary>
        [ObservableProperty]
        private bool _isSyncing;

        /// <summary>
        /// 当前 Token 显示格式标签（按钮上显示）。
        /// </summary>
        [ObservableProperty]
        private string _tokenFormatLabel = "当前Token显示格式：全部";

        /// <summary>
        /// 当前 Token 显示格式。
        /// </summary>
        [ObservableProperty]
        private TokenDisplayMode _tokenDisplayMode = TokenDisplayMode.FullNumber;

        /// <summary>
        /// Cookie 有效性状态文本（空表示正常，非空显示预警）。
        /// </summary>
        [ObservableProperty]
        private string _cookieStatusText = string.Empty;

        /// <summary>
        /// Cookie 预警颜色（红=已失效，黄=即将过期）。
        /// </summary>
        [ObservableProperty]
        private string _cookieStatusColor = string.Empty;

        /// <summary>
        /// 是否有 Cookie 预警（控制预警条可见性）。
        /// </summary>
        public bool HasCookieWarning => !string.IsNullOrEmpty(CookieStatusText);

        partial void OnCookieStatusTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasCookieWarning));
        }

        // 订阅信息

        /// <summary>
        /// 订阅开始日期文本。
        /// </summary>
        [ObservableProperty]
        private string _subStartDate = "-";

        /// <summary>
        /// 当前订阅周期结束日期（或下次续费/取消日期）。
        /// </summary>
        [ObservableProperty]
        private string _subEndDate = "-";

        /// <summary>
        /// 订阅状态文本（活跃、试用中、已取消、逾期）。
        /// </summary>
        [ObservableProperty]
        private string _subStatus = "-";

        /// <summary>
        /// 账单数量文本。
        /// </summary>
        [ObservableProperty]
        private string _invoiceCount = "-";

        private void OnDataFetched(object recipient, UsageDataFetchedMessage msg)
        {
            IsSyncing = false;
            _ = LoadAsync(msg);
        }

        private void OnSyncFailed(object recipient, SyncFailedMessage msg)
        {
            IsSyncing = false;
            StatusColor = "#dc3545"; // 红
            StatusText = $"失败：{msg.Error}";
            // 认证类失败（401/403）视为 cookie 失效
            if (msg.Error.Contains("401") || msg.Error.Contains("403") || msg.Error.Contains("认证"))
            {
                CookieStatusText = "Cookie 已失效，请在设置中更新";
                CookieStatusColor = "#dc3545";
            }
        }

        private void OnSyncStarted(object recipient, SyncStartedMessage msg)
        {
            IsSyncing = true;
            StatusColor = "#f59e0b"; // 黄=同步中
            StatusText = "同步中...";
        }

        private void OnCookieExpiring(object recipient, CookieExpiringSoonMessage msg)
        {
            if (msg.IsExpired)
            {
                CookieStatusText = "Cookie 已失效，请在设置中更新";
                CookieStatusColor = "#dc3545";
            }
            else
            {
                var days = Math.Max(0, (int)(msg.ExpiryUtc - DateTime.UtcNow).TotalDays);
                CookieStatusText = $"Cookie 将在 {days} 天后过期（{msg.ExpiryUtc:yyyy-MM-dd}）";
                CookieStatusColor = "#f59e0b";
            }
        }

        /// <summary>
        /// 端点探测发现失效端点：在状态栏红色提示用户客户端可能需要更新。
        /// </summary>
        private void OnEndpointDegraded(object recipient, EndpointDegradedMessage msg)
        {
            StatusColor = "#dc3545";
            StatusText = $"Cursor 接口已变更：{string.Join("、", msg.DegradedPaths)}，请检查客户端更新";
        }

        /// <summary>
        /// Token 加密存储不可用（DPAPI 不可用）：在状态栏红色提示，避免静默失败。
        /// </summary>
        private void OnTokenStorageUnavailable(object recipient, TokenStorageUnavailableMessage msg)
        {
            StatusColor = "#dc3545";
            StatusText = $"Token 加密存储不可用：{msg.Reason}，token 未保存";
        }

        /// <summary>
        /// 从数据库加载最新数据刷新大屏。
        /// </summary>
        public async Task LoadAsync(UsageDataFetchedMessage? msg = null)
        {
            var period = msg?.LatestPeriod ?? await _repository.GetLatestPeriodUsageAsync();
            var user = msg?.LatestUser ?? await _repository.GetLatestUserInfoAsync();
            var sub = msg?.LatestSubscription ?? await _repository.GetLatestSubscriptionAsync();
            var agg = msg?.AggregateStats;

            // 如果消息中没有聚合数据，尝试按当前周期自行查询
            // 优先 subscription 周期，其次 period 周期（sub 可能为空，period 仍可用）
            if (agg is null)
            {
                var aggStart = sub?.CurrentPeriodStart ?? period?.PeriodStart ?? 0;
                var aggEnd = sub?.CurrentPeriodEnd ?? period?.PeriodEnd ?? 0;
                if (aggStart > 0 && aggEnd > 0)
                {
                    agg = await _repository.AggregateStatsAsync(aggStart, aggEnd);
                }
            }

            // 用户信息：优先 user_info 表，其次事件表聚合，再其次 subscription
            string? resolvedEmail = null;
            string? resolvedName = null;
            string? resolvedPlan = null;
            if (user is not null)
            {
                resolvedEmail = string.IsNullOrEmpty(user.Email) ? null : user.Email;
                resolvedName = user.Name;
                resolvedPlan = user.PlanName;
            }

            resolvedEmail ??= agg?.UserEmail ?? sub?.Email;
            resolvedPlan ??= sub?.Plan ?? period?.PlanName;

            UserEmail = string.IsNullOrEmpty(resolvedEmail) ? "未知" : resolvedEmail;
            UserDisplayName = !string.IsNullOrEmpty(resolvedName) ? resolvedName
                              : !string.IsNullOrEmpty(resolvedEmail) ? resolvedEmail
                              : "未知";
            PlanName = resolvedPlan ?? "未知";

            // 订阅周期：优先 subscription 实体，其次 period entity
            var periodStart = sub?.CurrentPeriodStart ?? period?.PeriodStart ?? 0;
            var periodEnd = sub?.CurrentPeriodEnd ?? period?.PeriodEnd ?? 0;

            if (periodStart > 0 && periodEnd > 0)
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds(periodStart).LocalDateTime;
                var end = DateTimeOffset.FromUnixTimeMilliseconds(periodEnd).LocalDateTime;
                PeriodRange = $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}";
            }

            // 用量统计：优先 events 表聚合数据，其次 API 快照
            if (agg is not null)
            {
                PeriodInputTokens = agg.TotalInputTokens;
                PeriodOutputTokens = agg.TotalOutputTokens;
                PeriodCacheReadTokens = agg.TotalCacheReadTokens;
                PeriodCacheWriteTokens = agg.TotalCacheWriteTokens;
                PeriodSpendDollars = agg.TotalSpendCents / 100m;
                PeriodTotalTokens = agg.TotalInputTokens + agg.TotalOutputTokens
                                    + agg.TotalCacheReadTokens + agg.TotalCacheWriteTokens;
            }
            else if (period is not null)
            {
                PeriodInputTokens = period.UsedTokens;
                PeriodSpendDollars = period.TotalSpendCents / 100m;
                PeriodTotalTokens = period.UsedTokens;
            }

            // 本周统计
            var weekly = msg?.WeeklyAggregateStats;
            if (weekly is not null)
            {
                WeekInputTokens = weekly.TotalInputTokens;
                WeekOutputTokens = weekly.TotalOutputTokens;
                WeekCacheReadTokens = weekly.TotalCacheReadTokens;
                WeekCacheWriteTokens = weekly.TotalCacheWriteTokens;
                WeekSpendDollars = weekly.TotalSpendCents / 100m;
                WeekTotalTokens = weekly.TotalInputTokens + weekly.TotalOutputTokens
                                  + weekly.TotalCacheReadTokens + weekly.TotalCacheWriteTokens;
            }

            // 订阅信息（从 billing 页面获取）
            if (sub is not null)
            {
                if (sub.SubscriptionStart > 0)
                {
                    var subStart = DateTimeOffset.FromUnixTimeMilliseconds(sub.SubscriptionStart).LocalDateTime;
                    SubStartDate = $"{subStart:yyyy-MM-dd}";
                }
                if (sub.CurrentPeriodEnd > 0)
                {
                    var subEnd = DateTimeOffset.FromUnixTimeMilliseconds(sub.CurrentPeriodEnd).LocalDateTime;
                    SubEndDate = sub.CancelAtPeriodEnd
                        ? $"{subEnd:yyyy-MM-dd}[到期取消]"
                        : $"{subEnd:yyyy-MM-dd}[自动续费]";
                }
                SubStatus = sub.Status switch
                {
                    "active" => "活跃",
                    "trialing" => "试用中",
                    "canceled" => "已取消",
                    "past_due" => "逾期",
                    null => "-",
                    var s => s
                };
                InvoiceCount = sub.InvoiceCount > 0 ? $"{sub.InvoiceCount} 张" : "-";
            }

            if (msg is not null)
            {
                LastFetchTime = DateTimeOffset.FromUnixTimeMilliseconds(msg.FetchTimestampMs)
                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                StatusColor = "#28a745"; // 绿
                StatusText = "已同步";
            }
        }

        /// <summary>
        /// 循环切换 Token 全局显示格式：全部 → 万 → 百万 → 全部。
        /// 同步更新共享状态（UsageSyncOptions + TokenFormatConverter.Mode），
        /// 重抛 10 个 token 属性触发大屏重渲染，并广播消息让 QueryViewModel 刷新 DataGrid。
        /// </summary>
        [RelayCommand]
        private void CycleTokenFormat()
        {
            TokenDisplayMode = TokenDisplayMode switch
            {
                TokenDisplayMode.FullNumber => TokenDisplayMode.Wan,
                TokenDisplayMode.Wan => TokenDisplayMode.Million,
                _ => TokenDisplayMode.FullNumber
            };
            TokenFormatLabel = GetLabelForMode(TokenDisplayMode);

            // 同步共享状态：Services 通知读取 _syncOptions.TokenDisplayMode，
            // GUI 转换器读取 TokenFormatConverter.Mode
            _syncOptions.TokenDisplayMode = TokenDisplayMode;
            TokenFormatConverter.Mode = TokenDisplayMode;

            // 持久化到磁盘，重启后保留
            try
            {
                _userPrefs.TokenDisplayMode = TokenDisplayMode;
                _userPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存用户偏好失败：{ex.Message}");
            }

            // 重抛 10 个 token 属性，触发大屏 Binding 重新求值
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

            // 通知 QueryViewModel 刷新 DataGrid
            Messenger.Send(new TokenFormatChangedMessage(TokenDisplayMode));
        }

        /// <summary>
        /// 跳转到设置 Tab 更新 cookie（用于 Cookie 预警条"立即更新"按钮）。
        /// </summary>
        [RelayCommand]
        private void OpenSettings()
        {
            Messenger.Send(new SwitchToSettingsTabMessage());
        }

        /// <summary>
        /// 用系统默认浏览器打开 Cursor 登录页，方便用户重新登录获取新 cookie。
        /// UseShellExecute=true 让系统按 URL 协议选择默认浏览器。
        /// </summary>
        [RelayCommand]
        private void OpenBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://cursor.com/dashboard/billing",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText = $"打开浏览器失败：{ex.Message}";
                StatusColor = "#dc3545";
            }
        }
    }
}
