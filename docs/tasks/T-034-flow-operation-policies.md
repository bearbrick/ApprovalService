# T-034 流程级撤回与退回重提策略

状态：待验收
负责人：Codex 2026-10-20
里程碑：M2 / M1
依赖：T-001、T-004、T-008～T-010、T-032
设计书：§3、§4（审批人退回、发起人撤回）

## 目标

让流程管理员在自由画布工作台配置流程级撤回限制和退回后重提方式，使发布定义能携带稳定的策略；缺省定义保持现有引擎行为。

## 范围

可以新建、修改的文件：

- `docs/tasks/T-034-flow-operation-policies.md`、`docs/tasks/README.md`。
- `backend/src/ApprovalService.Contracts/Flows/**`、`backend/src/ApprovalService.Engine/Definitions/**`、`backend/tests/ApprovalService.Backend.Checks/**`：向后兼容的流程策略契约、发布校验和场景检查。
- `frontend/src/model/flow.ts`、`frontend/src/model/flow-execution.ts`、`frontend/src/designer/workbench/**`、`frontend/src/designer/canvas/**`、`frontend/tests/**`：契约副本、配置交互、纯逻辑默认值与检查。运行图类型单独放 `flow-execution.ts`，以维持单文件 500 行上限。
- `docs/contracts.md`、`backend/README.md`、`frontend/README.md`、必要时 `docs/design/approval-service-design.html`：同步现有设计和实现的约定。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/tasks/README.md`：追加 T-034 索引。
- `backend/src/ApprovalService.Contracts/Flows/**` 与 `frontend/src/model/flow.ts`、`docs/contracts.md`：同一提交同步新增可选策略及其默认语义。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：只有新增独立检查类时追加一行登记。

## 验收标准

- [x] 流程定义可配置“审批结束前可撤回 / 首人处理前可撤回”和“从头重提 / 回到退回节点”，缺省值维持现有行为。
- [x] 前端副本与后端流程契约同步；设计器配置、序列化、发布校验与场景检查覆盖新策略。
- [x] 不碰 T-006 在途集成代码，不新增依赖；共享文件同步、交接记录完整。
- [x] `verify.ps1` 全量通过，相关文档已更新。

## 不做

- 不修改运行时接口接线、已创建审批单的状态持久化、T-006 正在认领的发起构建器；由 T-006 后续把发布策略传给引擎并保存到审批单状态。
- 不合并到 `main`，不推送远程。

## 交接记录

- 2026-10-20 · Codex · 契约新增可省略的撤回限制与退回重提方式，前端副本、契约文档和后端检查同一提交同步；编译图固定策略，设计器流程设置提供两项选择，旧草稿自动采用现有默认值，前后端拒绝未知重提方式。更新前后端 README；设计书 §4 已明确两项模式，无须改动。新增后端场景检查和前端纯模型检查，`verify.ps1` 全量通过（后端 466、调用端 33、前端 109）；没有新增依赖或修改 T-006 文件。后续 T-006 须在发起时把发布图策略传入引擎操作上下文并持久化实例状态。外置 Chrome 启动仍受执行策略阻止，遵循个人规则未做浏览器视觉验证。分支保持独立，待验收，不合并 `main` 或推送。
- 2026-10-20 · Codex · 从 `main` 建立独立任务并认领；先登记将同步的共享流程契约、前端副本、契约文档、任务索引及可能的检查登记，再接入已验收 T-032 基线。外置 Chrome 在本轮被执行策略阻止；遵循个人规则停止浏览器步骤，使用源码和自动检查验证。
