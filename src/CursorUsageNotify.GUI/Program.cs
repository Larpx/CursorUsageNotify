using System;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.GUI.Tray;
using Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels;
using Larpx.PersonalTools.CursorUsageNotify.GUI.Views;
using Larpx.PersonalTools.CursorUsageNotify.Services.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Services.Export;
using Larpx.PersonalTools.CursorUsageNotify.Services.Http;
using Larpx.PersonalTools.CursorUsageNotify.Services.Notifications;
using Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling;
using Larpx.PersonalTools.CursorUsageNotify.Services.Security;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Serilog.Settings.Configuration;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI
{
    /// <summary>
    /// 程序入口：构建 Generic Host + 启动 Avalonia。
    /// Host 必须在 Avalonia 启动前 Start，确保后台 HostedService 与 UI 同步运行。
    /// 全程由全局异常处理保护，避免任何线程未捕获异常导致进程闪退。
    /// </summary>
    internal sealed class Program
    {
        private const uint MbIconError = 0x10;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        /// <summary>
        /// 程序主入口：构建 Host、初始化数据库、启动 Avalonia 桌面生命周期。
        /// 所有阶段由全局异常处理保护，崩溃时显示对话框提示用户而非闪退。
        /// </summary>
        /// <param name="args">
        /// 命令行参数。
        /// </param>
        [STAThread]
        public static void Main(string[] args)
        {
            // 启动最早期初始化 fallback logger，确保全局异常处理器有日志可写。
            // 必须在订阅异常事件之前完成，否则异常触发时 Log.Logger 仍为 null。
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File("logs/crash-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
                .CreateLogger();

            // 全局异常兜底：AppDomain 未处理异常 + 未观察 Task 异常。
            // 任何线程未捕获的异常都记录日志，避免静默丢失导致问题难以诊断。
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // 单实例互斥锁：防止多实例同时写 secrets.dat / data.db 导致损坏。
            // 使用 Local 前缀避免跨会话权限问题，单用户桌面场景足够。
            using var singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\CursorUsageNotify_SingleInstance",
                out bool createdNew);
            if (!createdNew)
            {
                ShowCrashDialog("程序已在运行", new Exception("Cursor用量统计 已在运行，请勿重复启动。"));
                return;
            }

            IHost? host = null;
            try
            {
                host = BuildHost(args);

                // 将持久化用户偏好同步到运行时状态（UsageSyncOptions + GUI 转换器）
                var userPrefs = host.Services.GetRequiredService<UserPreferences>();
                var syncOptions = host.Services.GetRequiredService<UsageSyncOptions>();
                syncOptions.TokenDisplayMode = userPrefs.TokenDisplayMode;
                Larpx.PersonalTools.CursorUsageNotify.GUI.Converters.TokenFormatConverter.Mode = userPrefs.TokenDisplayMode;

                // 建表（在 Host 启动前确保数据库 schema 就绪）
                try
                {
                    host.Services.GetRequiredService<IDbContext>().InitializeSchema();
                }
                catch (Exception ex)
                {
                    Log.Logger.Fatal(ex, "数据库初始化失败，程序退出");
                    ShowCrashDialog("数据库初始化失败", ex);
                    return;
                }

                host.Start();

                // 注入 DI 容器到 App（必须在 Avalonia 初始化前）
                App.Configure(host.Services, host);

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // 启动/运行期未预期异常：记录日志并提示用户，避免控制台窗口闪退无任何线索
                Log.Logger.Fatal(ex, "程序发生致命异常");
                ShowCrashDialog("程序运行失败", ex);
            }
            finally
            {
                // 优雅关闭 Host，确保后台服务有机会刷新数据
                if (host is not null)
                {
                    try
                    {
                        host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(ex, "Host 关闭过程异常（已忽略）");
                    }
                    try
                    {
                        host.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(ex, "Host 释放过程异常（已忽略）");
                    }
                }
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Avalonia 配置（视觉设计器也会调用）。
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// AppDomain 未处理异常兜底：记录致命日志后由运行时决定是否终止进程。
        /// .NET Core/5+ 中此事件触发通常意味着进程即将终止，无法阻止。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="e">未处理异常参数。</param>
        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Logger.Fatal(ex, "AppDomain 未处理异常（IsTerminating={IsTerminating}）", e.IsTerminating);
            }
            else
            {
                Log.Logger.Fatal("AppDomain 未处理异常：{ExceptionObject}", e.ExceptionObject);
            }
            Log.CloseAndFlush();
        }

        /// <summary>
        /// 未观察 Task 异常兜底：标记已观察防止进程崩溃，记录错误日志便于诊断。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="e">未观察 Task 异常参数。</param>
        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Logger.Error(e.Exception, "未观察的 Task 异常（已吸收，防止进程崩溃）");
            e.SetObserved();
        }

        /// <summary>
        /// 显示崩溃对话框：通过 Win32 MessageBox 提示用户，避免依赖 Avalonia（崩溃时 UI 可能不可用）。
        /// 对话框自身失败时静默吞掉，避免二次异常。
        /// </summary>
        /// <param name="title">对话框标题前缀。</param>
        /// <param name="ex">触崩溃的异常。</param>
        private static void ShowCrashDialog(string title, Exception ex)
        {
            try
            {
                var message = $"{title}\n\n" +
                              $"错误类型：{ex.GetType().Name}\n" +
                              $"错误信息：{ex.Message}\n\n" +
                              $"详细信息请查看 logs 目录下的日志文件。";
                MessageBox(IntPtr.Zero, message, "Cursor用量统计", MbIconError);
            }
            catch
            {
                // 对话框显示失败时忽略，避免崩溃处理本身再次抛出
            }
        }

        private static IHost BuildHost(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            // 显式加载 appsettings.json（Host.CreateApplicationBuilder 默认会加载，这里确保可选模式）
            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            // 配置 Serilog（覆盖 fallback logger，使用 appsettings.json 中的完整配置）。
            // SingleFile 发布后 Serilog.Settings.Configuration 无法反射扫描程序集查找 sink，
            // 必须通过 ConfigurationReaderOptions 显式声明 sink 所在程序集，否则启动时抛
            // InvalidOperationException: No Serilog:Using configuration section is defined。
            var serilogReaderOptions = new ConfigurationReaderOptions(
                typeof(Serilog.ConsoleLoggerConfigurationExtensions).Assembly,
                typeof(Serilog.FileLoggerConfigurationExtensions).Assembly);
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration, serilogReaderOptions)
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

            // 用户偏好（从 user-prefs.json 加载，重启保留）
            builder.Services.AddSingleton(UserPreferences.Load(appSettings.UserPrefsPath));

            // 安全
            builder.Services.AddSingleton<TokenProtector>();
            builder.Services.AddSingleton<SecureTokenHolder>();

            // 后台同步服务
            builder.Services.AddHostedService<UsageSyncHostedService>();
            // 启动端点探测：Host 启动后异步探测 7 个 Cursor 端点是否仍存在
            builder.Services.AddHostedService<EndpointProbeService>();

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

        /// <summary>
        /// 退出应用：调用 desktop.Shutdown 触发正常关闭流程。
        /// </summary>
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

        /// <summary>
        /// 确保 LocalAppData 目录存在（数据库、密钥、日志文件存放处）。
        /// </summary>
        private static void EnsureAppDataDir(AppSettings settings)
        {
            var dir = Path.GetDirectoryName(settings.DatabasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
