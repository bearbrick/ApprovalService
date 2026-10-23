# T-014 消息提醒、时限与工作日历

状态：进行中
负责人：Codex 2026-10-23
里程碑：M3
依赖：T-005、T-007、T-029
设计书：§center、§rules

## 目标

邮件和站内消息提醒审批人和发起人；使用 T-029 工作日历计算节点期限并发送到期前、超时提醒；每天一封待办汇总；渠道做成扩展点，企业微信、钉钉只预留。

## 范围

可以新建、修改的文件：

- `Notifications` 模块：扩展点 `INotificationChannel`，邮件（SMTP）、站内消息两个实现；企业微信、钉钉只留接口
- 提醒规则：新待办、被催办、快到期、超时、审批结果、被抄送、被 @；个人订阅设置
- 将节点时限配置、实际任务起始时间与 T-029 工作日历接通；工作日历规则本身由 T-029 交付
- 邮件库如果要新增依赖，按 R8 处理
- `Application/Runtime/ApprovalRuntimeAppService.cs`、`Domain/Runtime/IApprovalRuntimeRepository.cs` 与 `Infrastructure/Persistence/ApprovalRuntimeRepository.cs`：在审批事务中依据实际任务和事件固定通知待发项与节点期限，避免提交后补扫丢失。
- `Application/Notifications/`、`Infrastructure/Notifications/`、`Infrastructure/Jobs/`：通知规则、个人订阅、期限提醒和每日待办汇总；复用 T-030 渠道及持久队列，不新增邮件包。
- `backend/tests/ApprovalService.Backend.Checks/NotificationRulesChecks*.cs`：真实 SQLite、假 SMTP 和工作日历场景。
- `backend/README.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`：仅同步实际完成的通知行为。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- 登记文件
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Api/Program.cs`：新服务或接口登记。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：登记通知规则检查。
- `backend/src/ApprovalService.Contracts/**`、`docs/contracts.md`、`docs/architecture.md`：个人订阅和收件箱对外契约若变化时同步权威模型与文档。

## 验收标准

- [ ] 用假的 SMTP 服务检查邮件内容（摘要字段、打开链接，链接要登录）
- [x] 按 T-029 已确认的时限语义将节点期限写入任务，并安排到期前、超时提醒
- [ ] 每天早上的待办汇总可以关
- [ ] 提醒经待发队列发送，失败重试
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 超时自动处理（二期）
- 企业微信、钉钉的实现（预留）

## 交接记录

- 2026-10-23 · Codex · 到期提醒切片完成：新待办与排队任务激活时同事务排入提前 1 工作小时和到期提醒；后台发送前核对运行中审批单、待办状态及原期限，重启重试使用稳定 ID 去重，短于 1 小时的期限立即安排提前提醒。真实 SQLite 检查覆盖节假日/调休逆算、四封消息、重试、迟到和失效；全量 `verify.ps1` 通过（后端 708、调用端 50、前端 119）。尚需其他事件通知、个人订阅、每日汇总和假 SMTP 内容检查。
- 2026-10-23 · Codex · 已接通发布节点 `DueMinutes` 与实际待办起始时刻：新任务和排队任务激活时按可配置时区、工作时段、节假日与调休写入 `DueAt`，现有待办期限保持不变。真实 SQLite 场景及 `verify.ps1` 全量通过（后端 699、调用端 50、前端 119）。已同步后端 README、架构和设计书。尚需同事务生成新待办等事件通知、提前 1 工作小时/超时定时提醒、管理员配置的每日汇总和个人订阅；本任务继续保持进行中。
- 2026-10-23 · Codex · 用户确认快到期提醒提前 1 个工作小时；每日汇总的发送时刻由管理员配置，未配置时不发送。按此语义实现期限与汇总，不采用固定默认发送时间。
- 2026-10-23 · Codex · T-007 已在 `537c200` 独立验收，T-029/T-030 已完成；本轮从 main 认领并合入固定已验证集成提交，登记上列运行提交、通知规则及共享文件。先做定义明确的新待办、催办、结果、抄送和 @ 通知；提前多久提醒及“每天早上”的默认时间设计未写明，不擅自猜测，留待具体实现前按 R13 处理。
- 2026-10-22 · Codex · T-029 已增加按默认 09:00–18:00 或自定义工作时段累计 `DueMinutes` 的纯计算；本任务仍需提供日历/时区配置、在任务创建时写入 `DueAt`，并实现到期前与超时提醒。
- 2026-10-20 · Codex · 工作日历与可复用日期规则拆到 T-029，本任务保留身份相关通知、渠道以及节点期限接线。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
