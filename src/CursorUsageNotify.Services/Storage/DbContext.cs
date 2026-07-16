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

                // 迁移旧库：修复 user_info.hard_limit_override_dollars 的 NOT NULL 约束
                MigrateUserInfoNullableColumn();

                _logger.LogInformation("数据库表结构初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库表结构初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 迁移旧库 user_info 表：将 hard_limit_override_dollars 列从 NOT NULL 改为可空。
        /// 旧版本该列定义为 NOT NULL，DeepSeek 平台无此概念需存储 null，
        /// SQLite 不支持直接 ALTER COLUMN，需重建表。
        /// </summary>
        private void MigrateUserInfoNullableColumn()
        {
            try
            {
                // 检测 user_info 表是否存在
                var exists = _client.Ado.GetInt(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='user_info'");
                if (exists == 0) return;

                // 查询列信息，检测 hard_limit_override_dollars 是否 NOT NULL
                var dt = _client.Ado.GetDataTable("PRAGMA table_info(user_info)");
                bool needMigrate = false;
                foreach (System.Data.DataRow row in dt.Rows)
                {
                    var name = (string)row["name"];
                    var notnull = Convert.ToInt64(row["notnull"]);
                    if (name == "hard_limit_override_dollars" && notnull == 1)
                    {
                        needMigrate = true;
                        break;
                    }
                }

                if (!needMigrate) return;

                _logger.LogInformation("迁移 user_info 表：hard_limit_override_dollars 改为可空");
                // SQLite 重建表标准流程：建临时新表 → 复制数据 → 删旧表 → 重命名
                _client.Ado.ExecuteCommand(@"
                    BEGIN TRANSACTION;
                    CREATE TABLE user_info_new (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        platform INTEGER NOT NULL,
                        email TEXT NOT NULL,
                        name TEXT,
                        plan_name TEXT,
                        role TEXT,
                        monthly_limit_dollars REAL,
                        hard_limit_override_dollars REAL,
                        snapshot_time INTEGER
                    );
                    INSERT INTO user_info_new SELECT * FROM user_info;
                    DROP TABLE user_info;
                    ALTER TABLE user_info_new RENAME TO user_info;
                    COMMIT;
                ");
                _logger.LogInformation("user_info 表迁移完成");
            }
            catch (Exception ex)
            {
                // 迁移失败不阻断启动，DeepSeek Provider 已用 0 作为默认值兼容旧库
                _logger.LogWarning(ex, "user_info 表迁移失败（不影响运行，DeepSeek 将使用默认值 0）");
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
