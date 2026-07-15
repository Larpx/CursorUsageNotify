
namespace Larpx.PersonalTools.CursorUsageNotify.Models
{
    /// <summary>
    /// 支持的数据采集平台类型。
    /// 后续扩展新平台时在此枚举追加值，并实现对应的 IPlatformProvider。
    /// </summary>
    public enum PlatformType
    {
        /// <summary>
        /// Cursor 编辑器平台。
        /// </summary>
        Cursor = 0,

        /// <summary>
        /// DeepSeek 开放平台。
        /// </summary>
        DeepSeek = 1
    }
}
