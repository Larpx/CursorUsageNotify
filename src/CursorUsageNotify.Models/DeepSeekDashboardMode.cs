
namespace Larpx.PersonalTools.CursorUsageNotify.Models
{
    /// <summary>
    /// DeepSeek 数据大屏用量展示模式。
    /// </summary>
    public enum DeepSeekDashboardMode
    {
        /// <summary>
        /// 汇总账户下全部 API Key 的用量。
        /// </summary>
        AllApiKeys = 0,

        /// <summary>
        /// 仅展示单个 API Key 的用量。
        /// </summary>
        SingleApiKey = 1
    }
}
