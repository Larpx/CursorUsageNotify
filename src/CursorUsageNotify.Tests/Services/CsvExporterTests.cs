using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Export;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;


namespace Larpx.PersonalTools.CursorUsageNotify.Tests.Services
{
    /// <summary>
    /// CsvExporter 单元测试：验证 CSV 字段、空数据、目录创建。
    /// </summary>
    public class CsvExporterTests
    {
        private readonly CsvExporter _exporter = new(NullLogger<CsvExporter>.Instance);

        [Fact]
        public async Task ExportAsync_NormalEvents_WritesCsvWithHeaders()
        {
            var path = Path.Combine(Path.GetTempPath(), $"csv-test-{Guid.NewGuid():N}.csv");
            var events = new[]
            {
                new UsageEventEntity
                {
                    Timestamp = 1704067200000,
                    UserEmail = "a@x.com",
                    Model = "model-a",
                    InputTokens = 100,
                    OutputTokens = 50,
                    TotalCents = 12.5m
                }
            };

            try
            {
                await _exporter.ExportAsync(events, path);

                Assert.True(File.Exists(path));
                var content = await File.ReadAllTextAsync(path);
                Assert.Contains("UserEmail", content);
                Assert.Contains("a@x.com", content);
                Assert.Contains("model-a", content);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportAsync_EmptyEvents_WritesOnlyHeader()
        {
            var path = Path.Combine(Path.GetTempPath(), $"csv-empty-{Guid.NewGuid():N}.csv");

            try
            {
                await _exporter.ExportAsync(Array.Empty<UsageEventEntity>(), path);

                Assert.True(File.Exists(path));
                var lines = await File.ReadAllLinesAsync(path);
                Assert.Single(lines);
                Assert.Contains("UserEmail", lines[0]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task ExportAsync_CreatesDirectoryIfMissing()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"csv-dir-{Guid.NewGuid():N}");
            var path = Path.Combine(dir, "output.csv");

            try
            {
                await _exporter.ExportAsync(Array.Empty<UsageEventEntity>(), path);

                Assert.True(File.Exists(path));
                Assert.True(Directory.Exists(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }
}
