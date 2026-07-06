using System.Globalization;
using CursorUsageNotify.Models.Entities;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;

namespace CursorUsageNotify.Services.Export;

/// <summary>
/// 基于 CsvHelper 的 CSV 导出实现。
/// </summary>
public sealed class CsvExporter : ICsvExporter
{
    private readonly ILogger<CsvExporter> _logger;

    public CsvExporter(ILogger<CsvExporter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(IEnumerable<UsageEventEntity> events, string outputPath, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ","
        };

        await using var writer = new StreamWriter(outputPath, append: false);
        await using var csv = new CsvWriter(writer, config);

        await csv.WriteRecordsAsync(events, ct);
        _logger.LogInformation("已导出 CSV：{Path}", outputPath);
    }
}
