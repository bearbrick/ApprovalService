# T-008 自由画布工作台与审批节点配置

状态：进行中
负责人：Codex 2026-10-15
里程碑：M1
依赖：T-001
设计书：§designer、§rules

## 目标

统一自由画布工作台：流程元信息、模板选择、节点属性、审批规则、条件编辑与发布校验反馈。画布底层交互由 T-009 提供，二者组成同一个设计器（ADR 0007）。保留原任务文件名以保持既有链接可用。

## 范围

可以新建、修改的文件：

- `frontend/src/designer/workbench/`（新建）、`frontend/src/model/` 的纯逻辑（节点配置、条件编辑、发布检查反馈），`frontend/tests/checks.ts` 补检查
- `frontend/src/pages/DesignerPage.vue`
- `frontend/README.md`（同步设计工作台的实际接入状态）
- `frontend/src/components/layout/DesktopShell.vue`、`frontend/src/styles/layout.css`、`frontend/tests/ui-checks.ts`（设计器工作区移除重复左栏，其余工作区保持现有导航）
- 后端管理接口还没有时，先读写本地示例流程（`backend/samples/flows` 的副本放在前端测试数据里），接口好了再接

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `frontend/src/App.vue`、`frontend/src/model/routes.ts`（加设计器的子路由时）

## 验收标准

- [ ] 接入 T-009 的画布组件；全部节点类型有属性设置，条件、包容、并行分支能配置路线、汇合与兜底
- [ ] 抽屉里能设审批人来源、多人审批方式、自动处理、允许的操作、节点字段权限、时限
- [ ] 条件编辑：字段、运算符、且 / 或分组，能用明细合计和计算字段
- [ ] 撤销重做；和后端同样的发布校验，问题标在节点上
- [ ] 统一自由画布，不存在画法选择或转换入口；风格和标签服务一致；截图给用户评审
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 自由画布图形交互、连线和文件导入导出底层（T-009），本任务负责集成
- 审批数据和排版设计（T-010）

## 交接记录

- 2026-10-15 · Codex · 用户额度接近上限，按主 agent 要求暂停。工作台基础已提交：正式示例模板与本地草稿、流程元信息与范围、全部节点属性和字段权限、连线条件构建、打印设置、撤销重做、发布预检及问题定位；App 已路由到 DesignerPage。正式前端设计器重复左栏已移除，顶栏入口保留。T-009 最终版 `151ce6a` 已由主 agent 合入本任务分支（合并提交 `fcb3372`），但 `DesignerPage.vue` 尚未把临时元素列表替换为 `FlowCanvasEditor` 的唯一 `inspector` 插槽；这是下一个必做步骤。表达式静态预检应统一复用 T-009 `validateCanvasDefinition`，删除 T-008 里重复的 `publication-check.ts` 逻辑，避免漂移。合入前本任务前端 `npm run check` 77 项、`npm run build` 通过；Chrome 已验证设计器全宽布局、人工节点面板、跨页签草稿状态，并发现和修复了示例字段缺少 `children` 的运行错误。画布合入后尚未重新验证或截图。任务仍“进行中”，尚未跑本任务最终全量 `verify.ps1`，也未合并主分支或推送。
- 2026-10-15 · Codex · 用户此前要求移除与顶栏重复的导航；正式页面预览确认设计器仍有左栏。按主 agent 协调，范围增加桌面布局、布局样式及对应 UI 检查，只在设计器工作区取消左栏，其余页面保持原布局。
- 2026-10-15 · Codex · 共享文件登记：`frontend/src/App.vue` 只把设计器路由交给 DesignerPage；`frontend/tests/checks.ts` 只登记工作台纯逻辑检查。`frontend/src/model/routes.ts` 无需改动。两个共享文件各自单独提交。

- 2026-10-15 · Codex · T-001 和 T-010 已独立验收，从主分支建立任务分支并纳入两项成果；认领自由画布工作台和节点属性配置。T-009 图形底层正在独立分支实现，先只修改本任务范围内工作台与页面，待画布组件验收后接入。若需修改共享 `frontend/src/App.vue`、`frontend/src/model/routes.ts` 或 `frontend/tests/checks.ts`，先在此登记并单独提交。
- 2026-10-13 · Codex · 按用户明确决定和 ADR 0007，将尚未认领的双设计器计划调整为统一自由画布；任务状态仍待认领。本轮只同步规划，不实现业务代码。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

