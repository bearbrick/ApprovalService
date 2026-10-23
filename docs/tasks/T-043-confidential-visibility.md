# T-043 保密流程与抄送收件人的附件可见性

状态：进行中
负责人：Codex 2026-10-23
里程碑：M3
依赖：T-013、T-041、T-042
设计书：§rules、§engine、§api

## 目标

普通抄送收件人在送达后可持续查看审批单附件；保密流程仅向发起人、实际代发人、实际任务处理人和已送达的抄送收件人开放，历史参与人也保留权限。保密标记属于发布版本，可选且旧版本默认为非保密。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Contracts/Flows/`：发布版本的可选保密标记。
- `backend/src/ApprovalService.Engine/`：普通抄送与手动抄送的收件人审计投影所需纯逻辑。
- `backend/src/ApprovalService.Domain/Runtime/`、`Domain/Files/`：参与人和收件人持久模型与权限抽象。
- `backend/src/ApprovalService.Application/Runtime/`、`Application/Files/`：事务提交、附件授权与版本锁定语义。
- `backend/src/ApprovalService.Infrastructure/Persistence/`、`Infrastructure/Files/`：三种数据库映射、工具生成的迁移和权限查询。
- `backend/tests/ApprovalService.Backend.Checks/`：普通抄送、保密、跨应用、历史参与人及旧库升级场景。
- `frontend/src/model/flow.ts`：流程定义的保密标记副本。
- `backend/README.md`、`docs/design/approval-service-design.html`、本任务文件：同步已实现规则。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/src/ApprovalService.Contracts/Flows/**` 与 `frontend/src/model/flow.ts`：只追加默认关闭的可选保密标记并同步副本。
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`、`Migrations/**`：增加持久收件人投影并用 EF 工具生成三库迁移和快照。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：只追加场景检查登记。
- `docs/contracts.md`、`docs/architecture.md`：同步保密版本及附件权限规则。
- `docs/tasks/README.md`：只追加本任务索引。

## 验收标准

- [ ] 旧发布版本默认非保密，新发布版本能锁定可选保密标记。
- [ ] 普通抄送收件人持久可辨，送达后的历史收件人能下载附件。
- [ ] 保密流程只向已确认的四类参与人开放，管理员与额外查看人不自动获得附件权限。
- [ ] 嵌入会话同时受本应用归属限制，跨应用下载拒绝。
- [ ] 三库迁移由 EF 工具生成，旧 SQLite 数据可升级，模型一致。
- [ ] `verify.ps1` 全量通过；契约副本和文档同步。

## 不做

- 不实现审批中心列表和额外查看人配置；不修改 T-006 正在开发的开放接口文件。
- 不要求真实 MinIO 环境；未配置测试地址时只跑本地文件和模型检查。

## 交接记录

- 2026-10-23 · Codex · 从 main 建独立工作树认领；后续仅合入 T-041 已完整验证的固定提交 `ed91b32`，不碰其工作树。用户已确认：保密范围为发起人、实际代发人、实际任务处理人、已送达抄送收件人（含历史），管理员和额外查看人不自动有权；发布版本可选 bool，旧版默认非保密。上述共享文件在改动前登记；不下载新依赖，不碰 T-006 工作树。
