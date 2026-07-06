# CursorUsageNotify — Cursor 用量监控桌面应用

Windows 桌面应用，通过调用 Cursor 内部 Dashboard API 定时拉取用量事件，本地存储并可视化展示，支持系统托盘后台运行与 Toast 通知。

## 功能

- **实时数据大屏** — 展示当前计费周期用量概览：用户信息、订阅计划、已用 Token、已用/剩余请求数、周期支出
- **事件查询** — 按时间范围、模型筛选用量明细事件，支持 CSV 导出
- **定时同步** — 后台服务按可配置间隔自动拉取数据，增量入库去重
- **系统托盘** — 最小化到托盘，支持开始/暂停同步、退出
- **Toast 通知** — 定时推送用量提醒（Windows 原生通知）
- **Cookie 认证** — 支持直接粘贴完整 Cookie 字符串，自动解析 `WorkosCursorSessionToken`
- **安全存储** — 使用 Windows DPAPI 加密持久化凭证

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
<summary>📁 .vscode/launch.json</summary>

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
        },
        {
            "name": "Debug (Tests)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "dotnet",
            "args": [
                "test",
                "${workspaceFolder}/src/CursorUsageNotify.Tests/CursorUsageNotify.Tests.csproj",
                "--no-build",
                "-v",
                "n"
            ],
            "cwd": "${workspaceFolder}",
            "stopAtEntry": false,
            "console": "internalConsole"
        }
    ]
}
```

</details>

<details>
<summary>📁 .vscode/tasks.json</summary>

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
            "label": "build (Tests)",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "${workspaceFolder}/src/CursorUsageNotify.Tests/CursorUsageNotify.Tests.csproj",
                "-c",
                "Debug"
            ],
            "group": "build",
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

```
src/
├── CursorUsageNotify.Core/          # 核心抽象与配置
│   ├── Constants.cs                  # API 路径、Cookie 名、User-Agent 等常量
│   ├── Result.cs                     # Result<T> 返回类型（Success/Fail）
│   └── Configuration/
│       └── AppSettings.cs            # 应用配置模型（API 地址、间隔、路径等）
│
├── CursorUsageNotify.Models/        # 数据模型
│   ├── Entities/
│   │   ├── UsageEventEntity.cs       # 单次用量事件（SQLite 表）
│   │   ├── PeriodUsageEntity.cs      # 计费周期汇总快照（SQLite 表）
│   │   └── UserInfoEntity.cs         # 用户信息快照（SQLite 表）
│   ├── Dtos/
│   │   ├── CursorUsageEventDto.cs    # API 响应：用量事件 DTO
│   │   └── CursorPeriodUsageDto.cs   # API 响应：周期汇总 DTO
│   └── Api/
│       └── CursorApiResponse.cs      # API 响应包装
│
├── CursorUsageNotify.Services/      # 业务逻辑
│   ├── Http/
│   │   ├── ICursorApiClient.cs       # Cursor API 客户端接口
│   │   ├── CursorApiClient.cs        # HTTP 实现（Cookie 认证、Polly 重试）
│   │   └── CursorApiException.cs     # API 异常类型（Auth/BadRequest/RateLimit）
│   ├── Storage/
│   │   ├── IDbContext.cs             # 数据库上下文接口
│   │   ├── DbContext.cs              # SQLite/SqlSugar 实现
│   │   ├── IUsageRepository.cs       # 用量数据仓储接口
│   │   └── UsageRepository.cs        # upsert 去重实现
│   ├── Scheduling/
│   │   ├── UsageSyncHostedService.cs # 后台定时同步服务（BackgroundService）
│   │   └── UsageSyncOptions.cs       # 运行时选项（可热更新）
│   ├── Security/
│   │   └── TokenProtector.cs         # DPAPI 加密/解密凭证
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
│   │   └── SettingsViewModel.cs      # 设置 Tab（Cookie 认证、间隔、清空）
│   ├── Views/
│   │   ├── MainWindow.axaml/cs       # 主窗口（TabControl 布局）
│   │   ├── QueryView.axaml/cs        # 查询视图
│   │   └── SettingsView.axaml/cs     # 设置视图
│   ├── Tray/
│   │   └── TrayIconHost.cs           # 系统托盘（Windows Tray Icon）
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

| 类别         | 技术                                          |
| ------------ | --------------------------------------------- |
| 框架         | .NET 10 (Windows)                             |
| UI           | Avalonia 12 / Fluent Theme / DataGrid         |
| MVVM         | CommunityToolkit.Mvvm 8 (源生成器)             |
| ORM          | SqlSugarCore 5 + SQLite                       |
| 日志         | Serilog（文件 + 控制台）                       |
| HTTP         | HttpClient + Polly（指数退避重试）             |
| 安全         | System.Security.Cryptography.ProtectedData     |
| 导出         | CsvHelper 33                                  |
| 通知         | CommunityToolkit.WinUI.Notifications           |
| 测试         | xUnit / FluentAssertions / NSubstitute        |

## 架构要点

### Cookie 认证

用户只需在设置中粘贴从浏览器复制的完整 Cookie 字符串，应用自动通过正则 `WorkosCursorSessionToken\s*=\s*(?<token>[^;]+)` 提取 Token，并在 HTTP 请求中构造 `Cookie` 头：

```
Cookie: WorkosCursorSessionToken=<token>
```

### 增量同步

`UsageSyncHostedService` 作为 `BackgroundService` 运行两个独立循环：

- **同步循环** — 按间隔调用 Cursor API 拉取事件，通过 `Timestamp + UserEmail` 去重 upsert
- **通知循环** — 按独立间隔推送 Windows Toast 用量提醒

均通过 `IMessenger` 与 UI 通信，同步完成后自动刷新数据大屏。

### 安全存储

凭证使用 Windows DPAPI（`ProtectedData.Protect`/`Unprotect`），以 `CurrentUser` 作用域加密写入 `%LOCALAPPDATA%/CursorUsageNotify/secrets.dat`，仅当前用户可解密。

## 构建验证

```bash
# 完整构建
dotnet build

# 运行测试
dotnet test

# 构建要求：0 错误、0 警告（TreatWarningsAsErrors 已启用）
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