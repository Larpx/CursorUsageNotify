using CursorUsageNotify.Models.Entities;

namespace CursorUsageNotify.Services.Export;

/// <summary>
/// CSV 导出抽象。
/// </summary>
public interface ICsvExporter
{
    /// <summary>
    /// 将用量事件导出为 CSV 文件。
    /// </summary>
    /// <param name="events">事件列表。</param>
    /// <param name="outputPath">输出文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    Task ExportAsync(IEnumerable<UsageEventEntity> events, string outputPath, CancellationToken ct = default);
}
