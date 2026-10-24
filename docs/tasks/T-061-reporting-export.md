# T-061 报表汇总 Excel 导出

状态：待验收
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-058、T-059 已验证稳定快照
设计书：§stats

## 目标

用一份 XLSX 导出流程、节点、人员三类安全汇总和独立的保密匿名数量表，复用现有报表筛选和管理范围授权。

## 范围

- `backend/src/ApprovalService.Application/Reporting/`：安全汇总编码接口。
- `backend/src/ApprovalService.Infrastructure/Reporting/`：复用现有 MiniExcel 的四表 XLSX 编码。
- `backend/src/ApprovalService.Api/Endpoints/ReportingEndpoints.cs`：在同一查询入口新增下载路由。
- `backend/tests/ApprovalService.Backend.Checks/ReportingEndpointChecks.cs`：真实 HTTP 授权与文件场景。
- `backend/README.md`、`docs/design/approval-service-design.html`、`docs/contracts.md`：同步导出约定。

## 共享文件

- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`：追加导出路径常量，单独提交。
- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：登记编码器，单独提交。
- `docs/contracts.md`、`docs/tasks/README.md`：追加约定和任务索引，单独提交。

## 验收标准

- [x] 本地 JWT 管理范围、保密过滤和 UTC 半开筛选与四条 JSON 报表一致。
- [x] XLSX 有中文列名的流程、节点、人员及保密匿名数量四张表，保密维度只进匿名表；无审批单原始数据。
- [x] 导出限定累计 10,000 行、维度文本 256 字符、最终工作簿 16 MB；超限返回可理解的业务错误。
- [x] 真实 HTTP/XLSX 场景覆盖管理员、直属部门负责人、普通人员、匿名请求及越权筛选。
- [x] 完整 `verify.ps1` 通过，相关文档同步。

## 不做

- 不导出逐单审批明细或审批数据字段；留给后续按审批单访问权限单独设计。
- 不实现小时持久汇总、周月趋势。
- 本切片只提供后端 HTTP 下载；管理后台下载按钮留给后续前端切片。

## 交接记录

- 2026-10-24 · Codex · 已实现四表工作簿导出。入口复用 `ReportingQueryAppService` 的本地 JWT、直接负责部门和保密筛选；基础设施编码器只拿安全 DTO，并限制行数、维度文本和文件字节，处理表格公式前缀。真实 HTTP/XLSX 场景覆盖角色权限、直属范围、保密匿名表、时间错误、行数上界和公式文本。完整 `verify.ps1` 通过：后端 946、调用端 68、前端 158。当前页面尚无下载按钮；后续从最新集成分支按任务新增按钮，并继续单独设计逐单明细导出。
- 2026-10-24 · Codex · 已登记共享 Contracts 路径、基础设施 DI、契约文档和任务索引。选择单条 `/api/v1/admin/reports/export` 下载四张表；复用现有查询服务确保授权口径一致，编码器只接收安全 DTO。
