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
| `GET /health/ready` | 数据库可连接时返回 `ready`；不可连接时返回 503 |
| `GET /api/v1/info` | 服务信息：名称、版本、接口版本 |
| `GET /api/v1/flows`、`GET /api/v1/flows/{code}/schema` | 已授权的发布流程与审批数据定义，需 API Key |
| `GET /api/v1/instances/{id}`、`GET /api/v1/instances?flowCode=&businessKey=` | 本接入应用的审批单详情和历史，需 API Key |

所有响应都带 `X-Request-Id`、`X-Trace-Id`；出错统一返回 `{code, message, errors, traceId}`（见 [docs/contracts.md](../docs/contracts.md)）。

## 流程定义

正式流程定义模型位于 `src/ApprovalService.Contracts/Flows/`，只接受统一自由画布的 JSON；`schemaVersion` 标识格式版本。`src/ApprovalService.Engine/Definitions/` 负责在发布前检查设计内容并生成不含画布坐标的执行图。可导入的 BPMN 文件必须由本服务导出且携带本服务扩展信息。开发样例放在 `samples/flows/`，完整字段约定见 [docs/contracts.md](../docs/contracts.md)。

`src/ApprovalService.Engine/Execution/EngineRuntime` 是运行时的统一命令入口：输入已发布执行图、当前状态、命令和固定时间、连线条件结果、到节点时的组织快照，返回新状态与只追加事件。入口不修改传入状态；审批单 ID 与请求 ID 一起用于幂等及确定性生成令牌、任务、事件 ID。自动动作和子流程只产生待发事件，外部执行结果再以命令送回。包容网关按本次分叉实际选中的边汇合，回路累计推进超过 200 步后标记异常。撤销只接受已通过的审批单；审批中可撤回或终止。

节点推进通过 `Abstractions/INodeBehavior` 返回有限的状态动作，内置节点各有独立实现。行为读取的是状态、执行图、节点和上下文的隔离副本，不能修改真实输入；扩展人工节点创建任务时须指定同意、办理完成或确认已阅中的一种完成命令，可声明是否允许拒绝，命令入口据任务快照执行。新增运行节点可在 `EngineContext.NodeBehaviors` 注册纯行为，无须修改令牌推进核心；节点若要从设计画布发布，还须在流程定义发布校验中登记对应语义。

`Assignees/` 提供 14 类、15 个编码的找人来源（岗位与职级分别编码），由调用方提供本次组织、审批数据、矩阵命中与业务接口预取结果；引擎不直接访问外部系统。到达人工作业节点时重新取人，和预览不同时写事件。`Policies/` 按依次、会签、或签、票签计算任务结果；缺人、本人、重复、停用人员按节点配置处理。输入快照与扩展策略隔离，未识别的规则编码会报校验错误。组织存储和业务接口 HTTP 调用由后续任务接入。

`Commands/` 扩充同一个 `EngineRuntime.Execute` 入口：人工决定、退回和重新提交、转交、前后及并行加签、减签、征询回复、补充、评论与 @、暂存、抄送、催办、管理员干预都先校验状态、操作人、节点许可与意见要求，再修改状态并发事件。管理员不能直接跳到并行或包容汇合网关。事件的 `Audit` 带实际操作人、代谁处理、意见和干预原因；`EngineState.Operations` 与任务代理人信息必须随运行状态一起持久化。委托在节点到达时按固定时间和流程范围生效，旧待办可单独转给代理人；只解析一层，原审批人仍可处理。退回方式和撤回限制在发起时固定，不能靠后续请求临时更改。

应用层必须从已认证身份填入 `OperationInput.ActorId`，独立核实管理员身份后才设置 `IsAdministrator`，核实另一起撤销审批已通过后才设置 `OperationContext.CancellationApproved`。批量同意的纯计算结果须由应用层在一个事务中保存；跨审批单的离职交接和修改流程定义里的指定人员也由应用层协调。引擎只生成催办、抄送、重发和撤销审批申请的待发事件，不直接调用外部系统。

`src/ApprovalService.Engine/Expressions/` 提供纯逻辑的表达式解析、审批快照逐字段校验、JSON 样例生成和人民币大写。条件与计算字段用 `${fieldKey}` 引用快照，例如 `SUM(${items.amount}) >= 1000 AND ${initiator.level} >= 3`；明细列聚合、`IF`、`ANY` 和 `DATEDIFF` 的边界见契约文档。发起时可调用 `ApprovalDataValidator.Validate(definition, data)` 获得 `data.items[0].qty` 这样的错误路径，调用 `CreateExampleJson(definition)` 为 schema 接口生成示例。审批编号格式规则在领域层 `ApprovalSerialNumber.Format`，调用方必须先分配唯一流水号并传入时间；纯规则本身不抢号、不访问数据库。

## 配置

`appsettings.json` 的 `ApprovalService` 节：

| 配置 | 默认 | 说明 |
| --- | --- | --- |
| `Data:Provider` | `Sqlite` | 数据库：Sqlite、SqlServer、PostgreSql（MVP 用 SQLite） |
| `Data:Directory` | `data` | 数据目录：SQLite 库、本地附件、备份 |
| `Data:ConnectionString` | — | SQL Server、PostgreSQL 的连接字符串，不要提交到仓库 |

## 持久化与后台任务

流程元数据、不可变的发布版本、审批单、任务投影、操作记录、待发队列和定时任务使用 EF Core 保存。SQLite 在启动时自动创建数据目录并执行迁移；SQL Server 和 PostgreSQL 使用各自的迁移，配置连接串后按部署流程执行。三种数据库的模型与迁移一致性由检查程序核对，设置 `APPROVAL_TEST_SQLSERVER` 或 `APPROVAL_TEST_POSTGRES` 后还会在对应测试数据库执行发起和审批场景。

审批命令由应用服务加载固定的流程版本和引擎状态，以 `Version` 乐观锁一次写入新状态、任务、只追加事件及待发项。事件按顺序串接 SHA-256 校验值，审批单保存最终序号和哈希锚点，可检查历史记录被改写或末尾被删除；校验时在同一数据库事务快照内读取锚点和事件。后台任务使用数据库租约抢占工作；进程重启或租约到期后可以接着执行，执行超过租约的旧工作者不能再确认完成，领取后回读仍核对本次租约，失败按退避表重试。接入应用的 API Key 只存 SHA-256 摘要，回调签名密钥使用部署主密钥加密。回调正文在审批事务中固定，后台发送器用 HMAC-SHA256 签名、5 秒超时；每次发送留不含正文的结果日志，管理用例可重排原事件且不改变 `eventId`。四类业务钩子的精确协议待 ADR 0010 确认。

新增或修改实体配置后，用 `backend/tools/add-migration.ps1` 分别为 SQLite、SQL Server、PostgreSQL 生成迁移；不要手改生成的迁移或模型快照。

服务启动时校验这三项：`Provider` 只接受表中的三个值，`Directory` 不能留空，使用 SQL Server 或 PostgreSQL 时必须提供非空的 `ConnectionString`。配置错误会让服务启动失败，诊断会指出配置键，不打印连接字符串。`/health/ready` 会检查数据库连通性；数据库不可连接时返回 503。
