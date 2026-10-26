# T-067 在线工作日历管理

状态：进行中
负责人：Codex 2026-10-26
里程碑：M3
依赖：T-029（已完成）；承接 T-020 已批准工作日历切片
设计书：§org、§rules、§center、§api

## 目标

本地组织管理员可在线维护每日工作时段及逐日节假日、调休；新激活任务读取一致快照计算期限和提醒。

## 范围

- Contracts 工作日历协议、Domain 日历实体和仓储、Application 管理用例和运行时日历快照读取。
- Infrastructure 日历持久化、配置回退、数据库模型与三库官方 EF 迁移。
- Api 管理端点；后端真实 HTTP、SQLite 和任务期限边界场景。
- frontend 工作日历 API、页面、管理入口及相应检查。
- 前后端 README、契约、架构、设计书对应章节及本任务。

## 共享文件

- `docs/tasks/README.md` 只追加本任务索引。
- `backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 新增日历路由。
- 应用与基础设施 DI 登记、Api `Program.cs`、后端检查 `Program.cs`。
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs` 及三库迁移快照。
- `frontend/src/pages/AdminPage.vue`、管理导航模型和检查登记。
- `docs/contracts.md`、`docs/architecture.md`、设计书对应章节。

## 验收标准

- [ ] GET/PUT 仅本地组织管理员可用，工作时段、日期唯一性与原因严格校验。
- [ ] 修订 CAS 与管理审计原子保存，冲突不落审计，三库官方迁移齐全。
- [ ] 空库沿用部署配置，保存后数据库优先；每次任务计算使用一致快照。
- [ ] 已有期限和已排提醒不重算，排队任务激活使用新日历，汇总时区仍沿用部署配置。
- [ ] 真实管理页面可读写、显示冲突并保留输入；检查覆盖权限、SQLite 和运行边界。
- [ ] `verify.ps1` 全量通过。
- [ ] 相关文档同步。

## 不做

- 修改部署时区、自动获取法定节假日、重算旧任务或提醒。
- 中心、模拟、画布模块；新增依赖；主分支合并和推送。

## 交接记录

- 2026-10-26 · Codex · 用户已批准在线日历方案；从整合 `92e4ab4` 建立独立工作树及 `ai/codex/T-067-work-calendar-admin`，按授权从集成基线开分支。预先登记上述共享文件。GET/PUT `/api/v1/admin/work-calendar`，时区只读，PUT 带预期修订及原因；已有任务和提醒不重算。与角色切片各自生成三库迁移，集成时按顺序重生迁移，不硬解快照冲突。