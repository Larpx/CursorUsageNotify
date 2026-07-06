using CursorUsageNotify.Core.Configuration;
using CursorUsageNotify.Models.Entities;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace CursorUsageNotify.Services.Storage;

/// <summary>
/// SqlSugar 数据库上下文，封装 SQLite 连接和建表。
/// </summary>
public sealed class DbContext : IDbContext
{
    private readonly SqlSugarScope _client;
    private readonly ILogger<DbContext> _logger;

    public ISqlSugarClient Client => _client;

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
                typeof(UserInfoEntity));
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
