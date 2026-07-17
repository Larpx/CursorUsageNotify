using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;


namespace Larpx.PersonalTools.CursorUsageNotify.Tests.Services
{
    /// <summary>
    /// UsageRepository 单元测试：用临时 SQLite 文件验证 upsert 去重、聚合与 DeepSeek Key 过滤。
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
            var all = await _repository.QueryEventsPagedAsync(pageSize: 100);
            Assert.Equal(3, all.Items.Count);
        }

        [Fact]
        public async Task UpsertUsageEventsAsync_SameKeyDifferentModel_InsertsBoth()
        {
            // DeepSeek：同日同 Key 不同模型需分存，不能互相覆盖
            var first = CreateEvent(1000, "key-a", "model-a");
            var otherModel = CreateEvent(1000, "key-a", "model-b");

            await _repository.UpsertUsageEventsAsync(new[] { first });
            await _repository.UpsertUsageEventsAsync(new[] { otherModel });

            var all = await _repository.QueryEventsPagedAsync(pageSize: 100);
            Assert.Equal(2, all.Items.Count);
        }

        [Fact]
        public async Task UpsertUsageEventsAsync_DuplicateSameModel_Upserts()
        {
            var first = CreateEvent(1000, "a@x.com", "model-a");
            first.InputTokens = 10;
            var duplicate = CreateEvent(1000, "a@x.com", "model-a");
            duplicate.InputTokens = 99;

            await _repository.UpsertUsageEventsAsync(new[] { first });
            await _repository.UpsertUsageEventsAsync(new[] { duplicate });

            var all = await _repository.QueryEventsPagedAsync(pageSize: 100);
            Assert.Single(all.Items);
            Assert.Equal(99, all.Items[0].InputTokens);
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

            var result = await _repository.QueryEventsPagedAsync(startTime: 1500, endTime: 2500);

            Assert.Single(result.Items);
            Assert.Equal(2000, result.Items[0].Timestamp);
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

            var all = await _repository.QueryEventsPagedAsync(pageSize: 100);
            Assert.Empty(all.Items);
        }

        [Fact]
        public async Task AggregateStatsAsync_SumsRequestCount_NotRowCount()
        {
            await _repository.UpsertUsageEventsAsync(new[]
            {
                CreateDeepSeekEvent(1000, "key-a", "trk-a", "deepseek-chat", requestCount: 5, input: 10),
                CreateDeepSeekEvent(2000, "key-a", "trk-a", "deepseek-v4-pro", requestCount: 3, input: 20)
            });

            var stats = await _repository.AggregateStatsAsync(1, 3000, PlatformType.DeepSeek);

            Assert.Equal(8, stats.TotalRequests);
            Assert.Equal(30, stats.TotalInputTokens);
        }

        [Fact]
        public async Task AggregateStatsAsync_WithApiKeyFilter_OnlyMatchingKey()
        {
            await _repository.UpsertUsageEventsAsync(new[]
            {
                CreateDeepSeekEvent(1000, "key-a", "trk-a", "deepseek-chat", requestCount: 5, input: 10),
                CreateDeepSeekEvent(2000, "key-b", "trk-b", "deepseek-chat", requestCount: 7, input: 40)
            });

            var stats = await _repository.AggregateStatsAsync(1, 3000, PlatformType.DeepSeek, "trk-a");

            Assert.Equal(5, stats.TotalRequests);
            Assert.Equal(10, stats.TotalInputTokens);
        }

        [Fact]
        public async Task AggregateByApiKeyAndModelAsync_GroupsCorrectly()
        {
            await _repository.UpsertUsageEventsAsync(new[]
            {
                CreateDeepSeekEvent(1000, "key-a", "trk-a", "m1", requestCount: 2, input: 1, output: 2, spendCents: 10),
                CreateDeepSeekEvent(2000, "key-a", "trk-a", "m1", requestCount: 3, input: 4, output: 5, spendCents: 20),
                CreateDeepSeekEvent(3000, "key-a", "trk-a", "m2", requestCount: 1, input: 7, output: 0, spendCents: 5)
            });

            var rows = await _repository.AggregateByApiKeyAndModelAsync(1, 4000, PlatformType.DeepSeek);

            Assert.Equal(2, rows.Count);
            var m1 = Assert.Single(rows, r => r.Model == "m1");
            Assert.Equal(5, m1.RequestCount);
            Assert.Equal(5, m1.InputTokens);
            Assert.Equal(7, m1.OutputTokens);
            Assert.Equal(30, m1.SpendCents);
            var m2 = Assert.Single(rows, r => r.Model == "m2");
            Assert.Equal(1, m2.RequestCount);
            Assert.Equal(7, m2.InputTokens);
        }

        [Fact]
        public async Task GetDistinctApiKeysAsync_ReturnsUniqueKeys()
        {
            await _repository.UpsertUsageEventsAsync(new[]
            {
                CreateDeepSeekEvent(1000, "Alpha", "trk-a", "m1", 1),
                CreateDeepSeekEvent(2000, "Alpha", "trk-a", "m2", 1),
                CreateDeepSeekEvent(3000, "Beta", "trk-b", "m1", 1)
            });

            var keys = await _repository.GetDistinctApiKeysAsync(PlatformType.DeepSeek);

            Assert.Equal(2, keys.Count);
            Assert.Contains(keys, k => k.TrackingId == "trk-a" && k.Name == "Alpha");
            Assert.Contains(keys, k => k.TrackingId == "trk-b" && k.Name == "Beta");
        }

        private static UsageEventEntity CreateEvent(long timestamp, string email, string? model = null)
        {
            return new UsageEventEntity
            {
                Platform = PlatformType.Cursor,
                Timestamp = timestamp,
                UserEmail = email,
                Model = model,
                RequestCount = 1,
                FetchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static UsageEventEntity CreateDeepSeekEvent(
            long timestamp,
            string apiKeyName,
            string trackingId,
            string model,
            long requestCount,
            long input = 0,
            long output = 0,
            decimal spendCents = 0)
        {
            return new UsageEventEntity
            {
                Platform = PlatformType.DeepSeek,
                Timestamp = timestamp,
                UserEmail = apiKeyName,
                Kind = trackingId,
                Model = model,
                RequestCount = requestCount,
                InputTokens = input,
                OutputTokens = output,
                ChargedCents = spendCents,
                IsTokenBasedCall = true,
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
}
