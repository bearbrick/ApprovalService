# T-033 管理后台前端基础

状态：进行中
负责人：Codex 2026-10-20
里程碑：M3
依赖：T-025、T-008、T-031
设计书：§center、§org、§integration、§platform

## 目标

把已评审的管理后台原型结构做成正式 Vue 页面，覆盖管理导航、列表和详情展示状态，同时把鉴权和写入留给依赖 T-007 的 T-020。

## 范围

- `frontend/src/pages/AdminPage.vue`、`frontend/src/pages/admin/**`：流程、组织、接入应用、运行干预、回调、系统信息和日志的页面结构与状态。
- `frontend/src/model/admin.ts`：页面定义、只读筛选与安全展示的纯逻辑。
- `frontend/src/api/admin-adapter.ts`：只读数据适配接口与明确的未接入实现，不猜测未定的管理 API 路径。
- `frontend/src/styles/admin.css`：管理后台专属样式。
- `frontend/tests/admin-checks.ts`、`frontend/tests/checks.ts`：纯逻辑场景检查。
- `frontend/README.md` 和本任务文件：说明接入边界与验收。

## 共享文件

- `frontend/src/App.vue`：仅接入正式管理后台页面，其他页面不改。
- `frontend/src/model/routes.ts`：若现有管理地址不足，补充严格路由。
- `docs/tasks/README.md`：只追加 T-033 索引。

## 验收标准

- [ ] 已评审的管理入口有正式 Vue 页面与对应状态，流程、组织与绑定、应用接入、运行干预、系统信息和日志可导航。
- [ ] 加载、空结果、错误、未接入状态清晰，筛选只作用于已授权数据；无虚构成功统计、密钥或凭据。
- [ ] 所有管理写入和管理员干预均在真实权限与审计接口接入前禁用，原因与下一步明确。
- [ ] 前端场景检查、构建和全量 `verify.ps1` 通过，文档、交接记录齐全。

## 不做

- 不实现 T-020 的登录、服务端权限、管理员写接口、操作日志持久化、密钥生成/展示或接口调试沙箱。
- 不实现 T-019 的真实统计报表，不复制原型的 localStorage 演示数据，不新增依赖。

## 交接记录

- 2026-10-20 · Codex · 从 main 建独立任务，计划安全合入已完成的前端基础。修改共享 App.vue 和任务索引前在此登记；现有路由已覆盖管理一级入口和二级页签，预计无需改 routes.ts。Chrome 连接在本轮已被工具执行策略阻止，按个人规则不使用应用内浏览器，视觉验收留给后续 T-020。
