# T-053 BPMN 画布的审批服务属性

状态：进行中
负责人：Codex 2026-10-24
里程碑：M1
依赖：T-046 已验证稳定快照
设计书：§designer、§rules、§form

## 目标

让标准 BPMN 建模器只处理图形编辑，审批服务特有的找人规则和流程级策略可在工作台配置，并在兼容 BPMN 文件中完整保留。

## 范围

- `frontend/src/designer/workbench/NodeInspector.vue`：补齐已有契约支持的找人策略输入。
- `frontend/src/designer/canvas/bpmn-codec.ts`：修正服务属性的序列化遗漏。
- `frontend/tests/canvas-checks.ts`：增加带服务属性的导入导出场景。
- `frontend/README.md`：说明属性边界与配置入口。
- `docs/tasks/T-053-service-node-properties.md`：任务记录。

## 共享文件

- `docs/tasks/README.md`：仅追加任务索引，单独提交。

## 验收标准

- [ ] 角色限定发起部门、连续上级截止职级、找不到人时指定人员均可配置。
- [ ] 这些属性及流程撤回、重提策略经 BPMN 导出导入不丢失。
- [ ] `verify.ps1` 全量通过。
- [ ] 文档和交接记录已更新。

## 不做

- 不改变流程契约、审批引擎与通用 BPMN 语义。
- 不新增第三方依赖。

## 交接记录

- 2026-10-24 · Codex · 认领本任务。发现属性面板漏掉三个已有契约字段，前端 BPMN 兼容文件序列化漏掉相同字段及两项流程级策略；将在现有协议内修复。共享文件 `docs/tasks/README.md` 只追加索引行。
