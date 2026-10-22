# T-039 后续稳定功能集成快照

状态：待认领
负责人：—
里程碑：M2～M4 集成
依赖：T-038 `a23eb76` 已验收基线；T-006 `f4e4586`、T-007 `51dc9f1`、T-013 `b8ecec6`、T-029 `3ce08a8` 已验证稳定提交边界
设计书：§scope、§arch、§engine、§org、§integration、§api

## 目标

在独立分支集成四个后续稳定提交，验证三部分可共同工作。让 T-013 的应用归属附件可在 T-006 的发起事务中安全关联，保留现有数据库的逐步升级路径。

## 范围

- 本任务文件及任务索引。
- 只合入以上五个固定提交所涵盖的既有源文件和历史，不修改各来源任务工作树。
- 为解决确定的集成冲突，修改后端应用、领域、持久化、API、契约、检查与对应文档；只补集成需要的原子附件关联，不扩展其他业务逻辑。
- 三种 EF 提供程序最终模型快照仅用工具重建；用真实 SQLite 空库和旧库验证迁移。

## 共享文件

合入前登记；登记类冲突保留双方内容，其他文件逐项确认语义，无法确认按 R13 停止：

- `docs/tasks/README.md`、`docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`、`docs/design/approval-service-design.html` 与三部分 README：保留稳定来源的契约和实现说明。
- `backend/src/ApprovalService.Contracts/**`、`backend/src/ApprovalService.Api/Program.cs`、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`：保留各模块登记与镜像契约。
- `backend/src/ApprovalService.Infrastructure/Persistence/**` 与三种数据库迁移：核对已有迁移 ID，生成最终快照；不手改迁移和快照。
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、`client/src/ApprovalService.Client/Internal/Protocol.cs`、`client/tests/ApprovalService.Client.Checks/Program.cs`、`frontend/src/App.vue`、`frontend/src/model/routes.ts`：维持现有 SDK、前端和服务端契约一致。

## 验收标准

- [ ] 五个固定 HEAD 均是本分支祖先；来源工作树未被修改。
- [ ] 已上传附件只能由所属接入应用在发起审批时原子关联，非法或跨应用附件导致整笔发起失败。
- [ ] 三种提供程序最终模型与快照一致；SQLite 空库和 T-038 旧库升级通过。
- [ ] `verify.ps1` 全量通过，交接记录如实记录未实现和未实测范围。
- [ ] 无密钥、生成物和未解决冲突。

## 不做

- 不合并 `main`、不推送远程、不重写历史。
- 不把来源仍在进行中的任务标记为已完成，不承接 T-006 后续钩子等未集成开发。

## 交接记录

- 2026-10-22 · Codex · 从 `main` 创建独立工作树，登记后续固定稳定边界及预期共享文件。先合入 T-038，再逐项合入 T-006、T-007、T-029、T-013；迁移链与原子附件关联须在完整验证后才可验收。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
