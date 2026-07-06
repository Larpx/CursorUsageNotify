using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CursorUsageNotify.Core.Configuration;
using CursorUsageNotify.Services.Http;
using CursorUsageNotify.Services.Messages;
using CursorUsageNotify.Services.Scheduling;
using CursorUsageNotify.Services.Security;
using CursorUsageNotify.Services.Storage;

namespace CursorUsageNotify.GUI.ViewModels;

/// <summary>
/// 设置 Tab：API 密钥 + 测试、拉取间隔、通知间隔、立即拉取、清空数据。
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ICursorApiClient _apiClient;
    private readonly AppSettings _settings;
    private readonly UsageSyncOptions _options;
    private readonly TokenProtector _protector;
    private readonly IUsageRepository _repository;

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

    [ObservableProperty]
    private string _sessionToken = string.Empty;

    [ObservableProperty]
    private int _syncIntervalMinutes = 60;

    [ObservableProperty]
    private int _notificationIntervalMinutes = 60;

    [ObservableProperty]
    private string _testResult = string.Empty;

    [ObservableProperty]
    private bool _testSuccess;

    [ObservableProperty]
    private bool _isTesting;

    /// <summary>间隔选项（分钟）。</summary>
    public int[] IntervalOptions { get; } = { 30, 60, 180, 300 };

    /// <summary>测试 API 密钥是否可用。</summary>
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

    /// <summary>保存设置（间隔变化立即生效）。</summary>
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

    /// <summary>立即触发一次拉取。</summary>
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

    /// <summary>清空所有历史用量数据（用户手动触发，不可撤销）。</summary>
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

    /// <summary>属性变化时同步运行时选项并刷新命令可执行性。</summary>
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

    /// <summary>启动时从 secrets.dat 加载加密 token。</summary>
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

    /// <summary>加密保存 token 到 secrets.dat。</summary>
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
    public static void FireAndForget(this Task task)
    {
        _ = task.ConfigureAwait(false);
    }
}
