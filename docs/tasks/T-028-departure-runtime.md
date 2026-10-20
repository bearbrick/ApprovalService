# T-028 离职在途待办按节点策略处理

状态：已完成
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
- `backend/src/ApprovalService.Application/Organization/`、`Domain/Organization/`、`Infrastructure/Organization/`：从已提交且无人编辑的 T-007 节点接管离职推送的可靠触发；保持既有可信来源边界。
- `backend/src/ApprovalService.Infrastructure/Jobs/`：消费组织离职的持久任务，逐实例调用运行时入口并可重试。
- `backend/src/ApprovalService.Domain/Runtime/OperationEventEntity.cs`：为既有定时任务增加有界失败错误码，不保存敏感异常消息。
- `backend/src/ApprovalService.Infrastructure/Persistence/Migrations/`：由 EF 工具生成三库定时任务错误码增量迁移及快照。
- `backend/tests/ApprovalService.Backend.Checks/Organization*Checks.cs`：补真实 SQLite 离职排程与重启后消费检查。
- `backend/tests/ApprovalService.Backend.Checks/DepartureChecks*.cs`、`DepartureJobChecks.cs`：引擎、事务、并发和持久离职任务场景。
- `backend/tests/ApprovalService.Backend.Checks/JobChecks.cs`：校验既有持久队列的有界失败码及重试语义。
- `backend/tests/ApprovalService.Backend.Checks/PersistenceChecks.cs`、`EventChainConsistencyChecks.cs`：给原有仓储测试代理透传新增的只读候选查询接口。
- 本任务文件；`backend/README.md` 的功能同步说明。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/tasks/README.md`：只追加 T-028 索引行。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：只追加离职场景检查登记。
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：仅追加可靠离职处理服务登记。
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`：仅追加定时任务失败码字段配置；三库迁移由 EF 工具生成。
- `docs/architecture.md`：若需同步运行模块边界，仅追加说明。

## 验收标准

- [x] 节点分别选择直属上级和流程管理员时，待处理及排队任务走不同接手人并产生可追溯事件。
- [x] 找不到有效接手人时不转给停用人员，保持原状态并留下可供管理员处理的失败结果。
- [x] 重试幂等；并发审批与离职交接只有一个版本提交成功，失败方可重新读取。
- [x] 在途候选查询涵盖同一人的所有审批单，不扫描已结束任务。
- [x] 组织变更触发与该用例衔接，不直接修改任务投影。
- [x] `verify.ps1` 全量通过，相关文档已同步。

## 不做

- 管理员指定接手人及修改流程里固定成员（已有手动交接命令；管理页面属 T-020）。
- 开放接口和接入应用鉴权（T-006），组织可信来源判断（T-007）。
- 新增数据库表或第三方包；只为既有定时任务表增加失败错误码列。

## 交接记录

- 2026-10-20 · Codex · 补复职竞态：离职持久任务执行前若人员已恢复在职，后台不改派本人待办，只在再次核对当前仍在职的条件下删除过期交接提醒；组织变更历史继续保留。真实 SQLite 新增离职提醒已生成、复职后任务完成且提醒消失、本人待办和事件均不变的场景。`verify.ps1 -Part backend` 通过：后端 427 项、构建 0 警告；任务继续待验收。
- 2026-10-20 · Codex · 独立验收：已复核可信离职变更与定时任务同事务写入、消费者逐单调用引擎、缺接手人保留原待办及有界错误码、恢复后幂等重试的实现和场景；全量验证结果为后端 425、调用端 33、前端 54，零警告，工作树仅含任务范围内的源码、三库 EF 迁移和文档。T-028 验收通过；后续 T-007 集成可局部合入此分支，主分支仍未合并、远程未推送。
- 2026-10-20 · Codex · 已完成可靠离职触发和失败原因闭环：可信组织导入或推送在原事务写交接提醒及持久任务；后台逐在途审批单调用统一引擎命令，重启、租约和失败后可重试；缺接手人时保留原任务与提醒，定时任务只存有界 `VALIDATION_FAILED` 等稳定错误码，成功后清空。三库增量迁移由 EF 工具生成，SQLite 空库启动迁移和三库模型快照检查通过；未配置 SQL Server/PostgreSQL 实库连接。真实 SQLite 场景覆盖多单、重启、无接手人、补齐后重试与事件链；并发审批与幂等检查继续通过。最终 `verify.ps1` 全量通过：后端 425、调用端 33、前端 54，构建 0 警告。未合并 main、未推送。
- 2026-10-20 · Codex · 验收复核发现持久定时任务缺少失败原因；已登记跨领域定时任务实体、队列、三库 EF 迁移和 JobChecks 范围，以及共享 DbContext 的最小字段配置。仅存短错误码，不保存异常消息；完成后会重跑全量验证。
- 2026-10-20 · Codex · T-028 已接入可信组织导入和推送：人员由在职转为停用时，组织事务同写 `DepartureNotice` 与 `ScheduledJob`；后台按当前组织和流程管理员快照逐在途任务走统一引擎命令，按任务 ID 派生稳定幂等请求 ID，失败由持久队列退避重试。真实 SQLite 覆盖重启消费、跨单事件链、无接手人保留待办、补齐上级后重试仅改派一次；先前场景覆盖并发审批与改派。三库迁移快照与当前模型一致，SQLite 空库启动迁移通过；未配置 SQL Server/PostgreSQL 实库连接，未跑其实库命令链。`verify.ps1` 全量通过（后端 423、调用端 33、前端 54，构建 0 警告）。未新增实体或依赖；组织离职事务和待发任务通过已有表实现。失败待处理信息目前保存在交接提醒数量及任务的 Pending/Attempts/DueAt，未记录独立的失败原因码；如管理界面要求显示具体原因，应另行扩展任务模型和三库迁移。
- 2026-10-20 · Codex · 已获协调许可接管 T-007 已提交且无人编辑的组织文件与 Jobs，正在把离职变更事务内持久排程、后台逐单消费和失败重扫接入；共享 DI、上下文与检查入口将单独提交，不改变组织可信来源语义。
- 2026-10-20 · Codex · 已实现 `OperationKinds.ReassignInactive`：按任务所在发布节点的 `InactiveAssigneeBehavior` 选在职上级或管理员，任务、事件、待发项走 `ApprovalRuntimeAppService.ExecuteAsync` 同事务乐观锁；候选查询只读且覆盖待处理、排队任务。真实 SQLite 的跨单、幂等、事件链与审批并发检查通过。`verify.ps1` 全量通过（后端 356、调用端 33、前端 54，构建 0 警告）。可靠触发衔接入口：可信离职变更提交后同事务写持久待发事件或可重扫标记；消费者以已停用内部人员 ID 调用 `IApprovalRuntimeAppService.ListDepartureTasksAsync(personId)`，逐审批单构造含直属上级链及该流程管理员的 `AssigneeSnapshot`，再对每条候选调用 `ReassignDepartedTaskAsync(task, personId, snapshot, occurredAt, stableRequestId)`。重试需复用按组织变更 ID 与任务 ID 派生的请求 ID；并发 `CONFLICT` 要重新扫描候选，缺有效接手人的 `VALIDATION_FAILED` 要记录并提醒管理员，不可直接更新任务表。
- 2026-10-20 · Codex · 已从 main 新建独立工作树并局部合入已完成的 T-004、T-005；登记共享索引与检查入口，后续只追加改动。T-007 组织推送侧由其负责人衔接触发，不在本任务改动其认领文件。
