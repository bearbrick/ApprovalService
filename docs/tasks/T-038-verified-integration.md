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

- 2026-10-20 · Codex · 从 `main` 新建独立工作树，预登记所有跨部分共享文件和指定提交边界；先合入 T-036 已验收基线，再按序合入三个已验证但仍阻塞的任务 HEAD。5289 端口须在全量验证前协调使用。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
