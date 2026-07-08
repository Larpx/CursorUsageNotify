using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling
{
    /// <summary>
    /// 定时同步运行时配置（用户可在设置界面动态修改，无需重启）。
    /// 通过 IOptionsMonitor 或直接注入本对象读写。
    /// </summary>
    public sealed class UsageSyncOptions
    {
        /// <summary>
        /// 是否正在运行（托盘"开始/暂停"切换）。
        /// </summary>
        public bool IsRunning { get; set; } = true;

        /// <summary>
        /// 数据拉取间隔（分钟）。
        /// </summary>
        public int SyncIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// 通知间隔（分钟）。
        /// </summary>
        public int NotificationIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// 上次成功拉取时间（epoch 毫秒，0 表示从未拉取）。
        /// </summary>
        public long LastSyncTimeMs { get; set; }

        /// <summary>
        /// 上次通知时间（epoch 毫秒）。
        /// </summary>
        public long LastNotificationTimeMs { get; set; }

        /// <summary>
        /// 连续失败次数，用于退避策略计算下次同步间隔。
        /// 成功同步后重置为 0。
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>
        /// Token 全局显示格式（用户在大屏切换，后台通知读取此值格式化推送内容）。
        /// </summary>
        public TokenDisplayMode TokenDisplayMode { get; set; } = TokenDisplayMode.FullNumber;
    }
}
