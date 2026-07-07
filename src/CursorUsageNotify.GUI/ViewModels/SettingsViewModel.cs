using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Services.Http;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling;
using Larpx.PersonalTools.CursorUsageNotify.Services.Security;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels
{
    /// <summary>
    /// 设置 Tab：Cookie 认证 + 测试、拉取间隔、通知间隔、立即拉取、清空数据。
    /// </summary>
    public sealed partial class SettingsViewModel : ViewModelBase
    {
        private readonly ICursorApiClient _apiClient;
        private readonly AppSettings _settings;
        private readonly UsageSyncOptions _options;
        private readonly TokenProtector _protector;
        private readonly IUsageRepository _repository;

        // 匹配 "WorkosCursorSessionToken=<value>"，value 为分号或结尾前的非空内容
        private static readonly Regex TokenRegex = new(
            @"WorkosCursorSessionToken\s*=\s*(?<token>[^;]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 构造设置 ViewModel，注入 API 客户端、配置、运行时选项、
        /// Token 加密保护器与仓储，启动时加载已保存的 Token。
        /// </summary>
        /// <param name="apiClient">
        /// Cursor API 客户端。
        /// </param>
        /// <param name="settings">
        /// 应用配置。
        /// </param>
        /// <param name="options">
        /// 运行时同步选项。
        /// </param>
        /// <param name="protector">
        /// Token 加密保护器。
        /// </param>
        /// <param name="repository">
        /// 用量数据仓储。
        /// </param>
        /// <param name="messenger">
        /// 消息总线。
        /// </param>
        public SettingsViewModel(
            ICursorApiClient apiClient,
            AppSettings settings,
            UsageSyncOptions options,
            TokenProtector protector,
            IUsageRepository repository,
            IMessenger messenger)
            : base(messenger)
        {
            _apiClient = apiClient;
            _settings = settings;
            _options = options;
            _protector = protector;
            _repository = repository;

            SyncIntervalMinutes = _options.SyncIntervalMinutes;
            NotificationIntervalMinutes = _options.NotificationIntervalMinutes;
            SessionToken = _options.SessionToken;

            PropertyChanged += OnSettingsPropertyChanged;

            _ = LoadSavedTokenAsync();
        }

        /// <summary>
        /// Cursor 会话 Token（支持粘贴完整 Cookie 或纯 Token 值）。
        /// </summary>
        [ObservableProperty]
        private string _sessionToken = string.Empty;

        /// <summary>
        /// 从用户输入的原始字符串中提取 WorkosCursorSessionToken 值。
        /// 支持直接粘贴完整 Cookie 字符串，也兼容直接粘贴 Token 值。
        /// </summary>
        private static string ExtractToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            // 如果包含 "="，认为是 Cookie 格式，尝试解析
            if (raw.Contains('='))
            {
                var match = TokenRegex.Match(raw);
                if (match.Success)
                    return match.Groups["token"].Value.Trim();
            }

            // 不包含 "=" 或解析失败，直接当作 token 值
            return raw.Trim();
        }

        partial void OnSessionTokenChanged(string value)
        {
            var extracted = ExtractToken(value);
            if (extracted != value)
            {
                // 重新赋值触发属性变更，但需避免无限递归
                _sessionToken = extracted;
                OnPropertyChanged(nameof(SessionToken));
            }
        }

        /// <summary>
        /// 同步拉取间隔（分钟）。
        /// </summary>
        [ObservableProperty]
        private int _syncIntervalMinutes = 60;

        /// <summary>
        /// 通知检查间隔（分钟）。
        /// </summary>
        [ObservableProperty]
        private int _notificationIntervalMinutes = 60;

        /// <summary>
        /// 测试连接/操作的结果文本。
        /// </summary>
        [ObservableProperty]
        private string _testResult = string.Empty;

        /// <summary>
        /// 最近一次测试/操作是否成功。
        /// </summary>
        [ObservableProperty]
        private bool _testSuccess;

        /// <summary>
        /// 是否正在执行测试连接（控制按钮禁用与提示文本）。
        /// </summary>
        [ObservableProperty]
        private bool _isTesting;

        /// <summary>
        /// 间隔选项（分钟）。
        /// </summary>
        public int[] IntervalOptions { get; } = { 30, 60, 180, 300 };

        /// <summary>
        /// 测试 API 密钥是否可用。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanTest))]
        private async Task TestConnectionAsync()
        {
            IsTesting = true;
            TestResult = "测试中...";
            try
            {
                var result = await _apiClient.TestConnectionAsync(SessionToken);
                if (result.IsSuccess)
                {
                    TestSuccess = true;
                    TestResult = $"连接成功，当前周期事件数：{result.Value}";
                    _options.SessionToken = SessionToken;
                    await SaveTokenAsync(SessionToken);
                }
                else
                {
                    TestSuccess = false;
                    TestResult = result.Error ?? "未知错误";
                }
            }
            finally
            {
                IsTesting = false;
                TestConnectionCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanTest => !IsTesting && !string.IsNullOrWhiteSpace(SessionToken);

        /// <summary>
        /// 保存设置（间隔变化立即生效）。
        /// </summary>
        [RelayCommand]
        private void SaveSettings()
        {
            _options.SyncIntervalMinutes = SyncIntervalMinutes;
            _options.NotificationIntervalMinutes = NotificationIntervalMinutes;
            _options.SessionToken = SessionToken;

            _settings.SyncIntervalMinutes = SyncIntervalMinutes;
            _settings.NotificationIntervalMinutes = NotificationIntervalMinutes;

            SaveTokenAsync(SessionToken).FireAndForget();
            TestResult = "设置已保存";
        }

        /// <summary>
        /// 立即触发一次拉取。
        /// </summary>
        [RelayCommand]
        private void SyncNow()
        {
            if (string.IsNullOrEmpty(SessionToken))
            {
                TestResult = "请先填写 Session Token";
                TestSuccess = false;
                return;
            }
            _options.SessionToken = SessionToken;
            Messenger.Send(new TriggerSyncNowMessage());
            TestResult = "已触发立即拉取";
        }

        /// <summary>
        /// 用系统默认浏览器打开 Cursor 登录页，方便用户重新登录后复制新 cookie。
        /// UseShellExecute=true 让系统按 URL 协议选择默认浏览器。
        /// </summary>
        [RelayCommand]
        private void OpenBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://cursor.com/dashboard/billing",
                    UseShellExecute = true
                });
                TestSuccess = true;
                TestResult = "已打开浏览器，登录后请复制新 Cookie 粘贴到上方文本框";
            }
            catch (Exception ex)
            {
                TestSuccess = false;
                TestResult = $"打开浏览器失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 清空所有历史用量数据（用户手动触发，不可撤销）。
        /// </summary>
        [RelayCommand]
        private async Task ClearDataAsync()
        {
            try
            {
                await _repository.ClearAllAsync();
                TestSuccess = true;
                TestResult = "已清空所有历史数据";
            }
            catch (Exception ex)
            {
                TestSuccess = false;
                TestResult = $"清空失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 属性变化时同步运行时选项并刷新命令可执行性。
        /// </summary>
        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SessionToken):
                    TestConnectionCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(SyncIntervalMinutes):
                    _options.SyncIntervalMinutes = SyncIntervalMinutes;
                    break;
                case nameof(NotificationIntervalMinutes):
                    _options.NotificationIntervalMinutes = NotificationIntervalMinutes;
                    break;
            }
        }

        /// <summary>
        /// 启动时从 secrets.dat 加载加密 token。
        /// </summary>
        private async Task LoadSavedTokenAsync()
        {
            try
            {
                if (File.Exists(_settings.SecretsPath))
                {
                    var cipher = await File.ReadAllBytesAsync(_settings.SecretsPath);
                    var plain = _protector.Decrypt(cipher);
                    if (!string.IsNullOrEmpty(plain))
                    {
                        SessionToken = plain;
                        _options.SessionToken = plain;
                    }
                }
            }
            catch
            {
                // 静默忽略，用户重新输入即可
            }
        }

        /// <summary>
        /// 加密保存 token 到 secrets.dat。
        /// </summary>
        private async Task SaveTokenAsync(string token)
        {
            try
            {
                var dir = Path.GetDirectoryName(_settings.SecretsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var cipher = _protector.Encrypt(token);
                await File.WriteAllBytesAsync(_settings.SecretsPath, cipher);
            }
            catch
            {
                // 保存失败不阻塞 UI
            }
        }
    }

    internal static class TaskExtensions
    {
        /// <summary>
        /// 以"fire-and-forget"方式执行 Task，避免未观察异常导致进程崩溃。
        /// </summary>
        /// <param name="task">
        /// 待执行的 Task。
        /// </param>
        public static void FireAndForget(this Task task)
        {
            _ = task.ConfigureAwait(false);
        }
    }
}
