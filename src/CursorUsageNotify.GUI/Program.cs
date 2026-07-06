using System;
using System.Net;
using System.Net.Http;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Messaging;
using CursorUsageNotify.Core.Configuration;
using CursorUsageNotify.GUI.Tray;
using CursorUsageNotify.GUI.ViewModels;
using CursorUsageNotify.GUI.Views;
using CursorUsageNotify.Services.Export;
using CursorUsageNotify.Services.Http;
using CursorUsageNotify.Services.Notifications;
using CursorUsageNotify.Services.Scheduling;
using CursorUsageNotify.Services.Security;
using CursorUsageNotify.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Serilog;

namespace CursorUsageNotify.GUI;

/// <summary>
/// 程序入口：构建 Generic Host + 启动 Avalonia。
/// Host 必须在 Avalonia 启动前 Start，确保后台 HostedService 与 UI 同步运行。
/// </summary>
internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = BuildHost(args);

        // 建表（在 Host 启动前确保数据库 schema 就绪）
        try
        {
            host.Services.GetRequiredService<IDbContext>().InitializeSchema();
        }
        catch (Exception ex)
        {
            Log.Logger.Fatal(ex, "数据库初始化失败，程序退出");
            throw;
        }

        host.Start();

        // 注入 DI 容器到 App（必须在 Avalonia 初始化前）
        App.Configure(host.Services, host);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            host.Dispose();
            Log.CloseAndFlush();
        }
    }

    /// <summary>Avalonia 配置（视觉设计器也会调用）。</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // 显式加载 appsettings.json（Host.CreateApplicationBuilder 默认会加载，这里确保可选模式）
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        // 配置 Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateLogger();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        // 绑定 AppSettings
        var appSettings = new AppSettings();
        builder.Configuration.GetSection("App").Bind(appSettings);
        EnsureAppDataDir(appSettings);
        builder.Services.AddSingleton(appSettings);

        // 消息总线
        builder.Services.AddSingleton<IMessenger, WeakReferenceMessenger>();

        // 数据库
        builder.Services.AddSingleton<IDbContext, DbContext>();
        builder.Services.AddSingleton<IUsageRepository, UsageRepository>();

        // HttpClient + Polly 重试
        builder.Services.AddHttpClient<CursorApiClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            client.BaseAddress = new Uri(settings.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds);
        }).AddPolicyHandler(GetRetryPolicy());

        builder.Services.AddSingleton<ICursorApiClient>(sp => sp.GetRequiredService<CursorApiClient>());

        // 通知
        builder.Services.AddSingleton<INotificationService, WindowsToastService>();

        // 运行时选项
        builder.Services.AddSingleton<UsageSyncOptions>();

        // 安全
        builder.Services.AddSingleton<TokenProtector>();

        // 后台同步服务
        builder.Services.AddHostedService<UsageSyncHostedService>();

        // ViewModel
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<QueryViewModel>();

        // CSV 导出实现
        builder.Services.AddSingleton<CursorUsageNotify.Services.Export.ICsvExporter, CursorUsageNotify.Services.Export.CsvExporter>();

        // MainWindow
        builder.Services.AddSingleton<MainWindow>();

        // TrayIconHost（工厂模式，quitAction 延迟绑定 ApplicationLifetime）
        builder.Services.AddSingleton<TrayIconHost>(sp => new TrayIconHost(
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<UsageSyncOptions>(),
            sp.GetRequiredService<MainWindow>,
            QuitApplication,
            sp.GetRequiredService<ILogger<TrayIconHost>>()));


        return builder.Build();
    }

    /// <summary>退出应用：调用 desktop.Shutdown 触发正常关闭流程。</summary>
    private static void QuitApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode >= HttpStatusCode.InternalServerError)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    /// <summary>确保 LocalAppData 目录存在（数据库、密钥、日志文件存放处）。</summary>
    private static void EnsureAppDataDir(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(settings.DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
