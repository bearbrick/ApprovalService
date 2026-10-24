# T-058 受保护的报表查询接口

状态：待验收
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-057 已验证稳定快照
设计书：§stats、§org、§api

## 目标

给本地组织管理员和直接负责部门的负责人提供四条真实 JSON 报表查询路由，遵守双时间口径、范围权限和保密匿名数量规则。

## 范围

- `backend/src/ApprovalService.Application/Reporting/`：直接负责部门的授权与多部门事实合并。
- `backend/src/ApprovalService.Domain/Reporting/ReportingMetrics.cs`、`backend/src/ApprovalService.Infrastructure/Reporting/ReportingFactsRepository.cs`：负责人直接负责的部门集合在同一读事务下推筛选。
- `backend/src/ApprovalService.Domain/Reporting/ReportingMetricsCalculator.cs`：内部指标计算遵守同一部门集合边界。
- `backend/tests/ApprovalService.Backend.Checks/ReportingChecks.cs`：部门集合查询不混入其他部门的事实。
- `backend/src/ApprovalService.Contracts/Protocol/AdminReportModels.cs`、`ApiRoutes.cs`：新增四条向后兼容的查询契约。
- `backend/src/ApprovalService.Api/Endpoints/ReportingEndpoints.cs`、`Program.cs`：只从已验证本地 JWT 取人员身份，映射查询和结果。
- `backend/tests/ApprovalService.Backend.Checks/ReportingQueryChecks.cs`、`ReportingEndpointChecks.cs`、`Program.cs`：直接部门授权与真实 HTTP 场景。
- `backend/README.md`、`docs/contracts.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`：同步已确认的接口和权限。
- `docs/adr/0019-reporting-query-access.md`：记录用户已确认的管理范围、保密与路由决定。
- `docs/tasks/T-058-reporting-http.md`：任务交接。

## 共享文件

- `docs/tasks/README.md`、`docs/adr/README.md`：只追加索引，单独提交。
- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`AdminReportModels.cs`：只追加路由常量和响应模型。
- `backend/src/ApprovalService.Api/Program.cs`：只追加路由登记。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：只追加检查登记。
- `docs/contracts.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`：同步已确认的行为，不重写其他章节。

## 验收标准

- [x] 四条 GET 路由通过本地 JWT 鉴权，并仅返回实际授权范围。
- [x] 组织管理员可看全部或指定部门；部门负责人仅看直接负责部门，不能靠参数扩大范围。
- [x] 保密流程只提供匿名发起量与完成量，不在流程、节点、人员明细里出现。
- [x] 时间与筛选参数遵守契约，错误使用统一中文错误体。
- [x] 场景检查和全量 `verify.ps1` 通过，文档交接同步。

## 不做

- 不做小时汇总、CSV/Excel 导出和报表页面；由 T-019 接续。
- 不新增依赖或迁移。

## 交接记录

- 2026-10-24 · Codex · 四条本地 JWT 报表 GET 路由、直接负责部门集合过滤、保密匿名计数、契约和 ADR 0019 已实现；真实 HTTP 检查覆盖未登录、普通人员、管理员、负责人、越权参数、保密脱敏和错误时间。完整 `verify.ps1` 通过：后端 917、调用端 68、前端 148。后续 T-019 仍需小时汇总、导出及页面。
- 2026-10-24 · Codex · 用户已明确部门负责人只看直接负责部门，批准 `/api/v1/admin/reports/overview`、`/flows`、`/nodes`、`/people` 四条受本地 JWT 保护的 JSON 查询；保密流程只返回匿名数量且无人员、节点明细。本任务从 T-057 稳定集成快照接入正式契约和路由，按 R1 同步文档与检查。

