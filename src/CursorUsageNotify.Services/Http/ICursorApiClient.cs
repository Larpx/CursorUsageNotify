using CursorUsageNotify.Core;
using CursorUsageNotify.Models.Api;
using CursorUsageNotify.Models.Dtos;

namespace CursorUsageNotify.Services.Http;

/// <summary>
/// Cursor 内部 dashboard API 客户端抽象。
/// </summary>
public interface ICursorApiClient
{
    /// <summary>测试连接是否可用（拉取 1 条事件验证 token 有效性）。</summary>
    /// <param name="sessionToken">WorkosCursorSessionToken。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功时返回拉取到的事件数；失败时返回错误消息。</returns>
    Task<Result<int>> TestConnectionAsync(string sessionToken, CancellationToken ct = default);

    /// <summary>
    /// 分页拉取详细用量事件。
    /// </summary>
    /// <param name="sessionToken">WorkosCursorSessionToken。</param>
    /// <param name="page">页码（1 起始）。</param>
    /// <param name="pageSize">每页大小。</param>
    /// <param name="startTimestamp">起始时间戳（epoch 毫秒，0 表示从最早开始）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<CursorUsageEventsResponse> GetFilteredUsageEventsAsync(
        string sessionToken,
        int page = 1,
        int pageSize = 500,
        long startTimestamp = 0,
        CancellationToken ct = default);

    /// <summary>
    /// 拉取当前计费周期用量汇总。
    /// </summary>
    /// <param name="sessionToken">WorkosCursorSessionToken。</param>
    /// <param name="ct">取消令牌。</param>
    Task<CursorPeriodUsageDto> GetCurrentPeriodUsageAsync(string sessionToken, CancellationToken ct = default);
}
