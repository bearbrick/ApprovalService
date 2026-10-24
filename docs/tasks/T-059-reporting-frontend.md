# T-059 管理后台真实报表页面

状态：进行中
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-058 已验证稳定快照
设计书：§stats、§org

## 目标

让组织管理员和直接负责部门的负责人在管理后台查看真实的概览、流程、节点和人员指标，并按时间、流程、来源应用、发起部门快照筛选。

## 范围

- `frontend/src/api/admin-reports.ts`：四条 JWT 报表查询的前端副本、验证与请求。
- `frontend/src/pages/admin/AdminReports.vue`、`frontend/src/styles/admin-reports.css`：报表交互和真实指标展示。
- `frontend/src/pages/AdminPage.vue`、`frontend/src/api/admin-session.ts`、`frontend/src/model/admin.ts`：统计页入口、负责人本地登录与页面文字。
- `frontend/tests/report-checks.ts`、`frontend/tests/checks.ts`：报表响应、筛选和权限错误场景。
- `frontend/README.md`、`docs/tasks/T-059-reporting-frontend.md`：文档和交接。

## 共享文件

- `frontend/src/pages/AdminPage.vue`：只加统计页分支和登录范围参数，另行提交；已通知并行管理页开发者避开同一文件。
- `frontend/src/api/admin-session.ts`：只让报表页登录跳过组织管理员专属预检查，报表接口仍独立鉴权；另行提交。
- `frontend/tests/checks.ts`、`docs/tasks/README.md`：只追加检查或任务索引，另行提交。

## 验收标准

- [ ] 四条真实报表接口在统计页使用本地 JWT，显示明确的加载、空结果、失败与无权状态。
- [ ] 时间半开区间与流程、来源应用、部门筛选可用，负责人只看到服务端授权范围。
- [ ] 公开流程、节点、人员指标和保密匿名数量分别显示，不制造未返回的数值。
- [ ] 场景检查和完整 `verify.ps1` 通过，前端文档已同步。

## 不做

- 不做小时汇总、Excel/CSV 导出、趋势和审批单明细导出；由 T-019 后续完成。
- 不新增依赖、不修改后端契约。

## 交接记录

- 2026-10-24 · Codex · 从已合入 T-058 的集成快照开始独立前端切片。统计页需读取四条真实 JWT 接口；现有管理员登录先检查组织管理员身份，因此本任务将为报表页单独允许部门负责人建立本地会话，其他管理页仍维持原检查。
