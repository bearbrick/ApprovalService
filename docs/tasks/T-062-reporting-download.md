# T-062 报表页 Excel 下载入口

状态：进行中
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-061 已验证稳定快照
设计书：§stats

## 目标

让有报表权限的管理页使用者按当前时间、流程、来源应用和部门筛选，直接下载后端已授权的四表汇总工作簿。

## 范围

- `frontend/src/api/admin-reports.ts`：复用报表范围令牌和筛选参数获取 XLSX。
- `frontend/src/pages/admin/AdminReports.vue`：添加下载按钮、等待和失败反馈。
- `frontend/src/styles/admin-reports.css`：按钮和反馈适配。
- `frontend/tests/report-checks.ts`：JWT、筛选、文件响应和错误场景。
- `frontend/README.md`、`docs/tasks/T-062-reporting-download.md`：说明与交接。

## 共享文件

- `docs/tasks/README.md`：追加任务索引，单独提交。

## 验收标准

- [ ] 下载请求携带报表范围 JWT 和当前 UTC 半开筛选，不访问其他管理路由。
- [ ] 成功响应以约定文件名保存，等待时防止重复点击；失败不保存错误正文并显示中文原因。
- [ ] 无效时间、403、401、网络错误和损坏文件响应有可理解的反馈。
- [ ] 前端场景检查与完整 `verify.ps1` 通过；文档同步。

## 不做

- 不修改服务端导出契约、报表计算口径或新增依赖。
- 不实现逐单明细、小时持久汇总或趋势。

## 交接记录

- 2026-10-24 · Codex · 已登记任务索引这一共享文件；将复用 T-061 的固定导出路径、MIME 和文件名，API 与页面均只在报表范围内工作。
