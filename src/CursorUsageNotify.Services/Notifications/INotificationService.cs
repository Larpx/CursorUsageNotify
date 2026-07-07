
namespace Larpx.PersonalTools.CursorUsageNotify.Services.Notifications
{
    /// <summary>
    /// 桌面通知抽象。Windows 平台用 Toast，其他平台可替换实现。
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// 显示一条通知。
        /// </summary>
        /// <param name="title">
        /// 标题。
        /// </param>
        /// <param name="body">
        /// 正文（支持多行）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task ShowAsync(string title, string body, CancellationToken ct = default);
    }
}
