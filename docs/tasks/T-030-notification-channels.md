# T-030 消息渠道与持久发送基础

状态：已完成
负责人：Codex 2026-10-20
里程碑：M3
依赖：T-005
设计书：§center、§rules、§engine、§platform

## 目标

从 T-014 中独立交付可复用的通知传输层：SMTP 邮件与站内消息，通过已有待发队列持久重试；业务规则后续接入。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Domain/Notifications/`：站内消息实体、通知入队与读取仓储接口
- `backend/src/ApprovalService.Application/Notifications/`：`INotificationChannel` 扩展点、通知输入、入队和读取应用服务
- `backend/src/ApprovalService.Infrastructure/Notifications/`：SMTP 发送、站内消息持久化、待发项处理器与仓储
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs` 与三种数据库迁移：站内消息模型
- `backend/tests/ApprovalService.Backend.Checks/NotificationChecks.cs`：假 SMTP 服务、真实 SQLite、队列失败重试与读取场景
- `backend/README.md`：通知配置与集成方式

## 共享文件

改这些文件前先在交接记录里写明改什么；尽量只追加并单独提交：

- `docs/tasks/README.md`：新增任务索引
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`：登记通知应用服务
- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：登记通知仓储、渠道及待发处理器
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：登记通知检查
- `docs/architecture.md`：同步消息传输与业务规则分层

## 验收标准

- [x] SMTP 邮件使用内置 .NET 库，可由假 SMTP 服务收到内容，不提交凭据
- [x] 站内消息在真实 SQLite 持久化，能够按人员读取与标记已读
- [x] 两种渠道均通过 T-005 待发队列处理，失败留下待发项并按队列策略重试，重启不丢
- [x] 消息渠道扩展点无需修改引擎核心即可增加实现
- [x] `verify.ps1` 通过
- [x] README 与架构已同步，任务交接完整

## 不做

- 新待办、被催办、审批结果等业务事件触发与内容模板（T-014）
- 个人订阅、时限与工作日历、每日待办汇总（T-014 / T-029）
- 管理后台及审批中心页面（T-014 / T-020）
- 企业微信、钉钉等其他实际渠道

## 交接记录

- 2026-10-20 · Codex · 独立验收：复核通知内容校验、待发队列幂等与失败重试、站内收件人隔离、SMTP 纯文本和固定消息 ID；确认全量 `verify.ps1` 后端 248、调用端 33、前端 54 通过，零警告。业务事件接线和 HTTP 收件箱接口仍归后续任务；T-030 验收完成。
- 2026-10-20 · Codex · 已实现 `INotificationChannel`、SMTP 与站内消息渠道、SQLite 收件箱、三套 EF 迁移、通知待发处理器和应用读写接口；假 SMTP 首次暂时拒收后待发项退避，重新创建执行器后成功发送，站内消息按 ID 幂等、按人员隔离读取与标记已读。未实现业务事件触发、订阅/汇总、界面及收件箱 HTTP 路由，留给 T-014 等任务。SMTP 在服务器收件成功但确认待发前发生故障时可能重复发送，消息头携带稳定通知 ID；后续业务触发应在审批事务的 `RuntimeCommit.OutboxItems` 同时写入待发项。`dotnet format --verify-no-changes`、后端 248 项与全量 `verify.ps1`（后端 248、客户端 33、前端 54）通过；未新增 NuGet 包，未提交任何凭据或生成输出。
- 2026-10-20 · Codex · 从 main 建立独立任务工作树；T-005 已完成，将在认领后仅合入本任务分支。预计复用 `IOutboxHandler`/`OutboxQueue` 与现有重试规则，不修改引擎或 T-014 的事件触发。修改共享任务索引、DI、检查入口与架构前已在上文登记；SMTP 先用 .NET 内置库，不引入新包。通知出站项仍需定义不含业务策略的稳定负载与幂等键，随后补 SQLite/假 SMTP 场景。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
