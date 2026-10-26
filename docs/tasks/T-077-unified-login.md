# T-077 普通登录会话统一

状态：待验收
负责人：Codex 2026-10-26
里程碑：M1
依赖：T-072、T-050 稳定集成
设计书：§org、§center

## 目标

按用户要求统一普通登录：同一标签页内审批中心、管理后台、流程设计共享身份和单次续期；切换模块与刷新无需重复输入密码，拒绝访问不清除登录。

## 范围

- `frontend/src/api/local-session.ts`、`admin-session.ts`、`center-session.ts`。
- `frontend/src/pages/AdminPage.vue`、`CenterPage.vue`、`MobilePage.vue`：登录提示与权限错误。
- `frontend/tests/session-checks.ts`、`flow-management-checks.ts`、`report-checks.ts`、`admin-intervention-checks.ts`、`admin-integration-wizard-checks.ts`：统一登录与服务端拒绝场景。
- `frontend/src/pages/admin/AdminRoles.vue`、`AdminWorkCalendar.vue`、`AdminIntervention.vue`：403 保留普通身份。
- `frontend/README.md`、`docs/architecture.md`、本任务。

## 共享文件

- `docs/tasks/README.md`：追加索引。
- `docs/architecture.md`：同步统一会话与服务端授权说明。

## 验收标准

- [x] 一次普通登录可切换中心、管理和设计；页面刷新恢复同一身份。
- [x] 共用单次刷新，退出、账号切换或失效后迟到响应不复活旧身份；网络故障不注销。
- [x] 403 保留身份并显示权限错误，服务端既有授权不变。
- [x] 嵌入身份保持内存隔离，不继承普通登录或污染普通身份。
- [x] 原有缓存迁移、中文登录说明、Chrome 导航验证和全量验证完成。

## 不做

- 不延长令牌期限、不保存密码、不改后端权限或业务接口；当前标签页 sessionStorage 的存储边界不变。

## 交接记录

- 2026-10-26 · Codex · 完成普通身份单例、旧缓存迁移、跨中心/管理/设计共享与单次续期；普通退出同步清除，迟到登录/续期不可复活；403 保留身份，嵌入登录维持独立内存边界。Chrome 验证旧会话恢复、统一退出、流程入口登录后切换管理工作台/中心/设计及刷新。全量验证通过：后端 1196、调用端 96、前端 254 项。首次全量仅因新源文件未加入 Git 索引失败，登记后重跑通过。已更新 5280 预览。dotnet clean 清理本次构建输出约 278 MiB；递归目录删除被自动审批策略拒绝，剩余构建缓存和约 1.3 MiB dist 暂留，运行中预览及既有依赖保留。未修改后端授权、令牌期限或缓存到 localStorage。

- 2026-10-26 · Codex · 根因包括普通身份按中心/管理分别保存、前端按登录入口固定页面范围、部分管理页将 403 当注销。用户明确要求普通登录体验，本任务复用统一身份端点并合并前端会话，权限始终由原有后端端点判断；无新依赖。共享文档仅同步实现。
