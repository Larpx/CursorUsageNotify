using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Messaging;
using CursorUsageNotify.GUI.Views;
using CursorUsageNotify.Services.Messages;
using CursorUsageNotify.Services.Scheduling;
using Microsoft.Extensions.Logging;

namespace CursorUsageNotify.GUI.Tray;

/// <summary>
/// 托盘图标宿主：Avalonia 12 TrayIcon + 右键菜单（开始/暂停、设置、查询、退出）。
/// 通过 IMessenger 与后台 HostedService 通信，通过 MainWindow 控制窗口显隐。
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly IMessenger _messenger;
    private readonly UsageSyncOptions _options;
    private readonly ILogger<TrayIconHost> _logger;
    private readonly TrayIcon _trayIcon;
    private readonly Func<MainWindow> _mainWindowFactory;
    private readonly Action _quitAction;

    /// <summary>
    /// 构造托盘宿主。
    /// </summary>
    /// <param name="messenger">消息总线。</param>
    /// <param name="options">运行时同步选项。</param>
    /// <param name="mainWindowFactory">主窗口工厂（延迟解析，避免循环依赖）。</param>
    /// <param name="quitAction">退出动作（由 App 提供，调用 desktop.Shutdown）。</param>
    /// <param name="logger">日志。</param>
    public TrayIconHost(
        IMessenger messenger,
        UsageSyncOptions options,
        Func<MainWindow> mainWindowFactory,
        Action quitAction,
        ILogger<TrayIconHost> logger)
    {
        _messenger = messenger;
        _options = options;
        _mainWindowFactory = mainWindowFactory;
        _quitAction = quitAction;
        _logger = logger;

        _trayIcon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "Cursor 用量通知",
            Menu = BuildMenu(),
            IsVisible = true
        };

        _trayIcon.Clicked += OnTrayIconClicked;
        _logger.LogInformation("托盘图标已初始化");
    }

    private WindowIcon LoadIcon()
    {
        var asset = AssetLoader.Open(new Uri("avares://CursorUsageNotify.GUI/Assets/app.ico"));
        return new WindowIcon(asset);
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        var toggle = new NativeMenuItem { Header = "开始/暂停" };
        toggle.Click += OnToggleSync;
        menu.Items.Add(toggle);

        var settings = new NativeMenuItem { Header = "设置" };
        settings.Click += (_, _) => ShowMainWindow(tabIndex: 1);
        menu.Items.Add(settings);

        var query = new NativeMenuItem { Header = "查询" };
        query.Click += (_, _) => ShowMainWindow(tabIndex: 0);
        menu.Items.Add(query);

        menu.Items.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem { Header = "退出" };
        quit.Click += OnQuit;
        menu.Items.Add(quit);

        return menu;
    }

    private void OnToggleSync(object? sender, EventArgs e)
    {
        _options.IsRunning = !_options.IsRunning;
        _messenger.Send(new ToggleSyncMessage(_options.IsRunning));
        _logger.LogInformation("同步已 {State}", _options.IsRunning ? "启动" : "暂停");
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowMainWindow(tabIndex: 0);
    }

    private void ShowMainWindow(int tabIndex)
    {
        var window = _mainWindowFactory();
        window.SwitchToTab(tabIndex);
        if (!window.IsVisible)
        {
            window.Show();
        }
        window.Activate();
        window.WindowState = WindowState.Normal;
    }

    private void OnQuit(object? sender, EventArgs e)
    {
        _logger.LogInformation("用户从托盘菜单退出");
        _quitAction();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _trayIcon.Clicked -= OnTrayIconClicked;
        _trayIcon.Dispose();
    }
}
