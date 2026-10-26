# T-022 流程模板库、测试模式与接入向导

状态：进行中
负责人：Codex 2026-10-24
里程碑：M4
依赖：T-006、T-008、T-010
设计书：§designer、§integration

## 目标

常用流程复制就能用；发布前能模拟走一遍；业务系统接入有向导和生成好的调用代码。

## 范围

可以新建、修改的文件：

- 流程模板库：请假、加班、出差、费用报销、付款、采购、用印、合同、物品领用、质量异常（只有流程和审批数据定义）
- 测试模式：用假数据模拟发起，扮演每个审批人走完整条流程，不发真实提醒、不调业务接口
- 接入向导：建接入应用 → 生成 API Key 和签名密钥 → 选流程 → 生成 C# 调用代码 → 一键发测试回调
- `frontend/src/designer/workbench/simple-templates.ts`、`workbench-model.ts`、`frontend/tests/workbench-checks.ts`：内置可复制的十类流程模板及静态发布预检。
- `frontend/src/designer/workbench/integration-snippet.ts`、`frontend/src/pages/DesignerPage.vue`、对应前端检查：生成、展示、复制不嵌入 API Key 的 C# SDK 调用代码。
- `backend/src/ApprovalService.Application/Administration/FlowSimulation*.cs`、`backend/src/ApprovalService.Api/Endpoints/FlowSimulationEndpoints.cs`、`backend/tests/ApprovalService.Backend.Checks/FlowSimulationChecks.cs`：已确认的内存模拟会话。
- `backend/tests/ApprovalService.Backend.Checks/FlowTemplateSimulationChecks.cs`、`Fixtures/flow-templates.json`、`frontend/tests/workbench-checks.ts`：对十个正式模板逐一用同一模拟引擎推进，前端核对测试夹具始终等于模板原定义。
- `backend/src/ApprovalService.Application/Administration/FlowSimulationAppService.cs`：给模拟人工步骤传递可选假意见，以验证节点必填意见规则。
- `frontend/src/api/flow-simulation.ts`、`frontend/src/designer/workbench/FlowSimulationPanel.vue`、`frontend/src/designer/workbench/simulation-model.ts`、`frontend/tests/simulation-checks.ts`：用现有流程管理员会话在设计器试跑当前草稿和假数据，显示待办、外部等待与事件，不保存真实业务记录。
- `frontend/README.md`：同步测试模式页面的实际能力与限制。
- `backend/src/ApprovalService.Domain/Integration/ClientAppEntity.cs`、`IClientAppAdminRepository.cs`、`backend/src/ApprovalService.Application/Integration/ClientAppAdminAppService.cs`、`backend/src/ApprovalService.Infrastructure/Integration/ClientAppAdminRepository.cs`、`backend/src/ApprovalService.Api/Endpoints/ClientAppAdminEndpoints.cs`：已确认的按应用管理员人员 ID 名单与授权查询；本切片不发送测试回调。
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`、三种数据库迁移、`backend/tests/ApprovalService.Backend.Checks/ClientAppAdminChecks.cs`：存量名单默认为空并检查读写权限。
- `backend/src/ApprovalService.Domain/Integration/ITestCallbackRepository.cs`、`backend/src/ApprovalService.Application/Integration/TestCallbackAppService.cs`、`backend/src/ApprovalService.Infrastructure/Integration/TestCallbackRepository.cs`、`backend/src/ApprovalService.Api/Endpoints/TestCallbackEndpoints.cs`、`backend/tests/ApprovalService.Backend.Checks/TestCallbackChecks.cs`：已确认的签名持久测试回调，不建立审批单。
- `backend/src/ApprovalService.Infrastructure/Integration/CallbackOutboxHandler.cs`、`backend/src/ApprovalService.Infrastructure/Administration/AdminConsoleRepository.cs`：识别无真实审批单的测试回调，按登记应用投递并在管理队列摘要中展示。
- `client/src/ApprovalService.Client/ApprovalCallback.cs`、`ApprovalCallbackEventCodes.cs`、`client/tests/ApprovalService.Client.Checks/TestCallbackChecks.cs`：SDK 解析新增的可选应用编码和 `integration.test` 回调。
- `client/samples/ApprovalService.Demo.Integration/DemoBusinessHttpHost.cs`、`client/tests/ApprovalService.Client.Checks/DemoBusinessHttpChecks.cs`、`client/README.md`：示例接收端对测试回调只验签确认，不更新业务原单。
- `docs/adr/0021-simulation-and-integration-test.md`：同步已确认测试回调协议的落地状态。
- `frontend/src/api/admin-integration-wizard.ts`、`frontend/src/pages/admin/AdminIntegrationWizard.vue`、`frontend/src/pages/AdminPage.vue`、`frontend/src/api/admin-session.ts`、`frontend/src/model/admin.ts`、`frontend/src/styles/admin.css`、`frontend/tests/admin-integration-wizard-checks.ts`、`frontend/README.md`：接通组织管理员完整接入向导和应用管理员仅凭已知应用编码触发的受限测试入口。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`：追加测试模式路由常量。
- `backend/src/ApprovalService.Contracts/Protocol/FlowSimulationModels.cs`：新增测试模式契约。
- `backend/src/ApprovalService.Contracts/Protocol/FlowSimulationModels.cs`、`docs/contracts.md`、设计书 §designer/§integration：模拟步骤兼容新增可选 `opinion`，不会改变旧请求默认行为。
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Api/Program.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`：只追加服务、端点和检查登记。
- `docs/contracts.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`、`docs/adr/README.md`：同步已确认的跨部分测试协议。
- `frontend/src/pages/DesignerPage.vue`、`frontend/tests/checks.ts`：仅登记测试模式面板及对应检查；保持设计器主文件低于 500 行。
- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、新增名单请求/响应契约、`backend/src/ApprovalService.Api/Program.cs`、应用与基础设施 DI 登记、后端检查入口：追加应用管理员名单接口和服务。
- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`CallbackEventCodes.cs`、`CallbackNotification.cs`、新增测试回调请求/响应契约：追加测试回调接口与事件。
- `backend/src/ApprovalService.Api/Program.cs`、应用与基础设施 DI 登记、后端检查入口、`docs/contracts.md`、`docs/architecture.md`：追加测试回调服务与契约，和已确认协议同步。
- `frontend/src/model/navigation.ts`、`frontend/src/model/routes.ts`、`frontend/tests/checks.ts`：登记接入向导与受限测试页地址和检查入口；只追加前端路由/检查，不更改服务端协议。

## 验收标准

- [ ] 每个模板都能发布并在测试模式里走通
- [x] 测试模式不产生真实回调和提醒（检查覆盖）
- [x] 设计器可用当前草稿与假输入逐步试跑，并展示待办、等待和事件
- [x] 接入应用管理员名单由本地组织管理员维护，存量默认为空；可信授权查询覆盖应用启停和人员在职
- [x] 测试回调由本地组织管理员或本应用管理员带原因排入持久队列，接收端只验签确认
- [x] 向导生成的代码能直接编译（沿用已在 net8.0/net461 离线编译通过的 SDK 示例生成器）
- [x] 组织管理员可在接入向导创建应用、维护应用管理员、生成示例并测试回调；应用管理员只能以已知应用编码发送测试
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 已实现部分的 README、模拟与测试回调契约、设计书同步

## 不做

- 无

## 交接记录

- 2026-10-26 · Codex · 补齐设计器“流程规则”页的真实测试模式入口：复用已授权的流程管理员 JWT 与现有内存模拟 API，当前完整草稿、可编辑假数据和逐节点假处理人创建会话；逐项模拟人工完成/拒绝、自动动作与子流程结果，查看待办、等待令牌和最近事件，结束时删会话。JSON 样例复用已有审批字段样例生成器；独立前端检查覆盖输入校验、人员去重和完整 HTTP 往返。完整 `verify.ps1` 通过：后端 1071、调用端 96、前端 191 项及生产构建；尚未逐一用十模板经真实 HTTP 走完，验收项仍未勾选。未触碰真实实例/回调接口。下一步整合分支合入并复验，外置 Chrome 可用时检查视觉和交互。
- 2026-10-26 · Codex · 基于已整合 `f92d9c2` 完成接入向导前端接线：组织管理员在应用列表打开向导，新建应用时现有编辑器只显示一次密钥；独立读取原始授权流程编码以生成已编译验证过的 C# SDK 示例；应用管理员名单先 GET，再以旧名单条件 PUT，并按服务端要求填写原因；签名测试回调显示 `test-` 事件 ID、异步排队语义，组织管理员可跳到待发队列看摘要。新增 `#/admin/apps/test` 受限入口，本地人员登录后只可用已知应用编码发测试，页面不读取组织级应用列表、名单和待发队列；服务端仍在入队事务核对身份。新增前端场景检查，并同步前端 README。完整 `verify.ps1` 通过：后端 1071 项、调用端 96 项、前端 189 项及生产构建；本轮未修改服务端协议、未新增依赖。尚待整合后的复验和外置 Chrome 视觉检查（当前 Chrome 控制接口此前被系统拒绝），十模板逐个模拟验收仍未完成；T-022 保持进行中。现有服务端只提供组织级待发摘要，未提供单事件逐次发送日志查询，前端没有越权展示给应用管理员。
- 2026-10-25 · Codex · 为接入向导前端从整合提交 `6a449d1` 建立独立分支 `ai/codex/T-022-integration-wizard`，在用户要求近期暂停后停于动产品代码前。已核对现有 `AdminPage` 通过 `/api/v1/admin/session` 只允许组织管理员进入，应用列表和出站发送日志也仅组织管理员可读；新 `POST /api/v1/admin/apps/{code}/test-callback` 则已允许本应用管理员调用。后续最小安全接线：组织管理员在现有应用编辑页维护名单、授权流程、生成不含密钥的 C# 示例、填写原因发送测试回调并查看事件 ID/排队状态及发送日志；本应用管理员使用仅输入已知应用编码与原因的受限入口，服务端每次重验权限，不读取全部应用列表、管理员名单或组织级出站日志。若需要应用管理员看到其应用列表/发送日志，应先定义服务端按应用过滤的新契约，不可通过前端过滤组织级响应。下一步先在任务范围登记前端页面、API、会话守卫、检查与 README，再实现并跑前端检查/构建、错峰完整验证。本轮无产品代码或依赖改动，未运行验证；T-022 保持进行中。
- 2026-10-25 · Codex · 完成 `integration.test` 一键测试回调后端协议与示例接收端：本地组织管理员或已登记的本应用管理员带原因调用 `POST /api/v1/admin/apps/{code}/test-callback`，应用层和 SQLite 入队事务都重验权限、在职、应用启用及回调地址；返回合成 `test-` 事件 ID 和 `queued`。正文使用合成 `instanceId`、空 `serialNo/flowCode/businessKey`、`status=test`、`data={}` 与测试专有 `clientAppCode`，不建审批单、任务或业务动作。复用持久回调队列、HMAC/请求 ID、重试与逐次发送日志；管理队列摘要可识别无审批单的测试项。示例业务系统对测试事件只验签确认，不更新原单；SDK 新增可选应用编码与测试事件编码副本。真实 HTTP/SQLite/假出站场景覆盖匿名、普通人员、应用管理员、组织管理员、撤权、停用、无回调地址、失败重试、签名、发送日志和示例接收端。完整 `verify.ps1` 通过：后端 1060 项、调用端含真实服务端到端、前端 185 项及生产构建。**仍未完成**：接入向导页面把创建应用、管理员名单和测试回调串成完整操作，以及十模板逐一端到端走通；状态保持进行中。无新增依赖或数据库迁移，也未触碰 T-011 常用意见 agent 文件。
- 2026-10-25 · Codex · 从整合提交 `da89c33` 开始测试回调独立切片；按 ADR 0021 复用持久回调队列和出站签名。现有待发项没有审批单外键，但处理器和管理摘要依赖真实审批单查询；测试回调将用合成 `test-` 事件/实例 ID、仅测试事件携带可选接入应用编码，出站处理器按此识别，管理摘要对测试项读该编码。不增加数据库迁移、不修改 T-011 工作树。新 Contracts/SDK 副本、服务登记及共享文档改动已在上方预登记。
- 2026-10-25 · Codex · 完成接入应用管理员名单独立后端切片：`GET/PUT /api/v1/admin/apps/{code}/administrators` 仅供本地组织管理员读取、按旧名单条件替换，PUT 校验最多 50 名当前在职的内部人员、变更原因和并发冲突；在串行化事务中记录仅含人员 ID/原因的审计。新增供后续测试回调调用的 `CanSendTestCallbackAsync`，每次重新核对本地会话、应用启用、人员在职及本应用名单或组织管理员身份。三库增量迁移通过 `backend/tools/add-migration.ps1` 生成，存量名单默认 `[]`；真实 HTTP/SQLite 场景覆盖匿名、普通人员、组织管理员、非法/离职人员、重复、并发、应用隔离、停用与离职。同步 Contracts、契约/架构、后端 README 和设计书第 12 节。完整 `verify.ps1` 第二次运行全部通过：后端检查、SDK 真实服务端到端、前端 183 项及构建；第一次运行在与本切片无关的 `DemoBusinessHttpChecks` 发生一次连接提前关闭，单独 client 检查及完整复验均通过。**仍未完成**：接入向导的 `integration.test` 持久测试回调、应用创建接线及模板逐一端到端走通；状态保持进行中，下一位可直接复用 `ClientAppAdminAppService.CanSendTestCallbackAsync`，不得仅信任前端显示或名单旧快照。
- 2026-10-25 · Codex · 按用户的 50% 额度停工线在动产品代码前暂停；本分支 `ai/codex/T-022-client-app-admins` 只提交了范围/共享登记 `cff1b14`，工作树无未提交改动。已确认 `ClientAppEntity` 目前没有管理员身份字段，`ClientApps` 的三库 EF 模型可增加默认 `[]` 的内部人员 ID JSON 名单；`PersonEntity.IsActive` 与 `PersonRoleEntity` 可供授权时核对当前在职和组织管理员角色，管理端现有 `OrganizationAdminRequirement` 只接受本地会话。下一位从本分支继续：添加独立的名单仓储和应用服务、管理 GET/PUT 契约及路由；PUT 校验现存且在职人员、比较读取时的名单以防并发覆盖，并与只含人员 ID/原因的审计同事务写入；供后续测试回调使用的授权查询须再核对应用启用、当前在职、本地会话及名单或组织管理员。为 `ClientApps` 默认空名单用官方 `backend/tools/add-migration.ps1` 生成 SQLite、SQL Server、PostgreSQL 增量，不能覆盖已合入的 T-020 管理迁移；补匿名、普通人员、组织管理员、名单成员、停用应用、离职人员及并发冲突场景，最后全量 `verify.ps1`。本轮没有真实 HTTP 测试，T-022 保持进行中。
- 2026-10-25 · Codex · 从已验证整合提交 `73afc54` 恢复已确认的接入应用管理员名单切片。组织管理员维护各应用内部人员 ID 名单，存量应用默认空；后续 `integration.test` 仅向当前本地会话、当前在职且在本应用名单中的人员开放，组织管理员始终可操作。本轮先做持久名单、管理接口、可信授权查询与三库增量迁移，不实现测试回调。共享 Contracts、API/DI 登记、检查入口、架构/契约/设计同步及三库迁移已在上方预登记；与 T-020 前端分支隔离。
- 2026-10-24 · Codex · 新增纯内存模拟会话 API：完整未保存草稿、假数据与每个人工节点假处理人先通过当前流程管理员授权和发布预检，再驱动 `EngineRuntime`；仅创建人可读取、逐步处理、删除，30 分钟闲置过期，自动动作与子流程须显式假结果。应用服务没有运行仓储、消息或出站依赖；场景检查覆盖授权、假身份、完成、自动/子流程等待、删除与过期。必填附件可用格式正确的假文件 ID 模拟，不读取或关联真实文件。完整 `verify.ps1` 通过：后端 1014、调用端 93、前端 169 项及构建；假附件补丁增加一项后端场景检查通过。同步 `docs/contracts.md`、架构、后端 README、设计书第 11 节和 ADR 0021。**仍未完成**：十模板端到端逐一走通、接入向导应用创建接线、应用管理员名单和 `integration.test` 测试回调。下一位须从已整合 T-020 三库迁移的最新基线做后两项，按用户已确认的应用管理员内部人员 ID 名单（旧应用默认空）建立增量迁移，且不要覆盖 T-020 快照。Chrome 控制尚不可用，本切片未做浏览器视觉验证。状态保持进行中。
- 2026-10-24 · Codex · 用户已确认本地流程管理员的 30 分钟内存模拟会话协议，以及 `integration.test` 持久出站测试回调。接入应用管理员名单由组织管理员按内部人员 ID 维护，旧应用名单默认空。先完成可独立交付的模拟会话，再基于最新三库迁移基线实现应用管理员和回调；本轮恢复进行中，并登记新增契约、注册文件及设计同步范围。
- 2026-10-24 · Codex · 可独立完成的十类模板与 C# SDK 示例代码已提交；十类模板在离线临时工程由真实后端 `FlowCompiler.Compile` 全部编译成执行图。生成的示例源码离线引用真实 SDK，以 net8.0 和 net461（含引号、反斜杠、换行、中文及 Unicode 分隔符的恶意编码样例）编译且零警告。最终完整 `verify.ps1` 通过：后端 954、调用端 93、前端 169 项与生产构建。Chrome 控制接口此前被系统拒绝，本轮未使用非 Chrome 浏览器或应用内浏览器做视觉复核。**按 R13 阻塞**：设计只说“测试模式在内存跑同一引擎”和“一键发测试回调”，未规定跨请求模拟会话、身份权限、假处理人与自动/子流程结果、会话失效，亦未规定测试回调事件格式和入队/审计语义。建议测试模式使用仅本地组织管理员/流程管理员可见的 30 分钟内存会话，以当前草稿、假数据和假人员驱动原 `EngineRuntime`，模拟自动/子流程结果显式提交，绝不建真实审批单或调用钩子/消息/回调；测试回调由有权管理接入应用者发 `integration.test` 合成事件，经既有签名持久待发队列投递，业务系统只验签不修改单据。两套协议已由主 agent 请求用户确认，未确认前不能编写运行时接口或勾选对应验收项。T-020 管理后端正在整合，下一位应在其已验证基线上接授权与正式草稿，不要把当前本地草稿当成服务端草稿。
- 2026-10-24 · Codex · 新增业务系统 C# SDK 发起代码生成器和设计器“流程规则”页的生成/复制入口。生成代码读取 `APPROVAL_SERVICE_URL`、`APPROVAL_API_KEY`，不把明文凭据放入源码，带调用方稳定请求 ID；空流程编码拒绝，引号、反斜杠、换行、中文和 Unicode 分隔符均按 C# 字面量转义。前端检查 169 项和生产构建通过；把生成源码放进离线临时工程，引用真实 SDK 后以 net8.0、net461 编译，含恶意编码样例也零警告通过。正式接入向导的应用创建/测试回调流程尚未接通。
- 2026-10-24 · Codex · 已提交 `63ac223`：新增请假、加班、出差、付款、用印、合同、物品领用七类自由画布模板，合并原有采购、报销、质量异常共十类。新增模板只有审批图与字段定义，不含业务原单或自动外部动作；十类均通过工作台发布预检。把十类定义序列化到离线临时工程后，真实后端 `FlowCompiler.Compile` 逐一生成执行图且无发布问题。`npm ci --offline --ignore-scripts` 使用本机缓存未下载新包，前端检查和生产构建通过。测试模式及接入向导尚未完成，本任务保持进行中；接下来先做不依赖 T-020 管理 API 的安全 C# 示例代码生成。
- 2026-10-24 · Codex · T-006、T-008、T-010 均已完成，从最新已验证整合提交 `9830cf3` 建独立任务工作树。先核对正式自由画布已有的采购、报销、质量异常三个样例，补足十类可发布模板；测试模式和接入向导须复用现有纯引擎与已认证管理接口，不触碰 T-020 正在认领的流程管理、迁移和注册文件。共享登记和契约变更若确需使用，将先在本任务列明并错峰处理。
<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
