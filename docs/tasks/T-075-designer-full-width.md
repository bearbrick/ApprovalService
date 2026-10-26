# T-075 设计器后续页签铺满内容宽度

状态：进行中
负责人：Codex 2026-10-26
里程碑：M1
依赖：T-050 已验证集成
设计书：§designer、§form、§print

## 目标

按用户反馈移除后续页签遗留的窄面板上限，使详情排版、流程规则、打印设置和版本管理利用工作区宽度。

## 范围

- `frontend/src/styles/designer-workspace.css`。
- `frontend/src/designer/workbench/FlowRulesPanel.vue`、`FlowPrintPanel.vue`、`FlowSimulationPanel.vue`。
- `frontend/src/designer/data/ApprovalDetailLayoutEditor.vue`（仅需要时修正容器收缩）。
- `frontend/README.md`、本任务文件。

## 共享文件

- `docs/tasks/README.md`：追加索引。

## 验收标准

- [ ] 规则、测试模式、打印、版本与接入示例容器不再受 960px 限制。
- [ ] 详情排版保持利用全宽，所有页签两侧留白统一。
- [ ] Chrome 检查四个页签的容器边界和实际外观。
- [ ] 统一验证通过并更新预览。

## 不做

- 不改变审批规则、打印或版本行为，不改变打印纸张尺寸。

## 交接记录

- 2026-10-26 · Codex · 已定位规则、打印、模拟、版本和模板辅助区的 max-width:960px；详情排版主容器没有同类限制。按用户要求调整页面容器，保留控件自身合理宽度及窄屏分栏。无新依赖。
