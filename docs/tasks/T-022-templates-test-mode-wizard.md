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
- `backend/src/ApprovalService.Domain/Integration/ClientAppEntity.cs`、`IClientAppAdminRepository.cs`、`backend/src/ApprovalService.Application/Integration/ClientAppAdminAppService.cs`、`backend/src/ApprovalService.Infrastructure/Integration/ClientAppAdminRepository.cs`、`backend/src/ApprovalService.Api/Endpoints/ClientAppAdminEndpoints.cs`：已确认的按应用管理员人员 ID 名单与授权查询；本切片不发送测试回调。
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`、三种数据库迁移、`backend/tests/ApprovalService.Backend.Checks/ClientAppAdminChecks.cs`：存量名单默认为空并检查读写权限。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`：追加测试模式路由常量。
- `backend/src/ApprovalService.Contracts/Protocol/FlowSimulationModels.cs`：新增测试模式契约。
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Api/Program.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`：只追加服务、端点和检查登记。
- `docs/contracts.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`、`docs/adr/README.md`：同步已确认的跨部分测试协议。
- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、新增名单请求/响应契约、`backend/src/ApprovalService.Api/Program.cs`、应用与基础设施 DI 登记、后端检查入口：追加应用管理员名单接口和服务。

## 验收标准

- [ ] 每个模板都能发布并在测试模式里走通
- [x] 测试模式不产生真实回调和提醒（检查覆盖）
- [ ] 向导生成的代码能直接编译（检查程序编译一遍）
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 已实现部分的 README、模拟契约与设计书同步；测试回调待后续实现时同步

## 不做

- 无

## 交接记录

- 2026-10-25 · Codex · 从已验证整合提交 `73afc54` 恢复已确认的接入应用管理员名单切片。组织管理员维护各应用内部人员 ID 名单，存量应用默认空；后续 `integration.test` 仅向当前本地会话、当前在职且在本应用名单中的人员开放，组织管理员始终可操作。本轮先做持久名单、管理接口、可信授权查询与三库增量迁移，不实现测试回调。共享 Contracts、API/DI 登记、检查入口、架构/契约/设计同步及三库迁移已在上方预登记；与 T-020 前端分支隔离。
- 2026-10-24 · Codex · 新增纯内存模拟会话 API：完整未保存草稿、假数据与每个人工节点假处理人先通过当前流程管理员授权和发布预检，再驱动 `EngineRuntime`；仅创建人可读取、逐步处理、删除，30 分钟闲置过期，自动动作与子流程须显式假结果。应用服务没有运行仓储、消息或出站依赖；场景检查覆盖授权、假身份、完成、自动/子流程等待、删除与过期。必填附件可用格式正确的假文件 ID 模拟，不读取或关联真实文件。完整 `verify.ps1` 通过：后端 1014、调用端 93、前端 169 项及构建；假附件补丁增加一项后端场景检查通过。同步 `docs/contracts.md`、架构、后端 README、设计书第 11 节和 ADR 0021。**仍未完成**：十模板端到端逐一走通、接入向导应用创建接线、应用管理员名单和 `integration.test` 测试回调。下一位须从已整合 T-020 三库迁移的最新基线做后两项，按用户已确认的应用管理员内部人员 ID 名单（旧应用默认空）建立增量迁移，且不要覆盖 T-020 快照。Chrome 控制尚不可用，本切片未做浏览器视觉验证。状态保持进行中。
- 2026-10-24 · Codex · 用户已确认本地流程管理员的 30 分钟内存模拟会话协议，以及 `integration.test` 持久出站测试回调。接入应用管理员名单由组织管理员按内部人员 ID 维护，旧应用名单默认空。先完成可独立交付的模拟会话，再基于最新三库迁移基线实现应用管理员和回调；本轮恢复进行中，并登记新增契约、注册文件及设计同步范围。
- 2026-10-24 · Codex · 可独立完成的十类模板与 C# SDK 示例代码已提交；十类模板在离线临时工程由真实后端 `FlowCompiler.Compile` 全部编译成执行图。生成的示例源码离线引用真实 SDK，以 net8.0 和 net461（含引号、反斜杠、换行、中文及 Unicode 分隔符的恶意编码样例）编译且零警告。最终完整 `verify.ps1` 通过：后端 954、调用端 93、前端 169 项与生产构建。Chrome 控制接口此前被系统拒绝，本轮未使用非 Chrome 浏览器或应用内浏览器做视觉复核。**按 R13 阻塞**：设计只说“测试模式在内存跑同一引擎”和“一键发测试回调”，未规定跨请求模拟会话、身份权限、假处理人与自动/子流程结果、会话失效，亦未规定测试回调事件格式和入队/审计语义。建议测试模式使用仅本地组织管理员/流程管理员可见的 30 分钟内存会话，以当前草稿、假数据和假人员驱动原 `EngineRuntime`，模拟自动/子流程结果显式提交，绝不建真实审批单或调用钩子/消息/回调；测试回调由有权管理接入应用者发 `integration.test` 合成事件，经既有签名持久待发队列投递，业务系统只验签不修改单据。两套协议已由主 agent 请求用户确认，未确认前不能编写运行时接口或勾选对应验收项。T-020 管理后端正在整合，下一位应在其已验证基线上接授权与正式草稿，不要把当前本地草稿当成服务端草稿。
- 2026-10-24 · Codex · 新增业务系统 C# SDK 发起代码生成器和设计器“流程规则”页的生成/复制入口。生成代码读取 `APPROVAL_SERVICE_URL`、`APPROVAL_API_KEY`，不把明文凭据放入源码，带调用方稳定请求 ID；空流程编码拒绝，引号、反斜杠、换行、中文和 Unicode 分隔符均按 C# 字面量转义。前端检查 169 项和生产构建通过；把生成源码放进离线临时工程，引用真实 SDK 后以 net8.0、net461 编译，含恶意编码样例也零警告通过。正式接入向导的应用创建/测试回调流程尚未接通。
- 2026-10-24 · Codex · 已提交 `63ac223`：新增请假、加班、出差、付款、用印、合同、物品领用七类自由画布模板，合并原有采购、报销、质量异常共十类。新增模板只有审批图与字段定义，不含业务原单或自动外部动作；十类均通过工作台发布预检。把十类定义序列化到离线临时工程后，真实后端 `FlowCompiler.Compile` 逐一生成执行图且无发布问题。`npm ci --offline --ignore-scripts` 使用本机缓存未下载新包，前端检查和生产构建通过。测试模式及接入向导尚未完成，本任务保持进行中；接下来先做不依赖 T-020 管理 API 的安全 C# 示例代码生成。
- 2026-10-24 · Codex · T-006、T-008、T-010 均已完成，从最新已验证整合提交 `9830cf3` 建独立任务工作树。先核对正式自由画布已有的采购、报销、质量异常三个样例，补足十类可发布模板；测试模式和接入向导须复用现有纯引擎与已认证管理接口，不触碰 T-020 正在认领的流程管理、迁移和注册文件。共享登记和契约变更若确需使用，将先在本任务列明并错峰处理。
<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
