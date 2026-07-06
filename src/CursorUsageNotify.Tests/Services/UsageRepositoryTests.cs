using CursorUsageNotify.Core.Configuration;
using CursorUsageNotify.Models.Entities;
using CursorUsageNotify.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CursorUsageNotify.Tests.Services;

/// <summary>
/// UsageRepository 单元测试：用临时 SQLite 文件验证 upsert 去重和查询。
/// </summary>
public sealed class UsageRepositoryTests : IDisposable
{
    private readonly DbContext _dbContext;
    private readonly UsageRepository _repository;
    private readonly string _tempDbPath;

    public UsageRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"cursor-test-{Guid.NewGuid():N}.db");
        var settings = new AppSettings { DatabasePath = _tempDbPath };
        _dbContext = new DbContext(settings, NullLogger<DbContext>.Instance);
        _dbContext.InitializeSchema();
        _repository = new UsageRepository(_dbContext, NullLogger<UsageRepository>.Instance);
    }

    [Fact]
    public async Task UpsertUsageEventsAsync_NewEvents_InsertsAll()
    {
        var events = new[]
        {
            CreateEvent(1000, "a@x.com", "model-a"),
            CreateEvent(2000, "a@x.com", "model-b"),
            CreateEvent(3000, "b@x.com", "model-a")
        };

        await _repository.UpsertUsageEventsAsync(events);

        // 用实际入库行数验证，不依赖 Storageable 返回值语义
        var all = await _repository.QueryEventsAsync();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task UpsertUsageEventsAsync_DuplicateTimestampUpsert_DoesNotInsertDuplicate()
    {
        var first = CreateEvent(1000, "a@x.com", "model-a");
        var duplicate = CreateEvent(1000, "a@x.com", "model-b"); // 同 timestamp + email

        await _repository.UpsertUsageEventsAsync(new[] { first });
        await _repository.UpsertUsageEventsAsync(new[] { duplicate });

        var all = await _repository.QueryEventsAsync();
        Assert.Single(all);
        Assert.Equal("model-b", all[0].Model); // 被更新为新值
    }

    [Fact]
    public async Task QueryEventsAsync_WithTimeRange_FiltersCorrectly()
    {
        await _repository.UpsertUsageEventsAsync(new[]
        {
            CreateEvent(1000, "a@x.com"),
            CreateEvent(2000, "a@x.com"),
            CreateEvent(3000, "a@x.com")
        });

        var result = await _repository.QueryEventsAsync(startTime: 1500, endTime: 2500);

        Assert.Single(result);
        Assert.Equal(2000, result[0].Timestamp);
    }

    [Fact]
    public async Task GetDistinctModelsAsync_ReturnsUniqueModels()
    {
        await _repository.UpsertUsageEventsAsync(new[]
        {
            CreateEvent(1000, "a@x.com", "model-a"),
            CreateEvent(2000, "a@x.com", "model-b"),
            CreateEvent(3000, "a@x.com", "model-a")
        });

        var models = await _repository.GetDistinctModelsAsync();

        Assert.Equal(2, models.Count);
        Assert.Contains("model-a", models);
        Assert.Contains("model-b", models);
    }

    [Fact]
    public async Task ClearAllAsync_RemovesAllRows()
    {
        await _repository.UpsertUsageEventsAsync(new[]
        {
            CreateEvent(1000, "a@x.com"),
            CreateEvent(2000, "a@x.com")
        });

        await _repository.ClearAllAsync();

        var all = await _repository.QueryEventsAsync();
        Assert.Empty(all);
    }

    private static UsageEventEntity CreateEvent(long timestamp, string email, string? model = null)
    {
        return new UsageEventEntity
        {
            Timestamp = timestamp,
            UserEmail = email,
            Model = model,
            FetchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch
        {
            // 清理失败忽略
        }
    }
}
