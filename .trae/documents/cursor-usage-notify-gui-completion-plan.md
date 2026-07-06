# CursorUsageNotify GUI 层完成 + Tests + 构建验证计划

## 摘要

本计划聚焦完成项目的剩余工作:GUI 层 Views/Tray/App/Program/Converters/appsettings、Tests 层 3 个测试类、以及最终的构建验证。Core/Models/Services 三层和 GUI 的 3 个 ViewModel(MainViewModel、SettingsViewModel、QueryViewModel)已实现并通过编译,本计划不重复造轮子,只补齐缺失的 UI 与启动链路。

## 当前状态分析

### 已完成(无需改动)
- **Core**:`Constants.cs`、`AppSettings.cs`、`Result.cs`
- **Models**:3 个实体(UsageEventEntity/PeriodUsageEntity/UserInfoEntity)、2 个 DTO、CursorApiResponse
- **Services**:Http/Storage/Notifications/Scheduling/Export/Security/Messages 全部实现,Services 层已构建通过 0 错误 0 警告
- **GUI ViewModels**:`MainViewModel.cs`(订阅 UsageDataFetchedMessage/SyncFailedMessage,异步 LoadAsync)、`SettingsViewModel.cs`(TestConnectionAsync/SaveSettings/SyncNow,DPAPI 加密 token)、`QueryViewModel.cs`(LoadAsync/LoadModelsAsync/ExportCsvAsync,使用静态 MainWindowInstance)
- **配置**:`Directory.Build.props`(LangVersion=latest, Nullable=enable, TreatWarningsAsErrors=true)、`Directory.Packages.props`(CPM,Avalonia 12.0.5 等)、`app.manifest`(已存在)
- **资源**:`Assets/app.ico` 已就位

### 待补齐(本计划范围)
1. 模板残留文件需清理:`ViewModels/MainWindowViewModel.cs`(模板代码,与 MainViewModel 重复)、`Views/MainWindow.axaml`/`.axaml.cs`(模板代码)、`App.axaml`/`.axaml.cs`(模板代码)、`Program.cs`(模板代码)
2. 缺失文件:`Converters/`(2 个)、`Views/SettingsView.axaml`、`Views/QueryView.axaml`、`Tray/TrayIconHost.cs`、`appsettings.json`
3. Tests 层完全未实现

### 关键依赖确认(基于已读源码)
- `QueryViewModel.MainWindowInstance` 是静态 `Window?` 属性,需在 `MainWindow.axaml.cs` 构造时赋值
- `MainViewModel` 构造函数 `_ = LoadAsync()` 会触发 DB 查询,DI 必须先就绪
- `SettingsViewModel` 用 `[property: PasswordPropertyText]` 是 WinForms attribute,Avalonia 不识别但不报错,XAML 中用 `TextBox.PasswordChar="●"` 实现密码框
- `UsageSyncHostedService` 通过 `IMessenger` 订阅 `ToggleSyncMessage`/`TriggerSyncNowMessage`,托盘菜单需向同一 Messenger 发消息
- `AppSettings` 默认值已包含 `%LOCALAPPDATA%/CursorUsageNotify/` 路径,appsettings.json 主要覆盖 Serilog 等运行时配置
- Avalonia 12 `TrayIcon` 在 `Avalonia.Controls` 命名空间,代码方式创建更灵活
- csproj 已引用 `Avalonia.Controls.DataGrid`,但需在 App.axaml 引入 DataGrid 主题

## 实施步骤

### 步骤 1:清理模板残留

**文件**:`src/CursorUsageNotify.GUI/ViewModels/MainWindowViewModel.cs`
**动作**:删除
**原因**:模板残留,与已实现的 `MainViewModel` 功能重复。ViewLocator 的 `Match` 判断 `ViewModelBase`,删除不影响。

### 步骤 2:创建值转换器

#### 2.1 `src/CursorUsageNotify.GUI/Converters/CentsToCurrencyConverter.cs`
- 实现 `IValueConverter`
- `Convert`:decimal 美分 → `$X.XX`(InvariantCulture,2 位小数)
- `ConvertBack`:抛 `NotSupportedException`
- 用途:大屏 TotalSpendDollars 显示、DataGrid TotalCents 列

#### 2.2 `src/CursorUsageNotify.GUI/Converters/EpochToDateTimeConverter.cs`
- 实现 `IValueConverter`
- `Convert`:long epoch 毫秒 → `yyyy-MM-dd HH:mm:ss`(本地时区)
- `ConvertBack`:抛 `NotSupportedException`
- 用途:DataGrid Timestamp 列、大屏 LastFetchTime

### 步骤 3:重写 MainWindow

#### 3.1 `src/CursorUsageNotify.GUI/Views/MainWindow.axaml`
- 顶部数据大屏(深色卡片背景,Grid 4 列):
  - 用户信息区:UserEmail、PlanName、PeriodRange
  - 用量区:UsedTokens(N0 格式)、UsedRequests、RemainingRequests
  - 支出区:TotalSpendDollars(用 CentsToCurrencyConverter)、LastFetchTime
  - 状态区:StatusColor 圆点 + StatusText
- 底部 `TabControl`(2 个 Tab):
  - Tab "查询":ContentControl 绑定 QueryViewModel(ViewLocator 自动渲染 QueryView)
  - Tab "设置":ContentControl 绑定 SettingsViewModel(ViewLocator 自动渲染 SettingsView)
- 顶层 DataContext 绑定 MainViewModel
- 引入转换器命名空间 `xmlns:conv="using:CursorUsageNotify.GUI.Converters"`
- `x:DataType="vm:MainViewModel"` 启用编译绑定
- 窗口尺寸 900x650,Title="Cursor 用量通知",Icon=/Assets/app.ico

#### 3.2 `src/CursorUsageNotify.GUI/Views/MainWindow.axaml.cs`
- 构造函数接收 `MainViewModel`、`QueryViewModel`、`SettingsViewModel` 三个 VM(DI 注入)
- `InitializeComponents()` 后设置:
  - 顶层 DataContext = mainViewModel
  - 查询 Tab 的 ContentControl DataContext = queryViewModel
  - 设置 Tab 的 ContentControl DataContext = settingsViewModel
- 设置 `QueryViewModel.MainWindowInstance = this`(供 SaveFileDialog 使用)
- 重写 `OnClosing`:取消关闭、`Hide()` 最小化到托盘(由 App 控制 _forceClose 标志位决定是否真正关闭)
- 暴露 `ForceClose()` 方法供 App 退出时调用

### 步骤 4:创建两个 Tab View

#### 4.1 `src/CursorUsageNotify.GUI/Views/SettingsView.axaml`
- `x:DataType="vm:SettingsViewModel"`
- 布局:StackPanel,间距 16,Padding 24
- Session Token:TextBox PasswordChar=●,Watermark="粘贴 WorkosCursorSessionToken"
- 「测试连接」按钮,Command={Binding TestConnectionCommand},IsBusy 时禁用
- 测试结果 TextBlock:前景色根据 TestSuccess 动态切换(绿/红)
- 「数据拉取间隔」ComboBox,ItemsSource={Binding IntervalOptions},SelectedItem={Binding SyncIntervalMinutes}
- 「通知间隔」ComboBox,同上
- 「保存设置」按钮,Command={Binding SaveSettingsCommand}
- 「立即拉取一次」按钮,Command={Binding SyncNowCommand}
- 「清空历史数据」按钮(待实现:后续可加 ConfirmDialog,本步骤先放按钮+命令占位,实际清空逻辑由 SettingsViewModel 添加 ClearDataCommand)

注意:清空数据按钮本次计划暂不实现 ViewModel 命令(避免越界),XAML 中先注释或绑定到 SyncNowCommand 占位,在最终验证阶段补上。实际策略:在 SettingsViewModel 添加 `[RelayCommand] ClearData()` 调用 `_repository.ClearAllAsync()`,但这需要额外注入 IUsageRepository — **决策:本步骤同时给 SettingsViewModel 增加 IUsageRepository 注入和 ClearDataCommand**,因为这是用户原始需求("清空历史数据"在计划文件第 161 行明确要求)。

#### 4.2 `src/CursorUsageNotify.GUI/Views/SettingsView.axaml.cs`
- 仅 `InitializeComponent()`,无额外逻辑

#### 4.3 `src/CursorUsageNotify.GUI/Views/QueryView.axaml`
- `x:DataType="vm:QueryViewModel"`
- 布局:DockPanel
- 顶部筛选区(Grid 4 列 + 导出按钮):
  - 起始时间 DatePicker,SelectedDate={Binding StartTime}
  - 结束时间 DatePicker,SelectedDate={Binding EndTime}
  - 模型 ComboBox,ItemsSource={Binding Models},SelectedItem={Binding SelectedModel}
  - 「查询」按钮,Command={Binding LoadCommand}
  - 「导出 CSV」按钮,Command={Binding ExportCsvCommand}
- 状态栏:TextBlock Text={Binding StatusText}
- DataGrid:
  - ItemsSource={Binding Events}
  - AutoGenerateColumns=False
  - 列:时间(EpochToDateTimeConverter 转换 Timestamp)、模型、输入 token、输出 token、缓存读、缓存写、费用(CentsToCurrencyConverter 转换 TotalCents)、是否 max、是否 headless
  - IsReadOnly=True
  - GridLines=Horizontal

#### 4.4 `src/CursorUsageNotify.GUI/Views/QueryView.axaml.cs`
- 仅 `InitializeComponent()`

### 步骤 5:补全 SettingsViewModel 的 ClearData 命令

**文件**:`src/CursorUsageNotify.GUI/ViewModels/SettingsViewModel.cs`
**动作**:Edit
- 构造函数追加注入 `IUsageRepository repository`(已有 4 个依赖,变 5 个)
- 新增字段 `_repository`
- 新增 `[RelayCommand] private async Task ClearDataAsync()`:调用 `_repository.ClearAllAsync()`,TestResult = "已清空所有历史数据"

### 步骤 6:创建托盘

**文件**:`src/CursorUsageNotify.GUI/Tray/TrayIconHost.cs`
- `sealed class TrayIconHost : IDisposable`
- 依赖注入:`IMessenger messenger`、`Func<MainWindow> mainWindowFactory`、`ILogger<TrayIconHost> logger`
- 构造时创建 `TrayIcon`:
  - Icon:从 `avares://CursorUsageNotify.GUI/Assets/app.ico` 加载 `WindowIcon`
  - ToolTipText:"Cursor 用量通知"
  - Menu(NativeMenu):
    - "开始/暂停" → 发送 `ToggleSyncMessage(!isRunning)`(需跟踪状态,本步骤简化为始终切换)
    - "设置" → 显示主窗口并切换到设置 Tab(发送消息或直接调用 MainWindow)
    - "查询" → 显示主窗口并切换到查询 Tab
    - Separator
    - "退出" → 调用 `Environment.Exit(0)` 或通过回调通知 App 关闭
- `TrayIcon.Clicked` 事件:显示/隐藏主窗口
- `Dispose()`:销毁 TrayIcon
- 暴露 `IsRunning` 属性跟踪开始/暂停状态(用于菜单文字切换;本步骤简化为始终发 Toggle,不切换文字)
- **决策**:为简化,菜单"开始/暂停"统一发送 `ToggleSyncMessage(true)`(开始)或通过 `UsageSyncOptions.IsRunning` 当前值决定发什么;在 TrayIconHost 注入 `UsageSyncOptions` 读取当前状态

### 步骤 7:重写应用启动

#### 7.1 `src/CursorUsageNotify.GUI/App.axaml`
- 保留 `<FluentTheme />`
- 新增 `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"/>`(DataGrid 主题必需)
- 保留 `ViewLocator` DataTemplates
- RequestedThemeVariant=Light(用户规则要求背景 #fff 或 #f8f9fa,Light 更贴合)

#### 7.2 `src/CursorUsageNotify.GUI/App.axaml.cs`
- 持有 `IServiceProvider _services`、`IHost _host`、`TrayIconHost _trayHost`
- `Initialize()`:AvaloniaXamlLoader.Load(this)
- `OnFrameworkInitializationCompleted()`:
  - 检查 `ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop`
  - 从 DI 解析 `MainWindow`(用工厂方式,因为 MainWindow 需要构造参数)
  - 设置 `desktop.MainWindow = mainWindow`
  - 订阅 `desktop.ShutdownRequested` → 停止 Host、Dispose TrayIconHost
  - 不再创建 `MainWindowViewModel`(已删)
- 暴露静态 `App Instance` 属性供 TrayIconHost 等访问(可选)
- **关闭主窗口=最小化**:由 MainWindow.OnClosing 内部处理(App 不干预);真正退出由托盘"退出"菜单调用 `desktop.Shutdown()`

#### 7.3 `src/CursorUsageNotify.GUI/Program.cs`
- `[STAThread] Main`:
  - 构建 Generic Host:注册 AppSettings、IDbContext、IUsageRepository、ICursorApiClient(用 IHttpClientFactory + Polly)、INotificationService、UsageSyncOptions、TokenProtector、IMessenger(WeakReferenceMessenger 单例)、3 个 ViewModel、MainWindow、TrayIconHost
  - 配置 appsettings.json 加载
  - 配置 Serilog(Read from config)
  - `host.Start()`
  - `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args)`
  - finally:`host.StopAsync().Wait()`、`host.Dispose()`
- 注意:Avalonia 12 的 `WithDeveloperTools()` 已被 `AvaloniaUI.DiagnosticsSupport` 替代,DEBUG 模式下用 `.UseDetectives()`(检查 API;若不可用,保留原 `.WithDeveloperTools()`)

### 步骤 8:创建配置文件

**文件**:`src/CursorUsageNotify.GUI/appsettings.json`
- csproj 需追加 `<None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`
- 内容:
```json
{
  "App": {
    "ApiBaseUrl": "https://cursor.com",
    "SyncIntervalMinutes": 60,
    "NotificationIntervalMinutes": 60,
    "UsageEventsPageSize": 500,
    "HttpTimeoutSeconds": 30
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "File", "Args": { "path": "%LOCALAPPDATA%/CursorUsageNotify/logs/app.log", "rollingInterval": "Day" } }
    ]
  }
}
```
注意:`%LOCALAPPDATA%` 在 Serilog 里不支持环境变量展开,改用相对路径 `logs/app.log` 或在 Program.cs 中手动拼接绝对路径后写入配置。**决策**:appsettings.json 中 Serilog path 用 `logs/app-.log`(相对路径,Serilog 会基于 AppContext.BaseDirectory),AppSettings.LogFilePath 仍是绝对路径供代码使用。

### 步骤 9:Tests 层

#### 9.1 `src/CursorUsageNotify.Tests/Services/CursorApiClientTests.cs`
- 用 NSubstitute mock `HttpMessageHandler`
- 测试用例:
  - `TestConnectionAsync_ValidToken_ReturnsOkWithCount`
  - `TestConnectionAsync_401_ReturnsFailWithAuthMessage`
  - `GetFilteredUsageEventsAsync_ValidResponse_ReturnsEvents`
  - `GetCurrentPeriodUsageAsync_ValidResponse_ReturnsDto`
- 命名遵循 `Method_Scenario_ExpectedResult`

#### 9.2 `src/CursorUsageNotify.Tests/Services/UsageRepositoryTests.cs`
- 用 SQLite in-memory(`DataSource=:memory:`)构造 DbContext
- 测试用例:
  - `UpsertUsageEventsAsync_NewEvents_InsertsAll`
  - `UpsertUsageEventsAsync_DuplicateTimestampUpsert_DoesNotInsertDuplicate`
  - `QueryEventsAsync_WithTimeRange_FiltersCorrectly`
  - `GetDistinctModelsAsync_ReturnsUniqueModels`
  - `ClearAllAsync_RemovesAllRows`

#### 9.3 `src/CursorUsageNotify.Tests/Services/CsvExporterTests.cs`
- 测试用例:
  - `ExportAsync_NormalEvents_WritesCsvWithHeaders`
  - `ExportAsync_EmptyEvents_WritesOnlyHeader`
  - `ExportAsync_CreatesDirectoryIfMissing`

### 步骤 10:构建验证 + 修复

```powershell
dotnet build CursorUsageNotify.sln -c Release
# 要求:0 错误,0 警告
```
- 修复编译错误(预计风险点:Avalonia 12 TrayIcon API、DataGrid 列定义、CompiledBindings 类型推断)
- 运行 `dotnet test` 验证 Tests 通过

## 关键设计决策

1. **TrayIcon 用代码方式创建**(TrayIconHost.cs),而非 App.axaml 静态声明 — 便于 DI 注入 IMessenger/UsageSyncOptions,菜单命令可发消息
2. **关闭主窗口=最小化到托盘**:在 MainWindow.OnClosing 中 `e.Cancel = true; Hide()`,托盘"退出"调 `desktop.Shutdown()` 走正常关闭流程
3. **Tab 内容用 ContentControl + ViewLocator**:无需在 MainWindow.axaml 内联两个 View 的 XAML,保持 View 独立可复用
4. **SettingsViewModel 追加 IUsageRepository 注入**:支持"清空历史数据"按钮(用户原始需求)
5. **Serilog 路径用相对**:appsettings.json 中 `logs/app-.log`,避免环境变量展开问题;代码中 AppSettings.LogFilePath 仍是绝对路径供其他用途
6. **DataGrid 主题必须显式引入**:`<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"/>`,否则 DataGrid 无样式
7. **Tests 不引用 GUI**:GUI 是 WinExe 不便测试,Tests 聚焦 Services 层逻辑

## 验证步骤

1. `dotnet build CursorUsageNotify.sln -c Release` — 0 错误 0 警告
2. `dotnet test src/CursorUsageNotify.Tests/CursorUsageNotify.Tests.csproj` — 全部通过
3. 手动启动 GUI(用户自行验证):
   - 托盘图标出现
   - 右键菜单 4 项可见
   - 主窗口顶部数据大屏布局正常
   - 设置 Tab:输入 token → 测试按钮可用
   - 查询 Tab:DataGrid 表头正确、筛选控件可交互
   - 关闭主窗口 → 最小化到托盘
   - 托盘"退出" → 进程退出

## 风险与缓解

- **Avalonia 12 TrayIcon API 变化**:若 `new TrayIcon()` 不可用,改用 `TrayIcon.Icons` 静态属性 + App.axaml 声明
- **CompiledBindings 类型推断失败**:`x:DataType` 必须精确匹配,ContentControl 需用 ` DataContext` 显式绑定 VM 实例(而非 Type)
- **Avalonia 12 DataGrid 列 API**:用 `DataGridTextColumn` 而非 `DataGridBoundColumn`,Binding 写法与 WPF 略有差异
- **Generic Host 与 Avalonia 生命周期冲突**:Host 必须在 Avalonia `StartWithClassicDesktopLifetime` 之前 Start,在 `desktop.ShutdownRequested` 中 Stop
- **DPAPI 在非 Windows 测试环境失败**:Tests 项目 TFM 是 net10.0-windows10.0.18362.0,TokenProtector 测试不在本计划范围(避免 CI 环境 DPAPI 不可用)
