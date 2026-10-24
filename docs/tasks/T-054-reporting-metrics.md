# T-054 真实统计事实的指标计算

状态：进行中
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-049 已验证稳定快照
设计书：§stats

## 目标

在不开放未经授权的报表接口前，把已定的时间、部门、耗时、逾期统计口径实现为可复用的纯计算模块，供 T-019 的汇总与接口使用。

## 范围

- `backend/src/ApprovalService.Domain/Reporting/`：基于完整统计事实集合计算流程、节点和人员指标。
- `backend/tests/ApprovalService.Backend.Checks/ReportingMetricsChecks.cs`：跨期、缺失事件、逾期边界场景。
- `backend/README.md`：说明指标语义和未接入部分。
- `docs/tasks/T-054-reporting-metrics.md`：任务交接。

## 共享文件

- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：只追加统计指标检查注册，单独提交。
- `docs/tasks/README.md`：只追加任务索引，单独提交。

## 验收标准

- [ ] 发起量按发起时间、完成量及耗时按结束时间的 UTC 半开区间。
- [ ] 流程结果数量和 P90、节点及人员处理时长、到期任务逾期指标均由真实时间计算；缺失任务创建事件不编造时长。
- [ ] 部门使用审批单发起时快照，来源应用和流程筛选不串数据。
- [ ] 全量 `verify.ps1` 通过，文档和交接同步。

## 不做

- 不认领依赖尚未完成的 T-019；不开放报表 HTTP、导出或管理页。
- 不创建小时汇总表、数据库迁移或新增依赖。
- 保密流程和管理范围由 T-019 授权查询处理，本模块只在服务内部处理最小化事实。

## 交接记录

- 2026-10-24 · Codex · T-019 仍依赖 T-011，故从 T-049 和已验证集成快照拆出纯指标计算切片。共享 `docs/tasks/README.md` 仅追加索引；共享检查入口仅追加一行。用户已确定发起与完成的双时间口径、部门快照、P90 已完成、逾期分母为已到期任务。
