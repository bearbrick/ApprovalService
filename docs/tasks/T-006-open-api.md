# T-006 开放接口、接入应用、回调和钩子

状态：进行中
负责人：Codex 2026-10-20
里程碑：M4
依赖：T-004、T-005
设计书：§integration、§api

## 目标

业务系统能发起（传数据或只传单号）、查询、更新数据、撤回、重新提交、催办、终止；拿待办数量和列表、做审批操作；回调和钩子经出站适配层可靠送达。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Contracts/Protocol/`：开放接口的请求和响应、新错误码
- `backend/src/ApprovalService.Contracts/Protocol/CallbackNotification.cs`、`CallbackEventCodes.cs`：撤销同意恢复原待办的兼容回调事件码及可选任务 ID；其他回调正文格式不变
- `backend/src/ApprovalService.Contracts/Flows/FlowDefinition.cs`、`FlowExecutionGraph.cs`、`FlowKinds.cs`、`frontend/src/model/flow.ts`、`backend/tests/ApprovalService.Backend.Checks/ContractChecks.cs`：合入已验收的流程级撤回/退回策略契约
- `backend/src/ApprovalService.Engine/Definitions/FlowCompiler.cs`、`FlowSemanticValidator.cs`、`backend/tests/ApprovalService.Backend.Checks/FlowDefinitionChecks.cs`：仅从 T-034 已验收提交取发布编译、语义校验与场景检查；完整前端设计器由 T-036 集成
- `frontend/src/model/flow-execution.ts`、`frontend/src/model/flow.ts`：仅从 T-034 已验收提交取独立的执行图类型拆分，保持源码少于 500 行和旧导入路径兼容
- `backend/src/ApprovalService.Api/Endpoints/OpenApi/`；`Application/Integration/`（接入应用、API Key 只存 SHA-256、流程授权）
- `backend/src/ApprovalService.Infrastructure/Integration/`：出站适配层（HMAC 签名、5 秒超时、按 1 分钟 / 5 分钟 / 30 分钟 / 2 小时重试、发送日志）、钩子调用（取数、校验、取人、业务动作）
- `backend/src/ApprovalService.Engine/Execution/EngineWork.cs`、`EngineRuntime.cs`、`EngineResults.cs` 与 `backend/src/ApprovalService.Engine/Assignees/BusinessApiAssigneeResolver.cs`、`AssigneeModels.cs`：仅为业务接口找人增加纯计算的“实际命中节点待预取”结果，发现缺失时不提交状态，补齐可信快照后以同一命令重算；不在引擎做网络访问
- `backend/src/ApprovalService.Application/Runtime/ApprovalRuntimeAppService.cs`、`backend/src/ApprovalService.Application/Integration/OpenStartContextBuilder.cs`、`OpenRoutingInputs.cs`、`BusinessHookAppService.cs`、`BusinessAssigneeAppService.cs` 与 `backend/src/ApprovalService.Infrastructure/Integration/EngineEventOutboxHandler.cs`：事务前按实际节点取人、在路由快照保存发起时本应用外部人员 ID、提交后持久业务动作及同一事件 ID 的结果回写
- `backend/src/ApprovalService.Contracts/Flows/FlowPresentation.cs`、`frontend/src/model/flow.ts`、`backend/src/ApprovalService.Contracts/Protocol/OpenApiRoutes.cs` 与新增开放请求/响应模型：字段级审批中业务更新开关（默认关闭）、PATCH、外部重新提交及预览的兼容契约；前端只同步流程定义镜像，不增加业务界面
- `backend/src/ApprovalService.Engine/Commands/OperationModels.cs`、`OperationGuard.cs`、`OperationDispatcher.cs`、`backend/src/ApprovalService.Engine/Execution/EngineResults.cs`、`EngineRuntime.cs`：审批中数据更新走统一命令和只追加事件，记录改前改后，不让外部请求直接改引擎状态；退回重提/补充字段当前值策略按已确认语义处理
- `backend/src/ApprovalService.Engine/Commands/OperationUndoFrame.cs`：PATCH 增加命令/审计字段后按文件规模限制拆出既有撤销同意状态载体，不改变其行为
- `backend/src/ApprovalService.Engine/Commands/OperationStateCopy.cs`、新增审批人当前填写值投影辅助文件：重提清空、补充保留，历史审计不变
- `backend/src/ApprovalService.Engine/Execution/EngineModels.cs`、`EngineResults.cs`、`backend/src/ApprovalService.Application/Integration/CallbackNotifications.cs`：自动动作可选 result 在纯命令、事件与运行投影中独立留痕，并在后续结果回调中携带
- `backend/src/ApprovalService.Infrastructure/Jobs/OutboxQueue.cs`、`JobStatuses.cs`、`backend/src/ApprovalService.Infrastructure/Integration/ActionRetryRepository.cs`、`backend/src/ApprovalService.Domain/Integration/IActionRetryRepository.cs`、`backend/src/ApprovalService.Application/Integration/ActionRetryAppService.cs`、`backend/tests/ApprovalService.Backend.Checks/JobChecks*.cs`：自动动作第十次失败停自动重试，管理员用例可用同 eventId 触发一次人工重试；暂不新增权限边界未定的管理 HTTP 路由
- `backend/src/ApprovalService.Infrastructure/Jobs/ActionOutboxPayload.cs`：按持久事件的规范 JSON 判别自动动作，避免其他待发类型误用十次上限
- `backend/src/ApprovalService.Domain/Integration/IActionRetryRepository.cs`、`backend/src/ApprovalService.Infrastructure/Integration/ActionRetryRepository.cs`、`backend/src/ApprovalService.Application/Integration/ActionRetryAppService.cs`、新增管理路由和 `backend/tests/ApprovalService.Backend.Checks/ActionRetryEndpointChecks.cs`：按审批单所属流程的现有流程管理员配置核对当前本地登录人员，再允许原事件人工重排
- `backend/src/ApprovalService.Application/Integration/OpenApprovalDataPreparer.cs`、`OpenStartContextBuilder.cs`、`OpenRoutingInputs.cs`、新增数据更新/重新提交/预览应用服务、`backend/src/ApprovalService.Application/Runtime/ApprovalRuntimeAppService.cs`：锁定发布版本校验字段授权与数据，重算公式和后续路由，在原子运行提交中保存；预览只做纯计算，不落审批单
- `backend/src/ApprovalService.Api/Endpoints/OpenApi/`、`backend/tests/ApprovalService.Backend.Checks/OpenApiChecks*.cs`、`EngineScenarioChecks*.cs`：开放路由及跨应用、未授权字段、并发、审计、重提和预览场景
- `backend/tests/ApprovalService.Backend.Checks/EngineScenarioChecks*.cs`、`AssigneeChecks.cs` 与 `OpenApiChecks*.cs`：分支实际到达取人、严格绑定、动作重试和结果恢复场景
- `backend/src/ApprovalService.Domain/Integration/`：接入应用、流程授权、加密密文与出站发送记录实体和仓储接口
- `backend/src/ApprovalService.Domain/Runtime/IApprovalRuntimeRepository.cs`、`backend/src/ApprovalService.Infrastructure/Persistence/ApprovalRuntimeRepository.cs`：审批单和任务查询、在途唯一性与数据更新所需仓储能力
- `backend/src/ApprovalService.Domain/Runtime/ActiveInstanceKeyEntity.cs`：跨三种数据库保证在途“流程编码 + 业务单号”唯一的独立键
- `backend/src/ApprovalService.Domain/Runtime/InstanceEntity.cs`：保存发起时规范化的自选审批/抄送人与审批数据找人标识；后续节点到达重新读取最新组织与矩阵时仍保留业务选择
- `backend/src/ApprovalService.Application/Runtime/`：现有运行应用服务的发起、查询和更新数据接入
- `backend/src/ApprovalService.Engine/Commands/OperationModels.cs`、`OperationGuard.cs`：审批人填写字段的纯命令/事件审计承载，不改变引擎流转规则
- `backend/src/ApprovalService.Engine/Execution/EngineRuntime.cs`：仅放宽已授权终止命令可处理 `awaitingInitiator` 在途状态；撤回、审批及其他命令仍维持原状态限制
- `backend/src/ApprovalService.Engine/Commands/OperationDispatcher.cs`：核对征询回复任务与允许填写字段的实际任务一致，防止借另一任务的权限写审计字段
- `backend/tests/ApprovalService.Backend.Checks/OperationChecks.cs`、`OperationChecksBoundaries.cs`：征询回复合法路径与跨任务冒权回归场景
- `backend/src/ApprovalService.Application/Integration/OpenActionFieldValidator.cs`：节点可写字段白名单和类型校验，复用已有审批字段验证器
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`、`Migrations/`：接入应用实体、索引和出站记录的三种数据库迁移
- `backend/src/ApprovalService.Infrastructure/Options/`：签名密钥加密主密钥等接入配置
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`：新错误码的副本
- 检查程序 `OpenApiChecks*.cs`：用一个假的业务系统后端
- `backend/tests/ApprovalService.Backend.Checks/ContractChecks.cs`：核对新增回调编码与契约文档一致
- `backend/README.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`：同步开放接口和接入实现
- `docs/adr/0010-outbound-hook-protocol.md`：未定义的四类业务钩子协议提议，确认前不实施

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/contracts.md`：写全请求、响应、回调格式
- `backend/src/ApprovalService.Contracts/**`
- `backend/src/ApprovalService.Contracts/Protocol/BusinessHookModels.cs`：补齐业务取人/动作的请求响应模型
- `ApprovalErrorCodes.cs`
- `Program.cs`（Api、检查程序）
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：接入服务登记
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`：新增实体与索引所需模型登记
- `docs/architecture.md`：接入应用持久化和出站可靠发送说明
- `docs/adr/README.md`：登记 T-006 钩子协议提议 ADR
- `backend/src/ApprovalService.Contracts/Flows/FlowDefinition.cs`、`FlowExecutionGraph.cs`、`FlowKinds.cs`、`backend/tests/ApprovalService.Backend.Checks/ContractChecks.cs`、`frontend/src/model/flow.ts`：合入 T-034 已验收契约与检查
- `backend/src/ApprovalService.Contracts/Flows/FlowPresentation.cs`、`backend/src/ApprovalService.Contracts/Protocol/OpenApiRoutes.cs`、`frontend/src/model/flow.ts`、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Api/Program.cs`、`docs/contracts.md`、`docs/architecture.md`：本轮 PATCH、重提、预览兼容契约与登记；先完成独立 PATCH 切片并逐项同步
- `backend/src/ApprovalService.Contracts/Protocol/OpenQueryModels.cs`、新增重新提交请求、`OpenApiRoutes.cs`、应用服务与 API/检查登记、`docs/contracts.md`：本轮重提/补充独立切片增加可选当前审批人值投影和外部提交路由，保持既有字段兼容
- `backend/src/ApprovalService.Contracts/Protocol/CallbackNotification.cs`、`BusinessHookModels.cs`、`docs/contracts.md`：业务动作可选 result 的回调副本与现有动作响应契约同步，仅增加可选字段
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`docs/contracts.md`、`docs/architecture.md`：动作待处理重排服务登记和队列语义同步
- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 与新增管理请求模型、`backend/src/ApprovalService.Api/Program.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`、`docs/contracts.md`、`docs/architecture.md`：按流程管理员授权的动作人工重试管理接口及场景登记

## 验收标准

- [ ] 设计书第 13 节的审批服务接口全部可用（嵌入令牌、附件、打印除外）
- [x] “流程编码 + 业务单号”在进行中的审批里唯一，重复发起拿到已有的那张
- [ ] 只传单号发起时调取数接口；校验钩子不通过时挡住并返回原因；钩子超时有明确的错误
- [x] 回调带签名、失败按规则重试、可以手动重发；同一事件重发时 `eventId` 不变
- [x] `tasks/count` 能只算本接入应用的流程，也能算全部（按接入应用设置）
- [x] 性能初定：发起、审批 P95 ≤ 300 ms（不含钩子），待办数量 P95 ≤ 50 ms，写进交接记录
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 嵌入令牌和免登录地址（T-007）
- 附件（T-013）
- 打印（T-015）
- SDK 方法（T-016）

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

- 2026-10-23 · Codex：按用户确认完成流程管理员人工重试入口 `POST /api/v1/admin/actions/retry`，正文 `{eventId}` 可承载含斜杠的原事件 ID，成功返回 204。复用已持久的 `FlowEntity.AdministratorId` 流程管理员工号，仅当前在职、本地登录且工号与原事件审批单 `FlowId` 对应流程一致者可重排；组织管理员角色不自动跨流程获权。应用层校验后，数据库条件更新再次核对当时管理范围与 `needsAttention` 状态，避免权限或队列状态变化后误重排。真实 HTTP/SQLite 场景覆盖匿名、跨流程、仅有组织管理员角色、允许的管理员及重复请求；完整 `verify.ps1` 通过：后端 625、调用端 33、前端 65 项，零构建警告。Contracts、契约、设计书 §integration、架构与后端 README 已同步；上一轮授权阻塞解除，任务恢复进行中。T-006 仍余附件原子关联及需与身份任务协调的 `AuthGeneration`，不可标完成。
- 2026-10-23 · Codex：完成自动动作失败上限的内部切片：第十次失败后只将原 `actionPending` 待发项标为待管理员处理，暂停自动领取；其他回调待发项仍沿用原重试策略。内部管理用例仅在该状态下以原 `eventId` 重排同一待发项，每次人工操作只给一次发送机会，失败后再回待管理员处理，不生成新事件或修改业务数据。SQLite 队列场景覆盖第九/十次、停止自动领取及人工再失败；完整 `verify.ps1` 通过：后端 619、调用端 33、前端 65 项，零构建警告。本轮第一次全量验证仅本机性能基线的发起 P95 偶发为 335.3 ms，重跑后全量通过且发起/审批/计数分别为 176.9/20.2/6.0 ms。管理 HTTP 路由暂缓：现有 `OrganizationAdmin` 只代表本地组织管理员，设计中的流程管理员按范围授权尚无可复用权限点；建议定义可管理该实例所属流程的专用权限后再开放路由。按 R13 将任务标为阻塞，未把 T-006 标完成。另剩余附件原子关联及与身份任务联动的 `AuthGeneration`，交由主任务协调。
- 2026-10-23 · Codex：完成自动动作可选 `result` 独立切片：验签成功后以原动作 `eventId` 幂等回写，引擎的 `actionCompleted.actionResult` 事件和运行投影按该事件 ID 独立留痕；后续审批终态回调可选携带 `actionResults`，业务 `data` 不变。真实 SQLite/假签名业务端场景覆盖结果审计、固定回调与重领不重复出站，纯引擎场景覆盖状态不可原地改写。Contracts、契约、设计书、架构与后端 README 已同步；全量 `verify.ps1` 通过：后端 610、调用端 33、前端 65 项，零构建警告；5289 已释放。任务保持进行中，下一切片为动作 10 次自动失败转管理员待处理和管理员同 `eventId` 手动重试。
- 2026-10-23 · Codex：完成重提/补充独立切片：登记 `OperationStateCopy.cs`、当前值辅助文件及共享 Contracts 请求/响应/路径、DI/API/检查入口与契约文档；新增 `POST /api/v1/instances/{id}/resubmit`。只接受所属应用已绑定的原发起人和完整新数据；按锁定定义、公式、当前组织的可信路由映射预校验，再由引擎和运行仓储以同一乐观版本事务保存。成功推进时合并独立 `approverFieldValues` 当前值；退回重提清空、补充保留，历史事件不改。重提还清除上一轮“已同意/相邻同意”找人跟踪，防止默认相邻只审一次跳过新轮次首节点。HTTP/SQLite 场景覆盖权限、无效数据、公式和网关重算、幂等与原任务恢复；纯引擎场景覆盖当前值和历史留存。全量 `verify.ps1` 通过：后端 608、调用端 33、前端 65 项，零构建警告；5289 已释放。Contracts、契约、设计书 §api、架构和后端 README 已同步。T-006 继续进行中，剩余附件原子关联、动作可选 result/回调与 10 次失败转管理员处理及同 eventId 手动重试；接入应用 AuthGeneration 需与身份任务合拢，不能标完成。
- 2026-10-23 · Codex：完成流程预览 `POST /api/v1/flows/{code}/preview`：沿用发布流程授权、严格本应用发起人与自选人员绑定、字段类型和公式校验及当前组织快照，以纯引擎在内存里按同意/办理完成/外部动作成功路径逐步推进；只在预计实际命中业务接口找人节点时调用签名钩子。返回按到达顺序排列的节点及候选内部人员 ID/当前姓名、预计状态和到节点重算提示；业务单号可省略，此时取人钩子收到空值。不持久化实例、在途键、事件或待发项。真实 HTTP/SQLite 场景覆盖高低金额分支、姓名、未绑定发起人及零持久化；全量 `verify.ps1` 通过：后端 595、调用端 33、前端 65 项，零构建警告；5289 已释放。Contracts、契约、设计书 §api、架构及后端 README 已同步。T-006 保持进行中，其余未完成项见下一条 PATCH 交接。
- 2026-10-23 · Codex：完成审批中数据 PATCH 的可独立交付切片：锁定发布字段的 `businessUpdateAllowed` 默认关闭，所属应用和本应用在职绑定操作人双重校验，顶层/逐行明细子字段白名单、类型检查、公式与可信路由重算；统一引擎 `updateData` 命令在同一乐观版本提交中保存快照、映射与改前改后审计，同请求 ID 重试不重复落事件，已到达任务不静默改派。真实 HTTP/SQLite 场景覆盖跨应用、未绑定工号、禁用字段/公式/明细子字段、错误类型、公式和审计、后续排他网关按新金额选路。Contracts、前端镜像、契约、设计书 §form/#api、架构及后端 README 已同步；按规模限制拆出既有 `OperationUndoFrame`，行为未变。完整 `verify.ps1` 通过：后端 591、调用端 33、前端 65 项，零构建警告；5289 已释放。状态仍为进行中：开放重提/补充、流程预览、附件原子关联、自动动作可选 `result` 独立审计及结果回调、失败 10 次转管理员待处理并以原 `eventId` 手动重试尚未完成；重提所需审批人填写当前值投影尚缺，不能从历史事件推断。T-041/T-043 并行认领文件本次未触碰。
- 2026-10-22 · Codex：按用户要求在合适节点暂停，额度留给其他项目；本轮不再启动重提和预览。独立工作树 `C:\Users\dukun\.codex\worktrees\open-api\ApprovalService` 的稳定 HEAD 仍为 `0568558`，PATCH 草稿尚未提交：已加字段 `businessUpdateAllowed`（后端和前端定义，缺省 false）、`PATCH /api/v1/instances/{id}/data` 请求与路由、`OpenDataPatchAppService` 的本应用归属/已绑定操作人/顶层字段和逐行明细子字段授权、字段类型与公式重算、可信找人映射刷新、改前改后审计，以及引擎 `updateData` 命令、运行时原子保存和 HTTP 场景草稿。当前 `dotnet build backend/ApprovalService.Backend.slnx --no-restore` 为 0 警告 0 错误；尚未运行检查程序、格式或全量 `verify.ps1`，新源码尚需登记到 Git 索引，契约/设计/README 尚未同步，不能视为已完成或合入。下一位先看 `git status` 与上述草稿，运行新场景并修缺陷，再同步 `docs/contracts.md`、设计书 §form/#integration/#api、架构与后端 README、跑全量验证后小步提交；不要触碰 T-041 ClientApp/Identity、T-043 文件可见性认领文件。现有审批人填写值只在事件审计中，没有当前值投影；重提/补充不能靠反读历史猜当前值，应先设计并实现明确的当前值载体，再按已确认的“退回清空、补充保留”接线。预览尚未开始。用户另确认：自动动作可选 `result` 应独立留痕并进入后续结果回调，绝不覆盖审批数据；动作失败按既定间隔最多自动重试 10 次，随后转待管理员处理并暂停自动重试，管理员手动重试使用原 `eventId`。这两项仅登记决定，本轮未修改已提交的动作实现。
- 2026-10-22 · Codex：按已确认规则继续审批中 PATCH、退回/补充后重新提交和流程预览；先登记上列 Contracts/前端镜像/引擎命令/Runtime/API/检查及共享文件。PATCH 先做独立完整切片：发布字段显式开关默认关闭，仅已授权字段可更新，校验并重算公式和可信路由，原子保存数据与改前改后审计。严格核对接入应用归属和本应用已绑定实际操作人；不接触 T-041 的 ClientApp/Identity 或 T-043 文件可见性文件。重提当前审批人填写值与预览的具体载体待核对现有运行状态后继续，不猜测不兼容契约。
- 2026-10-22 · Codex：ADR 0010 取人与自动动作的最小完整切片已实现。纯引擎仅在实际命中 BusinessApi 节点且可信快照缺结果时报告该节点，无待提交事件；运行应用服务按原状态、请求 ID、固定时间与图有界重算，业务人员只经当前应用既有在职绑定解析，取人拒绝时无部分审批状态。发起路由快照保存本应用外部发起人 ID。自动节点继续使用原子写入的 `actionPending` 待发项，后台事务外双向验签发送，以 `eventId` 让业务系统去重；成功后同 ID 幂等提交 `ActionSucceeded`，远端失败/非成功响应保留队列重试，确认待发项失败后重领不重复出站。纯引擎分支场景与真实 SQLite/假签名业务端覆盖严格绑定、失败隔离、动作失败重试及回写；完整 `verify.ps1` 通过：后端 583、调用端 33、前端 65 项，零构建警告，5289 已释放。Contracts、契约、架构、设计书与后端 README 已同步。状态保持进行中：动作响应的可选 `result` 如何进入回调或审批数据、无限重试后的管理员处理边界，设计仍不明确，本轮只回写成功结论，不猜测业务结果持久语义；开放 PATCH、重新提交、预览、附件原子关联、`AuthGeneration` 仍未完成。
- 2026-10-22 · Codex：继续 ADR 0010 后两类钩子前已复核：T-002 状态已完成，当前无并行认领；`EngineWork.Advance` 会在一个纯命令里跨过多个实际分支并创建任务，应用层无法从整图安全预取取人结果。按主任务同意，本轮精确扩范围到上列 Engine/Runtime/出站与场景文件：引擎遇缺少的业务节点取人输入返回实际命中节点请求、无任何持久提交，应用层用同一命令和固定时间重新计算；自动动作使用已有 `actionPending` 待发事件及 `ActionSucceeded/Failed` 恢复命令。先完成纯引擎最小切片，随后补严格本应用绑定和持久动作；状态保持进行中。
- 2026-10-22 · Codex：已采纳 ADR 0010，接入应用四个完整 URL 保持默认 HTTPS、可信内网显式开启 HTTP。提交 `be8f839` 接通未传 `data` 时的发起前取数、发起前与任务同意前的业务校验；统一出站层对原始响应字节做 HMAC-SHA256 恒定时间核对，校验回显的请求 ID 后才解析 JSON，网络/超时/HTTP/签名/格式错误统一为 `BUSINESS_HOOK_FAILED`。真实数据库和假业务端场景证明签名错误或校验拒绝不保存部分审批，拒绝同意后任务仍待办；Contracts、SDK 错误码、DI 和契约/架构/设计/README 已同步。完整 `verify.ps1` 通过：后端 572、调用端 33、前端 65 项，零构建警告；5289 已释放。状态保持进行中：业务取人必须仅在即将进入目标节点前预取，并严格按当前应用已有人员绑定解析，不能全图预取未走分支；自动动作须从持久引擎待发事件在提交后发送，并以同一 `eventId` 幂等地把执行结果送回引擎，不能在写事务中等待网络。PATCH、外部重提、预览、附件原子关联仍待接线。T-007 已记录的停用再启用可能复活旧嵌入访问/刷新/未兑换令牌问题，需要接入应用启停/密钥轮换路径与身份令牌联合引入单调 `AuthGeneration`，本次未单方修改，不得把 T-006 标完成。
- 2026-10-22 · Codex：用户已确认 ADR 0010；本轮登记共享 `backend/src/ApprovalService.Contracts/Protocol/ErrorCodes.cs`、调用端错误码副本、应用/基础设施 DI、检查入口、`docs/contracts.md` 与 `docs/architecture.md`，用于四类钩子协议和发起前取数/校验接线。先完成独立可验证的双向 HMAC、响应请求 ID 核对、5 秒超时及同步钩子，后续继续取人和自动动作；状态保持进行中。
- 2026-10-22 · Codex：只读复核发现本应用终止在 `awaitingInitiator`（退回待重提或待补充）时被引擎当成非 `running` 拒绝，但仓储仍将其计为在途并占用业务单号；业务原单此时作废无法释放占用。T-004 已验收且无人认领，已精确登记并修改上面的 `EngineRuntime.cs`：仅放宽 `terminate` 可处理两个在途状态，清除暂停入口，其他命令与原管理员/所属应用授权、必填原因保持不变。引擎覆盖待补充和退回待重提、撤回仍受限；真实 HTTP/SQLite 覆盖待补充终止及在途键释放。共享 `docs/contracts.md`、设计书 §api 和后端 README 已同步。完整 `verify.ps1` 通过：后端 566、调用端 33、前端 65 项，零构建警告，5289 已释放。状态保持进行中。
- 2026-10-22 · Codex：完成批量发起 `POST /api/v1/instances/batch`，1–100 项复用单张发起流程与独立提交，每项使用由批次请求 ID 和下标确定的稳定命令 ID，按原序返回审批单或可修正业务错误；中间项目失败不回滚其他成功项目，重试返回原在途单。真实 HTTP/SQLite 覆盖空批次、有效/无效/null 混合、落库隔离和重复请求。共享 `OpenApiRoutes.cs`、新增批量契约模型、`docs/contracts.md` 及设计书 §api、后端 README 已同步；契约文档开头的人员 ID 约定也同步为所有代表人员的开放接口仅接受本应用绑定，工号只用于本地登录或管理员确认绑定。完整 `verify.ps1` 通过：后端 559、调用端 33、前端 65 项，零构建警告，5289 已释放。剩余 T-006 功能与下一步见下条，状态保持进行中。
- 2026-10-22 · Codex：审批人必填字段仅在 `approve`、`handle`、`acknowledge` 成功推进时强制；拒绝和暂存不强制，提交值仍校验类型。已完成任务的迟到征询回复在其他节点仍运行时也拒绝。业务系统终止接口要求审批单属本应用、实际操作人在本应用绑定且填写原因；此授权不能用于管理员其他干预。修复了撤回/终止引擎事件名与回调映射不一致导致结果回调漏发的问题。共享 `OpenApiRoutes.cs`、`OpenInstanceCommandRequest.cs`、`docs/contracts.md`、设计书 §api 和后端 README 已同步；引擎和真实 HTTP/SQLite 场景覆盖权限、审计、回调。完整 `verify.ps1` 通过：后端 555、调用端 33、前端 65 项，零构建警告，5289 已释放。尚未处理 ADR 0010 四类钩子、PATCH、外部重提、批量发起、预览、附件事务关联及与 T-007 联动的 `AuthGeneration`；状态保持进行中。明细子字段和附件字段仍缺提交协议，本轮必填检查仅覆盖当前节点可写的顶层字段。
- 2026-10-20 · Codex：按用户“把现有的工作弄完就行了，额度要不够用了”只收尾已在做的两处 P2：同意后撤销使原任务恢复 `pending` 时清空意见和完成时间，原同意仍保留在事件链；新增 `task.restored` 回调且只对该事件携带原 `taskId`，按既有出站队列失败重试同事件 ID、同正文。真实 HTTP/SQLite、回调出站重试与契约检查已覆盖，提交 `5be7c44`，完整 `verify.ps1` 通过：后端 540、调用端 33、前端 65 项，零构建警告；5289 已释放。用户已确认此前待决规则，但本轮未启动对应新功能：ADR 0010 的四类钩子按每应用完整 URL、POST JSON、双向 HMAC、5 秒；取数/校验/取人事务前同步调用，业务动作提交后进持久队列。终止要求本应用归属、已绑定实际操作人及必填原因。PATCH 仅 `BusinessUpdateAllowed=true` 字段可改（默认 false）并留痕、重算。退回重提清空当前审批人填写值但保留历史，补充恢复保留。批量发起逐单独立返回逐项结果；预览返回路径和当前候选审批人并说明到达时重算；`Required` 审批人字段只对成功推进的 approve/handle/acknowledge 强制；迟到征询回复一律拒绝。附件按 API Key 上传、仅接受同应用未关联文件，并在发起事务内原子绑定，需与 T-013 协调。接入应用代际 `AuthGeneration` 仍需 T-006/T-007 联合收口，防停用再启用旧令牌复活。上述均只记录未实施；按 §6.1 暂停时状态改回进行中，后续从此稳定提交继续。
- 2026-10-20 · Codex：主任务在稳定交接后要求补撤销同意回归：原任务恢复 `pending` 时必须清空任务投影的意见与完成时间，历史事件仍保留原同意；撤销同意要回调明确通知业务系统原任务恢复。修改在既有 Persistence/Integration 范围，新增的 Contracts 回调 `taskId?` 与 `task.restored` 先登记在 Protocol 共享文件并同步 `docs/contracts.md`、设计书 §integration/#api、后端 README，同提交全量验证。检查用真实 HTTP/SQLite 与出站重试同正文。其他待决事项不受此修复影响，状态保持阻塞。
- 2026-10-20 · Codex：T-006 已实现的开放发起、流程目录/数据定义、审批单查询、待办数量与列表、撤回/催办/任务操作、签名回调/重试/重发及锁定发布图策略均有场景检查；审阅发现的隐藏摘要、征询借任务权限、退回/补充漏回调、意见投影四处已修。完整 `verify.ps1` 最终通过：后端 532、调用端 33、前端 65 项，零构建警告；5289 已释放。稳定提交 `6fd8bf4`（前端执行图类型拆分）、`45a95e4`（运行策略持久场景）、`6c448db`（摘要/征询/意见安全修复）、`486cd14`（回调兼容契约）。尚未开放只传单号/四类钩子、PATCH、外部重提、终止、批量发起、预览；分别等待 ADR 0010 钩子协议、业务字段更新授权、重提审批人字段保留、终止权限、批量部分失败与预览响应范围的用户决策，不能把这些验收项标完成。审批人字段 `Required` 的操作范围和明细子字段行索引协议也待确认。附件同应用归属/事务绑定依赖 T-013 用户决策，尚拒绝附件 ID。另接入应用停用→启用后旧嵌入 JWT/刷新令牌可能复活；需 T-006/T-007 协作引入单调 `AuthGeneration`，在签发与验证、刷新及一次性令牌中完整绑定，本任务未单独加无效字段。按 R13 状态改为阻塞，等这些产品/安全边界确认后续接。
- 2026-10-20 · Codex：独立审阅的三处高优先级问题已修：待办摘要按锁定发布图的当前节点显式 `hidden` 权限过滤，重复权限发布时拒绝；征询回复必须使用原发问任务 ID，受询人仍可合法回复意见但不得携带 `fieldValues`；退回发起人、要求补充映射成稳定回调事件码，事务内固定等待状态、数据和填写值。任务意见投影在后续运行命令中保留。流程级撤回/重提策略从发布图固定到发起运行上下文，HTTP + SQLite 场景覆盖首人处理后拒撤、旧图默认可撤、退回原节点重提、任务意见和回调待发快照。后端 532 项、格式检查、前端 65 项及 build 已通过；完整 `verify.ps1` 首次仅因 `flow.ts` 525 行超限失败，已引入 T-034 已验收类型拆分修正，待 5289 窗口复验。外部终止/PATCH/重提/批量/预览与钩子协议仍待用户决策，状态保持进行中。
- 2026-10-20 · Codex：本轮完整 `verify.ps1` 后端 532、调用端 33 均通过，前端因 T-034 契约合入使 `flow.ts` 达 525 行而停在源码规模检查，验证进程已退出释放 5289。T-034 已验收的 `8d46176` 有不依赖缺失工作台的 `flow-execution.ts` 类型拆分及 `flow.ts` 兼容转出；已只取这两个模型文件，`flow.ts` 降为 496 行，前端 65 项及构建通过。全量复验发现原 `ContractChecks.cs` 仅扫描 `flow.ts`，已在其既有登记范围内同步扫描拆出的类型文件，待 5289 重新验证。
- 2026-10-20 · Codex：独立只读审阅指出待办摘要未按当前节点隐藏字段过滤、征询 `reply` 的 `relatedId` 与 `TaskId` 未一致校验、退回/补充事件未映射回调，以及待办意见摘要为空；已在本任务范围登记 `OperationDispatcher.cs`，其余位于现有 Integration/Runtime 范围。先补隔离/审计/回调场景后再开放新路由。
- 2026-10-20 · Codex：T-034 已验收提交 `8d46176` 的完整 cherry-pick 遇到前端工作台四个文件在本分支缺失的 modify/delete 冲突，已按协作规则中止，不强行加入孤立前端文件。经主任务确认，先仅取其中 `FlowCompiler.cs`、`FlowSemanticValidator.cs`、`FlowDefinitionChecks.cs` 三个后端文件并单独提交；完整设计器已在 T-036 集成分支。发布图策略仍需在 T-006 发起时映射到运行上下文，并做真实持久场景。
- 2026-10-20 · Codex：T-034 已验收的 `48d5adf` 定义流程级 `withdrawBeforeFirstDecision` 与 `returnMode`，为让发起时运行策略按锁定发布图固定，本任务登记合入该 Contracts/前端副本/契约检查原子提交，随后只在开放发起应用服务把图上的值映射到 `EngineContext.Operations`。`docs/contracts.md` 与设计书已有 T-006 审批人字段条款，合并时保留两边增量；旧图缺省值应与现有运行时一致。真实持久场景覆盖首人处理后撤回限制、退回到原节点重提以及旧图默认。
- 2026-10-20 · Codex：任务操作新增可选 `fieldValues`，仅锁定流程中 `approverInput=true` 且当前节点唯一权限为 `editable` 的顶层字段可写；复用既有字段类型规则、严格核对本应用人员/部门绑定。原业务 `data` 快照不变，填写值随引擎操作审计入事件链，并在任务完成/审批结果回调的独立字段中固定入队、重发同正文。HTTP 场景覆盖原字段/无权限字段越权、错误类型、未绑定人员、明细子字段缺行索引、历史可查及结果回调；`docs/contracts.md`、设计书 §api、后端 README 已同步。附件填写因归属未接通而拒绝；明细子字段仍待行索引协议。`Required` 在各人工命令中何时强制仍待用户答复，当前只校验已提交字段。完整 `verify.ps1` 通过：后端 506、调用端 33、前端 65 项，构建零警告。
- 2026-10-20 · Codex：审批人填写字段按已评审设计 §form605 只入操作记录和结果回调，由业务系统决定是否回写原单；审批数据快照保持不变。为实现该明确能力，先登记 T-006 范围扩展到 `OperationModels.cs`、`OperationGuard.cs` 和新 `OpenActionFieldValidator.cs`，共享 Contracts 请求/回调载体、`docs/contracts.md`、设计书 §api 将以同一契约提交同步。仅支持顶层 `ApproverInput`，明细子字段缺行索引协议将明确拒绝并交接；T-034 并行修改 Flows 契约和相同文档，合并时须保留双方条款。必填字段在不同人工命令中的生效边界待主任务确认，先实现已提交字段的严格权限与类型校验。
- 2026-10-20 · Codex：增加无外部钩子的真实 HTTP + SQLite 本机性能基线，预热 4 次、每项测量 20 次并用最近秩法计算 P95；本次发起 22.4 ms、审批 17.7 ms、待办数量 3.0 ms，前一次独立运行分别 52.2/41.3/7.2 ms，均低于 300/300/50 ms 初定目标。该基线包含本机 TestServer 和 SQLite，不代表生产网络、数据库或钩子耗时。回调场景补齐九类外部事件编码、内部事件不外发、开始事件状态/数据快照稳定，以及待发项与正文身份不一致时拒绝出站；原有签名、失败重试、手动重发同正文同事件 ID、发送日志和停用应用隔离场景继续通过。完整 `verify.ps1` 通过：后端 498、调用端 33、前端 65 项，构建零警告。
- 2026-10-20 · Codex：运行时重提/补充可把经应用层校验的审批数据和可信找人映射与状态、任务、事件、回调快照在同一事务提交，非重提命令拒绝替换；组织绑定变更后的数据人员/部门找人标识可重新解析，并保留发起时自选人员。真实 SQLite 场景覆盖无效命令不改数据、退回重提原子保存及本应用人员映射；`verify.ps1 -Part backend` 通过 492 项，零构建警告。这里只是内部基础，开放 PATCH/重新提交路由仍待用户确认字段级更新权限和 ApproverInput 保留规则；未把它们标成完成。
- 2026-10-20 · Codex：开放撤回、催办、人工任务操作已接入统一命令入口；先以 API Key 核对审批单/任务来源应用，再严格解析实际操作人、目标人员及提及人员的本应用绑定。任务 ID 含斜杠，路由尾部捕获并兼容百分号编码；真实 HTTP 检查覆盖跨应用、未绑定工号、错误任务人、一小时催办限制、撤回和同意。发起自选审批/抄送人与审批数据找人标识已入审批单路由快照，后续节点按最新组织和矩阵重算；新增三库 `AddRoutingInputs` 迁移，HTTP 场景覆盖自选抄送跨节点与组织变更后部门负责人变化。完整 `verify.ps1` 通过：后端 489、调用端 33、前端 65 项，构建零警告。尚待实现的数据更新必须与路由映射同事务重算；开放终止管理员语义仍待用户确认。
- 2026-10-20 · Codex：审批操作接入前核对设计书 #rules：审批人到达节点时按最新组织/矩阵求值，但发起自选人员、发起部门和业务数据保持发起快照。因此在本任务范围登记 `InstanceEntity.cs`，计划持久化发起时规范化的路由输入，后续人工命令用最新组织快照重算并复用这些输入，三库迁移由 EF 工具生成。设计 §api 的开放终止与现有引擎“仅管理员、必填原因”权限不一致，已向主任务报告，确认前暂不接该路由；先完成撤回/催办及明确的审批操作。
- 2026-10-20 · Codex：开放发起 POST 已接入已发布流程、可信组织快照、审批数据校验与公式重算、分支条件、自选审批/抄送人员、持久流水号和运行时原子在途键。请求新增本应用严格绑定的选定发起部门与代发人；发起部门核对人员归属，管理员配置的流程允许部门只用可信组织主数据解析，避免应用伪造同名绑定绕过。新增三库 `AddSerialNumberCounters` 迁移、HTTP 场景和契约/设计同步；流程级编号配置仍由 T-020 管理后台接入，当前为部署级默认 `SP-yyyyMM-六位`。取数/校验/取人/动作钩子仍待 ADR 0010 确认，附件引用在同应用归属与原子关联接通前拒绝；矩阵命中依赖可信提供者，未配置即明确报错。完整 `verify.ps1` 已通过：后端 477、调用端 33、前端 65 项。其余更新数据、撤回、重提、催办、终止、审批操作与批量发起继续实现。
- 2026-10-20 · Codex：待办数量和列表 HTTP 路由已接入现有数据库聚合与摘要投影；`user` 仅以本接入应用既有绑定解析，默认只看本应用，获授权才可传 `scope=all`。共享的 Contracts 路径、Api/检查 Program、`docs/contracts.md` 同步登记和更新，真实 HTTP 场景覆盖跨应用、未绑定工号及无效分页。完整 `verify.ps1` 后端 465、调用端 33、前端 65 项通过。发起审批的编号格式遵循设计 §form 的可配置方向；本任务不把示例前缀/日期/位数写死为最终规则。
- 2026-10-20 · Codex：局部合入 T-007 已提交基线 `04045e2`，保留双方的开放路由、身份/组织路由、DI、模型配置及契约说明。三库 `AddOrganizationIdentity` 迁移通过 EF 工具基于合并后模型重新生成；SQLite 从空库迁移和模型快照检查、后端 460 项、调用端 33 项、前端 65 项及完整 `verify.ps1` 均通过。发起和待办人员身份只使用 `ResolveBoundPersonAsync`，不能使用全局工号回退；未获确认的四类钩子协议和附件绑定仍保持隔离，后续继续实现明确的开放接口。
- 2026-10-20 · Codex：为继续发起/待办接口，计划局部合入 T-007 已提交基线 `04045e2`，只使用严格本应用绑定的 `ResolveBoundPersonAsync`，不调用待确认的全局工号回退。合并预计涉及共享 `Program.cs`、应用/基础设施 DI、`ApprovalDbContext.cs`、三库模型快照、`docs/contracts.md`、`docs/architecture.md` 与设计书；冲突只保留两任务的登记和已确认条款。离职在途重分配由独立任务处理，T-006 不修改其 Engine/Runtime/Persistence 文件。
- 2026-10-20 · Codex：共享文件增量登记：`InfrastructureServiceCollectionExtensions.cs` 增加部署级 HTTPS/内网 HTTP 策略并关闭 HTTP 自动重定向，`docs/contracts.md` 和 `docs/architecture.md` 同步全局在途键的跨应用冲突、回调停用与传输边界；这些共享改动单独提交。审批单查询产品与 HTTP 隔离场景已提交 `eba0029`、`f5a65a7`、`00728fe`、`7ca8835`；全量验证后端 392、调用端 33、前端 65 项通过。审阅修复 `3dfcec2` 补了跨应用同号只报 409、HTTPS 默认、停用应用不发送回调、审批详情一致快照；新场景后端 401 项通过，待 T-007 释放 5289 后重跑全量。
- 2026-10-20 · Codex：回调和流程目录已能独立运行，后端完整检查 383 项通过。发起接口要求同一业务单号结束后可再次发起，而现有 `Instances(FlowCode,BusinessKey)` 为全生命周期唯一索引；为跨 SQLite、SQL Server、PostgreSQL 保持一致事务语义，登记独立在途键实体 `Domain/Runtime/ActiveInstanceKeyEntity.cs`，在运行仓储的同一次提交中增删，之后才实现重复发起返回已有审批单。
- 2026-10-20 · Codex：恢复后完成流程目录与数据定义接口提交 `a0ca17a`、共享 DI/路由/检查登记 `d11fd63`。设计书 §api 对取数、校验、取人、业务动作钩子只写用途，未定义精确 URL/HTTP 方法和 JSON 请求响应；按 R13 登记提议 ADR，待确认前不实施这四类钩子协议。签名 HTTP 出站适配层和结果回调属于已明确部分，继续开发。
- 2026-10-20 · Codex：按用户要求在可恢复节点暂停，状态保持“进行中”。独立工作树 `C:\Users\dukun\.codex\worktrees\open-api\ApprovalService`、分支 `ai/codex/T-006-open-api`，未合入 main、未推送。已合入已完成的 T-005、T-004、T-010；认领 `dda795f`，范围登记 `f783d68`，接入应用实体/API Key SHA-256/签名密钥 AES 加密及三库迁移 `4a1f76b`，共享 DI `b234d49`，检查入口 `93b0e04`。接入应用后端构建零警告、检查程序 373 项通过；密钥只在应用层签发，尚无未鉴权管理接口。暂停时工作区另有尚未提交的流程目录 GET 接口、服务、仓储、Contracts 模型、场景检查、契约/设计书同步和 DI/Program 登记；产品新文件及契约文档已暂存，共享登记文件仍未暂存，恢复时先看 `git status` 和 `git diff --cached`，核对并提交。流程目录改动后 `dotnet build` 零警告，定向检查的三条新场景通过；尚未跑完整 verify。后续依次完成预览、发起/批量发起、查询/数据更新、撤回/重新提交/催办/终止、待办及审批操作、钩子和回调可靠发送与重发，并补三库迁移、完整场景和文档。T-007 组织身份任务在并行开发，已约定 `IOrganizationLookupAppService` 解析可信人员/部门和组织快照；其负责人可合入独立的接入应用认证提交，T-006 恢复后需合入完成的 T-007，绝不信任请求自造的组织快照。矩阵命中由可信 `IMatrixMatchProvider` 接口提供，持久配置由 T-020 实施；未配置时要明确报可纠正错误。完整交付前需全量 `verify.ps1`，验收未勾选。
- 2026-10-20 · Codex：基于 main 建立独立 T-006 工作树，合入已完成的 T-005、T-004 与 T-010 依赖；共享的检查入口与后端 README 冲突已保留两边内容。接入应用需落库并加密密钥，实例与任务接口需持久化查询和在途唯一性，已与主任务协调并在范围中显式登记 Domain/Integration、Application/Runtime、Runtime 仓储、Persistence 上下文与三库迁移及共享登记文件，之后实施开放接口。
