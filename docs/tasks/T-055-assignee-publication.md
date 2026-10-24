# T-055 指定审批人兜底的发布校验

状态：进行中
负责人：Codex 2026-10-24
里程碑：M1
依赖：T-053 已验证稳定快照
设计书：§rules、§designer

## 目标

发布流程前发现“找不到处理人时转交指定人员”却未配置目标人员的错误，在画布对应节点上提示，避免运行时才失败。

## 范围

- `backend/src/ApprovalService.Engine/Definitions/FlowSemanticValidator.cs`：权威发布校验。
- `backend/tests/ApprovalService.Backend.Checks/FlowDefinitionChecks.cs`：有效、无效场景。
- `frontend/src/designer/canvas/canvas-validation.ts`：前端预检镜像。
- `frontend/tests/canvas-checks.ts`：可定位的预检场景。
- `docs/tasks/T-055-assignee-publication.md`：交接记录。

## 共享文件

- `docs/tasks/README.md`：仅追加任务索引，单独提交。

## 验收标准

- [ ] 指定人员策略缺失人员 ID 时，前后端发布校验均定位到节点并拒绝。
- [ ] 配置人员 ID 或选择其他缺人策略时，保持原有发布行为。
- [ ] `verify.ps1` 全量通过，文档和交接记录更新。

## 不做

- 不验证人员是否在职或有权限；动态人员状态仍在节点到达时按最新组织快照判断。
- 不改变流程契约与运行时找人语义。

## 交接记录

- 2026-10-24 · Codex · 认领本任务。已有属性面板允许配置指定人员，但发布校验没有检查空目标 ID；计划只做静态必填校验，不引入新的组织查询。共享 `docs/tasks/README.md` 只追加索引。
