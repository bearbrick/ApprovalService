# T-056 跨期报表事实读取

状态：进行中
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-049、T-054 已验证稳定快照
设计书：§stats、§engine

## 目标

让内部报表能够读到筛选期间结束、处理、到期或仍待办的审批单，即使它们在筛选期前发起；为后续按权限的汇总接口提供可信事实。

## 范围

- `backend/src/ApprovalService.Domain/Reporting/IReportingFactsRepository.cs`：增设专用于指标计算的跨期读取方法。
- `backend/src/ApprovalService.Infrastructure/Reporting/ReportingFactsRepository.cs`：按发起、结束、任务完成、任务到期和当前待办条件取并集，沿用最小化事实投影。
- `backend/tests/ApprovalService.Backend.Checks/ReportingChecks.cs`：真实 SQLite 跨期与范围隔离检查。
- `backend/README.md`：说明跨期读取边界。
- `docs/tasks/T-056-reporting-cross-period.md`：交接记录。

## 共享文件

- `docs/tasks/README.md`：只追加任务索引，单独提交。

## 验收标准

- [ ] 筛选期前发起、期间结束的审批单计入完成事实。
- [ ] 筛选期前发起的任务按完成、到期及当前待办被纳入；时间范围左闭右开且到期不超过观察时刻。
- [ ] 流程、来源应用、发起部门快照过滤作用于所有入口；不读取标题、业务数据或附件。
- [ ] 旧的按发起时间事实查询保持兼容；全量 `verify.ps1` 通过。

## 不做

- T-019 的管理和保密授权、报表 HTTP、小时汇总、导出和页面；这些需在审批中心依赖完成后整体接入。
- 不新增依赖或数据库迁移。

## 交接记录

- 2026-10-24 · Codex · 认领独立跨期读取切片。`ReportingMetricsCalculator` 已按双时间口径计算，但 T-049 事实仓储仍只按发起时间查，期前发起的完成量与逾期任务会被漏掉。本任务补足读路径，保持既有 `ReadAsync` 语义。
