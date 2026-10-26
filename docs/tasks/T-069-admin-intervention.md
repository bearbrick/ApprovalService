# T-069 管理员运行干预协议与页面

状态：待验收
负责人：Codex 2026-10-26
里程碑：M3
依赖：T-020、管理员运行干预 HTTP 契约与授权查询
设计书：§rules、§center、§org

## 目标

接通受保护的真实审批实例查询、当前任务改派、人工节点跳转和终止 HTTP 协议与页面；所有写入填写原因并留审计记录。批量离职交接和流程草稿批量替换明确留待后续任务。

## 范围

- `frontend/src/pages/admin/AdminIntervention.vue`
- 新增独立干预 API、组合函数和纯逻辑模型及对应检查文件
- `frontend/tests/checks.ts`（仅登记新检查）
- `frontend/README.md`、`backend/README.md`、`frontend/src/model/admin.ts`
- 本任务文件

- 新增管理员干预 Contracts、应用服务、领域查询接口、基础设施查询/授权实现（含 FlowManagementRepository 身份读取帮助方法改为程序集内可复用）、HTTP 端点及后端场景检查
- 运行时应用输入与提交模型、运行仓储：仅增加管理员命令并发版本及事务内权限复核
- `frontend/src/pages/AdminPage.vue`、`frontend/src/api/admin-session.ts`：挂载真实干预页面和流程管理员范围
- `docs/adr/0024-admin-runtime-intervention.md`

## 共享文件

- `docs/tasks/README.md`：新增本任务索引行；修改前登记。

- `backend/src/ApprovalService.Contracts/Protocol/AdminInterventionModels.cs`、三层服务/端点登记文件、后端检查 `Program.cs`、`docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`、`docs/design/approval-service-design.html`：新增协议与同步已批准权限、草稿发布边界。

## 验收标准

- [x] 仅使用真实管理员授权接口，列表和详情均在服务端限定管理范围
- [x] 干预原因必填，展示真实结果并处理权限、并发和部分失败
- [x] 明示固定审批人仅改草稿并发布新版本，在途实例继续旧发布版本
- [x] 新行为场景检查通过
- [x] 全量 `verify.ps1` 通过
- [ ] 外置 Google Chrome 验收（由集成任务执行）
- [x] 相关文档已更新

## 不做

- 不调用业务 API Key 接口冒充管理员操作
- 不把委托规则操作当成改派或离职交接
- 不直接修改历史发布版本，不改变在途实例锁定版本
- 不改角色管理与工作日历文件，不新增依赖

## 交接记录

- 2026-10-26 · Codex · 已完成真实管理员运行查询、当前任务改派、人工节点跳转和终止 HTTP 与页面，权限复用直属部门规则，保密单叠加实际参与关系。稳定请求 ID 在实例内按操作者隔离；旧运行修订返回 409；最终提交事务复核权限和接手人在职状态，同事务保留原因审计与待发项。页面确认目标/原因，未知结果重试复用原 ID，固定审批人提示严格保持草稿发布/旧实例边界。已同步 ADR 0024、契约、架构、设计书 §rules 与前后端 README。全量 verify.ps1 通过：后端 1179 项、调用端 96 项（含真实服务端到端）、前端 221 项；新增 HTTP/SQLite 11 场景与前端 9 场景。依赖仅从本机缓存还原，无新库。外置 Chrome 由集成任务验收；批量离职交接、未来节点改派和批量草稿替换未做，页面无这些提交入口。基于 51d9f72 完成验证，后续集成新增切片须统一复验。

- 2026-10-26 · Codex · 用户已通过集成任务明确批准新管理 HTTP 协议；解除阻塞。权限复用直属部门 FlowManagementAccessRules，保密单另核实际参与关系。先实现改派、跳转、终止；批量离职和草稿批量替换不在本次实现。以上范围和共享文件在修改前登记。

- 2026-10-26 · Codex · 待批准提案：新增本地 JWT 管理员实例列表/详情和命令接口；服务端从身份与流程管理范围核实权限，不能接收调用方自报的 `IsAdministrator`。命令输入只收实例/任务定位、目标人员或节点、原因与并发/幂等信息。统一调用 `IApprovalRuntimeAppService.ExecuteAsync` → `EngineRuntime.Execute`，沿用 `OperationKinds.Reassign`（当前待办或未来人工节点）、`OperationKinds.Jump`、`OperationKinds.Handover` 与 `EngineCommandKinds.Terminate`；`OperationGuard` 已要求管理员及原因，跳转不允许汇合网关。离职交接应先列影响范围，逐实例事务返回成功/失败，并单独处理各流程草稿修订；不可改发布版本或冒充自动 `ReassignInactive` 策略。待接口设计批准后登记 Contracts、契约副本、`docs/contracts.md` 和设计书 §rules/§center/§org 的同步范围；设计书目前“一键把所有流程里写死的人转给接手人”须按用户确定的“改草稿、发布新版本、在途继续旧版本”准确说明。集成任务已向用户请求新 HTTP 协议授权，本任务等待转达后再扩范围。
- 2026-10-26 · Codex · 从集成分支 `ai/codex/T-050-feature-integration` 的 `51d9f72` 建立独立分支 `ai/codex/T-069-admin-intervention`，检查后按 R13 阻塞，未修改产品源码。`Contracts/Protocol` 和 `Api/Endpoints` 没有管理员实例查询、修改审批人、跳转、终止或手工离职交接接口。`IApprovalRuntimeAppService.ListDepartureTasksAsync` / `ReassignDepartedTaskAsync` 是内部应用服务，由 `DepartureJobHandler` 按发布策略自动处理，并非供浏览器调用的协议。已有 `CenterRoutes.AdminDelegations` / `AdminDelegateTasks` 仅管理委托；`OpenApiRoutes.InstanceTerminate` 使用接入应用 API Key，不能代替管理员 JWT 接口。建议先补受保护的管理员运行查询和统一命令用例及契约，再继续页面。用户已明确固定审批人仅修改草稿后重新发布，在途实例保持原发布版，此要求已记录，后续接口及页面应一致体现。未运行验证：当前仅新增阻塞任务记录，没有实现可交付功能。已将缺口报告给集成任务。
