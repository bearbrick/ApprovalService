# T-063 办理节点设计动作与运行命令兼容

状态：进行中
负责人：Codex 2026-10-26
里程碑：M1
依赖：T-002、T-004、T-022 已验证基线
设计书：§rules、§designer、§engine

## 目标

设计器及存量模板把办理节点的“办理提交”保存为 `allowedActions: ["submit"]`，执行引擎实际使用 `handle` 命令。让已发布的办理节点按配置展示并接受该成功命令，不放宽其他节点或其他操作。

## 范围

- `backend/src/ApprovalService.Engine/Commands/TaskActionPermission.cs`：集中解释办理节点的设计动作别名。
- `backend/src/ApprovalService.Engine/Commands/OperationGuard.cs`、`TaskActionAvailability.cs`：提交守卫和中心许可读取同一规则。
- `backend/tests/ApprovalService.Backend.Checks/OperationChecks.cs`：正式运行图的正反场景。
- `docs/contracts.md`、`backend/README.md`：解释已存在的流程定义编码和真实操作编码。
- `docs/tasks/T-063-handling-action-alias.md`：任务交接。

## 共享文件

- `docs/tasks/README.md`：仅追加任务索引，单独提交。
- `docs/contracts.md`：仅追加别名说明，不改现有流程 JSON 契约。

## 验收标准

- [ ] 已发布 `handling` 节点的 `submit` 配置使 `handle` 出现在服务端许可中，且真实引擎成功推进。
- [ ] `approval`/`cc` 节点和其他命令不会因别名获得额外权限；现存 `handle` 配置继续有效。
- [ ] 完整 `verify.ps1` 通过，文档和交接记录更新。

## 不做

- 不改变流程定义 JSON 或开放接口命令编码，不把 `submit` 作为新运行命令。
- 不在测试模式绕过正式引擎权限。

## 交接记录

- 2026-10-26 · Codex · 十模板真实模拟暴露首个采购模板的办理节点无法完成：画布配置 `submit`，引擎任务和开放命令为 `handle`，提交守卫按原字串拒绝，中心许可也不显示 `handle`。从集成提交 `de49527` 建独立修复分支，先在任务范围登记引擎与检查文件；修复后返回 T-022 逐模板验收。
