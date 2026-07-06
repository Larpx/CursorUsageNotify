using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Messaging;
using CursorUsageNotify.GUI.ViewModels;
using CursorUsageNotify.Services.Messages;

namespace CursorUsageNotify.GUI.Views;

/// <summary>
/// 主窗口：顶部数据大屏 + 下方 TabControl（查询/设置）。
/// 关闭按钮最小化到托盘，仅托盘"退出"才真正关闭进程。
/// </summary>
public partial class MainWindow : Window
{
    private bool _forceClose;
    private readonly QueryViewModel _queryViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IMessenger _messenger;

    /// <summary>构造函数由 DI 容器调用，注入三个 ViewModel。</summary>
    public MainWindow(
        MainViewModel mainViewModel,
        QueryViewModel queryViewModel,
        SettingsViewModel settingsViewModel,
        IMessenger messenger)
    {
        InitializeComponent();

        DataContext = mainViewModel;
        _queryViewModel = queryViewModel;
        _settingsViewModel = settingsViewModel;
        _messenger = messenger;

        // ContentControl 内容由 ViewLocator 根据 VM 类型自动渲染对应 View
        QueryTabContent.DataContext = _queryViewModel;
        SettingsTabContent.DataContext = _settingsViewModel;

        // 供 QueryViewModel 的 SaveFileDialog 使用
        QueryViewModel.MainWindowInstance = this;

        // Cookie 预警条"立即更新"按钮请求切换到设置 Tab
        _messenger.Register<SwitchToSettingsTabMessage>(this, (_, _) => SwitchToTab(1));
    }

    /// <summary>由托盘"退出"菜单调用，跳过最小化逻辑直接关闭。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    /// <summary>切换到指定 Tab（0=查询，1=设置）。</summary>
    public void SwitchToTab(int index)
    {
        if (MainTabs.Items.Count > index)
        {
            MainTabs.SelectedIndex = index;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_forceClose)
        {
            // 用户点关闭按钮 → 最小化到托盘而非退出
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
