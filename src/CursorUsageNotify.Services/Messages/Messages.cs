using CursorUsageNotify.Models.Entities;

namespace CursorUsageNotify.Services.Messages;

/// <summary>
/// 后台拉取数据成功后广播，UI 订阅后刷新数据大屏和查询列表。
/// </summary>
public sealed record UsageDataFetchedMessage(
    int NewEventsCount,
    PeriodUsageEntity? LatestPeriod,
    UserInfoEntity? LatestUser,
    long FetchTimestampMs);

/// <summary>
/// 托盘"开始/暂停"或"立即拉取"指令，发送到 HostedService。
/// </summary>
public sealed record ToggleSyncMessage(bool IsRunning);

/// <summary>
/// 立即触发一次拉取（不等下一个周期）。
/// </summary>
public sealed record TriggerSyncNowMessage();

/// <summary>
/// 拉取失败通知（用于 UI 状态灯变红）。
/// </summary>
public sealed record SyncFailedMessage(string Error);

/// <summary>
/// 通知已显示，UI 更新"最近一次通知时间"。
/// </summary>
public sealed record NotificationShownMessage(long TimestampMs);
