# CursorUsageNotify 实现计划

## Context（背景）

用户需要一个 Windows 桌面工具，定时拉取个人 Cursor 账号（Pro/Ultra/Plus）的用量和支出数据，在右下角弹出 Toast 通知，并提供数据大屏、设置、查询、CSV 导出等功能。

**关键约束**：Cursor 官方 Admin API 仅对 Enterprise teams 开放，个人账号无官方 API。本方案走 cursor.com 内部 dashboard 接口（非官方、依赖浏览器 session cookie，可能随 Cursor 改版失效），需要用户从浏览器 F12 复制 `WorkosCursorSessionToken` 作为"API 密钥"。

**已确认的内部端点**：
- `POST https://cursor.com/api/dashboard/get-filtered-usage-events` — 详细用量事件（model、tokenUsage、totalCents）
- `POST https://cursor.com/api/dashboard/get-current-period-usage` — 当前计费周期汇总（计划额度、已用）
- 认证：`Cookie: WorkosCursorSessionToken=YOUR_SESSION_TOKEN` + `Origin: https://cursor.com`
- 注意：URL 必须用 `https://cursor.com/...`，避免本地化 `/cn/...` 导致 400

## 技术栈

| 项目 | 选型 | 版本 |
|------|------|------|
| 框架 | .NET 10 | latest |
| UI | Avalonia | 12.x |
| MVVM | CommunityToolkit.Mvvm | 8.x（源生成器） |
| ORM | SqlSugarCore | 5.x（SQLite） |
| HttpClient | Microsoft.Extensions.Http | 10.x（Polly 重试） |
| 主机 | Microsoft.Extensions.Hosting | 10.x（DI + IHostedService 后台任务） |
| 日志 | Serilog + Serilog.Sinks.File/Console | 4.x |
| Toast | H.NotifyIcon.Win32 | 2.x（Win10/11 原生 Toast） |
| CSV | CsvHelper | 33.x |
| 测试 | xUnit + FluentAssertions | latest |

## 项目结构

按用户规则（`{Company}.{Product}.{Layer}` 命名，但为简化用 `CursorUsageNotify.{Layer}`）：

```
src/
├── CursorUsageNotify.Core/         # 常量、配置模型、错误类型
│   ├── Configuration/
│   │   └── AppSettings.cs
│   ├── Constants.cs
│   └── Result.cs                   # Result<T> 模式
├── CursorUsageNotify.Models/       # 数据模型（DB 实体 + DTO）
│   ├── Entities/
│   │   ├── UsageEventEntity.cs     # 用量事件（按 timestamp 去重）
│   │   ├── PeriodUsageEntity.cs    # 计费周期汇总（按 fetchTime 去重）
│   │   └── UserInfoEntity.cs       # 用户/订阅信息快照
│   ├── Dtos/
│   │   ├── CursorUsageEventDto.cs  # 对应 get-filtered-usage-events 响应
│   │   └── CursorPeriodUsageDto.cs # 对应 get-current-period-usage 响应
│   └── Api/
│       └── CursorApiResponse.cs    # 包裹 totalUsageEventsCount 等
├── CursorUsageNotify.Services/     # 业务服务
│   ├── Http/
│   │   ├── ICursorApiClient.cs
│   │   ├── CursorApiClient.cs      # 调用内部 dashboard 端点
│   │   └── CursorApiException.cs
│   ├── Storage/
│   │   ├── IDbContext.cs
│   │   ├── DbContext.cs            # SqlSugar 包装
│   │   ├── IUsageRepository.cs
│   │   └── UsageRepository.cs      # upsert by timestamp 去重
│   ├── Notifications/
│   │   ├── INotificationService.cs
│   │   └── WindowsToastService.cs  # H.NotifyIcon.Win32 实现
│   ├── Scheduling/
│   │   ├── UsageSyncHostedService.cs  # IHostedService + PeriodicTimer
│   │   └── UsageSyncOptions.cs
│   ├── Export/
│   │   ├── ICsvExporter.cs
│   │   └── CsvExporter.cs
│   └── Security/
│       └── TokenProtector.cs       # DPAPI 加密 session token
├── CursorUsageNotify.GUI/          # Avalonia 主程序
│   ├── App.axaml / App.axaml.cs
│   ├── Program.cs                  # Avalonia + Generic Host 启动
│   ├── ViewModels/
│   │   ├── MainViewModel.cs        # 数据大屏
│   │   ├── SettingsViewModel.cs    # API 密钥 + 测试 + 间隔
│   │   ├── QueryViewModel.cs       # 查询 + CSV 导出
│   │   └── ViewModelBase.cs
│   ├── Views/
│   │   ├── MainWindow.axaml        # 数据大屏 + TabControl
│   │   ├── SettingsView.axaml
│   │   └── QueryView.axaml
│   ├── Tray/
│   │   └── TrayIconHost.cs         # Avalonia TrayIcon + 右键菜单
│   ├── Messages/
│   │   ├── UsageDataFetchedMessage.cs  # IMessenger 广播
│   │   └── NotificationRequestedMessage.cs
│   ├── Converters/
│   │   ├── CentsToCurrencyConverter.cs
│   │   └── BytesToHumanConverter.cs
│   ├── Assets/
│   │   └── app.ico                 # 托盘图标
│   ├── appsettings.json
│   └── CursorUsageNotify.GUI.csproj
└── CursorUsageNotify.Tests/        # xUnit 单元测试
    ├── Services/
    │   ├── CursorApiClientTests.cs
    │   ├── UsageRepositoryTests.cs
    │   └── CsvExporterTests.cs
    └── CursorUsageNotify.Tests.csproj
```

## 关键设计决策

### 1. API 调用（Services/Http/CursorApiClient.cs）

- 使用 `IHttpClientFactory` + Polly（3 次指数退避重试，处理 5xx 和超时）
- 两个核心方法：
  - `GetFilteredUsageEventsAsync(int page, int pageSize, long startTimestamp, CancellationToken ct)` → 返回 `CursorApiResponse<List<CursorUsageEventDto>>`
  - `GetCurrentPeriodUsageAsync(CancellationToken ct)` → 返回 `CursorPeriodUsageDto`
- 请求头固定：`Cookie: WorkosCursorSessionToken={token}`、`Origin: https://cursor.com`、`Content-Type: application/json`、`User-Agent: Mozilla/5.0`
- 失败处理：401/403 → 抛 `CursorApiAuthException`（前端提示重新登录）；400 → 抛 `CursorApiBadRequestException`（提示 token 可能过期）；其他 → 通用 `CursorApiException`

### 2. 数据库去重（Services/Storage/UsageRepository.cs）

- SQLite 文件位置：`%LOCALAPPDATA%/CursorUsageNotify/data.db`（首次运行自动建库建表）
- `UsageEventEntity` 唯一键：`Timestamp`（事件时间，按毫秒）+ `UserEmail`
- `PeriodUsageEntity` 唯一键：`FetchTime`（按分钟截断到秒）
- 用 SqlSugar `Storageable` 实现 upsert（存在则更新，不存在则插入），防止定时任务重复插入脏数据
- 永久保留（用户选），提供"清空数据"按钮在设置 Tab

### 3. 定时任务（Services/Scheduling/UsageSyncHostedService.cs）

- `IHostedService` + `PeriodicTimer`，避免引入 Quartz 的复杂度
- 两个独立循环：
  - 数据拉取（默认 1 小时，用户可选 30min/1h/3h/5h）
  - 通知发送（默认 1 小时，独立间隔，避免碎片化）
- 拉取流程：
  1. 调 `GetFilteredUsageEventsAsync` 分页拉取最近一个周期（上次 fetch 到 now）的所有事件
  2. upsert 入库
  3. 调 `GetCurrentPeriodUsageAsync` 拉取当前计费周期汇总
  4. upsert 入库
  5. 通过 `IMessenger.Send(UsageDataFetchedMessage)` 广播给 ViewModel
- 通知流程：
  1. 计算本周期累计 token、本周期累计支出（cents → dollars）
  2. 调 `INotificationService.ShowAsync(title, body)` 弹 Toast
- 启动时立即跑一次（如果距上次拉取超过设定间隔）

### 4. 托盘 + 通知（GUI/Tray/TrayIconHost.cs + Services/Notifications/WindowsToastService.cs）

- 用 Avalonia 12 内置 `TrayIcon`：图标、双击显示主窗口、右键菜单（开始/暂停、设置、查询、退出）
- "开始/暂停"通过 `IMessenger` 发送 `ToggleSyncMessage` 到 HostedService
- "退出"调用 `IClassicDesktopStyleApplicationLifetime.Shutdown()`
- Toast 通知用 `H.NotifyIcon.Win32`：支持 Win10/11 原生 Toast，可带操作按钮（如"查看详情"打开主窗口）
- 通知服务抽象为 `INotificationService`，Windows 平台注册 `WindowsToastService`，跨平台时替换实现

### 5. 主界面布局（GUI/Views/MainWindow.axaml）

- **顶部数据大屏**（高度约 200px，深色背景卡片）：
  - 左侧：用户邮箱、订阅计划（Pro/Ultra/...）、订阅周期起止
  - 中间：本周期已用 token、本周期已用请求数、本周期已花费（美元）
  - 右侧：最近一次拉取时间、状态指示灯（绿=正常/红=token 失效/灰=未启动）
- **下方 TabControl**：
  - Tab 1「查询」：DataGrid 展示 `UsageEventEntity`（时间、模型、token 数、费用），支持时间范围筛选 + 模型筛选 + CSV 导出按钮
  - Tab 2「设置」：
    - API 密钥输入框（密码模式）+ "测试连接"按钮（调一次 `GetFilteredUsageEventsAsync(pageSize:1)`，成功显示绿色✓，失败显示红色错误信息）
    - 数据拉取间隔下拉（30分钟/1小时/3小时/5小时）
    - 通知间隔下拉（同上）
    - "立即拉取一次"按钮
    - "清空历史数据"按钮（带确认对话框）
- 样式遵循用户 UI 规则：CSS 变量、4px 网格、卡片二选一、6-8px 圆角、无渐变无 emoji

### 6. 配置与安全（Core/Configuration/AppSettings.cs + Services/Security/TokenProtector.cs）

- `appsettings.json`：默认间隔、API base URL、数据库路径、日志路径
- `WorkosCursorSessionToken` 用 DPAPI（`ProtectedData.Protect`）加密后存到 `%LOCALAPPDATA%/CursorUsageNotify/secrets.dat`，不入 appsettings.json、不入日志
- `TokenProtector`：`Encrypt(string plain) → byte[]`、`Decrypt(byte[] cipher) → string`，仅 Windows 可用

### 7. 事件订阅自动刷新（GUI/Messages/）

- `UsageDataFetchedMessage`：HostedService 拉取成功后发送，`MainViewModel` 和 `QueryViewModel` 订阅后重新查询 DB
- `ToggleSyncMessage`：托盘"开始/暂停"发送到 HostedService
- `NotificationShownMessage`：通知服务弹出后发送，主窗口状态栏更新"最近一次通知时间"
- 用 `WeakReferenceMessenger`（CommunityToolkit.Mvvm）避免内存泄漏

### 8. 启动流程（GUI/Program.cs）

- `Generic Host` 构建：注册 `AppSettings`、`IDbContext`、`IUsageRepository`、`ICursorApiClient`、`INotificationService`、`UsageSyncHostedService`
- Avalonia `AppBuilder` 配置：Win 平台、Fluent 主题、TrayIcon
- `App.OnFrameworkInitializationCompleted` 中：DI 创建 `MainViewModel`、显示 `MainWindow`、但**不关闭程序**（关闭主窗口=最小化到托盘，仅托盘"退出"才真正退出）
- HostedService 随宿主启动，与 UI 解耦

## 关键文件清单

需要新建的核心文件（按实现顺序）：

1. `src/CursorUsageNotify.GUI/CursorUsageNotify.GUI.csproj` — 项目文件 + PackageReferences
2. `src/CursorUsageNotify.Core/CursorUsageNotify.Core.csproj` + `AppSettings.cs` + `Constants.cs` + `Result.cs`
3. `src/CursorUsageNotify.Models/CursorUsageNotify.Models.csproj` + 实体/DTO
4. `src/CursorUsageNotify.Services/CursorUsageNotify.Services.csproj` + 5 个子模块
5. `src/CursorUsageNotify.GUI/Program.cs` + `App.axaml(.cs)` + `MainWindow.axaml(.cs)` + 3 个 ViewModel + 2 个 View + TrayIconHost
6. `src/CursorUsageNotify.GUI/appsettings.json` + `Assets/app.ico`
7. `src/CursorUsageNotify.Tests/CursorUsageNotify.Tests.csproj` + 3 个测试类
8. 根目录 `Directory.Build.props`（统一 LangVersion、Nullable、TreatWarningsAsErrors）

## 验证方案（端到端测试）

### 构建验证
```powershell
dotnet build src/CursorUsageNotify.GUI/CursorUsageNotify.GUI.csproj -c Release
# 要求：0 错误，0 警告
```

### 单元测试
```powershell
dotnet test src/CursorUsageNotify.Tests/CursorUsageNotify.Tests.csproj
# 覆盖：
#  - CursorApiClient：mock HttpMessageHandler，验证请求头、分页、错误码映射
#  - UsageRepository：用 SQLite in-memory，验证 upsert 去重
#  - CsvExporter：验证字段顺序、转义、空数据
```

### 手动功能验证
1. **启动**：运行 GUI，任务栏右下角出现托盘图标，主窗口自动显示
2. **设置 token**：
   - 浏览器登录 cursor.com → F12 → Application → Cookies → 复制 `WorkosCursorSessionToken`
   - 粘贴到设置 Tab → 点"测试连接" → 应显示绿色✓ + 拉到的事件数
3. **定时拉取**：把间隔改为 30 分钟，等 30 分钟后看查询 Tab 是否有新数据
4. **Toast 通知**：手动点"立即拉取一次" → 右下角应弹 Toast 显示本周期 token/费用
5. **托盘菜单**：右键托盘 → 开始/暂停切换、设置/查询切换 Tab、退出
6. **CSV 导出**：查询 Tab 选时间范围 → 导出 → 用 Excel 打开验证字段
7. **去重验证**：连续两次"立即拉取"（不刷新数据）→ 查询 Tab 同一 timestamp 不应出现重复行
8. **关闭主窗口**：应最小化到托盘而非退出；托盘"退出"才真正关闭进程

### 边界条件
- Token 失效（手动改坏）→ 测试按钮显示"401 Unauthorized，请重新登录"
- 网络断开 → 拉取失败后日志记录 Error，下次定时重试，UI 状态灯变红
- 数据库文件被锁 → 抛异常提示用户关闭其他实例
- 事件数量 >10000 → 分页拉取直到取完
