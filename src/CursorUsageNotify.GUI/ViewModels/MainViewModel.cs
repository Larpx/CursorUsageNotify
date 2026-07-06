using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CursorUsageNotify.Models.Entities;
using CursorUsageNotify.Services.Messages;
using CursorUsageNotify.Services.Storage;

namespace CursorUsageNotify.GUI.ViewModels;

/// <summary>
/// 数据大屏 ViewModel：展示用户信息、订阅、用量、总 token 数等统计数据。
/// 订阅 <see cref="UsageDataFetchedMessage"/> 自动刷新。
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly IUsageRepository _repository;

    public MainViewModel(IUsageRepository repository, IMessenger messenger)
        : base(messenger)
    {
        _repository = repository;
        Messenger.Register<UsageDataFetchedMessage>(this, OnDataFetched);
        Messenger.Register<SyncFailedMessage>(this, OnSyncFailed);
        _ = LoadAsync();
    }

    [ObservableProperty]
    private string _userEmail = "未获取";

    [ObservableProperty]
    private string _planName = "未获取";

    [ObservableProperty]
    private string _periodRange = "-";

    [ObservableProperty]
    private long _usedTokens;

    [ObservableProperty]
    private long _usedRequests;

    [ObservableProperty]
    private decimal _totalSpendDollars;

    [ObservableProperty]
    private long _remainingRequests;

    [ObservableProperty]
    private string _lastFetchTime = "-";

    [ObservableProperty]
    private string _statusColor = "#888888"; // 灰=未启动

    [ObservableProperty]
    private string _statusText = "等待中";

    // 订阅信息

    [ObservableProperty]
    private string _subStartDate = "-";

    [ObservableProperty]
    private string _subStatus = "-";

    [ObservableProperty]
    private string _invoiceCount = "-";

    private void OnDataFetched(object recipient, UsageDataFetchedMessage msg)
    {
        _ = LoadAsync(msg);
    }

    private void OnSyncFailed(object recipient, SyncFailedMessage msg)
    {
        StatusColor = "#dc3545"; // 红
        StatusText = $"失败：{msg.Error}";
    }

    /// <summary>
    /// 从数据库加载最新数据刷新大屏。
    /// </summary>
    public async Task LoadAsync(UsageDataFetchedMessage? msg = null)
    {
        var period = msg?.LatestPeriod ?? await _repository.GetLatestPeriodUsageAsync();
        var user = msg?.LatestUser ?? await _repository.GetLatestUserInfoAsync();
        var sub = msg?.LatestSubscription ?? await _repository.GetLatestSubscriptionAsync();

        if (user is not null)
        {
            UserEmail = string.IsNullOrEmpty(user.Email) ? "未知" : user.Email;
            PlanName = user.PlanName ?? "未知";
        }

        if (period is not null)
        {
            UsedTokens = period.UsedTokens;
            UsedRequests = period.UsedRequests;
            TotalSpendDollars = period.TotalSpendCents / 100m;
            RemainingRequests = period.RemainingRequests;

            var start = DateTimeOffset.FromUnixTimeMilliseconds(period.PeriodStart).LocalDateTime;
            var end = DateTimeOffset.FromUnixTimeMilliseconds(period.PeriodEnd).LocalDateTime;
            PeriodRange = $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}";
        }

        if (sub is not null)
        {
            if (sub.SubscriptionStart > 0)
            {
                var subStart = DateTimeOffset.FromUnixTimeMilliseconds(sub.SubscriptionStart).LocalDateTime;
                SubStartDate = $"{subStart:yyyy-MM-dd}";
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
}
