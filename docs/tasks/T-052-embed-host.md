# T-052 同源审批中心页面托管与内嵌来源限制

状态：进行中
负责人：Codex 2026-10-23
里程碑：M3
依赖：T-011 已提交稳定快照（正式待验收）
设计书：§arch、§center、§integration、§api；ADR 0016

## 目标

部署后的审批中心页面由审批服务同源提供。普通页面禁止被嵌入；应用专属页面只允许该应用登记的来源嵌入。已验证的嵌入会话可查询本应用允许的消息目标来源。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Api/Hosting/`、`Endpoints/Center/`、`Program.cs`、`ApprovalService.Api.csproj`：静态前端托管、HTML 入口与 CSP
- `backend/src/ApprovalService.Application/Integration/ClientAppAppService.cs`：通过应用层读取允许来源
- `backend/src/ApprovalService.Contracts/Protocol/CenterRoutes.cs`、`HeaderNames.cs`、`CenterEmbedContextResponse.cs`：新增受保护端点及头部契约
- `client/src/ApprovalService.Client/Internal/Protocol.cs`：HTTP 头契约副本
- `backend/tests/ApprovalService.Backend.Checks/`：真实 HTTP/CSP 检查
- `backend/README.md`、`docs/contracts.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`：部署与契约同步
- `docs/tasks/T-052-embed-host.md`、`docs/tasks/README.md`

## 共享文件

改动前登记：`Program.cs` 仅追加托管配置及路由；`CenterRoutes.cs` 与 `HeaderNames.cs` 仅追加常量；`client/.../Protocol.cs` 同步头副本；检查程序 `Program.cs` 仅追加一行；任务索引仅追加一行；`docs/contracts.md`、`docs/architecture.md` 仅追加/同步内嵌协议说明。

## 验收标准

- [ ] `GET /` 及绕过路径的 HTML 响应拒绝 iframe；`GET /embed/{clientAppCode}` 只把本应用登记来源放进 `frame-ancestors`
- [ ] 静态文件入口不能绕过 HTML 安全响应；发布目录缺失时服务明确报告配置错误
- [ ] `GET /api/v1/center/embed-context` 只接受已验证的嵌入 JWT，返回应用编码和登记来源；本地 JWT、匿名与伪造应用均不可用
- [ ] 后端 HTTP 检查覆盖正反场景，并完成全量 `verify.ps1`
- [ ] 契约副本、架构、设计和部署说明已同步

## 不做

- 前端令牌片段交换、地址清除、`postMessage`（T-011 负责）
- 管理后台编辑允许来源（T-020 负责）

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

- 2026-10-23 · Codex · 按已采纳 ADR 0016 和 T-011 稳定提交 `1f88d0d` 建立独立任务；计划增加同源 HTML 入口与受嵌入身份保护的来源查询。无新增第三方依赖。
