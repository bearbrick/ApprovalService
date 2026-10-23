# T-046 标准 BPMN 建模器

状态：进行中
负责人：Codex 2026-10-23
里程碑：M1
依赖：T-009、T-045
设计书：§designer、§engine

## 目标

把正式流程设计器的自绘 SVG 画布替换为成熟的 BPMN 2.0 建模器，保持审批服务既有流程定义、属性面板和引擎规则。节点、连线由建模器负责绘制与交互，审批特有配置继续由工作台负责。

## 范围

- `frontend/src/designer/canvas/`：BPMN 图与现有流程 JSON 的转换、建模器适配、画布交互和必要样式；逐步删除被替代的自绘代码。
- `frontend/tests/canvas-checks.ts`、`frontend/tests/checks.ts`：转换、交互规则和导入导出回归检查。
- `frontend/README.md`：建模器使用和第三方标识说明。
- `docs/adr/0015-bpmn-js-modeler.md`：记录依赖及模型边界。

## 共享文件

- `frontend/package.json`、`frontend/package-lock.json`：登记引入固定版本的 bpmn-js 及配套依赖，不改其他包。
- `docs/architecture.md`：同步画布由 bpmn-js 承担图形编辑，审批引擎仍消费现有执行图。
- `docs/tasks/README.md`、`docs/adr/README.md`：登记任务与决定。

## 验收标准

- [ ] 既有正式流程能在标准 BPMN 画布中正确显示、编辑节点和连线，审批属性不丢失。
- [ ] 所有正式节点类型、网关分叉/汇合、条件、回路及跨线均可编辑；线的端点与转折由 BPMN 建模器维护。
- [ ] 限制工具箱为本服务支持的 BPMN 子集，并保持发布校验与工作台选择联动。
- [ ] 导入导出与后端现有 BPMN 格式兼容，保存和撤销不丢完整审批定义。
- [ ] 浏览器视觉复核、`verify.ps1` 全量通过。
- [ ] 相关文档已同步、第三方标识遵守许可证。

## 不做

- 不把通用 BPMN 引擎替换为审批服务执行引擎；不引入本期不支持的事件和补偿语义。

## 交接记录

- 2026-10-23 · Codex · 用户明确要求改用标准 BPMN 建模器。拟引入 npm 官方 registry 的 `bpmn-js` 18.16.0（稳定版；包展开约 5.1 MB，另有传递依赖），本机 npm 缓存中没有。按 AGENTS.md R8，下载前必须取得用户同意。该库许可证要求始终显示 bpmn.io 标识，设计器需保留。将先确定现有审批 JSON 与 BPMN DI 坐标、连线的转换，并写 ADR。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
