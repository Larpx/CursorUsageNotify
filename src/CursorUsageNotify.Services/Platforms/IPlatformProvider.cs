using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Platforms
{
    /// <summary>
    /// 平台数据采集 Provider 抽象接口。
    /// 每个平台实现此接口，封装 API 调用、数据映射逻辑。
    /// UsageSyncHostedService 遍历所有注册的 Provider 执行同步。
    /// </summary>
    public interface IPlatformProvider
    {
        /// <summary>
        /// 平台类型。
        /// </summary>
        PlatformType Platform { get; }

        /// <summary>
        /// 测试连接是否有效（设置界面"测试连接"按钮调用）。
        /// </summary>
        /// <param name="token">平台认证 token（明文，调用后由 SecureTokenHolder 清理）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>成功时返回 Result.Ok（含总事件数等摘要），失败返回 Result.Fail。</returns>
        Task<Result<int>> TestConnectionAsync(string token, CancellationToken ct = default);

        /// <summary>
        /// 执行同步：拉取 API 数据并映射为实体，返回 PlatformSyncResult 供 HostedService 入库。
        /// 异常以 PlatformApiException 形式抛出，由 HostedService 统一捕获处理。
        /// </summary>
        /// <param name="token">平台认证 token（明文）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>同步结果（包含用量事件、周期汇总、用户信息等实体）。</returns>
        Task<PlatformSyncResult> SyncAsync(string token, CancellationToken ct = default);
    }
}
