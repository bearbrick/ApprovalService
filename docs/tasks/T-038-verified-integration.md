# T-038 已验证稳定分支整合快照

状态：进行中
负责人：Codex 2026-10-20
里程碑：M2～M4 集成
依赖：T-036 已验收基线；T-006 `6c1bc41`、T-007 `de9fcfd`、T-037 `52a8250` 的已验证稳定提交
设计书：§scope、§arch、§center、§org、§integration、§api、§demo

## 目标

在独立分支形成三部分可共同编译和验证的稳定快照，供后续功能开发与验收使用；被集成任务的阻塞状态和未完成接口边界保持原样。

## 范围

- 本任务文件及任务索引。
- 合入 T-036 `70a4d05` 已验收 UI 基线、T-006 `6c1bc41`、T-007 `de9fcfd`、T-037 `52a8250` 的原有提交与文件。
- 仅为确定的集成冲突修改登记文件和相应文档；不新增审批业务逻辑，不改各来源任务工作树。

## 共享文件

合入前登记；登记类冲突保留双方条目，其他冲突先核对语义，拿不准则按 R13 停止：

- `docs/tasks/README.md`：保留已验收和阻塞任务索引并追加 T-038。
- `docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`、`docs/design/approval-service-design.html`、三部分 README：保留来源分支各自已验证的契约与实现说明。
- `backend/src/ApprovalService.Contracts/**`、`backend/src/ApprovalService.Api/Program.cs`、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`、`backend/Directory.Packages.props`、持久化模型和三种数据库迁移：逐项核对来源分支的兼容内容，迁移冲突不得手工改写生成物。
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、`client/src/ApprovalService.Client/Internal/Protocol.cs`、`client/tests/ApprovalService.Client.Checks/Program.cs`、`frontend/src/App.vue`、`frontend/src/model/routes.ts`：保持后端契约副本和各路由来源一致。

## 验收标准

- [ ] 指定四个 HEAD 均是集成快照的祖先，且来源工作树未被修改。
- [ ] 保留 T-006、T-007、T-037 的真实阻塞状态与未完成事项；不合入 T-013。
- [ ] 冲突和处理结果在交接记录中完整列出，无未解决冲突、密钥或生成物。
- [ ] `verify.ps1` 全量通过（见 AGENTS.md）。
- [ ] 任务索引与本交接记录已更新。

## 不做

- 不合并 `main`、不推送远程、不重写历史。
- 不开发新业务行为，不把来源任务标记为已完成。
- 不合入仍在独立安全修复中的 T-013。

## 交接记录

- 2026-10-20 · Codex · 四个指定 HEAD 已成为当前分支祖先，T-006、T-007、T-037 的任务状态均保留“阻塞”。T-006 合入冲突为后端 README、DbContext、三份 EF 快照、ContractChecks、FlowDefinitionChecks、检查入口和 architecture；保留双方独立内容，检查入口去重。T-007 仅后端 README 冲突，保留较新的身份生命周期说明和原有开放接口说明；T-037 无冲突。三库 EF 工具生成的模型差异仅为 `ScheduledJobs.LastError` 和 `InAppNotifications`，与 T-036 已有迁移重复；只删除本次生成的六个重复迁移文件，保留工具生成的最终快照。三库 `has-pending-model-changes` 均报告无差异。
- 2026-10-20 · Codex · 合入 T-006 时发现其重生的三库 `AddOrganizationIdentity` 与 T-036 已验收的旧 ID 重复。逐库比较原始 `.cs` 完整内容（含 `Up`、`Down`）的 Git 对象哈希：SQLite 均为 `91e8ffb`、SQL Server 均为 `60a6426`、PostgreSQL 均为 `365cfad`，三库完全相同。依协调确认保留 T-036 的旧迁移 ID，弃用 T-006 的同义新 ID，保留 T-006 其余独立迁移；最终模型快照用 EF 工具重建，并验证空库及旧迁移升级路径。
- 2026-10-20 · Codex · 从 `main` 新建独立工作树，预登记所有跨部分共享文件和指定提交边界；先合入 T-036 已验收基线，再按序合入三个已验证但仍阻塞的任务 HEAD。5289 端口须在全量验证前协调使用。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
