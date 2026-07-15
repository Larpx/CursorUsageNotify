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
    /// 数据大屏 ViewModel：双列展示多平台用量数据，订阅同步事件自动刷新。
    /// 每个平台对应一个 <see cref="PlatformDashboardItem"/>，按 <see cref="UserPreferences"/> 启用状态控制可见性。
    /// </summary>
    public sealed partial class MainViewModel : ViewModelBase
    {
        private readonly IUsageRepository _repository;
        private readonly UsageSyncOptions _syncOptions;
        private readonly UserPreferences _userPrefs;

        /// <summary>
        /// 构造数据大屏 ViewModel，注入仓储、同步选项与用户偏好，
        /// 初始化各平台 Dashboard 并订阅同步事件。
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

            // 初始化各平台 Dashboard（后续可扩展更多平台）
            CursorDashboard = new PlatformDashboardItem
            {
                Platform = PlatformType.Cursor,
                PlatformName = "Cursor",
                HasCacheWrite = true,
                HasSubscription = true,
                CurrencySymbol = "$",
                IsEnabled = userPrefs.IsPlatformEnabled(PlatformType.Cursor)
            };
            DeepSeekDashboard = new PlatformDashboardItem
            {
                Platform = PlatformType.DeepSeek,
                PlatformName = "DeepSeek",
                HasCacheWrite = false,
                HasSubscription = false,
                CurrencySymbol = "¥",
                IsEnabled = userPrefs.IsPlatformEnabled(PlatformType.DeepSeek)
            };

            Messenger.Register<UsageDataFetchedMessage>(this, OnDataFetched);
            Messenger.Register<SyncFailedMessage>(this, OnSyncFailed);
            Messenger.Register<SyncStartedMessage>(this, OnSyncStarted);
            Messenger.Register<CookieExpiringSoonMessage>(this, OnCookieExpiring);
            Messenger.Register<EndpointDegradedMessage>(this, OnEndpointDegraded);
            Messenger.Register<TokenStorageUnavailableMessage>(this, OnTokenStorageUnavailable);
            _ = LoadAllAsync();
        }

        /// <summary>
        /// Cursor 平台大屏数据项。
        /// </summary>
        public PlatformDashboardItem CursorDashboard { get; }

        /// <summary>
        /// DeepSeek 平台大屏数据项。
        /// </summary>
        public PlatformDashboardItem DeepSeekDashboard { get; }

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

        /// <summary>
        /// 全局状态灯颜色（取所有启用平台最差状态：红>黄>绿>灰）。
        /// </summary>
        [ObservableProperty]
        private string _statusColor = "#888888";

        /// <summary>
        /// 全局状态文本（底部状态栏显示）。
        /// </summary>
        [ObservableProperty]
        private string _statusText = "等待中";

        /// <summary>
        /// 状态灯 tooltip 文本：列出所有启用平台的同步状态和时间。
        /// </summary>
        [ObservableProperty]
        private string _statusTooltip = string.Empty;

        partial void OnCookieStatusTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasCookieWarning));
        }

        private void OnDataFetched(object recipient, UsageDataFetchedMessage msg)
        {
            IsSyncing = false;
            _ = LoadPlatformAsync(msg);
        }

        private void OnSyncFailed(object recipient, SyncFailedMessage msg)
        {
            IsSyncing = false;
            var dashboard = GetDashboard(msg.Platform);
            if (dashboard is not null)
            {
                dashboard.IsSyncing = false;
                dashboard.StatusColor = "#dc3545"; // 红
                dashboard.StatusText = $"失败：{msg.Error}";
            }
            UpdateGlobalStatus();
            // 认证类失败（401/403）视为 cookie 失效
            if (msg.Error.Contains("401") || msg.Error.Contains("403") || msg.Error.Contains("认证"))
            {
                CookieStatusText = $"{msg.Platform} 凭证已失效，请在设置中更新";
                CookieStatusColor = "#dc3545";
            }
        }

        private void OnSyncStarted(object recipient, SyncStartedMessage msg)
        {
            IsSyncing = true;
            // SyncStartedMessage 不带 Platform，所有启用的平台都进入同步中状态
            foreach (var dashboard in GetEnabledDashboards())
            {
                dashboard.IsSyncing = true;
                dashboard.StatusColor = "#f59e0b"; // 黄=同步中
                dashboard.StatusText = "同步中...";
            }
            StatusColor = "#f59e0b";
            StatusText = "同步中...";
            UpdateStatusTooltip();
        }

        private void OnCookieExpiring(object recipient, CookieExpiringSoonMessage msg)
        {
            if (msg.IsExpired)
            {
                CookieStatusText = $"{msg.Platform} 凭证已失效，请在设置中更新";
                CookieStatusColor = "#dc3545";
            }
            else
            {
                var days = Math.Max(0, (int)(msg.ExpiryUtc - DateTime.UtcNow).TotalDays);
                CookieStatusText = $"{msg.Platform} 凭证将在 {days} 天后过期（{msg.ExpiryUtc:yyyy-MM-dd}）";
                CookieStatusColor = "#f59e0b";
            }
        }

        /// <summary>
        /// 端点探测发现失效端点：在状态栏红色提示用户客户端可能需要更新。
        /// </summary>
        private void OnEndpointDegraded(object recipient, EndpointDegradedMessage msg)
        {
            StatusColor = "#dc3545";
            StatusText = $"接口已变更：{string.Join("、", msg.DegradedPaths)}，请检查客户端更新";
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
        /// 根据平台类型获取对应的 Dashboard 项。
        /// </summary>
        /// <param name="platform">平台类型。</param>
        /// <returns>对应 Dashboard；未注册平台返回 null。</returns>
        private PlatformDashboardItem? GetDashboard(PlatformType platform) => platform switch
        {
            PlatformType.Cursor => CursorDashboard,
            PlatformType.DeepSeek => DeepSeekDashboard,
            _ => null
        };

        /// <summary>
        /// 获取所有已启用的 Dashboard 项。
        /// </summary>
        private IEnumerable<PlatformDashboardItem> GetEnabledDashboards()
        {
            if (CursorDashboard.IsEnabled) yield return CursorDashboard;
            if (DeepSeekDashboard.IsEnabled) yield return DeepSeekDashboard;
        }

        /// <summary>
        /// 加载所有启用平台的数据刷新大屏（启动时调用）。
        /// </summary>
        private async Task LoadAllAsync()
        {
            await LoadPlatformAsync(null, PlatformType.Cursor);
            await LoadPlatformAsync(null, PlatformType.DeepSeek);
            UpdateGlobalStatus();
        }

        /// <summary>
        /// 从数据库加载指定平台最新数据刷新对应 Dashboard。
        /// </summary>
        /// <param name="msg">同步消息（含已拉取的最新数据）；null 时从仓储查询。</param>
        /// <param name="forcePlatform">强制加载的平台（msg 为 null 时使用）。</param>
        private async Task LoadPlatformAsync(UsageDataFetchedMessage? msg = null, PlatformType? forcePlatform = null)
        {
            var platform = msg?.Platform ?? forcePlatform ?? PlatformType.Cursor;
            var dashboard = GetDashboard(platform);
            if (dashboard is null) return;

            // 同步可见性与启用开关
            dashboard.IsEnabled = _userPrefs.IsPlatformEnabled(platform);

            var period = msg?.LatestPeriod ?? await _repository.GetLatestPeriodUsageAsync(platform);
            var user = msg?.LatestUser ?? await _repository.GetLatestUserInfoAsync(platform);
            var sub = msg?.LatestSubscription ?? await _repository.GetLatestSubscriptionAsync(platform);
            var agg = msg?.AggregateStats;

            // 如果消息中没有聚合数据，尝试按当前周期自行查询
            if (agg is null)
            {
                var aggStart = sub?.CurrentPeriodStart ?? period?.PeriodStart ?? 0;
                var aggEnd = sub?.CurrentPeriodEnd ?? period?.PeriodEnd ?? 0;
                if (aggStart > 0 && aggEnd > 0)
                {
                    agg = await _repository.AggregateStatsAsync(aggStart, aggEnd, platform);
                }
            }

            // 账户信息
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

            dashboard.Email = string.IsNullOrEmpty(resolvedEmail) ? "-" : resolvedEmail;
            dashboard.DisplayName = !string.IsNullOrEmpty(resolvedName) ? resolvedName
                              : !string.IsNullOrEmpty(resolvedEmail) ? resolvedEmail
                              : "-";
            dashboard.PlanName = resolvedPlan ?? "-";

            // 周期范围
            var periodStart = sub?.CurrentPeriodStart ?? period?.PeriodStart ?? 0;
            var periodEnd = sub?.CurrentPeriodEnd ?? period?.PeriodEnd ?? 0;
            if (periodStart > 0 && periodEnd > 0)
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds(periodStart).LocalDateTime;
                var end = DateTimeOffset.FromUnixTimeMilliseconds(periodEnd).LocalDateTime;
                dashboard.PeriodRange = $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}";
            }

            // 本周期用量（Cursor=订阅周期，DeepSeek=本月）
            if (agg is not null)
            {
                dashboard.PeriodInputTokens = agg.TotalInputTokens;
                dashboard.PeriodOutputTokens = agg.TotalOutputTokens;
                dashboard.PeriodCacheReadTokens = agg.TotalCacheReadTokens;
                dashboard.PeriodCacheWriteTokens = agg.TotalCacheWriteTokens;
                dashboard.PeriodSpend = agg.TotalSpendCents / 100m;
                dashboard.PeriodTotalTokens = agg.TotalInputTokens + agg.TotalOutputTokens
                                    + agg.TotalCacheReadTokens + agg.TotalCacheWriteTokens;
            }
            else if (period is not null)
            {
                dashboard.PeriodInputTokens = period.UsedTokens;
                dashboard.PeriodSpend = period.TotalSpendCents / 100m;
                dashboard.PeriodTotalTokens = period.UsedTokens;
            }

            // 本周用量
            var weekly = msg?.WeeklyAggregateStats;
            if (weekly is null)
            {
                weekly = await _repository.AggregateWeeklyStatsAsync(platform);
            }
            if (weekly is not null)
            {
                dashboard.WeekInputTokens = weekly.TotalInputTokens;
                dashboard.WeekOutputTokens = weekly.TotalOutputTokens;
                dashboard.WeekCacheReadTokens = weekly.TotalCacheReadTokens;
                dashboard.WeekCacheWriteTokens = weekly.TotalCacheWriteTokens;
                dashboard.WeekSpend = weekly.TotalSpendCents / 100m;
                dashboard.WeekTotalTokens = weekly.TotalInputTokens + weekly.TotalOutputTokens
                                  + weekly.TotalCacheReadTokens + weekly.TotalCacheWriteTokens;
            }

            // 当天用量（始终从仓储查询，消息不带当天统计）
            var daily = await _repository.AggregateDailyStatsAsync(platform);
            if (daily is not null)
            {
                dashboard.TodayInputTokens = daily.TotalInputTokens;
                dashboard.TodayOutputTokens = daily.TotalOutputTokens;
                dashboard.TodayCacheReadTokens = daily.TotalCacheReadTokens;
                dashboard.TodayCacheWriteTokens = daily.TotalCacheWriteTokens;
                dashboard.TodaySpend = daily.TotalSpendCents / 100m;
                dashboard.TodayTotalTokens = daily.TotalInputTokens + daily.TotalOutputTokens
                                  + daily.TotalCacheReadTokens + daily.TotalCacheWriteTokens;
            }

            // 订阅状态（仅 Cursor 有意义）
            if (sub is not null && dashboard.HasSubscription)
            {
                dashboard.SubStatus = sub.Status switch
                {
                    "active" => "活跃",
                    "trialing" => "试用中",
                    "canceled" => "已取消",
                    "past_due" => "逾期",
                    null => "-",
                    var s => s
                };
            }

            // 同步成功：更新状态灯
            if (msg is not null)
            {
                dashboard.LastSyncTime = DateTimeOffset.FromUnixTimeMilliseconds(msg.FetchTimestampMs)
                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                dashboard.IsSyncing = false;
                dashboard.StatusColor = "#28a745"; // 绿
                dashboard.StatusText = "已同步";
            }

            UpdateGlobalStatus();
        }

        /// <summary>
        /// 汇总所有启用平台状态到全局状态灯与 tooltip。
        /// 取最差状态作为全局颜色：红 > 黄 > 绿 > 灰。
        /// </summary>
        private void UpdateGlobalStatus()
        {
            var dashboards = GetEnabledDashboards().ToList();
            if (dashboards.Count == 0)
            {
                StatusColor = "#888888";
                StatusText = "无启用平台";
                UpdateStatusTooltip();
                return;
            }

            // 优先级：红 > 黄 > 绿 > 灰
            var worst = dashboards
                .Select(d => d.StatusColor switch
                {
                    "#dc3545" => 3, // 红
                    "#f59e0b" => 2, // 黄
                    "#28a745" => 1, // 绿
                    _ => 0          // 灰
                })
                .Max();
            StatusColor = worst switch
            {
                3 => "#dc3545",
                2 => "#f59e0b",
                1 => "#28a745",
                _ => "#888888"
            };
            StatusText = worst switch
            {
                3 => "存在失败平台",
                2 => "同步中",
                1 => "已同步",
                _ => "等待中"
            };
            UpdateStatusTooltip();
        }

        /// <summary>
        /// 更新状态灯 tooltip：列出所有启用平台的同步状态和时间。
        /// </summary>
        private void UpdateStatusTooltip()
        {
            var lines = GetEnabledDashboards()
                .Select(d => $"{d.PlatformName}: {d.StatusText}（{d.LastSyncTime}）");
            StatusTooltip = string.Join("\n", lines);
        }

        /// <summary>
        /// 循环切换 Token 全局显示格式：全部 → 万 → 百万 → 全部。
        /// 同步更新共享状态（UsageSyncOptions + TokenFormatConverter.Mode），
        /// 重抛所有平台所有 token 属性触发大屏重渲染，并广播消息让 QueryViewModel 刷新 DataGrid。
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

            // 重抛所有平台所有 token 属性，触发大屏 Binding 重新求值
            foreach (var dashboard in new[] { CursorDashboard, DeepSeekDashboard })
            {
                dashboard.RefreshTokenBindings();
            }

            // 通知 QueryViewModel 刷新 DataGrid
            Messenger.Send(new TokenFormatChangedMessage(TokenDisplayMode));
        }

        /// <summary>
        /// 跳转到设置 Tab 更新凭证（用于预警条"立即更新"按钮）。
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

        /// <summary>
        /// 立即触发一次用量拉取（大屏刷新按钮）。
        /// 通过消息总线发送 <see cref="TriggerSyncNowMessage"/>，
        /// 由 <see cref="UsageSyncHostedService"/> 执行实际同步。
        /// </summary>
        [RelayCommand]
        private void RefreshNow()
        {
            Messenger.Send(new TriggerSyncNowMessage());
        }
    }
}
