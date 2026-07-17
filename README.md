# CursorUsageNotify — AI 用量监控桌面应用

Windows 桌面应用，同时支持 **Cursor** 与 **DeepSeek** 平台用量采集：定时拉取、本地 SQLite 存储、双列数据大屏、系统托盘与 Toast 通知。

## 功能

- **双平台数据大屏**
  - **Cursor**：账户/订阅周期、当天/本周/本周期 Token 与费用（含缓存读写）
  - **DeepSeek**：账户余额、累计消费、总请求次数；当天/本周/本月 Token；悬停显示各 API Key × 模型明细
- **DeepSeek 大屏模式**（设置中持久化）：汇总全部 API Key，或仅显示单个 Key
- **事件查询** — 按平台/时间/模型筛选，分页与 CSV 导出
- **定时同步** — 后台按间隔拉取；Cursor 增量，DeepSeek 拉取「当前日往前两个自然月」全量窗口
- **分平台通知开关** — 设置中可分别开关 Cursor / DeepSeek 用量通知；全部关闭则不发通知
- **Cookie / Token 认证** — Cursor Cookie；DeepSeek `userToken`（Local Storage）
- **安全存储** — Windows DPAPI 加密凭证（`secrets.dat` / `secrets_deepseek.dat`）
- **系统托盘** — 最小化驻留，开始/暂停同步、退出
- **Cookie 过期检测**（Cursor）— ≤7 天黄预警，已过期红预警

## 系统要求

- Windows 10 (10.0.18362+) 或更高版本
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（开发/构建）
- Cursor 账号（浏览器 Cookie）和/或 DeepSeek 开放平台账号（`userToken`）

## 快速开始

### Cursor：获取 Cookie

1. 登录 [cursor.com](https://cursor.com)
2. F12 → **Application** → **Cookies**
3. 复制 `WorkosCursorSessionToken` 或完整 Cookie
4. 应用 **设置** → Cursor 平台 → 粘贴并测试连接

### DeepSeek：获取 userToken

1. 登录 [platform.deepseek.com](https://platform.deepseek.com/usage)
2. F12 → **Application** → **Local Storage** → 复制 `userToken`（JSON 或裸 token 均可）
3. 应用 **设置** → DeepSeek 平台 → 粘贴并测试连接

### 构建运行

```bash
git clone <repo-url>
cd CursorUsageNotify

dotnet build src/CursorUsageNotify.GUI/CursorUsageNotify.GUI.csproj
dotnet run --project src/CursorUsageNotify.GUI/CursorUsageNotify.GUI.csproj
```

### 测试

```bash
dotnet test src/CursorUsageNotify.Tests/CursorUsageNotify.Tests.csproj
```

VS Code / Cursor 可使用仓库内 `.vscode/launch.json`、`.vscode/tasks.json`，F5 调试或运行 test 任务。

## 项目结构

命名空间前缀：`Larpx.PersonalTools.CursorUsageNotify`（块级命名空间）。

```
src/
├── CursorUsageNotify.Core/           # 常量、Result、AppSettings
├── CursorUsageNotify.Models/         # 实体、DTO、PlatformType、DeepSeekDashboardMode
├── CursorUsageNotify.Services/       # HTTP / 仓储 / 同步 / 通知 / 偏好
│   ├── Http/                         # CursorApiClient、DeepSeekApiClient
│   ├── Platforms/                    # CursorPlatformProvider、DeepSeekPlatformProvider
│   ├── Storage/                      # SqlSugar + UsageRepository
│   ├── Scheduling/                   # UsageSyncHostedService
│   ├── Configuration/                # UserPreferences（平台启用、通知、DeepSeek 大屏模式）
│   └── ...
├── CursorUsageNotify.GUI/            # Avalonia MVVM 桌面 UI
└── CursorUsageNotify.Tests/          # xUnit 单元测试
    ├── Services/
    │   ├── UsageRepositoryTests.cs
    │   ├── UserPreferencesTests.cs
    │   ├── CursorApiClientTests.cs
    │   └── CsvExporterTests.cs
    ├── Security/
    └── Http/
```

## 技术栈

| 类别 | 技术 |
|---|---|
| 框架 | .NET 10 (Windows) |
| UI | Avalonia / Fluent Theme / DataGrid |
| MVVM | CommunityToolkit.Mvvm |
| ORM | SqlSugarCore + SQLite |
| 日志 | Serilog |
| HTTP | HttpClient + Polly |
| 安全 | DPAPI (`ProtectedData`) |
| 导出 | CsvHelper |
| 通知 | CommunityToolkit.WinUI.Notifications |
| 测试 | xUnit |

## 架构要点

### Cursor API（Cookie 认证）

| 端点 | 用途 |
|---|---|
| `/api/dashboard/get-filtered-usage-events` | 用量事件（分页） |
| `/api/dashboard/get-current-period-usage` | 当前周期汇总 |
| `/api/auth/stripe` | 订阅计划/状态 |
| `/api/dashboard/get-user-profile` | 用户资料 |
| `/api/dashboard/get-current-billing-cycle` | 计费周期 |
| `/api/dashboard/list-invoices` | 发票 |
| `/api/auth/sessions` | Session 过期检测 |

### DeepSeek API（Bearer userToken）

| 端点 | 用途 |
|---|---|
| `/api/v0/users/get_user_summary` | 充值余额、本月/累计消费 |
| `/auth-api/v0/users/current` | 用户邮箱/手机号/资料 |
| `/api/v0/users/get_api_keys` | 账户全部 API Key 列表 |
| `/api/v0/usage/by_api_key/amount` | 按 Key×模型每日 Token / REQUEST |
| `/api/v0/usage/by_api_key/cost` | 按 Key×模型每日费用 |

DeepSeek 入库维度：`自然日 × API Key × 模型`（去重键含 `Model`）；`RequestCount` 存 `usage.REQUEST`。拉取窗口为「当前月往前第 2 个自然月的 1 日」至今日；大屏「本月」仍按当前自然月聚合。

### 同步与通知

`UsageSyncHostedService` 双循环：

1. **同步循环** — 仅同步已启用且持有 token 的平台；成功后广播 `UsageDataFetchedMessage`，并按**通知开关**决定是否 Toast
2. **通知循环** — 定时用量提醒；两边通知都关则跳过；只汇总已开通知的平台

用户偏好文件（Token 显示格式、平台启用、通知开关、DeepSeek 大屏模式）持久化到本地 JSON，与 `appsettings.json` 分离。

### 安全存储

| 平台 | 文件 |
|---|---|
| Cursor | `%LOCALAPPDATA%/CursorUsageNotify/secrets.dat` |
| DeepSeek | `%LOCALAPPDATA%/CursorUsageNotify/secrets_deepseek.dat` |

## 配置

`src/CursorUsageNotify.GUI/appsettings.json`：

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

拉取/通知间隔与平台开关可在设置页修改；通知平台开关即时落盘。

## 测试覆盖说明

已覆盖（单元测试）：

- 仓储 upsert 去重（含同日同 Key 多模型）、时间筛选、Clear
- 聚合：`RequestCount` 求和、API Key 过滤、按 Key×模型分组、`GetDistinctApiKeys`
- `UserPreferences` 加载/保存/通知开关/DeepSeek 过滤/坏 JSON 降级
- Cursor HTTP 客户端、CSV 导出、DPAPI / SecureTokenHolder、端点探测

暂未覆盖（偏集成/UI）：Avalonia ViewModel、DeepSeek 真实 HTTP（需账号）、托盘与 Toast 端到端。

## 构建验证

```bash
dotnet build
dotnet test
```

要求：0 错误（警告按项目配置处理）。

## 许可证

[MIT](LICENSE)
