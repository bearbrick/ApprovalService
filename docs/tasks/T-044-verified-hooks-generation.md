# T-044 钩子与应用身份代际集成验证

状态：进行中
负责人：Codex 2026-10-23
里程碑：M2～M4 集成
依赖：T-041 `ed91b32` 已验证提交、T-006 `0568558` 已验证稳定提交
设计书：§arch、§engine、§integration、§api

## 目标

把已验证的应用身份代际修复与实际节点取人、自动业务动作钩子集成到同一稳定分支，确认附件、身份、钩子和 SDK 真实服务检查可共同运行。

## 范围

- 本任务文件、任务索引，以及上述两个固定提交中的源文件和历史；不修改来源任务工作树。
- 只解决确定的集成冲突，保留已验证的附件归属、身份失效、钩子调用和检查登记。
- 全量验证三个部分、三种 EF 提供程序的模型一致性以及旧 SQLite 数据库升级；必要的同步文档只说明最终已实现行为。

## 共享文件

合入前登记，逐项核对双方语义；登记文件同时保留双方内容，拿不准按 R13 停止：

- `docs/tasks/README.md`、`docs/contracts.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`、三部分 README。
- `backend/src/ApprovalService.Contracts/**`、`backend/src/ApprovalService.Api/Program.cs`、应用和基础设施 DI、后端检查入口。
- `backend/src/ApprovalService.Infrastructure/Persistence/**` 与三库迁移快照；不手改生成文件。
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、客户端检查入口及 `verify.ps1`。

## 验收标准

- [ ] 两个固定 HEAD 均为本分支祖先，来源工作树未被修改。
- [ ] 应用身份代际、实际节点业务取人和持久自动动作同时生效。
- [ ] 三库模型与快照一致，旧 SQLite 升级通过。
- [ ] `verify.ps1` 全量通过，记录检查数与未实测范围。
- [ ] 无密钥、生成物及未解决冲突。

## 不做

- 不合入 T-006 当前未提交的 PATCH、重新提交、预览草稿。
- 不修改产品设计或新增依赖；若需要不兼容契约变更，按 R13 停止。

## 交接记录

- 2026-10-23 · Codex · 从 `main` 创建独立分支，先登记固定来源提交与共享文件，准备合并验证。
