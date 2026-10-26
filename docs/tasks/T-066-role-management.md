# T-066 组织角色管理

状态：进行中
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

- [ ] 六条角色端点只接受本地 JWT 当前在职组织管理员；权限从持久角色读取。
- [ ] 编码不可改、内置与自定义角色均不删除；名称与成员共用修订 CAS，原因审计同事务。
- [ ] 新成员必须在职；禁止撤销最后一位已开通账号且在职的组织管理员。
- [ ] 前端真实角色列表、创建、名称与成员编辑及冲突反馈。
- [ ] 三库官方 EF 迁移、真实 HTTP/SQLite 场景与全量 verify.ps1 通过。
- [ ] 文档同步。

## 不做

- 删除角色、改角色编码；中心、模拟、画布改动；主分支合并或推送。

## 交接记录

- 2026-10-26 · Codex · 用户已确认 T-020 角色管理方案，由主代理分派本切片。基线 92e4ab4，独立工作树 role-management、分支 ai/codex/T-066-role-management。登记上述共享文件，新增 GET/POST /api/v1/admin/roles、GET/PUT /api/v1/admin/roles/{code}、GET/PUT /api/v1/admin/roles/{code}/members。无新增依赖；与 T-067 各自生成迁移，集成时由主代理顺序合入并按需官方重生，不能手解快照。索引仅追加本任务行。