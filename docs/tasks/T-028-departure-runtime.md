# T-028 离职在途待办按节点策略处理

状态：进行中
负责人：Codex 2026-10-20
里程碑：M2 / M3
依赖：T-004、T-005；组织变更触发衔接 T-007
设计书：§rules、§engine、§org

## 目标

人员离职或停用后，把其进行中审批单的待办按各节点的“审批人离职”配置转给在职直属上级或流程管理员；状态、事件、任务投影和待发项经过统一引擎命令与原子事务保存。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Engine/Commands/`、`Execution/`、`Assignees/`：离职系统命令、节点规则和事件。
- `backend/src/ApprovalService.Application/Runtime/`、`backend/src/ApprovalService.Domain/Runtime/`：离职处理用例与候选审批单查询接口。
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalRuntimeRepository.cs`：只追加在途候选查询，不改任务表绕过引擎。
- `backend/tests/ApprovalService.Backend.Checks/DepartureChecks*.cs`：引擎、事务、并发场景。
- 本任务文件；`backend/README.md` 的功能同步说明。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/tasks/README.md`：只追加 T-028 索引行。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：只追加离职场景检查登记。
- `docs/architecture.md`：若需同步运行模块边界，仅追加说明。

## 验收标准

- [ ] 节点分别选择直属上级和流程管理员时，待处理及排队任务走不同接手人并产生可追溯事件。
- [ ] 找不到有效接手人时不转给停用人员，保持原状态并留下可供管理员处理的失败结果。
- [ ] 重试幂等；并发审批与离职交接只有一个版本提交成功，失败方可重新读取。
- [ ] 在途候选查询涵盖同一人的所有审批单，不扫描已结束任务。
- [ ] 组织变更触发与该用例衔接，不直接修改任务投影。
- [ ] `verify.ps1` 全量通过，相关文档已同步。

## 不做

- 管理员指定接手人及修改流程里固定成员（已有手动交接命令；管理页面属 T-020）。
- 开放接口和接入应用鉴权（T-006），组织可信来源判断（T-007）。
- 新增数据库表或第三方包。

## 交接记录

- 2026-10-20 · Codex · 已从 main 新建独立工作树并局部合入已完成的 T-004、T-005；登记共享索引与检查入口，后续只追加改动。T-007 组织推送侧由其负责人衔接触发，不在本任务改动其认领文件。
