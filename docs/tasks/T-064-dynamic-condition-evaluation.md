# T-064 后续网关按审批人填写值求条件

状态：待验收
负责人：Codex 2026-10-26
里程碑：M1
依赖：T-004、T-022、T-063 已验证基线
设计书：§rules、§form、§engine、§designer

## 目标

质量异常模板在质检审批人填写 `reworkDecision` 后由网关决定返工路径。发起时该字段尚不存在，当前正式发起和模拟都提前计算所有条件而失败。把条件求值更新到每次可能流转的命令，使用锁定业务数据、已成功提交的审批人填写值及本次待提交值，不让业务系统在发起数据中伪造审批人字段。

## 范围

- `backend/src/ApprovalService.Application/Integration/OpenStartContextBuilder.cs`、`backend/src/ApprovalService.Application/Runtime/ApprovalRuntimeAppService.cs`、新增条件求值组件：正式发起与后续命令的条件快照。
- `backend/src/ApprovalService.Application/Administration/FlowSimulationAppService.cs`、`FlowSimulationSession.cs`：假数据和内存审批人填写值的同规则条件求值。
- `backend/src/ApprovalService.Contracts/Protocol/FlowSimulationModels.cs`：模拟人工步骤可选 `fieldValues`，不更改已有字段含义。
- `backend/tests/ApprovalService.Backend.Checks/DynamicConditionChecks.cs`、`FlowSimulationChecks.cs`：发起、审批填写与后续网关的正式及模拟场景。
- `docs/contracts.md`、`backend/README.md`、`docs/design/approval-service-design.html`：同步条件求值时机与模拟字段协议。
- `docs/tasks/T-064-dynamic-condition-evaluation.md`：交接和验收。

## 共享文件

- `docs/tasks/README.md`：追加任务索引，单独提交。
- `docs/contracts.md`：追加兼容的模拟步骤可选字段说明。
- `backend/src/ApprovalService.Contracts/Protocol/FlowSimulationModels.cs`：可选请求属性。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：追加场景检查调用。

## 验收标准

- [x] 发起时未填审批人字段仍可进入质检任务，填写 `reworkDecision` 后正确选择返工或直接结束路径。
- [x] 后续命令使用最新业务数据与已提交审批人值；失败、拒绝或不推进的操作不把本次字段用于流转。
- [x] 同一流程在内存模拟可由假处理人提交字段值走完，模拟无持久写入或外部发送。
- [x] 全量 `verify.ps1` 通过；十个内置模板的完整模拟由 T-022 在模板回路配置修正后验收。

## 交接记录

- 2026-10-26 · Codex · 正式运行在每次命令用锁定业务快照、已成功填写值及本次成功推进值重算条件，退回重提不带旧填写值；发起时可延迟审批人字段条件，网关实际缺值会拒绝。内存模拟增加可选 `fieldValues`，仅允许当前节点可编辑的审批人字段，同样逐步重算。真实质量异常模板的正式返工及完成路径、拒绝/无权填写和内存模拟均有场景检查；全量 `verify.ps1` 通过。设计书 §form、契约与后端 README 已同步。模板库的 `inspection` 回路仍须由 T-022 改成 `duplicateAssigneeBehavior=everyTime`，并同步十模板夹具；本任务的正式场景仅在内存中将该配置改为每次审批以验证条件语义。
- 2026-10-26 · Codex · T-022 独立验收确认十模板均通过正式发布编译，九个可完成内存模拟；`QUALITY_EXCEPTION` 的 q03 依赖质检节点后才填写的 `reworkDecision`，发起时提前求值失败。正式 `OpenStartContextBuilder` 与模拟服务都存在同类问题。先修正式条件语义，再回 T-022 完成十模板验收。
