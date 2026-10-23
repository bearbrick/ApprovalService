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

- 2026-10-23 · Codex · 第三切片接通 `GET /admin/outbox` 和 `POST /admin/callbacks/{eventId}/retry`：队列列表只投影最近 200 条待发项的安全元数据，管理员带 2–200 字原因原地重发回调；仍处于有效租约的回调拒绝重排，原事件 ID 和正文保持不变，重排与管理员审计在同一数据库事务内完成。新增真实 HTTP/SQLite 权限、正文隔离、原因和审计检查；待发页面可输入/选择事件 ID 重投，其它待发类型不能被该接口重发。全量 `verify.ps1` 通过，独立临时服务与外置 Chrome 已复核待发队列列表和重投面板，临时服务已停止。当前统计、系统配置、流程管理及应用密钥生命周期等页面仍待接线。

- 2026-10-23 · Codex · 下一切片接通待发队列安全列表与已批准的回调原事件重发。拟改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`docs/contracts.md`；列表只投影状态、次数、错误类别和所属应用，绝不返回正文，手动重发保留 eventId、要求原因并在同一事务记管理员审计。

- 2026-10-23 · Codex · 第二切片已接通 `GET /admin/bindings` 和 `GET /admin/audit` 两个仅本地组织管理员可读的安全摘要；绑定页可选择应用、人员/部门和外部 ID 调用原有受保护手工绑定接口，操作日志页展示最近 200 条组织/应用变更且不发送原始前后快照。后端用真实 HTTP/SQLite 验证权限、换绑令牌代际及不泄露联系方式和密钥；前端安全列和类型检查通过。用独立临时 SQLite 服务及外置 Google Chrome 复核了登录、绑定编辑器和操作日志页面，布局与导航正常；临时服务已停止。共享路由、DI 注册及契约文档已同步。系统日志目前只覆盖组织和应用变更，其它模块须在各自写入接线时补审计。

- 2026-10-23 · Codex · 下一切片补管理端业务系统绑定列表与手工绑定页面、组织及接入应用操作日志安全摘要。拟改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`docs/contracts.md`，仅添加路由、只读仓储注册和契约说明；已有绑定写入接口沿用原权限及事务审计。

- 2026-10-23 · Codex · 已接通本地管理员会话、组织与应用安全摘要、组织文件预检/导入、每应用允许嵌入来源的配置 UI 和 GET/PUT；来源验证限制 HTTPS 或本机开发 HTTP、上限 20 项，保存实际修改时带原因与管理员 ID 在同一事务脱敏审计，并用旧值条件更新拒绝并发覆盖。已同步 Contracts、副本、README、架构和设计书，真实 HTTP/SQLite 场景检查与 `verify.ps1` 全量通过。T-020 的流程/角色/矩阵/日历/应用密钥与回调/系统信息/操作日志/运行干预等管理写入仍未接线；本任务保持进行中，页面将这些入口明确禁用，不制造假数据。管理页面尚未在外置 Chrome 做截图检查。T-011 的实体字段与三库迁移以 `61bdb52` 的 cherry-pick `bb98972` 纳入本分支；后续整合时注意避免重复应用。

- 2026-10-23 · Codex · 新增接入应用允许嵌入来源的管理配置接口/UI 时，拟再次修改共享 `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs` 和 `docs/contracts.md`，保持前后端契约同步。来源字段与三库迁移由 T-011 负责，字段定为 `ClientAppEntity.AllowedEmbedOriginsJson`；本任务只接受本地组织管理员配置完整 HTTPS origin（开发时 HTTP 仅本机）、至多 20 项，保存时记录操作者及原因。

- 2026-10-23 · Codex · 认领后从 main 合入已验收的 T-007 基线，先接入管理员登录、权限校验、部门/人员与接入应用安全列表。拟修改共享文件：`backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 增加管理读取路由；`backend/src/ApprovalService.Api/Program.cs` 登记管理端点；`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs` 登记管理用例；`backend/tests/ApprovalService.Backend.Checks/Program.cs` 登记场景检查；`docs/contracts.md` 与 `docs/architecture.md` 同步新管理读取接口。共享文件改动单独提交。正式页面的其它写入功能尚需逐项实现。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
