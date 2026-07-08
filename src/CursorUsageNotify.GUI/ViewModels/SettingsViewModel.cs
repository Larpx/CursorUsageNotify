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
    /// 真实 token 由 <see cref="SecureTokenHolder"/> 以字节形式持有，UI 仅显示掩码，
    /// 输入框仅用于一次性提交，提交后立即清空，不在内存中保留明文 string。
    /// </summary>
    public sealed partial class SettingsViewModel : ViewModelBase
    {
        private readonly ICursorApiClient _apiClient;
        private readonly AppSettings _settings;
        private readonly UsageSyncOptions _options;
        private readonly TokenProtector _protector;
        private readonly SecureTokenHolder _tokenHolder;
        private readonly IUsageRepository _repository;

        // 匹配 "WorkosCursorSessionToken=<value>"，value 为分号或结尾前的非空内容
        private static readonly Regex TokenRegex = new(
            @"WorkosCursorSessionToken\s*=\s*(?<token>[^;]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 构造设置 ViewModel，注入 API 客户端、配置、运行时选项、
        /// Token 加密保护器、安全持有器与仓储。
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
        /// <param name="tokenHolder">
        /// 安全 token 持有器。
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
            SecureTokenHolder tokenHolder,
            IUsageRepository repository,
            IMessenger messenger)
            : base(messenger)
        {
            _apiClient = apiClient;
            _settings = settings;
            _options = options;
            _protector = protector;
            _tokenHolder = tokenHolder;
            _repository = repository;

            SyncIntervalMinutes = _options.SyncIntervalMinutes;
            NotificationIntervalMinutes = _options.NotificationIntervalMinutes;

            PropertyChanged += OnSettingsPropertyChanged;

            // 订阅后台 token 状态变化（加载/清除/过期）
            Messenger.Register<TokenStateChangedMessage>(this, (_, _) => RefreshTokenStatus());

            // 初始刷新一次（后台加载早于本构造时由消息驱动；晚于时本次读取）
            RefreshTokenStatus();
        }

        /// <summary>
        /// 一次性输入框：用户粘贴新 Cookie/Token 后提交，提交后立即清空。
        /// 不作为真实 token 长期驻留，仅绑定到输入控件。
        /// </summary>
        [ObservableProperty]
        private string _inputToken = string.Empty;

        /// <summary>
        /// 是否已保存有效 token。
        /// </summary>
        [ObservableProperty]
        private bool _hasToken;

        /// <summary>
        /// 掩码显示文本（"●●●●●● 已设置" 或空）。
        /// </summary>
        [ObservableProperty]
        private string _tokenMaskedDisplay = string.Empty;

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

        /// <summary>
        /// 刷新掩码状态：从 holder 读取 HasToken 并更新显示文本。
        /// </summary>
        private void RefreshTokenStatus()
        {
            HasToken = _tokenHolder.HasToken;
            TokenMaskedDisplay = HasToken ? "●●●●●● 已设置" : string.Empty;
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
        /// 测试 API 密钥是否可用：从输入框提取 token，写入 holder，调用测试接口。
        /// 成功则持久化保存；失败则清除 holder 中的临时 token。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanTest))]
        private async Task TestConnectionAsync()
        {
            var token = ExtractToken(InputToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                TestResult = "请先粘贴 Session Token";
                TestSuccess = false;
                return;
            }

            IsTesting = true;
            TestResult = "测试中...";
            try
            {
                // 临时写入 holder 供测试调用
                _tokenHolder.Set(token);
                var result = await _tokenHolder.UseAsync(
                    (t, ct) => _apiClient.TestConnectionAsync(t, ct), default);

                if (result.IsSuccess)
                {
                    TestSuccess = true;
                    TestResult = $"连接成功，当前周期事件数：{result.Value}";
                    await SaveTokenAsync();
                }
                else
                {
                    TestSuccess = false;
                    TestResult = result.Error ?? "未知错误";
                    // 测试失败，清除临时写入的 token
                    _tokenHolder.Clear();
                }
            }
            catch (InvalidOperationException)
            {
                TestSuccess = false;
                TestResult = "Token 处理异常，请重试";
                _tokenHolder.Clear();
            }
            finally
            {
                // 输入框立即清空，避免明文 string 残留
                InputToken = string.Empty;
                IsTesting = false;
                RefreshTokenStatus();
                TestConnectionCommand.NotifyCanExecuteChanged();
                SyncNowCommand.NotifyCanExecuteChanged();
                ClearTokenCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanTest => !IsTesting && !string.IsNullOrWhiteSpace(InputToken);

        /// <summary>
        /// 保存设置（间隔变化立即生效）；若输入框有新 token 则一并保存。
        /// </summary>
        [RelayCommand]
        private void SaveSettings()
        {
            _options.SyncIntervalMinutes = SyncIntervalMinutes;
            _options.NotificationIntervalMinutes = NotificationIntervalMinutes;

            _settings.SyncIntervalMinutes = SyncIntervalMinutes;
            _settings.NotificationIntervalMinutes = NotificationIntervalMinutes;

            // 若输入框有新 token，提取后写入 holder 并持久化
            if (!string.IsNullOrWhiteSpace(InputToken))
            {
                var token = ExtractToken(InputToken);
                _tokenHolder.Set(token);
                InputToken = string.Empty;
                SaveTokenAsync().FireAndForget();
                RefreshTokenStatus();
                SyncNowCommand.NotifyCanExecuteChanged();
                ClearTokenCommand.NotifyCanExecuteChanged();
            }

            TestResult = "设置已保存";
        }

        /// <summary>
        /// 立即触发一次拉取（需已保存 token）。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSyncNow))]
        private void SyncNow()
        {
            Messenger.Send(new TriggerSyncNowMessage());
            TestResult = "已触发立即拉取";
        }

        private bool CanSyncNow => HasToken;

        /// <summary>
        /// 清除已保存的 token（内存 + 磁盘）。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanClearToken))]
        private void ClearToken()
        {
            _tokenHolder.Clear();
            // 清除磁盘 secrets.dat
            try
            {
                if (File.Exists(_settings.SecretsPath))
                {
                    File.Delete(_settings.SecretsPath);
                }
            }
            catch
            {
                // 删除失败不阻塞 UI
            }
            RefreshTokenStatus();
            TestSuccess = true;
            TestResult = "已清除保存的 Token";
        }

        private bool CanClearToken => HasToken;

        /// <summary>
        /// 用系统默认浏览器打开 Cursor 登录页，方便用户重新登录后复制新 cookie。
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
                case nameof(InputToken):
                    TestConnectionCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(SyncIntervalMinutes):
                    _options.SyncIntervalMinutes = SyncIntervalMinutes;
                    break;
                case nameof(NotificationIntervalMinutes):
                    _options.NotificationIntervalMinutes = NotificationIntervalMinutes;
                    break;
                case nameof(HasToken):
                    SyncNowCommand.NotifyCanExecuteChanged();
                    ClearTokenCommand.NotifyCanExecuteChanged();
                    break;
            }
        }

        /// <summary>
        /// 借用 holder 中的明文 token 加密后写入 secrets.dat。
        /// DPAPI 不可用时抛 <see cref="InvalidOperationException"/>，此处发送
        /// <see cref="TokenStorageUnavailableMessage"/> 通知 UI 弹窗警告而非静默失败。
        /// </summary>
        private async Task SaveTokenAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settings.SecretsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await _tokenHolder.UseAsync(async (token, ct) =>
                {
                    var cipher = _protector.Encrypt(token);
                    await File.WriteAllBytesAsync(_settings.SecretsPath, cipher, ct);
                }, default);
            }
            catch (InvalidOperationException ex)
            {
                // DPAPI 不可用：通知 UI 弹窗警告，token 未保存
                Messenger.Send(new TokenStorageUnavailableMessage(ex.Message));
                TestSuccess = false;
                TestResult = "当前平台不支持加密存储，token 未保存";
                _tokenHolder.Clear();
                RefreshTokenStatus();
            }
            catch
            {
                // 其他异常保存失败不阻塞 UI
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
