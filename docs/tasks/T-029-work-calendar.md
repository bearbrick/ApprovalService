# T-029 工作日历与节点时限基础

状态：阻塞
负责人：Codex 2026-10-20
里程碑：M3
依赖：T-005
设计书：§rules、§center

## 目标

把不依赖组织账号和消息渠道的工作日历、节假日与调休规则从 T-014 拆出，供节点期限与提醒计算复用。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Domain/Notifications/Calendar/`：工作日历规则、逐日覆盖模型和纯计算。
- `backend/tests/ApprovalService.Backend.Checks/WorkCalendarChecks.cs`、`Program.cs`：跨周末、节假日、调休、时区和夏令时场景。
- `backend/README.md`、`docs/architecture.md`：同步独立规则的使用边界。
- `docs/tasks/T-014-notifications.md`、`docs/tasks/README.md`：拆分原任务及登记索引。

## 共享文件

改动前已在下方交接记录登记：

- `docs/tasks/README.md`：仅追加 T-029 索引行，单独提交。
- `docs/architecture.md`：补充工作日历基础所在模块，单独提交。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：追加一行场景检查入口，单独提交。

## 验收标准

- [x] 周末、法定节假日、调休工作日覆盖与连续工作日计算场景通过。
- [x] 有明确的时区/DST 边界检查，算法不依赖机器本地时区。
- [x] 已为节点分钟时限到工作日的换算语义记录待确认点。
- [x] `verify.ps1` 全量通过。
- [x] 相关文档已更新。

## 不做

- 邮件、站内消息、个人订阅、身份和定时派发，这些仍属 T-014。
- 在业务语义未确认前修改对外节点契约、任务 DueAt 或提前/超时通知策略。

## 交接记录

- 2026-10-20 · Codex · 已交付纯工作日期规则 `WorkCalendarRules`、逐日节假日/调休覆盖模型、指定时区本地日期投影和场景检查；已从 T-014 拆分索引与范围，同步后端 README、架构。全量 `verify.ps1` 通过：后端 251 项、调用端 33 项、前端 54 项，真实服务端到端检查通过；分支未合入主线、未推送。阻塞点：设计只写“按工作日”，现有流程契约 `DueMinutes` 为分钟，没有分钟如何折算到工作日的定义。待用户在“可配置每日工作时段（建议默认 09:00–18:00）”与“每个工作日按完整 24 小时”之间确认后，再实现实际节点 DueAt 的纯换算与相应边界检查；周末起算、夏令时无效/重复本地时刻的策略也应随选项确定。此时不能擅自修改已发布契约或计算任务期限。
- 2026-10-20 · Codex · 从已完成 T-005 基线建立独立工作树。预先登记上述共享文件。设计书只说“按工作日”，现有 `FlowNodeSettings.DueMinutes` 为分钟，未说明每天可计时小时数、起算/截止时刻及跨非工作日如何处理。先实现日期层面确定的规则，并在交接时标明时限换算阻塞点。
