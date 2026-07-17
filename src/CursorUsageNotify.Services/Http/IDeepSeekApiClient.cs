using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Http
{
    /// <summary>
    /// DeepSeek 开放平台 API 客户端抽象。
    /// 认证方式：Authorization: Bearer {userToken}（token 来自 localStorage userToken.value）。
    /// </summary>
    public interface IDeepSeekApiClient
    {
        /// <summary>
        /// 测试连接是否可用（调用 get_user_summary 验证 token 有效性）。
        /// </summary>
        /// <param name="userToken">DeepSeek userToken（明文）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>成功时返回 Result.Ok，失败返回 Result.Fail。</returns>
        Task<Result<int>> TestConnectionAsync(string userToken, CancellationToken ct = default);

        /// <summary>
        /// 获取用户账户汇总（充值余额、累计消费、本月消费）。
        /// GET /api/v0/users/get_user_summary
        /// </summary>
        /// <param name="userToken">DeepSeek userToken。</param>
        /// <param name="ct">取消令牌。</param>
        Task<DeepSeekUserSummaryDto> GetUserSummaryAsync(string userToken, CancellationToken ct = default);

        /// <summary>
        /// 获取当前登录用户信息（邮箱、手机号、资料）。
        /// GET /auth-api/v0/users/current
        /// </summary>
        /// <param name="userToken">DeepSeek userToken。</param>
        /// <param name="ct">取消令牌。</param>
        Task<DeepSeekCurrentUserDto> GetCurrentUserAsync(string userToken, CancellationToken ct = default);

        /// <summary>
        /// 获取按 API Key 分组的每日 token 用量（含各模型 REQUEST/Token）。
        /// GET /api/v0/usage/by_api_key/amount?start={sec}&amp;end={sec}&amp;tz=0
        /// </summary>
        /// <param name="userToken">DeepSeek userToken。</param>
        /// <param name="startSec">起始时间（Unix 秒）。</param>
        /// <param name="endSec">结束时间（Unix 秒）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<DeepSeekUsageAmountDto> GetUsageAmountAsync(string userToken, long startSec, long endSec, CancellationToken ct = default);

        /// <summary>
        /// 获取按 API Key 分组的每日费用。
        /// GET /api/v0/usage/by_api_key/cost?start={sec}&amp;end={sec}&amp;tz=0
        /// </summary>
        /// <param name="userToken">DeepSeek userToken。</param>
        /// <param name="startSec">起始时间（Unix 秒）。</param>
        /// <param name="endSec">结束时间（Unix 秒）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<DeepSeekUsageCostDto> GetUsageCostAsync(string userToken, long startSec, long endSec, CancellationToken ct = default);

        /// <summary>
        /// 获取账户下 API Key 列表（可多个，各自独立统计；不是用户信息）。
        /// GET /api/v0/users/get_api_keys
        /// </summary>
        /// <param name="userToken">DeepSeek userToken。</param>
        /// <param name="ct">取消令牌。</param>
        Task<DeepSeekApiKeysDto> GetApiKeysAsync(string userToken, CancellationToken ct = default);
    }
}
