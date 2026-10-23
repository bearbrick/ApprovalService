# T-049 报表查询基础

状态：进行中
负责人：Codex 2026-10-23
里程碑：M5
依赖：T-005、T-007
设计书：§stats、§engine

## 目标

以已持久化的审批单、任务和事件为依据，提供流程、节点、人员三个角度的可信统计读模型，为 T-019 的汇总任务和报表接口提供统一口径。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Domain/Reporting/`：查询条件和输出模型。
- `backend/src/ApprovalService.Application/Reporting/`：统计口径和聚合计算。
- `backend/src/ApprovalService.Infrastructure/Reporting/`：从 EF Core 读取真实审批数据。
- `backend/tests/ApprovalService.Backend.Checks/ReportingChecks.cs`：真实 SQLite 场景检查。
- `backend/README.md`：记录读模型能力。

## 共享文件

改这些文件前先在交接记录写明；本任务只追加登记行，不改现有功能：

- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：登记报表检查。

## 验收标准

- [ ] 按时间段、流程、来源应用和发起部门过滤，给出流程状态比例与耗时、节点停留与超时、人员待办与处理量。
- [ ] 任务时长以不可变 `taskCreated` 事件为起点、任务完成时间为终点；缺失起点不伪造时长。
- [ ] 有真实 SQLite 检查覆盖多流程、筛选、跨天、代理任务与缺失事件。
- [ ] `verify.ps1 -Part backend` 通过。
- [ ] 后端说明已同步，交接清楚标明 T-019 仍需汇总表、授权接口、前端和导出。

## 不做

- 不新增 HTTP 契约、端点、前端页面；不修改 T-011、T-020、T-046。
- 不实现小时汇总持久化、保密流程视图、Excel/CSV 导出；由 T-019 接续。

## 交接记录

- 2026-10-23 · Codex · T-019 依赖 T-011，而 T-011 当前按 R13 阻塞，故拆出只依赖已完成 T-005/T-007 的统计读模型基础。共享检查登记已在上方列明。已有任务表无创建时间，使用只追加引擎事件 `taskCreated` 的时间作处理时长起点；若缺失该事件不推断。基于 main 建本任务分支，接入 T-007 稳定快照后实现，不修改管理接口/页面或设计器。
