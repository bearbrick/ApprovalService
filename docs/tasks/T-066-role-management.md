# T-066 组织角色管理

状态：已完成
负责人：Codex 2026-10-26
里程碑：M3
依赖：T-007；T-020 已有管理身份基础
设计书：§org、§api

## 目标

本地组织管理员通过真实管理页面维护角色名称、成员，并以修订控制并发、记录原因和保护最后一位可登录管理员。

## 范围

- 后端 Contracts/Protocol 角色请求响应与路由；Domain/Organization 角色修订和管理规则、仓储接口；Application/Administration、Infrastructure/Organization、Api/Endpoints 角色管理。
- OrganizationModelConfiguration 与三种数据库官方 EF 迁移和快照；后端角色管理场景检查。
- frontend/src/api/admin-role-management.ts、pages/admin/AdminRoles.vue、pages/AdminPage.vue、前端角色检查。
- 前后端 README、docs/contracts.md、docs/architecture.md、设计书 §org/§api、本任务和索引。

## 共享文件

- Contracts/Protocol/ApiRoutes.cs 与角色模型；ApplicationServiceCollectionExtensions.cs、InfrastructureServiceCollectionExtensions.cs、Api/Program.cs、检查 Program.cs。
- docs/contracts.md、docs/architecture.md、docs/tasks/README.md、frontend/src/pages/AdminPage.vue。

## 验收标准

- [x] 六条角色端点只接受本地 JWT 当前在职组织管理员；权限从持久角色读取。
- [x] 编码不可改、内置与自定义角色均不删除；名称与成员共用修订 CAS，原因审计同事务。
- [x] 新成员必须在职；禁止撤销最后一位已开通账号且在职的组织管理员。
- [x] 前端真实角色列表、创建、名称与成员编辑及冲突反馈。
- [x] 三库官方 EF 迁移、真实 HTTP/SQLite 场景与全量 verify.ps1 通过。
- [x] 文档同步。

## 不做

- 删除角色、改角色编码；中心、模拟、画布改动；主分支合并或推送。

## 交接记录

- 2026-10-26 · Codex · 整合代理独立验收：与 T-067 三库迁移顺序合并后，全量 `verify.ps1` 通过（后端 1181、调用端 96、前端 217 项及生产构建）。外置 Google Chrome 在真实预览 SQLite 中读取内置流程管理员和组织管理员、成员数与修订号；编辑视图展示成员及“至少保留一位已开通账号且在职管理员”限制。角色页面未出现错误，T-066 验收完成。SQL Server/PostgreSQL 未连实库，沿用迁移模型验证边界。

- 2026-10-26 · Codex · 已完成六条本地 JWT 角色端点、真实角色页、名称/成员共用修订 CAS、事务内重查权限与原因审计、新增成员在职校验、最后一位已开通账号且在职管理员保护。内置编码按大小写归一化，避免 SQL Server 常见排序规则绕过保护；角色不删除、不改编码。官方 EF 三库迁移仅新增 OrganizationRoles.Revision。全量 verify.ps1 通过：后端 1164、调用端 96、前端 206 项，生产构建通过；新增真实 HTTP/SQLite 检查覆盖匿名/普通/嵌入权限、重复编码、过期修订、无效成员、最后管理员及旧 JWT 即时失权，前端检查覆盖 JWT/修订/原因、409 不重试和响应校验。已同步 Contracts、前后端 README、架构和设计 §org/§api。未新增依赖、未改中心/模拟/画布、未推送或合主分支。未单独运行 Chrome 视觉复核，交整合代理统一检查 #/admin/org/roles；SQL Server/PostgreSQL 仅验证迁移与模型，未连真实外部数据库。产品提交 15ec3ec，共享登记提交 b1e2f4e；整合日历后若快照冲突，官方移除并重生迁移，不手改。

- 2026-10-26 · Codex · 用户已确认 T-020 角色管理方案，由主代理分派本切片。基线 92e4ab4，独立工作树 role-management、分支 ai/codex/T-066-role-management。登记上述共享文件，新增 GET/POST /api/v1/admin/roles、GET/PUT /api/v1/admin/roles/{code}、GET/PUT /api/v1/admin/roles/{code}/members。无新增依赖；与 T-067 各自生成迁移，集成时由主代理顺序合入并按需官方重生，不能手解快照。索引仅追加本任务行。
