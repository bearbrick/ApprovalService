# T-034 流程级撤回与退回重提策略

状态：进行中
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
- `frontend/src/model/flow.ts`、`frontend/src/designer/workbench/**`、`frontend/src/designer/canvas/**`、`frontend/tests/**`：契约副本、配置交互、纯逻辑默认值与检查。
- `docs/contracts.md`、`backend/README.md`、`frontend/README.md`、必要时 `docs/design/approval-service-design.html`：同步现有设计和实现的约定。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/tasks/README.md`：追加 T-034 索引。
- `backend/src/ApprovalService.Contracts/Flows/**` 与 `frontend/src/model/flow.ts`、`docs/contracts.md`：同一提交同步新增可选策略及其默认语义。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：只有新增独立检查类时追加一行登记。

## 验收标准

- [ ] 流程定义可配置“审批结束前可撤回 / 首人处理前可撤回”和“从头重提 / 回到退回节点”，缺省值维持现有行为。
- [ ] 前端副本与后端流程契约同步；设计器配置、序列化、发布校验与场景检查覆盖新策略。
- [ ] 不碰 T-006 在途集成代码，不新增依赖；共享文件同步、交接记录完整。
- [ ] `verify.ps1` 全量通过，相关文档已更新。

## 不做

- 不修改运行时接口接线、已创建审批单的状态持久化、T-006 正在认领的发起构建器；由 T-006 后续把发布策略传给引擎并保存到审批单状态。
- 不合并到 `main`，不推送远程。

## 交接记录

- 2026-10-20 · Codex · 从 `main` 建立独立任务并认领；先登记将同步的共享流程契约、前端副本、契约文档、任务索引及可能的检查登记，再接入已验收 T-032 基线。外置 Chrome 在本轮被执行策略阻止；遵循个人规则停止浏览器步骤，使用源码和自动检查验证。
