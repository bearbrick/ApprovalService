# T-038 已验证稳定分支整合快照

状态：已完成
负责人：Codex 2026-10-20
里程碑：M2～M4 集成
依赖：T-036 已验收基线；T-006 `0fe7fd9`、T-007 `de9fcfd`、T-037 `52a8250` 的已验证稳定提交
设计书：§scope、§arch、§center、§org、§integration、§api、§demo

## 目标

在独立分支形成三部分可共同编译和验证的稳定快照，供后续功能开发与验收使用；被集成任务的阻塞状态和未完成接口边界保持原样。

## 范围

- 本任务文件及任务索引。
- 合入 T-036 `70a4d05` 已验收 UI 基线、T-006 `0fe7fd9`、T-007 `de9fcfd`、T-037 `52a8250` 的原有提交与文件。
- 仅为确定的集成冲突修改登记文件和相应文档；不新增审批业务逻辑，不改各来源任务工作树。

## 共享文件

合入前登记；登记类冲突保留双方条目，其他冲突先核对语义，拿不准则按 R13 停止：

- `docs/tasks/README.md`：保留已验收和阻塞任务索引并追加 T-038。
- `docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`、`docs/design/approval-service-design.html`、三部分 README：保留来源分支各自已验证的契约与实现说明。
- `backend/src/ApprovalService.Contracts/**`、`backend/src/ApprovalService.Api/Program.cs`、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`、`backend/Directory.Packages.props`、持久化模型和三种数据库迁移：逐项核对来源分支的兼容内容，迁移冲突不得手工改写生成物。
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、`client/src/ApprovalService.Client/Internal/Protocol.cs`、`client/tests/ApprovalService.Client.Checks/Program.cs`、`frontend/src/App.vue`、`frontend/src/model/routes.ts`：保持后端契约副本和各路由来源一致。

## 验收标准

- [x] 指定四个 HEAD 均是集成快照的祖先，且来源工作树未被修改。
- [x] 保留 T-006、T-007、T-037 的真实任务状态与未完成事项；不合入 T-013。
- [x] 冲突和处理结果在交接记录中完整列出，无未解决冲突、密钥或生成物。
- [x] `verify.ps1` 全量通过（见 AGENTS.md）。
- [x] 任务索引与本交接记录已更新。

## 不做

- 不合并 `main`、不推送远程、不重写历史。
- 不开发新业务行为，不把来源任务标记为已完成。
- 不合入仍在独立安全修复中的 T-013。

## 交接记录

- 2026-10-20 · Codex · 独立验收通过：核对四个指定来源 HEAD 均为祖先、任务范围和未完成状态保持真实，工作树干净；最终全量检查 576/45/119 通过，三库模型快照一致，SQLite 空库与旧库升级均成功。T-038 集成快照达到本任务完成定义；未合并主分支或推送。
- 2026-10-20 · Codex · 最终独立验收：T-036 `70a4d05`、T-006 最终稳定 HEAD `0fe7fd9`、T-007 `de9fcfd`、T-037 `52a8250` 均为当前分支祖先；T-013 `703b498` 不在祖先中。最终完整 `verify.ps1` 通过：后端 576、调用端 45（含真实服务端到端）、前端 119 项及构建，编译零警告，5289 已释放；工作树无冲突和生成物。保留 T-006 “进行中”、T-007/T-037 “阻塞”，剩余接口、首次管理员引导、绑定语义和 SDK 成功端到端等由原任务后续完成。本任务只形成稳定整合快照，未合并 `main`、未推送。三库模型与快照一致，真实迁移运行仅在 SQLite 空库及 T-036 旧库验证；SQL Server、PostgreSQL 需有测试库时再做升级实测。
- 2026-10-20 · Codex · 原四分支验证后，在交接先登记并择取 T-006 已验证 P2 原子修复 `5be7c44` 及任务记录 `e40c201`，随后合入其最终稳定 HEAD `0fe7fd9` 保留来源祖先与“进行中”状态。仅 `T-006-open-api.md` 的交接新增项发生冲突，保留该项；产品、契约和设计文档自动合并。没有纳入新功能或 T-013；接下来对最终快照重跑全量验证。
- 2026-10-20 · Codex · SQLite 迁移专项：先用 T-036 原程序集创建实际旧库，已应用 6 个迁移；再用本整合分支升级，已应用 11 个迁移、无待执行且模型无差异。另用本整合分支从空库直接迁移至相同 11 项，也无待执行和模型差异。SQL Server、PostgreSQL 没有配置测试数据库，仅用 EF 的模型与快照检查验证；不能把 SQLite 实测推断成另外两库的真实升级结果。
- 2026-10-20 · Codex · 原四分支全量 `verify.ps1` 已通过：后端 568、调用端 43（含真实服务端到端）、前端 119，构建零警告；5289 已释放。协调者另提供 T-006 已验证 P2 修复 `5be7c44` 及登记 `e40c201`，拟仅在本分支择取这两个原子提交。预登记可能触及的共享 `backend/src/ApprovalService.Contracts/Protocol/**`、`docs/contracts.md`、`docs/design/approval-service-design.html`、后端 README 和 `backend/tests/ApprovalService.Backend.Checks/ContractChecks.cs`；若有复杂冲突，保持上述已验证快照并将修复留给下一节点。
- 2026-10-20 · Codex · 四个指定 HEAD 已成为当前分支祖先，T-006、T-007、T-037 的任务状态均保留“阻塞”。T-006 合入冲突为后端 README、DbContext、三份 EF 快照、ContractChecks、FlowDefinitionChecks、检查入口和 architecture；保留双方独立内容，检查入口去重。T-007 仅后端 README 冲突，保留较新的身份生命周期说明和原有开放接口说明；T-037 无冲突。三库 EF 工具生成的模型差异仅为 `ScheduledJobs.LastError` 和 `InAppNotifications`，与 T-036 已有迁移重复；只删除本次生成的六个重复迁移文件，保留工具生成的最终快照。三库 `has-pending-model-changes` 均报告无差异。
- 2026-10-20 · Codex · 合入 T-006 时发现其重生的三库 `AddOrganizationIdentity` 与 T-036 已验收的旧 ID 重复。逐库比较原始 `.cs` 完整内容（含 `Up`、`Down`）的 Git 对象哈希：SQLite 均为 `91e8ffb`、SQL Server 均为 `60a6426`、PostgreSQL 均为 `365cfad`，三库完全相同。依协调确认保留 T-036 的旧迁移 ID，弃用 T-006 的同义新 ID，保留 T-006 其余独立迁移；最终模型快照用 EF 工具重建，并验证空库及旧迁移升级路径。
- 2026-10-20 · Codex · 从 `main` 新建独立工作树，预登记所有跨部分共享文件和指定提交边界；先合入 T-036 已验收基线，再按序合入三个已验证但仍阻塞的任务 HEAD。5289 端口须在全量验证前协调使用。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
