# T-042 钩子与 SDK 夹具稳定集成快照

状态：进行中
负责人：Codex 2026-10-22
里程碑：M2～M4 集成
依赖：T-039 `b3e25e9` 已验证提交；T-040 `5967662`、T-006 `f0e9cbc` 已验证稳定提交边界
设计书：§arch、§engine、§integration、§api

## 目标

在独立分支合入已验证的应用附件基线、SDK 真实服务夹具和发起前取数、校验钩子，确认三部分与旧数据库可共同运行。

## 范围

- 本任务文件、任务索引，以及上述三个固定提交中的源文件和历史；不修改来源任务工作树。
- 只解决确定的集成冲突，保留发起时附件原子关联、钩子事务前调用、检查登记及动态验证端口；不加入 T-006 未提交的后续取人或动作开发。
- 对合并结果做全量验证、三库 EF 模型一致性检查和 T-038 旧 SQLite 升级；必要的同步文档仅说明最终已实现行为。

## 共享文件

合入前登记，逐项核对双方语义；登记文件同时保留双方内容，拿不准按 R13 停止：

- `verify.ps1`、`docs/tasks/README.md`、`docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`、`docs/design/approval-service-design.html`、三部分 README：保留动态端口、任务索引与稳定来源说明。
- `backend/Directory.Packages.props`、`backend/src/ApprovalService.Contracts/**`、`backend/src/ApprovalService.Api/Program.cs`、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`：保留契约、服务和检查登记。
- `backend/src/ApprovalService.Infrastructure/Persistence/**` 与三库历史迁移、快照：保留 T-039 已验证的迁移链，不手改生成文件。
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、`client/src/ApprovalService.Client/Internal/Protocol.cs`、`client/tests/ApprovalService.Client.Checks/Program.cs`：保持后端契约与 SDK 副本一致。

## 验收标准

- [ ] 三个固定 HEAD 均为本分支祖先，来源工作树未被修改，未合并主分支或推送远程。
- [ ] 同应用附件原子关联与发起前取数、校验钩子共同生效；检查程序登记和 SDK 真实服务夹具完整。
- [ ] 三库模型与快照一致；SQLite 空库和 T-038 旧库升级通过。
- [ ] `verify.ps1` 全量通过，记录各部分检查数和未实测范围。
- [ ] 无密钥、生成物及未解决冲突。

## 不做

- 不合并 T-006 当前未提交的动态取人、自动动作或其他后续工作。
- 不修改产品设计或新增依赖；若合并需要不兼容契约变更，按 R13 停止。

## 交接记录

- 2026-10-22 · Codex · 从 `main` 创建独立工作树，在合并前登记固定来源提交、共享文件及冲突处理边界；待逐项核对并验证。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
