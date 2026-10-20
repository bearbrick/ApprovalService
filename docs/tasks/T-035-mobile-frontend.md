# T-035 手机审批中心前端基础

状态：进行中
负责人：Codex 2026-10-20
里程碑：M3
依赖：T-032、T-033（已验收的前端基线）
设计书：§center、§api

## 目标

将已评审的手机审批中心布局落成正式 Vue 页面，并为后续 T-012 的真实身份、列表、详情和审批接口接入留出安全边界。

## 范围

- `frontend/src/pages/mobile/`、`frontend/src/pages/MobilePage.vue`：手机卡片列表、详情、我的页面与状态交互。
- `frontend/src/model/mobile.ts`、`frontend/tests/mobile-checks.ts`、`frontend/tests/checks.ts`：筛选、路由和触摸刷新的纯逻辑及检查。
- `frontend/src/styles/mobile-approval.css`：375 像素及其他手机宽度的响应式布局。
- `frontend/README.md`：同步页面现状和接入边界。
- 本任务文件与 `docs/tasks/README.md`。

## 共享文件

修改前已登记：

- `frontend/src/App.vue`：只追加 `#/m` 手机页分流，不改其他页面。
- `frontend/src/model/routes.ts`：仅在现有手机详情路由不能满足需要时补充。
- `docs/tasks/README.md`：只追加任务索引行。

## 验收标准

- [ ] 四个手机标签、卡片、筛选与刷新、详情和固定底部动作区均有正式 Vue 页面。
- [ ] 缺少授权 API 时明确显示未接入；离线、加载和错误状态可辨，不伪造待办或审批成功。
- [ ] 列表与详情使用电脑端相同的只读适配器接口，写入动作保持禁用。
- [ ] 手机路由与触摸刷新纯逻辑有检查。
- [ ] `verify.ps1` 通过（见 AGENTS.md）。
- [ ] 相关文档已更新。

## 不做

- 不认领依赖 T-011 的 T-012；不实现真实登录、授权、审批提交、草稿、设备上传与消息提醒。
- 不修改开放契约或后端、调用端。

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

- 2026-10-20 · Codex · 已登记共享文件：App.vue 仅追加手机页面分流；routes.ts 仅在必要时修改；任务索引仅追加一行。外置 Chrome 在前一前端任务已被系统策略阻止启动，本任务不使用内置浏览器；375 像素视觉截图留待 T-012。
