using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Microsoft.Extensions.Logging;
using SqlSugar;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Storage
{
    /// <summary>
    /// SqlSugar 数据库上下文，封装 SQLite 连接和建表。
    /// </summary>
    public sealed class DbContext : IDbContext
    {
        private readonly SqlSugarScope _client;
        private readonly ILogger<DbContext> _logger;

        /// <summary>
        /// 获取 SqlSugar 客户端实例。
        /// </summary>
        public ISqlSugarClient Client => _client;

        /// <summary>
        /// 初始化 <see cref="DbContext"/> 实例，配置 SQLite 连接并确保数据库目录存在。
        /// </summary>
        public DbContext(AppSettings settings, ILogger<DbContext> logger)
        {
            _logger = logger;

            EnsureDirectory(settings.DatabasePath);

            _client = new SqlSugarScope(new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = $"DataSource={settings.DatabasePath}",
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            },
            db =>
            {
                db.Aop.OnLogExecuting = (sql, _) =>
                {
    #if DEBUG
                    _logger.LogDebug("SQL: {Sql}", sql);
    #endif
                };
            });
        }

        /// <inheritdoc/>
        public void InitializeSchema()
        {
            try
            {
                _client.CodeFirst.InitTables(
                    typeof(UsageEventEntity),
                    typeof(PeriodUsageEntity),
                    typeof(UserInfoEntity),
                    typeof(SubscriptionEntity));
                _logger.LogInformation("数据库表结构初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库表结构初始化失败");
                throw;
            }
        }

        private static void EnsureDirectory(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
