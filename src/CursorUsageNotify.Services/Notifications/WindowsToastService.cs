using CommunityToolkit.WinUI.Notifications;
using CursorUsageNotify.Core;
using Microsoft.Extensions.Logging;

namespace CursorUsageNotify.Services.Notifications;

/// <summary>
/// Windows 10/11 原生 Toast 通知实现，基于 CommunityToolkit.WinUI.Notifications。
/// </summary>
public sealed class WindowsToastService : INotificationService
{
    private readonly ILogger<WindowsToastService> _logger;

    public WindowsToastService(ILogger<WindowsToastService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task ShowAsync(string title, string body, CancellationToken ct = default)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument("source", Constants.ToastAppId)
                .AddText(title)
                .AddText(body)
                .Show();
            _logger.LogInformation("Toast 已显示：{Title}", title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toast 显示失败");
        }

        return Task.CompletedTask;
    }
}
