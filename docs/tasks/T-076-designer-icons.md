# T-076 设计器导航与展开图标

状态：进行中
负责人：Codex 2026-10-26
里程碑：M1
依赖：T-050 稳定集成
设计书：§designer、§form

## 目标

将用户指出的返回、公式展开文字箭头替换为现有统一 SVG 图标，同时统一字段和排版中的上下移动图标。

## 范围

- `frontend/src/pages/DesignerPage.vue`、`frontend/src/styles/designer-workspace.css`。
- `frontend/src/designer/data/ApprovalFieldsEditor.vue`、`ApprovalDetailLayoutEditor.vue`。
- `frontend/README.md`、本任务。

## 共享文件

- `docs/tasks/README.md`：追加索引。

## 验收标准

- [ ] 返回、展开收起与同类移动按钮复用 UiIcon，尺寸对齐，无字符箭头。
- [ ] 图标按钮保留明确中文可访问名称，展开状态及禁用逻辑不变。
- [ ] Chrome 检查图标外观、公式开关及返回跳转。
- [ ] 统一验证通过，更新本地预览及文档。

## 不做

- 不新增图标依赖，不修改审批逻辑与契约。

## 交接记录

- 2026-10-26 · Codex · 使用现有 Bootstrap Icons 本地子集与 UiIcon，返回用 arrow、展开和移动用旋转 chevron；只调整展示和可访问名称。登记任务索引追加，无新依赖。
