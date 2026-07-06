using SqlSugar;

namespace CursorUsageNotify.Services.Storage;

/// <summary>
/// SqlSugar 数据库上下文抽象。
/// </summary>
public interface IDbContext
{
    /// <summary>获取 SqlSugar 客户端实例。</summary>
    ISqlSugarClient Client { get; }

    /// <summary>首次启动时初始化表结构。</summary>
    void InitializeSchema();
}
