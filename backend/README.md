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

统一验证会在随机回环端口启动真实服务。后端检查程序的 `seed-sdk-e2e` 命令只供 `verify.ps1` 使用：在全新临时 SQLite 库中创建测试应用、发布流程和人员绑定，并将本次随机生成的 API Key 写进同目录的临时文件。它不注册生产 HTTP 路由；脚本完成后会停服并清理整个临时目录。

## 现在有的接口

| 接口 | 说明 |
| --- | --- |
| `GET /health/live` | 存活检查 |
| `GET /health/ready` | 数据库可连接时返回 `ready`；不可连接时返回 503 |
| `GET /api/v1/info` | 服务信息：名称、版本、接口版本 |
| `GET /api/v1/center/instances`、`/center/instances/{id}` | 已登录人员的四列表与只读详情；嵌入范围与人员参与关系在数据库同时过滤 |
| `POST /api/v1/center/tasks/{id}/actions`、`/center/tasks/batch-approve` | 登录人员对当前任务提交引擎命令，批量同意逐项独立处理 |
| `GET /api/v1/flows`、`GET /api/v1/flows/{code}/schema` | 已授权的发布流程与审批数据定义，需 API Key |
| `GET /api/v1/instances/{id}`、`GET /api/v1/instances?flowCode=&businessKey=` | 本接入应用的审批单详情和历史，需 API Key |
| `POST /api/v1/instances` | 传审批数据快照发起；已在途同号返回原审批单，需 API Key |
| `POST /api/v1/instances/batch` | 1–100 张逐单独立发起，逐项返回成功审批单或业务错误 |
| `POST /api/v1/instances/{id}/terminate` | 所属应用用 API Key、已绑定实际操作人和原因终止运行中或等待发起人处理的在途审批 |
| `GET /api/v1/tasks/count?user=`、`GET /api/v1/tasks?user=` | 按本应用已绑定人员读取待办数量和列表，需 API Key |
| `POST /api/v1/identity/login`、`/identity/refresh`、`/identity/change-password` | 工号密码登录、刷新令牌轮换、首次或日常改密 |
| `POST /api/v1/identity/provision`、`/identity/revoke-all` | 本地组织管理员开通账号、作废人员全部令牌 |
| `GET /api/v1/notifications/preferences`、`PUT /api/v1/notifications/preferences/{category}`、`GET/PUT /api/v1/admin/notifications/daily-digest` | 个人提醒类别开关和管理员每日汇总时间 |
| `POST /api/v1/embed-tokens`、`/embed-tokens/exchange` | 已认证接入应用为已绑定人员签发一次性嵌入令牌并交换登录态 |
| `POST /api/v1/org/changes`、`/org/bindings` | 接入应用推送本应用来源的组织数据并绑定自身人员、部门 ID |
| `POST /api/v1/admin/org/import`、`/admin/org/bindings`、`GET /api/v1/admin/org/template` | 本地组织管理员预检查、导入 CSV/XLSX、手工绑定、下载模板 |
| `POST /api/v1/admin/actions/retry` | 本地登录的所属流程管理员以原事件 ID 人工重试自动动作 |
| `POST /api/v1/files?fileName=...` | 原始字节上传附件；支持接入应用 API Key 或可信人员会话 |
| `GET /api/v1/files/{id}` | 按应用归属或人员权限验权下载；必要时短时跳转 |

所有响应都带 `X-Request-Id`、`X-Trace-Id`；出错统一返回 `{code, message, errors, traceId}`（见 [docs/contracts.md](../docs/contracts.md)）。

可信组织导入或推送把人员停用时，同一事务写入管理员交接提醒和持久离职任务；后台工作者在重启或失败退避后可继续扫描该人员的在途待办，逐项提交引擎离职命令。命令按任务所在发布节点的配置选择在职直属上级或流程管理员；任务投影、操作事件和待发项在单审批单事务中保存，若无有效接手人则保留原任务与待处理交接提醒，补齐组织数据后自动重试。人员在任务执行前复职时保留本人待办，并取消过期的离职交接提醒。
身份访问令牌为 15 分钟 JWT，刷新令牌可轮换且有效 30 天；首次开通账号后只能先改密码。嵌入令牌 5 分钟内只能交换一次，必须先通过接入应用 API Key 验证并找到本应用绑定。可信人员离职、复职或外部人员绑定换人时，在组织事务内单调递增旧、新账号的令牌版本；一次性嵌入令牌也保存签发版本，旧访问、刷新和未兑换令牌不会因状态恢复而复活。接入应用的已授权管理用例调用 `ClientAppAppService.SetEnabledAsync` 或 `RotateSecretsAsync` 时原子递增应用身份代际；签发、兑换、刷新和访问嵌入令牌均核对代际，停用再启用或轮换密钥后旧令牌永久失效，本地会话不受影响。接入应用管理 HTTP 路由要求本地组织管理员权限，不能直接修改数据库启用标记。嵌入会话不能使用组织管理接口，刷新后仍保留应用来源。组织主数据存部门、人员、角色、外部 ID 绑定与每次变更的前后快照；CSV/XLSX 模板可同时导入主数据及外部绑定，预览一次返回可定位的组织问题，正式保存会在事务中按最新主数据重验。组织管理接口要求本地登录 JWT 及当前 `organization-admin` 角色；动作人工重试按审批单所属流程单独授权。首位管理员须在服务器本机交互式运行 `dotnet run --project backend/src/ApprovalService.Api -- bootstrap-admin`：输入内部人员 ID、工号、姓名及两次临时密码；已有在职人员可将工号和姓名留空。命令先应用数据库迁移，再在串行化事务中开通账号和角色；一旦已有管理员角色即拒绝重复执行，不提供匿名 HTTP 初始化入口。

## 审批中心查询

`CenterQueryAppService` 提供当前人员的待办、已办、抄送和发起/代发列表及只读详情，分页与总数基于同一授权查询。只接受 JWT，本地会话跨应用按参与关系读取，嵌入会话再限定所属应用；API Key 不能替代登录。当前原审批人与代理人均可查看 pending 任务；历史实际处理和已送达抄送复用持久参与者投影。无关管理员、未来/取消任务不能直接取得保密详情，不可见与不存在统一 404。

详情任务的 `allowedActions` 由纯引擎从当前运行状态和锁定发布图按登录人员计算，列表另给可批量同意的实际任务 ID。`CenterActionAppService` 在写入前再次检查查询授权、状态、节点许可、组织目标和审批人字段；同意前照常调用所属应用的校验钩子，最终走 `IApprovalRuntimeAppService` 的统一命令入口。批量同意逐项返回成功或业务错误，已成功任务不因另一项失败而回滚；中心不修改业务数据快照。

列表支持流程、状态、发起时间和标题/审批编号/业务单号搜索，加急优先。摘要与详情递归过滤隐藏字段及明细列；详情不暴露运行状态、原始事件载荷和暂存意见。桌面前端已通过本地工号密码和短期 JWT 接入真实列表、详情及人工任务写入口，令牌只在页面内存保存并在到期前轮换；首次登录先改密。中心查询返回真实人员、来源、当前人工环节，以及与已授权数据逐项匹配的锁定发布版本字段名称和明细列；隐藏字段的名称同样不返回。附件元数据只从已授权的顶层及明细附件字段提取，并核对文件仍关联当前审批单；内容下载走现有独立验权接口。读标记、字段内容搜索、完整自定义详情排版和个人设置仍需接入，任务保持进行中。完整参数与返回结构见契约文档。

## 流程定义

正式流程定义模型位于 `src/ApprovalService.Contracts/Flows/`，只接受统一自由画布的 JSON；`schemaVersion` 标识格式版本。`src/ApprovalService.Engine/Definitions/` 负责在发布前检查设计内容并生成不含画布坐标的执行图。可导入的 BPMN 文件必须由本服务导出且携带本服务扩展信息。开发样例放在 `samples/flows/`，完整字段约定见 [docs/contracts.md](../docs/contracts.md)。

流程级 `withdrawBeforeFirstDecision` 和 `returnMode` 是可省略的发布策略：旧定义分别按“审批结束前可撤回”和“退回后从头重提”处理。业务系统发起时把编译图中的值固定到审批单操作状态，不接受每次操作临时更改。

`src/ApprovalService.Engine/Execution/EngineRuntime` 是运行时的统一命令入口：输入已发布执行图、当前状态、命令和固定时间、连线条件结果、到节点时的组织快照，返回新状态与只追加事件。入口不修改传入状态；审批单 ID 与请求 ID 一起用于幂等及确定性生成令牌、任务、事件 ID。自动动作和子流程只产生待发事件，外部执行结果再以命令送回。包容网关按本次分叉实际选中的边汇合，回路累计推进超过 200 步后标记异常。撤销只接受已通过的审批单；审批中可撤回或终止。

节点推进通过 `Abstractions/INodeBehavior` 返回有限的状态动作，内置节点各有独立实现。行为读取的是状态、执行图、节点和上下文的隔离副本，不能修改真实输入；扩展人工节点创建任务时须指定同意、办理完成或确认已阅中的一种完成命令，可声明是否允许拒绝，命令入口据任务快照执行。新增运行节点可在 `EngineContext.NodeBehaviors` 注册纯行为，无须修改令牌推进核心；节点若要从设计画布发布，还须在流程定义发布校验中登记对应语义。

`Assignees/` 提供 14 类、15 个编码的找人来源（岗位与职级分别编码），由调用方提供本次组织、审批数据、矩阵命中与业务接口预取结果；引擎不直接访问外部系统。到达人工作业节点时重新取人，和预览不同时写事件。`Policies/` 按依次、会签、或签、票签计算任务结果；缺人、本人、重复、停用人员按节点配置处理。输入快照与扩展策略隔离，未识别的规则编码会报校验错误。组织存储与业务取人 HTTP 钩子已接入，纯引擎只消费可信预取结果。

`Commands/` 扩充同一个 `EngineRuntime.Execute` 入口：人工决定、退回和重新提交、转交、前后及并行加签、减签、征询回复、补充、评论与 @、暂存、抄送、催办、管理员干预都先校验状态、操作人、节点许可与意见要求，再修改状态并发事件。管理员不能直接跳到并行或包容汇合网关。事件的 `Audit` 带实际操作人、代谁处理、意见和干预原因；`EngineState.Operations` 与任务代理人信息必须随运行状态一起持久化。委托在节点到达时按固定时间和流程范围生效，旧待办可单独转给代理人；只解析一层，原审批人仍可处理。退回方式和撤回限制在发起时固定，不能靠后续请求临时更改。

应用层必须从已认证身份填入 `OperationInput.ActorId`，独立核实管理员身份后才设置 `IsAdministrator`，核实另一起撤销审批已通过后才设置 `OperationContext.CancellationApproved`。批量同意的纯计算结果须由应用层在一个事务中保存；跨审批单的离职交接和修改流程定义里的指定人员也由应用层协调。引擎只生成催办、抄送、重发和撤销审批申请的待发事件，不直接调用外部系统。

`src/ApprovalService.Engine/Expressions/` 提供纯逻辑的表达式解析、审批快照逐字段校验、JSON 样例生成和人民币大写。条件与计算字段用 `${fieldKey}` 引用快照，例如 `SUM(${items.amount}) >= 1000 AND ${initiator.level} >= 3`；明细列聚合、`IF`、`ANY` 和 `DATEDIFF` 的边界见契约文档。发起时可调用 `ApprovalDataValidator.Validate(definition, data)` 获得 `data.items[0].qty` 这样的错误路径，调用 `CreateExampleJson(definition)` 为 schema 接口生成示例。审批编号格式规则在领域层 `ApprovalSerialNumber.Format`，调用方必须先分配唯一流水号并传入时间；纯规则本身不抢号、不访问数据库。

审批中的业务数据 PATCH 只接受审批单所属应用和本应用已绑定的操作人。锁定发布定义中的字段需显式开启 `businessUpdateAllowed`（默认关闭）；服务按字段授权合并并校验类型、重算公式和后续路由，已到达任务不改派。新快照与 `businessDataUpdated` 的改前改后审计、路由映射在同一乐观版本事务中提交，同一请求 ID 可安全重试。

退回发起人或要求补充后，所属应用可代表已绑定的原发起人提交完整新数据。服务复用锁定发布定义校验与公式、以当前可信组织刷新找人映射，随后经同一引擎入口原子恢复审批。退回重提清空独立的审批人填写当前值投影，补充保留；历史操作事件始终保留原填写值。开放审批单详情同时返回业务 `data` 与独立的 `approverFieldValues`。

开放流程预览使用已发布定义、当前组织和与发起相同的审批数据在内存中运行纯引擎：按同意/成功路径逐步模拟，返回预计到达节点及当前候选审批人。业务接口找人只在模拟实际命中的节点预取；业务单号可省略，钩子此时收到空业务单号。预览不创建审批单、占用业务单号或产生待发项；实际到达节点时仍以最新组织和业务数据重算。

## 配置

`appsettings.json` 的 `ApprovalService` 节：

| 配置 | 默认 | 说明 |
| --- | --- | --- |
| `Data:Provider` | `Sqlite` | 数据库：Sqlite、SqlServer、PostgreSql（MVP 用 SQLite） |
| `Data:Directory` | `data` | 数据目录：SQLite 库、本地附件、备份 |
| `Data:ConnectionString` | — | SQL Server、PostgreSQL 的连接字符串，不要提交到仓库 |
| `Integration:AllowHttpUrls` | `false` | 仅受信任内网部署显式开启；默认回调与钩子地址必须用 HTTPS |
| `Integration:SerialPrefix`、`SerialDatePattern`、`SerialDigits` | `SP`、`yyyyMM`、`6` | 部署级审批编号默认格式；流程级配置由管理后台后续接入 |
| `Identity:SigningKey` | 数据目录内随机密钥文件 | 可选的 Base64 JWT 签名密钥；多实例部署须配置同一密钥 |
| `Integration:MasterKey` | — | 接入应用签名密钥加密所需的 32 字节 Base64 主密钥；不要提交仓库 |
| `Files:Provider` | `Local` | 新上传附件的存储：`Local` 或 `Minio`；历史文件按元数据读取 |
| `Files:MaxUploadBytes` | `20971520` | 单文件大小限制，默认 20 MB |
| `Files:Endpoint`、`Files:Bucket` | — | 使用 MinIO 时的绝对服务地址和私有桶名 |
| `Files:AccessKey`、`Files:SecretKey` | — | 仅从部署环境提供的对象存储凭据，不要写入仓库 |
| `Files:BrowserAccessible` | `false` | 浏览器可直连时签发短时地址，否则服务转发 |
| `Files:PresignedUrlSeconds` | `300` | 预签名地址有效秒数，只能设为 1—300 秒 |

## 管理后台读取接口

本地组织管理员通过 `GET /api/v1/admin/session` 核验当前身份，`GET /api/v1/admin/org/directory` 读取部门与人员摘要，`GET /api/v1/admin/apps` 读取接入应用的非敏感配置。三个接口逐项核对当前组织管理员角色和本地密码会话；应用列表仓储只投影名称、编码、授权流程和状态，数据库中的 API Key 摘要及签名密钥密文不会进入响应。组织 Excel/CSV 导入继续使用已有预检与同事务审计接口。

接入应用允许嵌入来源由本地组织管理员通过 `GET/PUT /api/v1/admin/apps/{code}/embed-origins` 配置。PUT 提交完整来源列表及变更原因；服务端限制 HTTPS 原点、本机开发 HTTP、数量和长度，并在同一数据库事务中记录操作者及不含密钥的前后快照。并发修改返回冲突，管理页须重新读取后再保存。

`GET /api/v1/admin/bindings` 读取人员/部门外部 ID 绑定安全摘要；手工绑定沿用 `POST /api/v1/admin/org/bindings`，包含当前管理员身份和事务内审计。`GET /api/v1/admin/audit` 仅投影最近 200 条组织、接入应用及回调重发变更的元数据和显式原因，不返回前后快照。三个接口都逐项应用本地组织管理员权限。

`GET /api/v1/admin/outbox` 只投影最近 200 条待发项元数据，不加载发送正文。`POST /api/v1/admin/callbacks/{eventId}/retry` 要求本地组织管理员填写原因，原地重排回调并在同一事务留审计；有效租约中的发送不可重排。已完成的回调也可按原事件 ID 再次投递，接入应用负责去重。

接入应用管理页通过 `POST /api/v1/admin/apps` 登记编码、名称、授权流程、回调与四类钩子地址；`PUT /api/v1/admin/apps/{code}/enabled` 启停，`POST /api/v1/admin/apps/{code}/rotate-secrets` 轮换凭据。每次管理变更要求原因并与脱敏审计同事务提交。创建和轮换响应中的 API Key 与签名密钥只显示一次，列表及日志不提供密钥；轮换或实际启停会使旧嵌入会话失效。

## 审批中心同源托管

正式部署先构建 `frontend/`，再发布 `ApprovalService.Api`，将 `frontend/dist/` 的内容复制到发布目录的 `wwwroot/`。服务默认从自身内容根目录的 `wwwroot/index.html` 提供页面；也可用环境变量 `ApprovalService__Frontend__DistPath` 指向完整的前端构建目录。生产环境找不到 `index.html` 时启动失败；开发环境未配置构建目录时可单独运行 API。前端文件必须经本服务或等效保留安全头的同源反向代理提供，不可另开放一个不受策略保护的静态入口。

普通页面为 `/`，响应禁止被 iframe 嵌入。接入应用专属入口为 `/embed/{clientAppCode}`，只允许该应用登记的来源嵌入；来源配置不包含路径、查询或通配符，未配置时仅允许同源。业务系统应从后端签发一次性令牌，并把它放在片段里，例如 `/embed/erp#/center/approvals/instance-id?embedToken=...`。片段不会发送到服务器；页面兑换后立即清理。审批中心的接口与资源同源，嵌入上下文接口仅接受该应用的已验证嵌入 JWT。直接访问 `/index.html` 等静态 HTML 仍为禁止嵌入，不能绕过专用入口。

## 工作日历基础

`Domain/Notifications/Calendar` 提供纯日历规则：默认周一至周五为工作日，调用方可传入本地日期覆盖清单，将节假日设为休息日、调休周末设为工作日。`GetWorkingDateAfter` 从指定日期之后计数，不把起点算作第一个工作日；`GetLocalDate` 按指定时区将时间点换成本地日期，避免使用服务器本地时区或固定 UTC 偏移。配置清单由调用方提供，规则本身不访问数据库。

`GetDueAt` 从任务开始的实际时间点累计节点 `DueMinutes`，只计入工作日每天的工作时段，默认 09:00–18:00，也可以传 `WorkCalendarWorkingHours` 指定当天的起止时间。提前开始从当日开工起算；下班后、周末和节假日顺延到下个工作时段；恰好在下班时到期则保留该时刻。夏令时缺口中的时段边界顺延到第一个有效本地分钟，重复的开始边界取较早一次、结束边界取较晚一次，累计的是实际经过的分钟。通知任务负责提供日历配置、把结果写入任务期限并安排提醒。

审批任务实际进入待办时，服务根据该审批单锁定的发布节点时限计算并保存 `DueAt`；依次审批中尚在排队的任务到激活时才开始计时，既有待办的期限不会因后续操作重新计算。部署配置 `ApprovalService:Notifications:Calendar` 可指定 `TimeZoneId`（默认 `Asia/Shanghai`）、`WorkingStart`/`WorkingEnd`（默认 `09:00`/`18:00`），以及 `Overrides` 中形如 `2026-10-01: false`、`2026-10-11: true` 的节假日和调休日期。无效时区、时间或日期会在启动时拒绝。待办激活时同事务排定到期前一个工作小时和到期两条延迟待发项；发送前重读任务状态与期限，已完成或重排期的旧提醒不会发送，重试按稳定消息 ID 去重。

## 持久化与后台任务

`ReportingFactsRepository` 提供内部只读统计事实：按审批单发起时间的 UTC 半开区间、流程编码、来源应用和发起部门快照筛选，返回最小化的审批单与任务字段，并从只追加的 `taskCreated`、`taskActivated` 事件还原任务开始处理的时间。读取结果不包含审批标题、业务数据或附件；缺少真实事件时不推断处理起点。

`ReportingMetricsCalculator` 对调用方提供的完整事实集合按 UTC 半开区间计算流程、节点和人员内部指标：发起量按 `StartedAt`，完成量与历时按 `EndedAt`，部门按发起时快照；P90 仅对已结束审批单使用最近秩算法。任务处理时长从激活事件开始，初始待办从创建事件开始；排队任务不计入当前待办。逾期分母是到期时间落在筛选区间且观察时已到期的任务，未设置期限的任务不计入。
`ReportingFactsRepository.ReadForMetricsAsync` 已增加跨期事实读取：选择筛选期内发起、结束、任务完成、到期时间已过或当前仍有待办的审批单，再统一按流程、来源应用和发起部门快照过滤。旧 `ReadAsync` 仍只按发起时间读取，供现有调用方使用。当前待办来自现时任务投影，不能用过去的观察时刻重建历史待办。正式报表仍须完成小时汇总、管理范围和保密授权后才能开放 HTTP 查询和导出。

流程元数据、不可变的发布版本、审批单、任务投影、操作记录、待发队列和定时任务使用 EF Core 保存。SQLite 在启动时自动创建数据目录并执行迁移；SQL Server 和 PostgreSQL 使用各自的迁移，配置连接串后按部署流程执行。三种数据库的模型与迁移一致性由检查程序核对，设置 `APPROVAL_TEST_SQLSERVER` 或 `APPROVAL_TEST_POSTGRES` 后还会在对应测试数据库执行发起和审批场景。

审批命令由应用服务加载固定的流程版本和引擎状态，以 `Version` 乐观锁一次写入新状态、任务、只追加事件及待发项。事件按顺序串接 SHA-256 校验值，审批单保存最终序号和哈希锚点，可检查历史记录被改写或末尾被删除；校验时在同一数据库事务快照内读取锚点和事件。后台任务使用数据库租约抢占工作；进程重启或租约到期后可以接着执行，执行超过租约的旧工作者不能再确认完成，领取后回读仍核对本次租约，失败按退避表重试并只保存有界错误码，成功后清除旧错误码。接入应用的 API Key 只存 SHA-256 摘要，回调签名密钥使用部署主密钥加密。回调正文在审批事务中固定，后台发送器用 HMAC-SHA256 签名、5 秒超时；每次发送留不含正文的结果日志，管理用例可重排原事件且不改变 `eventId`。四类业务钩子按已采纳的 ADR 0010 使用 POST JSON、双向 HMAC、请求 ID 回显与 5 秒超时；目前发起前取数、发起前和同意前校验已接通，取人与自动动作待后续接线。

通知传输基础在 `Notifications/`：新待办、催办、审批结果、抄送和评论提及依实际引擎事件规划，收件人须是能查看该审批单的在职人员；保密流程只对发起人、代发人、当前待办人和实际历史参与人发送。站内和邮件待发项与审批状态、任务、事件一起提交，发送失败由持久队列退避重试。后台处理器在事务外选择 `INotificationChannel`，发送 SMTP 邮件或按稳定 ID 写入站内收件箱；邮件正文包含审批编号、标题及需登录的审批中心链接，不含免登录令牌。邮件在 SMTP 接收后、待发确认前故障时可能重发，带稳定的 `X-Approval-Notification-Id` 供下游去重。个人可按类别分别关闭邮件或站内提醒，投递前再次读取最新开关；组织管理员可配置每日待办汇总的当地 `HH:mm` 时刻，未配置则不发送，每人每天最多一封且可单独关闭。

在途业务单号按全局 `(flowCode, businessKey)` 唯一，接入应用不能把其他应用占用的单据当作幂等结果。审批详情在同一数据库快照读取状态、任务和事件。出站地址默认要求 HTTPS，回调应用停用时保留待发项并记录原因；HTTP 重定向不会把签名正文转发到其他地址。

开放发起要求本应用已绑定的在职发起人；可选的代发人、发起部门和自选审批/抄送人员也须先绑定。指定发起部门必须是发起人的所属部门，流程配置的允许部门只以可信组织主数据判断。流水号按部署级默认格式持久分配，流程级格式后续由管理后台接入。

开放撤回、催办和人工任务操作在接入应用归属与操作人绑定双重校验后交给统一引擎；任务 ID 含斜杠时通过路径编码传递。发起时自选人员与审批数据找人映射持久保存，后续节点到达仍以最新组织与矩阵计算。人工任务可提交当前节点显式可写的顶层审批人字段，经类型与本应用绑定校验后只记操作审计和相关回调，不覆盖业务数据快照；明细子字段和附件在相应协议接通前拒绝。节点显式可写的必填审批人字段只在同意、办理完成和确认已阅时强制填写；征询回复在原发问任务完成后拒绝。

发起时把锁定发布图的撤回限制与退回重提方式固定到运行状态，旧图缺省仍保持原行为。待办摘要按当前节点隐藏字段过滤，任务意见在后续节点推进后仍保留。征询回复只接受原发问任务上的受询人意见，不接受审批人填写字段；退回发起人和要求补充会产生可重试的固定回调正文。

审批人撤销同意后，原审批任务恢复待办，任务投影清空已撤销的意见与完成时间；原操作意见仍在事件链。回调增加 `task.restored`，仅该事件带原任务 `taskId`，让业务系统刷新待办；待发正文和事件 ID 按既有重试机制保持不变。

发起接口只接受当前应用已有人员/部门绑定，由服务端加载组织快照、重算公式和分支条件；编号以数据库唯一键和乐观版本分配，同日同前缀并发不会重号。编号先分配再提交审批，发起校验或并发冲突后允许留空号。只传单号时由本应用取数钩子取得业务数据；已配置的校验钩子在发起或同意的写事务前调用，不通过则不保存部分审批。业务取人只在引擎实际到达目标节点时用同一命令和固定时间有界重算，返回人员必须是当前应用已绑定的在职人员；自动节点先持久化待发事件，后台任务在审批事务外调业务动作，成功后以原事件 ID 幂等推进引擎，失败则保留待发项重试。

业务动作成功响应的可选 `result` 进入独立的 `actionCompleted` 事件与按原动作事件 ID 保存的运行投影；后续审批终态回调可选带 `actionResults`，原审批数据 `data` 不变。

业务自动动作第十次失败转 `needsAttention`，停止后台自动领取，保留原令牌和待发正文。本地登录的流程管理员以当前工号匹配事件审批单所属流程的 `AdministratorId` 后，可向管理路由提交 `{eventId}`，按原事件排入一次人工重试；再次失败仍待管理员处理。组织管理员角色不自动拥有其他流程权限。

新增或修改实体配置后，用 `backend/tools/add-migration.ps1` 分别为 SQLite、SQL Server、PostgreSQL 生成迁移；不要手改生成的迁移或模型快照。

附件的文件字节不入数据库，`StoredFiles` 只保存名称、大小、内容类型、SHA-256、存储位置、上传人员或接入应用归属、所属审批单和清理租约。本地内容在数据目录的 `files/年/月/` 下，上传临时文件同目录原子转正。每小时扫描一次，清理上传超过 24 小时仍未关联的内容；多实例通过数据库条件更新抢租约。切换到 MinIO 时，先配置 `ApprovalService__Files__Provider=Minio` 和对象存储凭据，再运行 `powershell -NoProfile -ExecutionPolicy Bypass -File backend/tools/migrate-files.ps1` 搬运旧本地文件；中断后重跑会继续。真实对象存储检查需要设置 `APPROVAL_TEST_MINIO`（服务 URL）、`APPROVAL_TEST_MINIO_BUCKET`、`APPROVAL_TEST_MINIO_ACCESS_KEY`、`APPROVAL_TEST_MINIO_SECRET_KEY`，桶需事先存在。

上传白名单是 PDF、PNG、JPEG、GIF、WebP、DOCX、XLSX、PPTX；Office 文件须具有有效包清单、入口关系和主部件 XML，只下载查看，不做在线预览。嵌入会话只可下载同一接入应用审批单的已关联附件；人员上传的未关联文件仅本人本地会话可读，应用上传的仅同一已认证应用可读。接入应用可用 API Key 上传并读取本应用尚未关联的附件；发起审批时只接受该应用上传且未被占用的文件，附件与审批单在同一数据库事务中关联。普通抄送到达、人工抄送及实际任务完成的人员按来源写入只追加参与人投影；附件权限使用发起人、实际代发人、任务及此投影，保密版本不会让仅有已取消或转交任务的人员继续查看，管理员与额外查看人也无自动权限。旧版本缺少保密标记时仍按非保密处理。OFD 和旧版 Office 文件的下载范围尚待单独确认。

服务启动时校验这三项：`Provider` 只接受表中的三个值，`Directory` 不能留空，使用 SQL Server 或 PostgreSQL 时必须提供非空的 `ConnectionString`。配置错误会让服务启动失败，诊断会指出配置键，不打印连接字符串。`/health/ready` 会检查数据库连通性；数据库不可连接时返回 503。
