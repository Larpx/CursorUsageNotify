using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CursorUsageNotify.GUI.Tray;
using CursorUsageNotify.GUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CursorUsageNotify.GUI;

/// <summary>
/// 应用入口：负责在框架初始化完成后从 DI 容器解析 MainWindow 与 TrayIconHost，
/// 并管理 Generic Host 生命周期。
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _services;
    private IHost? _host;
    private TrayIconHost? _trayHost;

    /// <summary>由 Program.cs 在启动前注入的 DI 容器。</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>由 Program.cs 在启动前注入的 Host 实例。</summary>
    public static IHost HostInstance { get; private set; } = default!;

    /// <summary>
    /// 由 Program.cs 调用，注入 DI 容器与 Host。
    /// 必须在 Avalonia 启动前调用。
    /// </summary>
    public static void Configure(IServiceProvider services, IHost host)
    {
        Services = services;
        HostInstance = host;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = Services;
            _host = HostInstance;

            // 从 DI 解析主窗口（构造函数注入三个 ViewModel）
            var mainWindow = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            // 创建托盘（传入退出动作）
            _trayHost = _services.GetRequiredService<TrayIconHost>();

            // 应用退出时清理资源
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        try
        {
            _trayHost?.Dispose();
            _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host?.Dispose();
        }
        catch
        {
            // 退出阶段忽略清理异常，避免阻塞关闭
        }
    }
}
