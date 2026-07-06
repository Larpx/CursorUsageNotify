# Tests 层完成与整体构建验证计划

## 目标

完成 Task #17(Tests 层)和 Task #18(整体构建验证 + 修复),确保整个 CursorUsageNotify 解决方案:
- **0 错误,0 警告**(Release 配置)
- 所有单元测试通过

## 当前状态分析

### 已完成

- **Tests 项目 3 个测试文件已创建**:
  - `src/CursorUsageNotify.Tests/Services/CursorApiClientTests.cs`(4 个测试,自定义 `StubHttpMessageHandler`)
  - `src/CursorUsageNotify.Tests/Services/UsageRepositoryTests.cs`(5 个测试,临时 SQLite 文件)
  - `src/CursorUsageNotify.Tests/Services/CsvExporterTests.cs`(3 个测试)
- **Tests.csproj 已正确配置**:引用 Core/Models/Services 项目 + 所需 NuGet 包(xunit、Microsoft.NET.Test.Sdk、xunit.runner.visualstudio、FluentAssertions、NSubstitute、CsvHelper、SqlSugarCore、Microsoft.Extensions.Logging.Abstractions)
- **GUI 项目已构建成功**(0 错误 1 警告:AVLN3001 MainWindow 无参构造,不影响运行)
- **Services/Models/Core 层已构建通过**

### 已核对接口签名(Phase 1 探索结论)

- `ICursorApiClient.TestConnectionAsync(string, CancellationToken)` 返回 `Task<Result<int>>`
- `ICursorApiClient.GetFilteredUsageEventsAsync(...)` 返回 `Task<CursorUsageEventsResponse>`
- `ICursorApiClient.GetCurrentPeriodUsageAsync(...)` 返回 `Task<CursorPeriodUsageDto>`
- `IUsageRepository.UpsertUsageEventsAsync(IEnumerable<UsageEventEntity>, CancellationToken)` 返回 `Task<int>`
- `IUsageRepository.QueryEventsAsync(long, long, string?, CancellationToken)` 返回 `Task<List<UsageEventEntity>>`
- `IUsageRepository.GetDistinctModelsAsync(CancellationToken)` 返回 `Task<List<string>>`
- `IUsageRepository.ClearAllAsync(CancellationToken)` 返回 `Task`
- `ICsvExporter.ExportAsync(IEnumerable<UsageEventEntity>, string, CancellationToken)` 返回 `Task`
- `UsageEventEntity` 全部属性已存在(Timestamp/UserEmail/Model/InputTokens/OutputTokens/TotalCents 等)

### 已知遗留问题

1. **Tests 项目有 `Class1.cs` 模板残留**(默认生成的空类,应清理)
2. **obj 目录有多目标框架历史残留**(net10.0、net10.0-windows10.0.18362.0、17763 三个 AssemblyInfo),构建前应清理避免干扰
3. NSubstitute/FluentAssertions 引而未用(冗余但不影响构建,本次不清理,保持包引用供未来扩展)

## 实施步骤

### 步骤 1:清理 Tests 项目模板残留

**文件**:`src/CursorUsageNotify.Tests/Class1.cs`

**操作**:删除该文件(默认模板生成,无实际用途)

**原因**:用户规则要求项目结构清晰,模板残留会影响代码审查和构建产物整洁度

### 步骤 2:清理多目标框架构建残留

**操作**:删除 Tests 项目的 `bin/` 和 `obj/` 目录

**命令**:
```powershell
Remove-Item -Recurse -Force "d:\Work\repos\CursorUsageNotify\src\CursorUsageNotify.Tests\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "d:\Work\repos\CursorUsageNotify\src\CursorUsageNotify.Tests\obj" -ErrorAction SilentlyContinue
```

**原因**:obj 下存在 net10.0、18362、17763 三个 TFM 的 AssemblyInfo 残留,可能导致构建时引用混乱

### 步骤 3:构建 Tests 项目(单独)

**命令**:
```powershell
dotnet build "d:\Work\repos\CursorUsageNotify\src\CursorUsageNotify.Tests\CursorUsageNotify.Tests.csproj" -c Release
```

**目标**:快速定位 Tests 项目自身的编译错误(避免被其他项目错误干扰)

**预期可能的错误**(基于 Phase 1 分析,概率较低):
- 测试文件中调用的方法名/参数与接口不一致(已核对,匹配)
- using 命名空间错误(已核对,匹配)
- 临时 SQLite 文件路径权限问题(运行时才暴露,构建不报错)

**修复策略**:
- 若出现 CS#### 编译错误,逐一对照 Services 接口签名修复
- 若出现 NU#### 包还原错误,检查 Directory.Packages.props 版本

### 步骤 4:整体构建解决方案

**命令**:
```powershell
dotnet build "d:\Work\repos\CursorUsageNotify\CursorUsageNotify.slnx" -c Release
```

**目标**:验证 Core/Models/Services/GUI/Tests 五个项目联合构建 0 错误 0 警告

**已知警告**:
- AVLN3001 MainWindow 无参构造(1 个,GUI 项目,不影响运行,可保留)

**验收标准**:
- 错误数 = 0
- 警告数 ≤ 1(允许 AVLN3001)

### 步骤 5:运行单元测试

**命令**:
```powershell
dotnet test "d:\Work\repos\CursorUsageNotify\src\CursorUsageNotify.Tests\CursorUsageNotify.Tests.csproj" -c Release --logger "console;verbosity=normal"
```

**目标**:12 个测试全部通过(4 + 5 + 3)

**预期可能的失败**:
- `UsageRepositoryTests`:SqlSugar 初始化失败(临时路径权限、表结构不匹配)
- `CursorApiClientTests`:Stub 响应与实际反序列化不匹配(JSON 结构差异)
- `CsvExporterTests`:文件路径或编码问题

**修复策略**:
- 测试失败时先看 `dotnet test` 详细输出 + 堆栈
- 区分「测试代码 bug」vs「被测代码 bug」
- 若是被测代码 bug,修复 Services 层;若是测试 bug,修复测试代码

### 步骤 6:整体测试 + 最终验证

**命令**:
```powershell
dotnet test "d:\Work\repos\CursorUsageNotify\CursorUsageNotify.slnx" -c Release --logger "console;verbosity=normal"
```

**验收标准**:
- 全部测试通过
- 输出包含 "Passed: 12" 或类似字样

## 假设与决策

### 假设

1. **CPM 配置正确**:Directory.Packages.props 已包含所有 Tests 所需包版本(已确认)
2. **Tests.csproj 的 TargetFramework `net10.0-windows10.0.18362.0` 正确**:与 Services 项目一致(因 Services 引用了 Windows 专用包 CommunityToolkit.WinUI.Notifications)
3. **GUI 项目 1 个 AVLN3001 警告可接受**:不影响运行,修复需要改 MainWindow 构造函数签名,超出本次任务范围

### 决策

1. **不清理 NSubstitute/FluentAssertions 包引用**:虽然当前测试未使用,但保留可供未来扩展(接口隔离测试);删除会触发 csproj 改动,增加本次变更范围
2. **不修复 AVLN3001 警告**:该警告是 Avalonia 12 对 ViewLocator 模式的要求,需要重构 MainWindow 构造函数,影响 DI 链路,风险高于收益
3. **采用分阶段构建**(Tests 单独 → 整体 → 测试):快速定位问题,避免整体构建失败时难以定位根因
4. **使用 slnx 而非 sln**:用户项目使用新版解决方案文件格式,保持一致

## 验证步骤(给执行者)

执行完上述 6 步后,确认:

1. `src/CursorUsageNotify.Tests/Class1.cs` 已删除
2. `src/CursorUsageNotify.Tests/bin` 和 `obj` 已重新生成(只包含 net10.0-windows10.0.18362.0 单一 TFM)
3. `dotnet build CursorUsageNotify.slnx -c Release` 输出 "Error: 0, Warning: ≤1"
4. `dotnet test` 输出 "Passed: 12, Failed: 0"
5. 将 Task #17 和 Task #18 标记为 completed
6. 向用户返回最终响应(简要总结完成情况)

## 风险

- **低风险**:Tests.csproj 配置已确认正确,接口签名已核对
- **中风险**:测试运行时可能暴露 SQLite/Json 反序列化的运行时问题(静态分析无法发现)
- **回滚方案**:若修复成本超出预期,可保留测试文件原样,仅删除 Class1.cs,标记 Task 部分完成并向用户说明
