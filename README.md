# CursorUsageNotify — Cursor 用量监控桌面应用

Windows 桌面应用，通过调用 Cursor 内部 Dashboard API 定时拉取用量事件，本地存储并可视化展示，支持系统托盘后台运行与 Toast 通知。

## 功能

- **数据大屏** — 四行布局展示全量信息：
  - 第一行：用户名（含订阅类型 pro/pro+）、订阅起始、订阅结束、订阅状态、发票数
  - 第二行：本周用量（输入/输出/缓存读/缓存写/总用量/费用）
  - 第三行：本周期用量（输入/输出/缓存读/缓存写/总用量/费用）
  - 第四行：最近拉取时间、同步状态
- **事件查询** — 按时间范围、模型筛选用量明细，支持每页 10/20/50/100 行可选，CSV 导出
- **定时同步** — 后台服务按可配置间隔自动拉取数据，增量入库去重
- **同步成功通知** — 每次同步成功（含启动首次拉取）推送 Toast 通知，含新增事件数和用量摘要
- **Cookie 过期检测** — 自动检测 Session 过期时间，剩余 ≤7 天黄色预警，已过期红色预警，支持一键打开浏览器更新
- **系统托盘** — 最小化到托盘，支持开始/暂停同步、退出
- **Cookie 认证** — 支持直接粘贴完整 Cookie 字符串，自动解析 `WorkosCursorSessionToken`
- **安全存储** — 使用 Windows DPAPI 加密持久化凭证
- **缩放优化** — DataGrid 列宽固定，避免窗口缩放时重新计算布局导致卡顿

## 系统要求

- Windows 10 (10.0.18362+) 或更高版本
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（开发/构建）
- Cursor 账号（需从浏览器获取 `WorkosCursorSessionToken`）

## 快速开始

### 获取 Cookie

1. 在浏览器中登录 [cursor.com](https://cursor.com)
2. 打开开发者工具（F12）→ **Application**（应用）→ **Cookies**
3. 复制 `WorkosCursorSessionToken` 的值，或直接复制完整的 Cookie 字符串
4. 打开本应用的 **设置** → **Cookie 认证** → 粘贴

### 构建运行

```bash
# 克隆仓库
git clone <repo-url>
cd CursorUsageNotify

# 构建
dotnet build src/CursorUsageNotify.GUI/CursorUsageNotify.GUI.csproj

# 运行
dotnet run --project src/CursorUsageNotify.GUI/CursorUsageNotify.GUI.csproj
```

### VS Code 调试

在项目根目录创建 `.vscode/launch.json` 和 `.vscode/tasks.json`（见下方资产模板），按 F5 即可调试。

<details>
<summary>.vscode/launch.json</summary>

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug (GUI)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/src/CursorUsageNotify.GUI/bin/Debug/net10.0-windows10.0.18362.0/CursorUsageNotify.GUI.exe",
            "args": [],
            "cwd": "${workspaceFolder}/src/CursorUsageNotify.GUI",
            "stopAtEntry": false,
            "console": "internalConsole",
            "env": {
                "DOTNET_ENVIRONMENT": "Development"
            },
            "sourceFileMap": {
                "/Views": "${workspaceFolder}/src/CursorUsageNotify.GUI/Views"
            }
        }
    ]
}
```

</details>

<details>
<summary>.vscode/tasks.json</summary>

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "${workspaceFolder}/src/CursorUsageNotify.GUI/CursorUsageNotify.GUI.csproj",
                "-c",
                "Debug"
            ],
            "group": {
                "kind": "build",
                "isDefault": true
            },
            "problemMatcher": "$msCompile"
        },
        {
            "label": "test",
            "command": "dotnet",
            "type": "process",
            "args": [
                "test",
                "${workspaceFolder}/src/CursorUsageNotify.Tests/CursorUsageNotify.Tests.csproj",
                "-c",
                "Debug",
                "-v",
                "n"
            ],
            "group": {
                "kind": "test",
                "isDefault": true
            },
            "problemMatcher": "$msCompile"
        }
    ]
}
```

</details>

## 项目结构

所有项目统一使用 `Larpx.PersonalTools.CursorUsageNotify` 命名空间前缀，采用块级命名空间风格。

```
src/
├── CursorUsageNotify.Core/          # 核心抽象与配置
│   ├── Constants.cs                  # API 路径、Cookie 名、User-Agent 等常量
│   ├── Result.cs                     # Result<T> 返回类型（Ok/Fail）
│   ├── PagedResult.cs                # 分页结果包装
│   └── Configuration/
│       └── AppSettings.cs            # 应用配置模型
│
├── CursorUsageNotify.Models/        # 数据模型
│   ├── Entities/                     # SQLite 表实体
│   │   ├── UsageEventEntity.cs       # 单次用量事件
│   │   ├── PeriodUsageEntity.cs      # 计费周期汇总快照
│   │   ├── UserInfoEntity.cs         # 用户信息快照
│   │   └── SubscriptionEntity.cs     # 订阅信息快照
│   ├── Dtos/                         # API 响应 DTO
│   │   ├── CursorUsageEventDto.cs    # 用量事件 DTO
│   │   ├── CursorPeriodUsageDto.cs   # 周期用量汇总 DTO
│   │   ├── CursorBillingDto.cs       # 订阅/资料/周期/发票/会话 DTO
│   │   └── UsageAggregateStats.cs    # 聚合统计结果
│   └── Api/
│       └── CursorApiResponse.cs      # API 响应包装
│
├── CursorUsageNotify.Services/      # 业务逻辑
│   ├── Http/
│   │   ├── ICursorApiClient.cs       # API 客户端接口
│   │   ├── CursorApiClient.cs        # HTTP 实现（Cookie 认证）
│   │   └── CursorApiException.cs     # 异常层次（Auth/BadRequest/RateLimit）
│   ├── Storage/
│   │   ├── IDbContext.cs             # 数据库上下文接口
│   │   ├── DbContext.cs              # SQLite/SqlSugar 实现
│   │   ├── IUsageRepository.cs       # 仓储接口
│   │   └── UsageRepository.cs        # upsert 去重 + 聚合查询
│   ├── Scheduling/
│   │   ├── UsageSyncHostedService.cs # 后台定时同步服务
│   │   └── UsageSyncOptions.cs       # 运行时选项（可热更新）
│   ├── Security/
│   │   └── TokenProtector.cs         # DPAPI 加密/解密
│   ├── Notifications/
│   │   ├── INotificationService.cs   # 通知服务接口
│   │   └── WindowsToastService.cs    # Windows Toast 实现
│   ├── Export/
│   │   ├── ICsvExporter.cs           # CSV 导出接口
│   │   └── CsvExporter.cs            # CsvHelper 实现
│   └── Messages/
│       └── Messages.cs               # CommunityToolkit.Mvvm 消息类型
│
├── CursorUsageNotify.GUI/           # Avalonia 桌面 UI
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs          # ViewModel 基类
│   │   ├── MainViewModel.cs          # 数据大屏
│   │   ├── QueryViewModel.cs         # 查询 Tab
│   │   └── SettingsViewModel.cs      # 设置 Tab
│   ├── Views/
│   │   ├── MainWindow.axaml/cs       # 主窗口
│   │   ├── QueryView.axaml/cs        # 查询视图
│   │   └── SettingsView.axaml/cs     # 设置视图
│   ├── Tray/
│   │   └── TrayIconHost.cs           # 系统托盘
│   ├── Converters/                   # 值转换器
│   ├── App.axaml/cs                  # Application 入口
│   └── Program.cs                    # Host 构建 + 服务注册
│
└── CursorUsageNotify.Tests/         # 单元测试（xUnit）
    └── Services/
        ├── CursorApiClientTests.cs   # HTTP 客户端测试
        ├── UsageRepositoryTests.cs   # 仓储层测试
        └── CsvExporterTests.cs       # CSV 导出测试
```

## 技术栈

| 类别 | 技术 |
|---|---|
| 框架 | .NET 10 (Windows) |
| UI | Avalonia / Fluent Theme / DataGrid |
| MVVM | CommunityToolkit.Mvvm（源生成器） |
| ORM | SqlSugarCore + SQLite |
| 日志 | Serilog（文件 + 控制台） |
| HTTP | HttpClient + Polly（指数退避重试） |
| 安全 | System.Security.Cryptography.ProtectedData |
| 导出 | CsvHelper |
| 通知 | CommunityToolkit.WinUI.Notifications |
| 测试 | xUnit / FluentAssertions / NSubstitute |

## 架构要点

### API 调用

通过 Cursor 内部 Dashboard API 获取数据，所有端点均使用 Cookie 认证：

| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/dashboard/get-filtered-usage-events` | POST | 用量事件明细（分页） |
| `/api/dashboard/get-current-period-usage` | GET | 当前周期用量汇总 |
| `/api/auth/stripe` | GET | Stripe 订阅信息（计划/状态） |
| `/api/dashboard/get-user-profile` | GET | 用户资料（handle/displayName） |
| `/api/dashboard/get-current-billing-cycle` | GET | 计费周期起止时间 |
| `/api/dashboard/list-invoices` | GET | 发票列表 |
| `/api/auth/sessions` | GET | 会话列表（检测 Cookie 过期） |

### 增量同步与通知

`UsageSyncHostedService` 作为 `BackgroundService` 运行两个独立循环：

- **同步循环** — 按间隔调用上述 API 拉取数据，通过 `Timestamp + UserEmail` 去重 upsert；同步成功后立即推送 Toast 通知
- **通知循环** — 按独立间隔推送定时用量提醒（作为补充）

同步流程：
1. 拉取用量事件（POST，分页直至拉完）
2. 拉取周期汇总（GET，容错）
3. 并行拉取 5 个辅助 API（stripe/profile/billing-cycle/invoices/sessions）
4. 检测 Cookie 过期时间，必要时发送预警消息
5. 数据入库（period/subscription/userInfo）
6. 按订阅周期和本周分别聚合统计
7. 广播 `UsageDataFetchedMessage` 刷新 UI
8. 推送同步成功通知

### Cookie 过期检测

每次同步时调用 `/api/auth/sessions` 获取会话过期时间：
- 已过期 → 红色预警条 + "Cookie 已失效，请在设置中更新"
- 剩余 ≤7 天 → 黄色预警条 + "Cookie 将在 N 天后过期"
- 预警条提供"打开浏览器"和"立即更新"按钮

### 安全存储

凭证使用 Windows DPAPI（`ProtectedData.Protect`/`Unprotect`），以 `CurrentUser` 作用域加密写入 `%LOCALAPPDATA%/CursorUsageNotify/secrets.dat`，仅当前用户可解密。

### 命名空间规范

全解决方案统一使用 `Larpx.PersonalTools.CursorUsageNotify` 命名空间前缀，采用块级命名空间（`namespace Xxx { ... }`）而非文件级命名空间（`namespace Xxx;`）。

## 构建验证

```bash
# 完整构建
dotnet build

# 运行测试
dotnet test

# 要求：0 错误、0 警告
```

## 配置

`appsettings.json` 位于 GUI 项目根目录：

```json
{
  "App": {
    "ApiBaseUrl": "https://cursor.com",
    "SyncIntervalMinutes": 60,
    "NotificationIntervalMinutes": 60,
    "UsageEventsPageSize": 500,
    "HttpTimeoutSeconds": 30
  }
}
```

用户可在设置界面动态修改拉取/通知间隔，无需重启。

## 许可证

[MIT](LICENSE)
