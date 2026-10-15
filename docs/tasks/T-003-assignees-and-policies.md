# T-003 找审批人与多人审批方式

状态：进行中
负责人：Codex 2026-10-15
里程碑：M2
依赖：T-002
设计书：§rules

## 目标

设计书第 4 节“找审批人”的 14 种来源、“怎么审、自动处理”的全部设置在引擎里实现，每条都有场景检查。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Engine/Assignees/`：审批人来源（扩展点 `IAssigneeResolver`），组织查询通过上下文传入的抽象接口
- `backend/src/ApprovalService.Engine/Policies/`：依次、会签、或签、票签（比例、权重）
- 自动处理：找不到审批人、审批人就是发起人、同一人多次出现（相邻 / 全流程 / 每次）、审批人离职或停用
- `backend/tests/ApprovalService.Backend.Checks/AssigneeChecks.cs`、`ApprovalPolicyChecks.cs`（新建）
- `backend/src/ApprovalService.Engine/Execution/EngineModels.cs`、`EngineWork.cs`、`EngineRuntime.cs`、`EngineStateCopy.cs`、`EngineNodeInputCopy.cs`：T-002 运行时只接受单人，需接入多人任务、逐票命令、节点到达时取人及持久化的处理记录；仅修改与这些规则直接相关的代码
- `backend/src/ApprovalService.Contracts/Flows/FlowNodeSettings.cs`、`FlowApprovalKinds.cs`（新建）、`frontend/src/model/flow.ts`、`flow-approval-kinds.ts`（新建）：补足设计书已定的角色本部门限定、连续上级截止职级、无处理人转指定人员配置，并统一来源/方式/异常行为编码；常量另放新文件以保持 `flow.ts` 少于 500 行
- `backend/tests/ApprovalService.Backend.Checks/ContractChecks.cs`：核对新增流程编码的跨端镜像
- `docs/contracts.md`、`docs/design/approval-service-design.html`：同步兼容新增的流程配置字段和编码

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/tests/ApprovalService.Backend.Checks/Program.cs`
- `backend/src/ApprovalService.Contracts/Flows/**`：流程 JSON 契约权威定义，需与前端镜像同一提交同步
- `docs/contracts.md`：增加流程审批规则契约说明

## 验收标准

- [ ] 14 种来源各有场景：指定成员、角色（可限定本部门）、岗位职级、直属上级（第 N 级）、连续多级上级、部门负责人（第 N 级）、业务数据里的部门、分管领导、业务数据里的人员、审批矩阵、业务系统接口、上一步指定、发起人自选、发起人本人
- [ ] 发起人在多个部门时按发起部门找；审批人到节点时才算，和预览不一样时记进事件
- [ ] 票签按比例和权重算，反对多到不可能达标时提前按拒绝处理；会签中有人拒绝的两种设置
- [ ] 自动处理的每个选项都有场景（加粗的是默认值）
- [ ] “业务系统接口”来源在引擎里只定义抽象，HTTP 调用在 T-006 实现
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 组织架构的存储和维护（T-007）

## 交接记录

- 2026-10-15 · Codex · 设计书已有“角色限定发起部门”“连续上级直到某职级”“无处理人转指定人员”，但 T-001 的流程模型缺少可保存的配置。将兼容的可选字段、来源与规则编码常量、前端镜像、契约核对及文档同步纳入本任务，由协调者单独提交跨端契约变更，不修改已发布字段语义或默认值。
- 2026-10-15 · Codex · 扩围记录：现有 `EngineContext.Assignees` 每节点仅一人、`EngineWork.CreateTask` 仅生成一项任务、`EngineRuntime` 拒绝即整单结束，无法在原范围内实现会签、或签、票签和到节点才解析；将上述五个 Execution 文件列入任务范围。`FlowApprovalSettings` 尚无“找不到审批人时转交指定的人”的目标人字段，契约同步方案已向协调者提出，其他规则先继续实现。
- 2026-10-15 · Codex · T-002 已独立验收，从主分支建立任务分支并纳入 T-002；认领纯审批人来源解析和多人审批规则。会修改共享 `backend/tests/ApprovalService.Backend.Checks/Program.cs` 以登记检查，已预先登记。若实现需要修改 Execution 等范围外文件，先在本任务记录原因并明确扩围；不下载新依赖，不合并主分支或推送。
<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
