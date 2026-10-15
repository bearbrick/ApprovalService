# 后端（backend/）

审批流服务的服务端：.NET 10、ASP.NET Core，按领域驱动设计分层。分层和模块见 [docs/architecture.md](../docs/architecture.md)，规则见根目录 [AGENTS.md](../AGENTS.md)。

## 项目

| 项目 | 职责 | 可以引用 |
| --- | --- | --- |
| `src/ApprovalService.Contracts` | 契约：开放接口的路径、HTTP 头、错误码、请求和响应体；以后的流程定义 JSON | 无 |
| `src/ApprovalService.Engine` | 引擎核心：执行图、令牌、命令、事件、审批规则的纯逻辑 | Contracts |
| `src/ApprovalService.Domain` | 领域层：实体、领域规则、仓储接口、`UserFriendlyException` | Contracts、Engine |
| `src/ApprovalService.Application` | 应用层：应用服务、输入输出模型 | Contracts、Engine、Domain |
| `src/ApprovalService.Infrastructure` | 基础设施层：EF Core 和三种数据库、仓储、附件存储、消息、出站适配层、后台任务 | 以上全部 |
| `src/ApprovalService.Api` | 接口层：路由、鉴权、错误格式、组装 | Contracts、Domain、Application、Infrastructure |
| `tests/ApprovalService.Backend.Checks` | 检查程序：服务端行为、分层和仓库规范、注释、跨部分的契约一致性 | — |

## 命令

```powershell
dotnet build backend/ApprovalService.Backend.slnx
dotnet format backend/ApprovalService.Backend.slnx --verify-no-changes --no-restore
dotnet run --project backend/tests/ApprovalService.Backend.Checks
dotnet run --project backend/tests/ApprovalService.Backend.Checks -- docs    # 只检查注释，列出全部问题
dotnet run --project backend/src/ApprovalService.Api                         # http://localhost:5280
```

## 现在有的接口

| 接口 | 说明 |
| --- | --- |
| `GET /health/live` | 存活检查 |
| `GET /health/ready` | 就绪检查（以后加上数据库连通性） |
| `GET /api/v1/info` | 服务信息：名称、版本、接口版本 |

所有响应都带 `X-Request-Id`、`X-Trace-Id`；出错统一返回 `{code, message, errors, traceId}`（见 [docs/contracts.md](../docs/contracts.md)）。

## 流程定义

正式流程定义模型位于 `src/ApprovalService.Contracts/Flows/`，只接受统一自由画布的 JSON；`schemaVersion` 标识格式版本。`src/ApprovalService.Engine/Definitions/` 负责在发布前检查设计内容并生成不含画布坐标的执行图。可导入的 BPMN 文件必须由本服务导出且携带本服务扩展信息。开发样例放在 `samples/flows/`，完整字段约定见 [docs/contracts.md](../docs/contracts.md)。

`src/ApprovalService.Engine/Execution/EngineRuntime` 是运行时的统一命令入口：输入已发布执行图、当前状态、命令和固定时间、连线条件结果、单人处理人，返回新状态与只追加事件。入口不修改传入状态；审批单 ID 与请求 ID 一起用于幂等及确定性生成令牌、任务、事件 ID。自动动作和子流程只产生待发事件，外部执行结果再以命令送回。包容网关按本次分叉实际选中的边汇合，回路累计推进超过 200 步后标记异常。撤销只接受已通过的审批单；审批中可撤回或终止。

节点推进通过 `Abstractions/INodeBehavior` 返回有限的状态动作，内置节点各有独立实现。行为读取的是状态、执行图、节点和上下文的隔离副本，不能修改真实输入；扩展人工节点创建任务时须指定同意、办理完成或确认已阅中的一种完成命令，可声明是否允许拒绝，命令入口据任务快照执行。新增运行节点可在 `EngineContext.NodeBehaviors` 注册纯行为，无须修改令牌推进核心；节点若要从设计画布发布，还须在流程定义发布校验中登记对应语义。当前只处理单人节点，人员来源与多人审批规则由后续任务接入。

## 配置

`appsettings.json` 的 `ApprovalService` 节：

| 配置 | 默认 | 说明 |
| --- | --- | --- |
| `Data:Provider` | `Sqlite` | 数据库：Sqlite、SqlServer、PostgreSql（MVP 用 SQLite） |
| `Data:Directory` | `data` | 数据目录：SQLite 库、本地附件、备份 |
| `Data:ConnectionString` | — | SQL Server、PostgreSQL 的连接字符串，不要提交到仓库 |

服务启动时校验这三项：`Provider` 只接受表中的三个值，`Directory` 不能留空，使用 SQL Server 或 PostgreSQL 时必须提供非空的 `ConnectionString`。配置错误会让服务启动失败，诊断会指出配置键，不打印连接字符串。当前 `/health/ready` 只确认服务已启动；数据库连通性检查随持久化功能接入。
