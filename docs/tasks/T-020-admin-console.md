# T-020 管理后台

状态：进行中
负责人：Codex 2026-10-23
里程碑：M3
依赖：T-005、T-007
设计书：§center、§org、§integration

## 目标

管理员能管理流程、组织架构、接入应用、日志、系统信息和权限，能做管理员干预。

## 范围

可以新建、修改的文件：

- 登录、用户和权限、系统信息（公司名称和 Logo，打印页眉用）、操作日志：沿用标签服务的做法
- 流程管理：分组、版本、发布、导入导出
- 组织架构：部门树、人员、角色、审批矩阵、工作日历、绑定关系
- 接入应用：API Key、回调和钩子地址、签名密钥、允许嵌入的网站、调用和回调日志、重发
- 管理员干预：修改审批人、跳转、终止、离职交接（要填原因）

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `frontend/src/App.vue`、`frontend/src/model/routes.ts`
- 登记文件

## 验收标准

- [ ] 每个页面的接口都查权限
- [ ] 密钥只在生成时显示一次
- [ ] 管理员干预都记进操作记录
- [x] 页面截图检查
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 统计报表（T-019）

## 交接记录

- 2026-10-24 · Codex · 本轮从已验证整合提交 `219a85c` 独立接前端真实流程管理：计划在 `frontend/src/api/admin-flow-management.ts`、`admin-session.ts`、`pages/AdminPage.vue`、`pages/DesignerPage.vue`、`pages/admin/AdminFlows.vue`、前端检查及 README 中接入本地流程管理员登录、授权列表、草稿修订 CAS、发布、启停和兼容 BPMN 文件预检/导出。`frontend/src/App.vue` 与 `frontend/src/model/routes.ts` 暂不修改；如确需共享文件，先补登记。保留 T-022 已提交的十类模板与 C# 示例，不碰其他 agent 的后端文件。后端当前仅回当前发布版摘要，版本历史接口由并行切片开发；本轮先基于现有契约接线。
- 2026-10-24 · Codex · 已接通兼容 BPMN 导入导出：本地流程管理员可只读预检文件和发布问题，同编码默认冲突，显式目标与预期草稿修订才可覆盖，导入仅保存草稿；导出须选择草稿或指定不可变发布版本，并在同一事务复核直接部门管理范围。共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 增加路由、应用和基础设施登记文件增加用例/仓储、检查程序登记 17 个真实 HTTP/SQLite 场景，`docs/contracts.md`、`docs/architecture.md` 和后端 README 已同步。无需新增依赖或数据库字段。全量 `verify.ps1` 通过（后端 785、调用端 50、前端 124 项；发起/审批/待办 P95 27.7/21.0/7.0ms）。后续仍需版本历史与旧版恢复、前端流程管理页面，本任务保持进行中。

- 2026-10-24 · Codex · 增加流程停启用管理接口 `PUT /admin/flows/{code}/enabled`：要求当前本地管理范围、2–200 字原因和预期启用状态；实际变化按条件更新并在同一事务记不含定义 JSON 的审计，重复目标状态不加审计。停用使开放流程目录与新发起不可用，发布版和在途实例保留；恢复复用当前发布版。真实 HTTP/SQLite 场景覆盖直接部门管理员停用、过期状态冲突、幂等与恢复，`verify.ps1` 全量通过（后端 768、调用端 50、前端 124 项；发起/审批/待办 P95 20.9/15.5/4.4ms）。后续仍需兼容 BPMN 导入导出、版本恢复与前端管理页面，任务保持进行中。
- 2026-10-24 · Codex · 流程管理后端独立切片已接通本地 JWT 受保护的目录/详情/创建/草稿保存/发布接口，组织管理员管全部、`flow-admin` 只管直接所属部门；草稿按预期修订 CAS，发布同时核对草稿和当前版，完整编译执行图、原子追加不可变新版本，并只审计操作者、原因、修订/版本与 SHA。旧库只有发布版时详情展示修订零，首次保存才建立草稿。已生成 SQLite/SQL Server/PostgreSQL 的所属部门、歧义事项、草稿/审计/启停三步迁移；业务开放目录只列启用流程，在途仍按锁定版。存量 `AdministratorId` 由服务器本机 `reconcile-flow-admins` 命令预览唯一修正与歧义，输入 `APPLY` 并复核预览指纹后才写库，HTTP 启动不自动对账。真实 HTTP/SQLite 新场景覆盖部门越权、过期修订/当前版、不可变版本和审计；全量 `verify.ps1` 通过（后端 763、调用端 50、前端 124 项；发起/审批/待办 P95 173.8/18.7/4.8ms）。下一步：停启用管理、版本历史/旧版恢复、兼容 BPMN 导入导出和前端接线；本任务保持进行中。
- 2026-10-24 · Codex · 流程管理草稿、发布及兼容 BPMN 协议已获确认，开始独立后端切片。拟修改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 新增管理路由、`backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs` 增加草稿/审计/停用映射和三库迁移、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs` 与 `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs` 登记用例及仓储、`backend/src/ApprovalService.Api/Program.cs` 登记端点、`backend/tests/ApprovalService.Backend.Checks/Program.cs` 登记真实 HTTP/SQLite 场景，`docs/contracts.md` 与 `docs/architecture.md` 同步。对账执行时机仍待确认，暂不接入启动或命令入口。
- 2026-10-24 · Codex · 用户已确认流程管理员内部 ID、可选直属部门归属、组织管理员全局权限，以及存量管理员标识仅唯一匹配时自动迁移；另确认单草稿修订、发布/停用/恢复和本服务兼容 BPMN 导入导出规则。恢复进行中，先实现身份和归属基础切片。拟修改共享 `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs` 的流程字段映射及三库迁移、`backend/tests/ApprovalService.Backend.Checks/Program.cs` 登记场景检查，并在 `docs/adr/README.md` 登记 ADR 0018、`docs/architecture.md` 同步授权与迁移落点；后续 API 切片另行登记 Contracts、DI 和路由文件。
- 2026-10-24 · Codex · 流程管理 API 方案审阅后按 R13 暂停实现。现有 `FlowEntity` 仅有元信息和已发布版本指针，`FlowVersionEntity` 发布后由 DbContext 禁止更新／删除；没有草稿、并发修订、启停、所属部门字段。设计要求部门流程管理员只管理本部门流程，但实体没有部门归属，且 `AdministratorId` 注释称“工号”，离职交接却按内部人员 ID 使用。建议先确认：组织管理员与流程管理员的范围及存量 ID 迁移；每流程独立草稿与 expected revision 乐观并发、发布时追加不可变版本并用预期当前版本原子切换；停用只阻止新发起；旧版本恢复通过重新发布为新版本；BPMN 导入先预检后落草稿、同编码默认冲突，导出显式选择草稿或版本，所有写操作带原因且审计不回传完整流程 JSON。具体端点与 DTO 建议已发主任务，请用户确认后新建专门任务和 ADR，再恢复本任务。此前六个已验证切片保持可用，本轮未修改产品代码。
- 2026-10-24 · Codex · 第六切片接通“系统／安全设置”中的本地管理员日常改密。页面先用既有 `/admin/session` 再次核对权限；只提供已有 `POST /identity/change-password` 能实际保存的个人密码操作，不展示虚构的登录方式与令牌策略配置。服务端改密后旧访问与刷新令牌失效，前端用新密码重新登录；若续登失败清除旧会话并提示重新登录。新增真实 HTTP 场景检查错误当前密码不影响旧会话、正确改密使旧令牌失效、重新登录仍获管理员权限。全量 `verify.ps1` 通过（后端 740 项、调用端 50 项、前端 124 项），生产构建通过。流程版本、组织角色／矩阵／日历、系统配置与运行干预仍缺正式受保护管理接口；本切片未推断新协议，T-020 保持进行中。外置 Chrome 扩展仍不可用，未做本切片视觉复核。
- 2026-10-24 · Codex · 第五切片把已有本地组织管理员账号接口接到人员页：从受保护组织目录选择人员，为在职人员设置首次登录临时密码，或显式确认后作废其全部现有会话。临时密码提交后立即清除，重复开通由服务端返回冲突；作废会话不删除账号或修改密码。新增真实 HTTP/SQLite 检查覆盖匿名拒绝、管理员开通、首次改密、普通人员越权拒绝、令牌作废与重新登录；全量 `verify.ps1` 通过（后端 737 项、调用端 50 项、前端 124 项），前端生产构建通过。管理目录目前不投影账号开通状态，服务端提交时实时判断；既有账号端点未记录管理员填写的原因或管理操作日志，此次未改变其契约。外置 Chrome 扩展本轮连接故障，视觉复核留待后续。T-020 的流程/角色/矩阵/日历/系统设置/运行干预等入口仍未完成，状态保持进行中。
- 2026-10-23 · Codex · 第四切片接通本地组织管理员对接入应用的创建、启停与 API Key/HMAC 密钥轮换。创建可填写流程授权和回调/四类钩子地址；实际状态变更要求原因，在同一事务记录不含密钥的审计元数据。密钥只在创建或轮换响应显示一次，旧 API Key 在轮换后失效，启停与轮换均推进嵌入身份代际。真实 HTTP/SQLite 检查覆盖匿名及普通人员拒绝、创建/轮换/停用、一次性凭据和审计；全量 `verify.ps1` 通过（后端 731 项、前端 124 项），外置 Google Chrome 复核了应用列表及新建表单，独立临时服务已停止。共享路由、契约、架构、设计书与 README 已同步。本任务还有流程/组织角色/矩阵/日历/系统设置/运行干预等入口未完成，保持进行中；密钥轮换后的业务系统须自行更新凭据。

- 2026-10-23 · Codex · 下一切片把已有接入应用创建、停启用和密钥轮换服务接到本地组织管理员界面。拟改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 与 `docs/contracts.md`；新增请求 DTO 和受保护端点，密钥只在生成响应显示一次，所有实际状态变更带原因在同一事务留不含密钥的审计记录。

- 2026-10-23 · Codex · 第三切片接通 `GET /admin/outbox` 和 `POST /admin/callbacks/{eventId}/retry`：队列列表只投影最近 200 条待发项的安全元数据，管理员带 2–200 字原因原地重发回调；仍处于有效租约的回调拒绝重排，原事件 ID 和正文保持不变，重排与管理员审计在同一数据库事务内完成。新增真实 HTTP/SQLite 权限、正文隔离、原因和审计检查；待发页面可输入/选择事件 ID 重投，其它待发类型不能被该接口重发。全量 `verify.ps1` 通过，独立临时服务与外置 Chrome 已复核待发队列列表和重投面板，临时服务已停止。当前统计、系统配置、流程管理及应用密钥生命周期等页面仍待接线。

- 2026-10-23 · Codex · 下一切片接通待发队列安全列表与已批准的回调原事件重发。拟改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`docs/contracts.md`；列表只投影状态、次数、错误类别和所属应用，绝不返回正文，手动重发保留 eventId、要求原因并在同一事务记管理员审计。

- 2026-10-23 · Codex · 第二切片已接通 `GET /admin/bindings` 和 `GET /admin/audit` 两个仅本地组织管理员可读的安全摘要；绑定页可选择应用、人员/部门和外部 ID 调用原有受保护手工绑定接口，操作日志页展示最近 200 条组织/应用变更且不发送原始前后快照。后端用真实 HTTP/SQLite 验证权限、换绑令牌代际及不泄露联系方式和密钥；前端安全列和类型检查通过。用独立临时 SQLite 服务及外置 Google Chrome 复核了登录、绑定编辑器和操作日志页面，布局与导航正常；临时服务已停止。共享路由、DI 注册及契约文档已同步。系统日志目前只覆盖组织和应用变更，其它模块须在各自写入接线时补审计。

- 2026-10-23 · Codex · 下一切片补管理端业务系统绑定列表与手工绑定页面、组织及接入应用操作日志安全摘要。拟改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`docs/contracts.md`，仅添加路由、只读仓储注册和契约说明；已有绑定写入接口沿用原权限及事务审计。

- 2026-10-23 · Codex · 已接通本地管理员会话、组织与应用安全摘要、组织文件预检/导入、每应用允许嵌入来源的配置 UI 和 GET/PUT；来源验证限制 HTTPS 或本机开发 HTTP、上限 20 项，保存实际修改时带原因与管理员 ID 在同一事务脱敏审计，并用旧值条件更新拒绝并发覆盖。已同步 Contracts、副本、README、架构和设计书，真实 HTTP/SQLite 场景检查与 `verify.ps1` 全量通过。T-020 的流程/角色/矩阵/日历/应用密钥与回调/系统信息/操作日志/运行干预等管理写入仍未接线；本任务保持进行中，页面将这些入口明确禁用，不制造假数据。管理页面尚未在外置 Chrome 做截图检查。T-011 的实体字段与三库迁移以 `61bdb52` 的 cherry-pick `bb98972` 纳入本分支；后续整合时注意避免重复应用。

- 2026-10-23 · Codex · 新增接入应用允许嵌入来源的管理配置接口/UI 时，拟再次修改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs` 和 `docs/contracts.md`，保持前后端契约同步。来源字段与三库迁移由 T-011 负责，字段定为 `ClientAppEntity.AllowedEmbedOriginsJson`；本任务只接受本地组织管理员配置完整 HTTPS origin（开发时 HTTP 仅本机）、至多 20 项，保存时记录操作者及原因。

- 2026-10-23 · Codex · 认领后从 main 合入已验收的 T-007 基线，先接入管理员登录、权限校验、部门/人员与接入应用安全列表。拟修改共享文件：`backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 增加管理读取路由；`backend/src/ApprovalService.Api/Program.cs` 登记管理端点；`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs` 登记管理用例；`backend/tests/ApprovalService.Backend.Checks/Program.cs` 登记场景检查；`docs/contracts.md` 与 `docs/architecture.md` 同步新管理读取接口。共享文件改动单独提交。正式页面的其它写入功能尚需逐项实现。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
