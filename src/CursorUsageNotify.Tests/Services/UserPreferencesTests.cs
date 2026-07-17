using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Services.Configuration;
using Xunit;


namespace Larpx.PersonalTools.CursorUsageNotify.Tests.Services
{
    /// <summary>
    /// UserPreferences 单元测试：覆盖加载/保存、平台开关、通知开关与 DeepSeek 过滤。
    /// </summary>
    public sealed class UserPreferencesTests : IDisposable
    {
        private readonly string _tempPath;

        public UserPreferencesTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), $"cun-prefs-{Guid.NewGuid():N}.json");
        }

        [Fact]
        public void Load_FileNotExists_ReturnsDefaults()
        {
            var prefs = UserPreferences.Load(_tempPath);

            Assert.Equal(TokenDisplayMode.FullNumber, prefs.TokenDisplayMode);
            Assert.True(prefs.IsPlatformEnabled(PlatformType.Cursor));
            Assert.True(prefs.IsPlatformEnabled(PlatformType.DeepSeek));
            Assert.True(prefs.IsNotificationEnabled(PlatformType.Cursor));
            Assert.True(prefs.IsNotificationEnabled(PlatformType.DeepSeek));
            Assert.Equal(DeepSeekDashboardMode.AllApiKeys, prefs.DeepSeekDashboardMode);
            Assert.Null(prefs.GetDeepSeekApiKeyFilter());
        }

        [Fact]
        public void Save_ThenLoad_RoundTripsAllFields()
        {
            var prefs = new UserPreferences(_tempPath)
            {
                TokenDisplayMode = TokenDisplayMode.Wan,
                DeepSeekDashboardMode = DeepSeekDashboardMode.SingleApiKey,
                DeepSeekSelectedApiKeyId = "trk-1"
            };
            prefs.SetPlatformEnabled(PlatformType.DeepSeek, false);
            prefs.SetNotificationEnabled(PlatformType.Cursor, false);
            prefs.Save();

            var loaded = UserPreferences.Load(_tempPath);

            Assert.Equal(TokenDisplayMode.Wan, loaded.TokenDisplayMode);
            Assert.False(loaded.IsPlatformEnabled(PlatformType.DeepSeek));
            Assert.True(loaded.IsPlatformEnabled(PlatformType.Cursor));
            Assert.False(loaded.IsNotificationEnabled(PlatformType.Cursor));
            Assert.True(loaded.IsNotificationEnabled(PlatformType.DeepSeek));
            Assert.Equal(DeepSeekDashboardMode.SingleApiKey, loaded.DeepSeekDashboardMode);
            Assert.Equal("trk-1", loaded.GetDeepSeekApiKeyFilter());
        }

        [Fact]
        public void GetDeepSeekApiKeyFilter_AllKeysMode_ReturnsNull()
        {
            var prefs = new UserPreferences(_tempPath)
            {
                DeepSeekDashboardMode = DeepSeekDashboardMode.AllApiKeys,
                DeepSeekSelectedApiKeyId = "trk-1"
            };

            Assert.Null(prefs.GetDeepSeekApiKeyFilter());
        }

        [Fact]
        public void GetDeepSeekApiKeyFilter_SingleKeyWithoutId_ReturnsNull()
        {
            var prefs = new UserPreferences(_tempPath)
            {
                DeepSeekDashboardMode = DeepSeekDashboardMode.SingleApiKey,
                DeepSeekSelectedApiKeyId = "  "
            };

            Assert.Null(prefs.GetDeepSeekApiKeyFilter());
        }

        [Fact]
        public void HasAnyNotificationEnabled_BothOff_ReturnsFalse()
        {
            var prefs = new UserPreferences(_tempPath);
            prefs.SetNotificationEnabled(PlatformType.Cursor, false);
            prefs.SetNotificationEnabled(PlatformType.DeepSeek, false);

            Assert.False(prefs.HasAnyNotificationEnabled());
        }

        [Fact]
        public void HasAnyNotificationEnabled_OneOn_ReturnsTrue()
        {
            var prefs = new UserPreferences(_tempPath);
            prefs.SetNotificationEnabled(PlatformType.Cursor, false);
            prefs.SetNotificationEnabled(PlatformType.DeepSeek, true);

            Assert.True(prefs.HasAnyNotificationEnabled());
        }

        [Fact]
        public void Load_CorruptedJson_ReturnsDefaults()
        {
            File.WriteAllText(_tempPath, "{ not-json");

            var prefs = UserPreferences.Load(_tempPath);

            Assert.True(prefs.IsPlatformEnabled(PlatformType.Cursor));
            Assert.True(prefs.IsNotificationEnabled(PlatformType.DeepSeek));
        }

        [Fact]
        public void Load_LegacyJsonWithoutNotification_DefaultsNotificationOn()
        {
            // 旧版偏好文件无 NotificationEnabled 字段
            File.WriteAllText(_tempPath, """
                {
                  "TokenDisplayMode": 0,
                  "PlatformEnabled": { "Cursor": true, "DeepSeek": true },
                  "DeepSeekDashboardMode": 0
                }
                """);

            var prefs = UserPreferences.Load(_tempPath);

            Assert.True(prefs.IsNotificationEnabled(PlatformType.Cursor));
            Assert.True(prefs.IsNotificationEnabled(PlatformType.DeepSeek));
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_tempPath))
                {
                    File.Delete(_tempPath);
                }
            }
            catch
            {
                // 清理失败忽略
            }
        }
    }
}
