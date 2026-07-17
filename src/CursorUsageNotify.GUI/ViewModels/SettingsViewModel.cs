using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Services.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Services.Http;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling;
using Larpx.PersonalTools.CursorUsageNotify.Services.Security;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels
{
    /// <summary>
    /// 设置 Tab：多平台凭证管理 + 启用开关、拉取间隔、通知间隔、立即拉取、清空数据。
    /// 真实 token 由 <see cref="SecureTokenHolder"/> 以字节形式持有，UI 仅显示掩码，
    /// 输入框仅用于一次性提交，提交后立即清空，不在内存中保留明文 string。
    /// </summary>
    public sealed partial class SettingsViewModel : ViewModelBase
    {
        private readonly ICursorApiClient _apiClient;
        private readonly IDeepSeekApiClient _deepSeekClient;
        private readonly AppSettings _settings;
        private readonly UsageSyncOptions _options;
        private readonly TokenProtector _protector;
        private readonly SecureTokenHolder _tokenHolder;
        private readonly IUsageRepository _repository;
        private readonly UserPreferences _userPrefs;

        // 匹配 "WorkosCursorSessionToken=<value>"，value 为分号或结尾前的非空内容
        private static readonly Regex TokenRegex = new(
            @"WorkosCursorSessionToken\s*=\s*(?<token>[^;]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 匹配 DeepSeek localStorage userToken JSON 中的 value 字段
        private static readonly Regex DeepSeekTokenValueRegex = new(
            @"""value""\s*:\s*""(?<token>[^""]+)""",
            RegexOptions.Compiled);

        /// <summary>
        /// 构造设置 ViewModel，注入 API 客户端、配置、运行时选项、
        /// Token 加密保护器、安全持有器与仓储。
        /// </summary>
        /// <param name="apiClient">
        /// Cursor API 客户端。
        /// </param>
        /// <param name="deepSeekClient">
        /// DeepSeek API 客户端。
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
        /// <param name="userPrefs">
        /// 用户偏好（平台启用开关持久化）。
        /// </param>
        /// <param name="messenger">
        /// 消息总线。
        /// </param>
        public SettingsViewModel(
            ICursorApiClient apiClient,
            IDeepSeekApiClient deepSeekClient,
            AppSettings settings,
            UsageSyncOptions options,
            TokenProtector protector,
            SecureTokenHolder tokenHolder,
            IUsageRepository repository,
            UserPreferences userPrefs,
            IMessenger messenger)
            : base(messenger)
        {
            _apiClient = apiClient;
            _deepSeekClient = deepSeekClient;
            _settings = settings;
            _options = options;
            _protector = protector;
            _tokenHolder = tokenHolder;
            _repository = repository;
            _userPrefs = userPrefs;

            SyncIntervalMinutes = _options.SyncIntervalMinutes;
            NotificationIntervalMinutes = _options.NotificationIntervalMinutes;

            // 从持久化偏好加载启用开关（默认 true）
            CursorEnabled = _userPrefs.IsPlatformEnabled(PlatformType.Cursor);
            DeepSeekEnabled = _userPrefs.IsPlatformEnabled(PlatformType.DeepSeek);

            // DeepSeek 大屏模式
            _suppressDeepSeekModeNotify = true;
            IsDeepSeekAllKeysMode = _userPrefs.DeepSeekDashboardMode != DeepSeekDashboardMode.SingleApiKey;
            IsDeepSeekSingleKeyMode = !IsDeepSeekAllKeysMode;
            _suppressDeepSeekModeNotify = false;

            PropertyChanged += OnSettingsPropertyChanged;

            // 订阅后台 token 状态变化（加载/清除/过期），刷新对应平台状态
            Messenger.Register<TokenStateChangedMessage>(this, (_, m) =>
            {
                if (m.Platform == PlatformType.Cursor)
                {
                    RefreshTokenStatus();
                }
                else if (m.Platform == PlatformType.DeepSeek)
                {
                    RefreshDeepSeekTokenStatus();
                }
            });

            // 同步成功后刷新 API Key 下拉列表
            Messenger.Register<UsageDataFetchedMessage>(this, (_, m) =>
            {
                if (m.Platform == PlatformType.DeepSeek)
                {
                    _ = LoadDeepSeekApiKeyOptionsAsync();
                }
            });

            // 初始刷新一次（后台加载早于本构造时由消息驱动；晚于时本次读取）
            RefreshTokenStatus();
            RefreshDeepSeekTokenStatus();
            _ = LoadDeepSeekApiKeyOptionsAsync();
        }

        /// <summary>
        /// 加载 DeepSeek 大屏模式时抑制 PropertyChanged 副作用。
        /// </summary>
        private bool _suppressDeepSeekModeNotify;

        /// <summary>
        /// 是否启用 Cursor 平台数据采集（关闭后不刷新，但保留历史数据）。
        /// </summary>
        [ObservableProperty]
        private bool _cursorEnabled = true;

        /// <summary>
        /// 是否启用 DeepSeek 平台数据采集。
        /// </summary>
        [ObservableProperty]
        private bool _deepSeekEnabled = true;

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
        /// DeepSeek userToken 一次性输入框。提交后立即清空。
        /// 支持粘贴 localStorage userToken JSON（自动提取 value）或裸 token。
        /// </summary>
        [ObservableProperty]
        private string _deepSeekInputToken = string.Empty;

        /// <summary>
        /// 是否已保存有效 DeepSeek token。
        /// </summary>
        [ObservableProperty]
        private bool _hasDeepSeekToken;

        /// <summary>
        /// DeepSeek token 掩码显示文本。
        /// </summary>
        [ObservableProperty]
        private string _deepSeekTokenMaskedDisplay = string.Empty;

        /// <summary>
        /// DeepSeek 测试/操作结果文本。
        /// </summary>
        [ObservableProperty]
        private string _deepSeekTestResult = string.Empty;

        /// <summary>
        /// DeepSeek 最近一次测试/操作是否成功。
        /// </summary>
        [ObservableProperty]
        private bool _deepSeekTestSuccess;

        /// <summary>
        /// 是否正在执行 DeepSeek 测试连接。
        /// </summary>
        [ObservableProperty]
        private bool _isDeepSeekTesting;

        /// <summary>
        /// DeepSeek 大屏：显示全部 API Key 汇总。
        /// </summary>
        [ObservableProperty]
        private bool _isDeepSeekAllKeysMode = true;

        /// <summary>
        /// DeepSeek 大屏：仅显示单个 API Key。
        /// </summary>
        [ObservableProperty]
        private bool _isDeepSeekSingleKeyMode;

        /// <summary>
        /// DeepSeek API Key 下拉选项。
        /// </summary>
        public ObservableCollection<DeepSeekApiKeyOption> DeepSeekApiKeyOptions { get; } = new();

        /// <summary>
        /// 当前选中的 DeepSeek API Key（单 Key 模式）。
        /// </summary>
        [ObservableProperty]
        private DeepSeekApiKeyOption? _selectedDeepSeekApiKey;

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
        /// 从用户输入中提取 DeepSeek userToken 值。
        /// DeepSeek localStorage 中 userToken 存储为 {"value":"xxx","__version":"0"}，
        /// 用户可能粘贴整个 JSON 或仅 value 字段或裸 token。
        /// </summary>
        private static string ExtractDeepSeekToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            raw = raw.Trim();

            // JSON 对象格式：尝试正则提取 value 字段（避免严格 JSON 解析的额外开销）
            if (raw.Contains("\"value\"", StringComparison.OrdinalIgnoreCase))
            {
                var match = DeepSeekTokenValueRegex.Match(raw);
                if (match.Success)
                    return match.Groups["token"].Value.Trim();
            }

            // 兜底：尝试用 JsonDocument 解析（用户可能粘贴带转义的复杂 JSON）
            if (raw.StartsWith('{'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("value", out var valueEl)
                        && valueEl.ValueKind == JsonValueKind.String)
                    {
                        return valueEl.GetString() ?? string.Empty;
                    }
                }
                catch
                {
                    // 解析失败忽略，按裸 token 处理
                }
            }

            // 直接当作裸 token
            return raw;
        }

        /// <summary>
        /// 刷新 Cursor 平台掩码状态：从 holder 读取 HasToken 并更新显示文本。
        /// </summary>
        private void RefreshTokenStatus()
        {
            HasToken = _tokenHolder.HasToken(PlatformType.Cursor);
            TokenMaskedDisplay = HasToken ? "●●●●●● 已设置" : string.Empty;
        }

        /// <summary>
        /// 刷新 DeepSeek 平台掩码状态。
        /// </summary>
        private void RefreshDeepSeekTokenStatus()
        {
            HasDeepSeekToken = _tokenHolder.HasToken(PlatformType.DeepSeek);
            DeepSeekTokenMaskedDisplay = HasDeepSeekToken ? "●●●●●● 已设置" : string.Empty;
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
        /// 测试 Cursor API 密钥是否可用：从输入框提取 token，写入 holder，调用测试接口。
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
                _tokenHolder.Set(PlatformType.Cursor, token);
                var result = await _tokenHolder.UseAsync(
                    PlatformType.Cursor,
                    (t, ct) => _apiClient.TestConnectionAsync(t, ct), default);

                if (result.IsSuccess)
                {
                    TestSuccess = true;
                    TestResult = $"连接成功，当前周期事件数：{result.Value}";
                    await SaveTokenForPlatformAsync(PlatformType.Cursor);
                }
                else
                {
                    TestSuccess = false;
                    TestResult = result.Error ?? "未知错误";
                    // 测试失败，清除临时写入的 token
                    _tokenHolder.Clear(PlatformType.Cursor);
                }
            }
            catch (InvalidOperationException)
            {
                TestSuccess = false;
                TestResult = "Token 处理异常，请重试";
                _tokenHolder.Clear(PlatformType.Cursor);
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
        /// 测试 DeepSeek userToken 是否可用：调用 get_user_summary 验证。
        /// 成功则持久化到 secrets_deepseek.dat；失败则清除临时 token。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanTestDeepSeek))]
        private async Task TestDeepSeekConnectionAsync()
        {
            var token = ExtractDeepSeekToken(DeepSeekInputToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                DeepSeekTestResult = "请先粘贴 userToken";
                DeepSeekTestSuccess = false;
                return;
            }

            IsDeepSeekTesting = true;
            DeepSeekTestResult = "测试中...";
            try
            {
                _tokenHolder.Set(PlatformType.DeepSeek, token);
                var result = await _tokenHolder.UseAsync(
                    PlatformType.DeepSeek,
                    (t, ct) => _deepSeekClient.TestConnectionAsync(t, ct), default);

                if (result.IsSuccess)
                {
                    DeepSeekTestSuccess = true;
                    DeepSeekTestResult = $"连接成功，账户事件数：{result.Value}";
                    await SaveTokenForPlatformAsync(PlatformType.DeepSeek);
                    await LoadDeepSeekApiKeyOptionsAsync();
                }
                else
                {
                    DeepSeekTestSuccess = false;
                    DeepSeekTestResult = result.Error ?? "未知错误";
                    _tokenHolder.Clear(PlatformType.DeepSeek);
                }
            }
            catch (InvalidOperationException)
            {
                DeepSeekTestSuccess = false;
                DeepSeekTestResult = "Token 处理异常，请重试";
                _tokenHolder.Clear(PlatformType.DeepSeek);
            }
            finally
            {
                DeepSeekInputToken = string.Empty;
                IsDeepSeekTesting = false;
                RefreshDeepSeekTokenStatus();
                TestDeepSeekConnectionCommand.NotifyCanExecuteChanged();
                ClearDeepSeekTokenCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanTestDeepSeek => !IsDeepSeekTesting && !string.IsNullOrWhiteSpace(DeepSeekInputToken);

        /// <summary>
        /// 保存设置（间隔变化立即生效，平台启用开关持久化）；若输入框有新 token 则一并保存。
        /// </summary>
        [RelayCommand]
        private void SaveSettings()
        {
            _options.SyncIntervalMinutes = SyncIntervalMinutes;
            _options.NotificationIntervalMinutes = NotificationIntervalMinutes;

            _settings.SyncIntervalMinutes = SyncIntervalMinutes;
            _settings.NotificationIntervalMinutes = NotificationIntervalMinutes;

            // 持久化平台启用开关 + DeepSeek 大屏模式
            _userPrefs.SetPlatformEnabled(PlatformType.Cursor, CursorEnabled);
            _userPrefs.SetPlatformEnabled(PlatformType.DeepSeek, DeepSeekEnabled);
            PersistDeepSeekDashboardPrefs(notify: true);
            try
            {
                _userPrefs.Save();
            }
            catch
            {
                // 偏好保存失败不阻塞 UI
            }

            // 若 Cursor 输入框有新 token，提取后写入 holder 并持久化
            if (!string.IsNullOrWhiteSpace(InputToken))
            {
                var token = ExtractToken(InputToken);
                _tokenHolder.Set(PlatformType.Cursor, token);
                InputToken = string.Empty;
                SaveTokenForPlatformAsync(PlatformType.Cursor).FireAndForget();
                RefreshTokenStatus();
                SyncNowCommand.NotifyCanExecuteChanged();
                ClearTokenCommand.NotifyCanExecuteChanged();
            }

            // 若 DeepSeek 输入框有新 token，提取后写入 holder 并持久化
            if (!string.IsNullOrWhiteSpace(DeepSeekInputToken))
            {
                var token = ExtractDeepSeekToken(DeepSeekInputToken);
                _tokenHolder.Set(PlatformType.DeepSeek, token);
                DeepSeekInputToken = string.Empty;
                SaveTokenForPlatformAsync(PlatformType.DeepSeek).FireAndForget();
                RefreshDeepSeekTokenStatus();
                ClearDeepSeekTokenCommand.NotifyCanExecuteChanged();
            }

            TestResult = "设置已保存";
        }

        /// <summary>
        /// 立即触发一次拉取（任意平台已保存 token 即可）。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSyncNow))]
        private void SyncNow()
        {
            Messenger.Send(new TriggerSyncNowMessage());
            TestResult = "已触发立即拉取";
        }

        private bool CanSyncNow => HasToken || HasDeepSeekToken;

        /// <summary>
        /// 清除 Cursor 已保存的 token（内存 + 磁盘 secrets.dat）。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanClearToken))]
        private void ClearToken()
        {
            _tokenHolder.Clear(PlatformType.Cursor);
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
            TestResult = "已清除 Cursor Token";
        }

        private bool CanClearToken => HasToken;

        /// <summary>
        /// 清除 DeepSeek 已保存的 token（内存 + 磁盘 secrets_deepseek.dat）。
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanClearDeepSeekToken))]
        private void ClearDeepSeekToken()
        {
            _tokenHolder.Clear(PlatformType.DeepSeek);
            try
            {
                var path = GetSecretsPath(PlatformType.DeepSeek);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 删除失败不阻塞 UI
            }
            RefreshDeepSeekTokenStatus();
            DeepSeekTestSuccess = true;
            DeepSeekTestResult = "已清除 DeepSeek Token";
        }

        private bool CanClearDeepSeekToken => HasDeepSeekToken;

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
        /// 用系统默认浏览器打开 DeepSeek 开放平台，方便用户登录后从 F12 → Application → Local Storage 复制 userToken。
        /// </summary>
        [RelayCommand]
        private void OpenDeepSeekBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://platform.deepseek.com/usage",
                    UseShellExecute = true
                });
                DeepSeekTestSuccess = true;
                DeepSeekTestResult = "已打开浏览器，登录后按 F12 → Application → Local Storage → userToken 复制";
            }
            catch (Exception ex)
            {
                DeepSeekTestSuccess = false;
                DeepSeekTestResult = $"打开浏览器失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 清空所有平台历史用量数据（用户手动触发，不可撤销）。
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
                case nameof(DeepSeekInputToken):
                    TestDeepSeekConnectionCommand.NotifyCanExecuteChanged();
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
                case nameof(HasDeepSeekToken):
                    SyncNowCommand.NotifyCanExecuteChanged();
                    ClearDeepSeekTokenCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(CursorEnabled):
                    // 实时写入运行时偏好，立即生效（无需点保存）
                    _userPrefs.SetPlatformEnabled(PlatformType.Cursor, CursorEnabled);
                    break;
                case nameof(DeepSeekEnabled):
                    _userPrefs.SetPlatformEnabled(PlatformType.DeepSeek, DeepSeekEnabled);
                    break;
                case nameof(IsDeepSeekAllKeysMode):
                    if (_suppressDeepSeekModeNotify) break;
                    if (IsDeepSeekAllKeysMode)
                    {
                        _suppressDeepSeekModeNotify = true;
                        IsDeepSeekSingleKeyMode = false;
                        _suppressDeepSeekModeNotify = false;
                        PersistDeepSeekDashboardPrefs(notify: true);
                    }
                    break;
                case nameof(IsDeepSeekSingleKeyMode):
                    if (_suppressDeepSeekModeNotify) break;
                    if (IsDeepSeekSingleKeyMode)
                    {
                        _suppressDeepSeekModeNotify = true;
                        IsDeepSeekAllKeysMode = false;
                        _suppressDeepSeekModeNotify = false;
                        PersistDeepSeekDashboardPrefs(notify: true);
                    }
                    break;
                case nameof(SelectedDeepSeekApiKey):
                    if (_suppressDeepSeekModeNotify) break;
                    PersistDeepSeekDashboardPrefs(notify: true);
                    break;
            }
        }

        /// <summary>
        /// 从 get_api_keys 加载本账户当前全部 API Key；无 token 时回退到本地用量库。
        /// </summary>
        private async Task LoadDeepSeekApiKeyOptionsAsync()
        {
            try
            {
                IReadOnlyList<(string TrackingId, string Name)> keys;

                if (_tokenHolder.HasToken(PlatformType.DeepSeek))
                {
                    // 设置页应展示账户当前全部 Key，不以用量库出现过的为准
                    var dto = await _tokenHolder.UseAsync(
                        PlatformType.DeepSeek,
                        (t, ct) => _deepSeekClient.GetApiKeysAsync(t, ct),
                        default);

                    keys = (dto.ApiKeys ?? [])
                        .Select(k =>
                        {
                            var trackingId = k.TrackingId ?? k.SensitiveId ?? k.Name ?? string.Empty;
                            var name = k.Name ?? k.SensitiveId ?? trackingId;
                            return (TrackingId: trackingId, Name: name);
                        })
                        .Where(k => !string.IsNullOrEmpty(k.TrackingId))
                        .OrderBy(k => k.Name)
                        .ToList();
                }
                else
                {
                    keys = await _repository.GetDistinctApiKeysAsync(PlatformType.DeepSeek);
                }

                _suppressDeepSeekModeNotify = true;
                DeepSeekApiKeyOptions.Clear();
                foreach (var (trackingId, name) in keys)
                {
                    DeepSeekApiKeyOptions.Add(new DeepSeekApiKeyOption
                    {
                        TrackingId = trackingId,
                        Name = name
                    });
                }

                var selectedId = _userPrefs.DeepSeekSelectedApiKeyId;
                SelectedDeepSeekApiKey = DeepSeekApiKeyOptions
                    .FirstOrDefault(k => k.TrackingId == selectedId || k.Name == selectedId)
                    ?? DeepSeekApiKeyOptions.FirstOrDefault();
                _suppressDeepSeekModeNotify = false;
            }
            catch
            {
                _suppressDeepSeekModeNotify = false;
            }
        }

        /// <summary>
        /// 将 DeepSeek 大屏模式写入偏好并可选通知大屏刷新。
        /// </summary>
        private void PersistDeepSeekDashboardPrefs(bool notify)
        {
            _userPrefs.DeepSeekDashboardMode = IsDeepSeekSingleKeyMode
                ? DeepSeekDashboardMode.SingleApiKey
                : DeepSeekDashboardMode.AllApiKeys;
            _userPrefs.DeepSeekSelectedApiKeyId = SelectedDeepSeekApiKey?.TrackingId
                                                 ?? SelectedDeepSeekApiKey?.Name;

            try
            {
                _userPrefs.Save();
            }
            catch
            {
                // 偏好保存失败不阻塞 UI
            }

            if (notify)
            {
                Messenger.Send(new UserPreferencesChangedMessage(PlatformType.DeepSeek));
            }
        }

        /// <summary>
        /// 获取指定平台的加密 token 持久化文件路径。
        /// 与后台同步服务保持一致：
        /// Cursor 使用 secrets.dat（兼容旧版），DeepSeek 使用 secrets_deepseek.dat。
        /// </summary>
        /// <param name="platform">平台类型。</param>
        /// <returns>secrets 文件绝对路径。</returns>
        private string GetSecretsPath(PlatformType platform) => platform switch
        {
            PlatformType.Cursor => _settings.SecretsPath,
            PlatformType.DeepSeek => Path.Combine(
                Path.GetDirectoryName(_settings.SecretsPath) ?? string.Empty,
                "secrets_deepseek.dat"),
            _ => _settings.SecretsPath
        };

        /// <summary>
        /// 借用指定平台 holder 中的明文 token 加密后写入对应 secrets 文件。
        /// DPAPI 不可用时抛 <see cref="InvalidOperationException"/>，此处发送
        /// <see cref="TokenStorageUnavailableMessage"/> 通知 UI 弹窗警告而非静默失败。
        /// </summary>
        /// <param name="platform">平台类型。</param>
        private async Task SaveTokenForPlatformAsync(PlatformType platform)
        {
            try
            {
                var path = GetSecretsPath(platform);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await _tokenHolder.UseAsync(platform, async (token, ct) =>
                {
                    var cipher = _protector.Encrypt(token);
                    await File.WriteAllBytesAsync(path, cipher, ct);
                }, default);
            }
            catch (InvalidOperationException ex)
            {
                // DPAPI 不可用：通知 UI 弹窗警告，token 未保存
                Messenger.Send(new TokenStorageUnavailableMessage(ex.Message));
                if (platform == PlatformType.Cursor)
                {
                    TestSuccess = false;
                    TestResult = "当前平台不支持加密存储，token 未保存";
                }
                else
                {
                    DeepSeekTestSuccess = false;
                    DeepSeekTestResult = "当前平台不支持加密存储，token 未保存";
                }
                _tokenHolder.Clear(platform);
                if (platform == PlatformType.Cursor)
                {
                    RefreshTokenStatus();
                }
                else
                {
                    RefreshDeepSeekTokenStatus();
                }
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
