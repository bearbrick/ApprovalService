# T-032 已验收功能的可验证集成基线

状态：进行中
负责人：Codex 2026-10-20
里程碑：M2～M4 集成
依赖：T-001～T-005、T-008～T-010、T-025～T-028、T-030 的已验收提交
设计书：§1、§2、§3、§4、§5、§6、§9、§10、§12、§13

## 目标

把已验收的后端、前端和调用端功能汇集到一个独立任务分支，形成能通过完整 `verify.ps1` 的集成基线，供后续未完成任务安全接入。

## 范围

可以新建、修改的文件：

- `docs/tasks/T-032-integration-baseline.md`、`docs/tasks/README.md`。
- 合入已验收任务提交所涉及的 `backend/`、`frontend/`、`client/`、`docs/` 文件及必要的 `docs/prototypes/` 文件；只为解决明确的集成冲突做最小修改，不增加业务行为。
- 如验证暴露仅由集成引起的错误，修改对应受影响文件并在交接记录里逐项说明。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/tasks/README.md`：仅追加 T-032 索引。
- 各任务已验收提交带入的 `docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`、`backend/ApprovalService.Backend.slnx`、`backend/Directory.Packages.props`、`backend/src/ApprovalService.Contracts/**`、后端三个服务登记文件与检查程序 `Program.cs`、`client/ApprovalService.Client.slnx`、客户端协议副本与检查程序 `Program.cs`、`frontend/package.json`、`frontend/src/App.vue`、`frontend/src/model/routes.ts`：只保留各任务已实现的兼容内容；冲突时逐项核对。

## 验收标准

- [ ] 已验收任务按可追溯提交集成，交接记录列明纳入及未纳入的分支。
- [ ] 无未解决冲突、无生成物或密钥、未改动其他任务仍在认领中的工作树。
- [ ] `verify.ps1` 全量通过。
- [ ] 相关文档已更新，任务状态与交接记录完整。

## 不做

- 不合并到 `main`、不推送远程、不实施 T-006、T-007、T-031 等进行中的新功能。

## 交接记录

- 2026-10-20 · Codex · 认领集成任务；先登记共享文件及预期用途，再逐分支核对合入。T-028 的已验收分支包含 T-007 的必要已提交基线，合入时需显式记录且不代替 T-007 验收。
