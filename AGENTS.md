# AGENTS.md：给 AI 编码助手的项目说明

本文件是所有 AI 助手（Codex、Claude Code、Cursor、GitHub Copilot、Gemini 等）的共同入口。`CLAUDE.md`、`GEMINI.md`、`.github/copilot-instructions.md` 都只是指向这里。给人看的介绍在 `README.md`，三个部分各有自己的 `README.md`。

本仓库可能有多个 AI 同时开发。下面的规则是为了让大家互不踩踏、改动可以自动验证。**规则和任务要求冲突时，以本文为准，并在任务文件里提出来。**

---

## 0. 开始前按顺序读

1. 本文件全文。
2. `docs/contracts.md`：开放接口、错误码、HTTP 头、回调的约定。
3. 你的任务文件 `docs/tasks/T-xxx-*.md`。**没有任务文件就不要改代码**：先问人，或者按 `docs/tasks/_template.md` 新建任务。
4. 任务涉及的那个部分的 `README.md`（`frontend/`、`backend/`、`client/`）。
5. 任务里写的设计书章节：`docs/design/approval-service-design.html`。它是 HTML，按章节 id 查找：`#scope`（初版范围）、`#arch`（总体架构）、`#designer`（流程设计器）、`#rules`（中国式审批规则）、`#form`（审批数据与详情展示）、`#center`（审批中心、手机端与提醒）、`#print`（打印审批单）、`#stats`（统计报表）、`#engine`（引擎、数据与文件）、`#platform`（可扩展与平台化）、`#org`（组织架构与账号）、`#integration`（业务系统对接）、`#api`（接口清单）、`#demo`（示例业务系统与试点）、`#plan`（计划与已定事项）。
6. `docs/architecture.md` 里和任务相关的模块，`docs/adr/` 里相关的决定。

## 1. 项目是什么

独立部署的**审批流服务**，是公司产品平台里的一个微服务（和标签服务并列）。它**只管审批**：画审批流程（钉钉式和自由画布两种画法）、按流程流转、记录每个节点谁在什么时候怎么批的；**不产生业务单据**，所有审批都由业务系统通过接口发起。审批人在审批中心（电脑网页、手机网页）处理，结果回调给业务系统。对业务系统**只提供数据接口**（开放接口和 .NET SDK），不提供界面组件；示例业务系统（WinUI 3 界面 + 单独的对接类库）演示怎么接。

核心要求：中国式审批规则尽量做全（设计书第 4 节的清单）；业务系统接入改得越少越好；引擎做扎实、可扩展，为以后的平台（统一登录、主数据、ESB 统一出口）留好位置。

当前阶段：骨架已完成（T-000）：三个部分的项目结构、分层和依赖检查、注释检查、统一验证脚本，加上一条最小链路（服务信息接口 → SDK → 对接类库，前端顶栏显示连接状态）。其余工作按 `docs/tasks/README.md` 里的任务逐个做。

## 2. 仓库地图

仓库分成三个**互不引用**的部分，只通过 HTTP 交互。一个任务通常只改一个部分。

| 路径 | 内容 | 备注 |
| --- | --- | --- |
| `frontend/` | 流程设计器、审批中心（电脑和手机）、管理后台（Vue 3 + TypeScript + Vite） | npm 项目 |
| `backend/ApprovalService.Backend.slnx` | 后端解决方案 | |
| `backend/src/ApprovalService.Contracts/` | 开放接口的路径、HTTP 头、错误码、请求和响应体；以后的流程定义 JSON 模型 | **契约**，改动见 §6.4 |
| `backend/src/ApprovalService.Engine/` | 引擎核心：执行图、令牌、命令、事件、审批规则的纯逻辑 | 遵守 R3 |
| `backend/src/ApprovalService.Domain/` | 领域层：实体（`XxxEntity`）、领域规则、仓储接口（`IXxxRepository`）、`UserFriendlyException` | 不引用 EF Core、ASP.NET Core |
| `backend/src/ApprovalService.Application/` | 应用层：应用服务（`IXxxAppService`）、输入输出模型（`XxxDto`） | 按业务模块分文件夹 |
| `backend/src/ApprovalService.Infrastructure/` | 基础设施层：EF Core 和三种数据库、仓储实现、附件存储、消息渠道、出站适配层、后台任务 | 包都加在这一层 |
| `backend/src/ApprovalService.Api/` | 接口层（ASP.NET Core）：路由（`XxxEndpoints`）、鉴权、错误格式、组装（`Program`） | 只做“读请求 → 调应用服务 → 写响应” |
| `backend/tests/ApprovalService.Backend.Checks/` | 后端检查程序，**也负责跨部分的契约一致性检查** | 不是 xUnit，见 §5 |
| `client/ApprovalService.Client.slnx` | 调用端解决方案 | |
| `client/src/ApprovalService.Client/` | 给业务系统的 .NET SDK（net461、netstandard2.0、net8.0） | 遵守 R4 |
| `client/samples/ApprovalService.Demo.Integration/` | 示例业务系统的对接类库（net48、net10.0），业务系统照抄它 | 不依赖界面框架 |
| `client/samples/ApprovalService.Demo.WinUI/` | 示例业务系统的 WinUI 3 界面（还没建，见任务） | 只引用对接类库 |
| `client/tests/ApprovalService.Client.Checks/` | 调用端检查程序 | 设置 `APPROVAL_SERVICE_URL` 时含端到端检查 |
| `docs/design/` | 和用户评审过的设计书（HTML） | 只做同步性修改，见 R10 |
| `docs/contracts.md`、`architecture.md` | 规范文档 | |
| `docs/adr/` | 架构决策记录 | |
| `docs/tasks/` | 任务，每个任务一个文件 | 协作的中心，见 §6 |
| `verify.ps1` | 一次验证三个部分 | 交付前必跑 |

## 3. 常用命令

在仓库根目录执行。需要 .NET SDK 10.0.100 以上（.NET Framework 4.6.1、4.8 的目标包在 Windows 上随 Visual Studio 生成工具安装）；前端需要 Node 22.18+ 或 24（前端检查程序靠 Node 自带的类型擦除直接运行 .ts）。

**统一验证（交付前必须通过）**：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1                # 全部三个部分 + SDK 对真实服务的端到端检查
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1 -Part backend  # 只验证一个部分：backend / client / frontend
```

**后端**：

```powershell
dotnet build backend/ApprovalService.Backend.slnx
dotnet format backend/ApprovalService.Backend.slnx --verify-no-changes --no-restore
dotnet run --project backend/tests/ApprovalService.Backend.Checks
dotnet run --project backend/tests/ApprovalService.Backend.Checks -- docs    # 只检查注释
dotnet run --project backend/src/ApprovalService.Api                         # http://localhost:5280
curl.exe http://localhost:5280/api/v1/info
```

**调用端**：

```powershell
dotnet build client/ApprovalService.Client.slnx
dotnet format client/ApprovalService.Client.slnx --verify-no-changes --no-restore
dotnet run --project client/tests/ApprovalService.Client.Checks
```

**前端**：

```powershell
cd frontend; npm ci; npm run dev        # 开发，http://localhost:5273（接口转给 5280）
cd frontend; npm run check              # 检查程序（不依赖浏览器的模块和源码规范）
cd frontend; npm run build              # 类型检查 + 打包
```

`dotnet format` 报格式问题时，对对应的解决方案运行一次不带 `--verify-no-changes` 的 `dotnet format` 修正，再检查改动是否只涉及你的文件。

## 4. 硬性规则

**R1 契约优先。** 开放接口的路径、HTTP 头、错误码、请求和响应体、回调和事件格式、流程定义 JSON 只在 `backend/src/ApprovalService.Contracts` 定义，后端其他地方引用常量，不写字面量。调用端（和以后前端）的副本见 §6.4。改契约走 §6.4。

**R2 依赖方向。** 三个部分互不引用项目和文件：`backend/`、`client/` 的项目只引用本部分的项目；`frontend/` 不 import 本文件夹以外的文件。后端分层：Contracts ← Engine ← Domain ← Application ← Infrastructure ← Api；Contracts 不引用任何项目和包；引擎核心只引用 Contracts、不引用任何包；领域层不用 EF Core、ASP.NET Core，不引用包；应用层不用 EF Core 和 ASP.NET Core；接口层不直接访问数据库。检查程序会拦。

**R3 引擎核心要纯。** `ApprovalService.Engine` 只有流程执行的逻辑：不访问数据库、文件、网络，不用依赖注入、配置、日志；时间、ID 由调用方传入（不直接调 `DateTime.Now`、`Guid.NewGuid`），同样的输入一定得到同样的结果。所有审批操作都是命令，经引擎的同一个入口处理，规则只写一处（设计书第 9 节）。调外部系统（钩子、回调、自动节点、消息）只能经待发队列和出站适配层，不在数据库事务里等外部系统。引擎的新行为必须有场景检查（设计书第 4 节的每条规则至少一个场景）。

**R4 SDK 兼容。** `client/src/ApprovalService.Client` 必须能编译到 net461：不用 record、init、required、Index/Range 之类需要新运行时类型的语法；不依赖任何 NuGet 包（JSON 读写是自带的 `Internal/JsonText.cs`）。对接类库 `ApprovalService.Demo.Integration` 不依赖任何界面框架，能编译到 net48。公开 API 变化要同步设计书第 12、13 节，并遵守语义化版本。

**R5 语言和注释。** 代码注释、XML 注释、文档、给用户看的错误消息用**中文**；标识符、提交信息用**英文**。每个类型、函数、属性、参数都写完整的注释（包括私有的、检查程序和示例里的）：C# 用三行写法的 `/// <summary>`，加上每个参数的 `<param>`、有返回值时的 `<returns>`、泛型参数的 `<typeparam>`，重写和接口实现可以用 `<inheritdoc />`；TypeScript / Vue 用多行的 `/** … */`，函数写 `@param`、`@returns`，接口和类型的属性、组件的 props 和 emits 都要写。函数里关键的步骤（分支的理由、算法、容易误解的地方）写行内注释。代码、项目文件和脚本里不写任务编号。三个部分的检查程序都会核对，漏写时失败并列出位置。

**R6 错误处理。** 调用方或使用者能修正的问题（数据不对、状态不允许、没有权限）抛 `UserFriendlyException(错误码, 中文说明)`，接口层按错误码转成 400、401、403、404、409，统一返回 `{code, message, errors, traceId}`。程序缺陷不要吞掉、也不要包装成 400，让它以 `INTERNAL_ERROR` 暴露。

**R7 安全。** 不提交任何真实 Key、密码、证书、连接字符串；API Key 只存 SHA-256；回调、钩子的签名密钥和数据库连接串加密保存。附件下载先查权限（能看这张审批单的人才能下载）。不打包微软雅黑、宋体等系统字体（授权不允许）；打印审批单用的中文字体沿用标签服务的做法，由使用单位在管理后台上传。

**R8 依赖。** 新增 NuGet / npm 包要在任务文件里说明理由；NuGet 版本写在对应部分的 `Directory.Packages.props`，npm 版本写死不加 `^`；选发布满两周的稳定版；和标签服务用到的同一个包，版本保持一致。引入新的第三方库（不是升级）写 ADR。**本机缓存里没有、要从网上下载的包（如 Windows App SDK、流程图库、手机组件库、PDF 库），先在任务交接记录里写明包名、来源和大约大小，征得用户同意后再下载。**

**R9 范围。** 只改任务“范围”里的文件；“共享文件”先在交接记录登记再改。不顺手重构、不批量改格式、不改别人认领中的任务的文件。发现范围外的问题，记到交接记录或新建任务。

**R10 设计文档。** `docs/design/` 是和用户评审过的设计，只做“让文档和实现一致”的同步修改，并在交接记录说明改了哪里；要改设计本身，先写 ADR 请人确认。

**R11 文件规模。** 产品源码单个文件少于 500 行（检查程序会拦，工具生成的文件如 EF Core 迁移的 Designer 和快照除外）。快到上限就按职责拆分。

**R12 不提交生成物。** `bin/`、`obj/`、`node_modules/`、`dist/` 已在 .gitignore。`frontend/package-lock.json` 要提交。

**R13 不确定就停。** 遇到下面任何一种情况，不要猜：设计书没写清楚、和本文冲突、需要改契约中不兼容的部分、需要新增依赖、需要改别人认领的文件、验收标准做不到。在任务文件的交接记录里写清问题和你的建议，把状态改成 `阻塞`，结束本轮工作。

**R14 产品边界。** 审批服务不产生业务单据：不做“在审批中心发起审批”“填业务表单”，发起、撤回、重新提交都由业务系统调接口；审批中心不能改业务数据。对业务系统只提供数据接口（开放接口和 .NET SDK），不提供 Vue 组件、WinForms 控件、ASP.NET Core 扩展这类界面或框架组件（ADR 0002）。任务里出现越界的需求时按 R13 停下来问。

## 5. 编码约定

**C#（backend、client）**

- 遵循根目录 `.editorconfig`：4 空格、CRLF、文件范围命名空间、控制流一律带大括号、System 命名空间的 using 排最前。没有初始值的自动属性会被 `dotnet format` 展开成多行，这是公司约定，照做即可。
- `Nullable` 开启、`TreatWarningsAsErrors` 开启，0 警告。
- 内部类型用 `internal`；只有真正给别的程序集用的才 `public`。
- 后端命名（参照 ABP 的习惯，不引入 ABP 框架）：实体 `XxxEntity`，仓储 `IXxxRepository` / `XxxRepository`，应用服务 `IXxxAppService` / `XxxAppService`，输出模型 `XxxDto`，输入模型 `CreateXxxDto`、`UpdateXxxDto`、`XxxInput`，路由 `XxxEndpoints`，配置 `XxxOptions`。
- 新功能的落点：流程执行的规则写在引擎核心；其他业务规则写在领域层（实体旁边的 `XxxRules` 或实体方法）；用例写在应用层的 `XxxAppService`（做权限和范围检查、记操作日志、转换成 Dto）；数据库访问只写在基础设施层的仓储里；接口层的处理函数一般一行：调应用服务。新的应用服务在 `AddApprovalApplication` 登记，基础设施的服务在 `AddApprovalInfrastructure` 登记。
- 扩展点（节点类型、审批人来源、多人审批方式、消息渠道、附件存储等，设计书第 10 节）一个实现一个文件，在登记处加一行，不改引擎核心。
- 测试写进对应部分的检查程序（`backend/tests/ApprovalService.Backend.Checks` 或 `client/tests/ApprovalService.Client.Checks`）：按主题放在 `XxxChecks.cs`，用 `check("中文描述", 条件)`，在 `Program.cs` 加一行调用。新行为必须有对应检查。
- 后端检查程序的 `Program` 在命名空间里，引用服务端入口要写 `global::Program`；进程内启动服务用 `ServerFactory`。

**TypeScript / Vue（frontend）**

- 严格模式，`npm run check` 和 `npm run build`（含 vue-tsc）必须通过。
- `src/model/` 里的纯逻辑模块要能被 Node 直接运行：相对导入写 `.ts` 扩展名、只用可擦除的语法（不用 enum、namespace、构造函数参数属性）、不碰 DOM；新增这类模块时在 `tests/checks.ts` 补检查。
- `src/` 下的文件不超过 500 行（`npm run check` 会拦），快到上限就按职责拆分；组件里成块的逻辑放进 `src/composables/useXxx.ts`。
- 页面用地址的 # 部分区分（`src/model/routes.ts`），手机端是 `#/m` 开头。

**提交信息**：英文 Conventional Commits，带任务号，scope 用部分或模块名。例：`feat(engine): execution graph and tokens (T-002)`、`fix(api): return 409 when task already handled (T-004)`、`feat(frontend): tree designer (T-008)`、`docs(tasks): claim T-002`。类型用 feat、fix、perf、refactor、test、docs、build、chore。

## 6. 多 AI 协作

### 6.1 任务的一生

1. **认领**：确认任务状态是 `待认领`，且“依赖”都已完成。把状态改成 `进行中`，负责人写“工具 + 日期”（如 `Codex 2026-10-14`），单独提交 `docs(tasks): claim T-xxx`。两个 AI 同时认领同一任务时，以先合并进主分支的为准，后到的换一个任务。
2. **实现**：小步提交，每个提交都能编译。共享文件的改动单独提交。
3. **自检**：逐项过 §7 完成定义，在任务文件里勾选验收标准。
4. **交接**：在“交接记录”最上面追加一条：做了什么、没做什么、遇到的坑、留给下一位的话。状态改成 `待验收`。由人或另一个 AI 验收后改成 `已完成`。
5. **中途停下**（额度用完、上下文太长、被打断）：也要写交接记录，状态保持 `进行中`，写清下一步从哪里接着做。

三个部分互相独立，前端、后端、调用端的任务可以同时由不同的 AI 做。

### 6.2 分支与合并

- 每个任务一个分支：`ai/<工具>/<任务号>-<短名>`，如 `ai/codex/T-002-engine-core`。从主分支拉，开分支前先确认当前在主分支。
- 只在自己的任务分支上提交。合并到主分支、推送远程、改写历史，需要人确认。
- 合并冲突时：登记类文件（`ApplicationServiceCollectionExtensions.cs`、`InfrastructureServiceCollectionExtensions.cs`、`Directory.Packages.props`、任务索引）保留双方的行；其他文件拿不准就停下（R13）。

### 6.3 共享文件

这些文件多个任务都可能改，是冲突的高发区。改之前在任务文件“共享文件”里列出，改动尽量只**追加**行，单独提交：

- 全仓库：`.editorconfig`、`.gitignore`、`global.json`、`verify.ps1`、`AGENTS.md`、`docs/contracts.md`、`docs/architecture.md`、`docs/tasks/README.md`、`docs/adr/README.md`
- 后端：`backend/ApprovalService.Backend.slnx`、`backend/Directory.Build.props`、`backend/src/Directory.Build.props`、`backend/Directory.Packages.props`、`backend/src/ApprovalService.Contracts/**`、`backend/src/ApprovalService.Api/Program.cs`、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`
- 调用端：`client/ApprovalService.Client.slnx`、`client/Directory.Build.props`、`client/Directory.Packages.props`、`client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、`client/src/ApprovalService.Client/Internal/Protocol.cs`、`client/tests/ApprovalService.Client.Checks/Program.cs`
- 前端：`frontend/package.json`、`frontend/src/App.vue`、`frontend/src/model/routes.ts`

### 6.4 契约变更

契约的权威定义在后端 Contracts，调用端（以后还有前端）有副本：

| 契约内容 | 后端（权威） | 调用端副本 | 前端副本 |
| --- | --- | --- | --- |
| 错误码 | `Protocol/ErrorCodes.cs` | `ApprovalErrorCodes.cs` | — |
| HTTP 头 | `Protocol/HeaderNames.cs` | `Internal/Protocol.cs` | — |
| 流程定义 JSON（待建） | `Flows/*.cs` | — | `src/model/flow.ts` |
| 开放接口的请求和响应 | `Protocol/*.cs` | SDK 的公开模型 | `src/api/*.ts` |

- **向后兼容**（新增可选属性、新增错误码、新增节点类型、新增事件）：在同一个提交里同时改后端 Contracts、副本、`docs/contracts.md`、设计书对应章节，然后运行完整的 `verify.ps1`。后端检查程序会按文本读取副本，核对错误码、HTTP 头是否同步。
- **不兼容**（改名、删除、改语义或默认值）：先写 ADR（状态“提议”），在任务交接记录里请人确认，确认后才改，并升级接口版本或流程定义的格式版本。

### 6.5 架构决策（ADR）

跨部分、难回退、以后有人会问“为什么”的决定写 ADR，格式和时机见 `docs/adr/README.md`。

## 7. 完成定义

交付前逐项确认：

- [ ] `verify.ps1` 通过：只改了一个部分可以用 `-Part`，改了契约、文档或多个部分必须跑全量
- [ ] 新行为有对应的新检查（引擎的规则有场景检查）
- [ ] 改了契约：§6.4 表里的文件都已同步
- [ ] 文档同步：对应部分的 README、架构、契约、设计书中受影响的部分
- [ ] 任务文件：验收标准已勾选、交接记录已追加、状态已更新
- [ ] 没有提交密钥、生成物、和任务无关的改动

## 8. 术语

| 中文 | 代码里 | 说明 |
| --- | --- | --- |
| 前端 / 后端 / 调用端 | `frontend/` / `backend/` / `client/` | 仓库的三个部分 |
| 流程 | `Flow` | 一种审批的定义，如“采购申请”，有流程编码 |
| 流程版本 | `FlowVersion` | 发布产生新版本，发布后不可修改；审批单按发起时的版本走 |
| 画法 | `DesignMode` | 钉钉式（树形）或自由画布（BPMN 子集），发布时都编译成执行图 |
| 执行图 | `ExecutionGraph` | 节点、连线、网关组成的图，引擎只认它 |
| 节点 / 网关 | `Node` / `Gateway` | 审批、办理、抄送、子流程、自动节点；排他、包容、并行网关 |
| 令牌 | `Token` | 运行时标记流程走到哪；并行分叉生出多个令牌，汇合时等齐 |
| 审批单 | `Instance` | 一次审批的记录（流程实例），**不是业务单据** |
| 任务 | `ApprovalTask` | 审批单在某个节点上给某个人的一项待办 |
| 命令 / 事件 | `Command` / `Event` | 每个操作是一条命令，处理后产生事件写进操作记录 |
| 审批数据 | `ApprovalData` | 业务系统发起时传来的字段，保存为快照 |
| 业务单号 | `businessKey` | 业务系统自己的单据号，和流程编码一起唯一 |
| 接入应用 | `ClientApp` | 一个接入的业务系统：API Key、回调和钩子地址、签名密钥 |
| 回调 / 钩子 | `Callback` / `Hook` | 回调：审批服务通知业务系统结果；钩子：审批服务向业务系统要数据或问能不能过 |
| 出站适配层 | `OutboundAdapter` | 所有调外部系统的出口，以后改配置走 ESB |
| 嵌入令牌 | `EmbedToken` | 业务系统后端签发的几分钟有效的令牌，换成免登录打开地址 |
| 依次 / 会签 / 或签 / 票签 | `Sequential` / `All` / `Any` / `Vote` | 多人审批方式 |

## 9. 已知的坑

- **Windows 命令行发中文**：bash 或 cmd 直接 `--data '{"title":"中文"}'` 可能按 GBK 发送。用 UTF-8 文件加 `--data-binary @文件`。
- **PowerShell 脚本要带 BOM**：Windows PowerShell 5.1 只有带 BOM 才按 UTF-8 读取中文，`.editorconfig` 已对 `*.ps1` 设置 `utf-8-bom`。运行脚本用 `-ExecutionPolicy Bypass`。
- **行尾**：`.editorconfig` 要求 CRLF。用脚本生成的文件如果是 LF，`dotnet format --verify-no-changes` 会报错，跑一次 `dotnet format` 即可。Git Bash 的 `sed -i` 会把 CRLF 文件改成 LF。
- **csproj 里的 XML 注释不能出现 `--`**（比如写命令行参数），否则项目加载失败。
- **SDK 在 netstandard2.0 下没有可空性特性**：`string.IsNullOrEmpty` 不会帮编译器推断非空，写显式判断。
- **WebApplicationFactory 不认识 .slnx**：后端检查程序里已经显式指定内容根目录，新增服务端检查时沿用 `ServerFactory`。
- **WinUI 3 示例**：要从 nuget.org 下载 Windows App SDK（几百 MB，本机缓存里没有），按 R8 先征得用户同意；WinUI 3 只能 .NET 8 以上、Windows 10 以上，所以对接代码放在不依赖界面的对接类库里，.NET Framework 4.8 的兼容由对接类库保证。
- **SQLite**（接上数据库以后）：数据目录不能放网络共享盘，同一数据目录只能有一个服务进程；时间列存 UTC `DateTime`；检查程序用临时数据目录（`TempDataDirectory`），删除前要 `SqliteConnection.ClearAllPools()`。
- **EF Core 迁移**（接上数据库以后）：改了实体或配置要给三种数据库（SQLite、SQL Server、PostgreSQL）各生成一次迁移，不要手改迁移和快照（做法参照标签服务的 `backend/tools/add-migration.ps1`）。
