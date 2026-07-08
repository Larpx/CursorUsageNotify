
namespace Larpx.PersonalTools.CursorUsageNotify.Core.Configuration
{
    /// <summary>
    /// 应用配置根模型，对应 appsettings.json 中 "App" 节。
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>
        /// Cursor 内部 dashboard API 的基础地址常量（避免本地化 URL 导致 400）。
        /// </summary>
        public const string DefaultApiBaseUrl = "https://cursor.com";

        /// <summary>
        /// Cursor 内部 dashboard API 的基础地址。
        /// 只读属性：强制使用常量，配置 bind 无法覆盖，防止用户误配导致 token 泄露。
        /// </summary>
        public string ApiBaseUrl { get; } = DefaultApiBaseUrl;

        /// <summary>
        /// 数据拉取间隔（分钟）。用户可在设置界面覆盖。
        /// </summary>
        public int SyncIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// 通知间隔（分钟），与数据拉取独立。
        /// </summary>
        public int NotificationIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// SQLite 数据库文件绝对路径。默认在 %LOCALAPPDATA%/CursorUsageNotify/data.db。
        /// </summary>
        public string DatabasePath { get; set; } = DefaultDatabasePath;

        /// <summary>
        /// 加密后的 session token 持久化文件路径。
        /// </summary>
        public string SecretsPath { get; set; } = DefaultSecretsPath;

        /// <summary>
        /// Serilog 日志文件路径。
        /// </summary>
        public string LogFilePath { get; set; } = DefaultLogFilePath;

        /// <summary>
        /// 单个分页拉取的最大事件数（Cursor 内部接口上限 10000）。
        /// </summary>
        public int UsageEventsPageSize { get; set; } = 500;

        /// <summary>
        /// HttpClient 超时（秒）。
        /// </summary>
        public int HttpTimeoutSeconds { get; set; } = 30;

        private static string AppDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorUsageNotify");

        /// <summary>
        /// 默认数据库路径。
        /// </summary>
        public static string DefaultDatabasePath => Path.Combine(AppDataDir, "data.db");

        /// <summary>
        /// 默认密钥文件路径。
        /// </summary>
        public static string DefaultSecretsPath => Path.Combine(AppDataDir, "secrets.dat");

        /// <summary>
        /// 默认日志文件路径。
        /// </summary>
        public static string DefaultLogFilePath => Path.Combine(AppDataDir, "logs", "app.log");
    }
}
