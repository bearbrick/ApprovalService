# T-057 组织管理员内部报表查询

状态：进行中
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-054、T-056 已验证稳定快照
设计书：§stats、§org

## 目标

建立可复用的内部报表查询用例：只接受本地登录且当前具有组织管理员角色的在职人员，按已定统计口径读取真实事实，并让保密流程仅贡献匿名数量。

## 范围

- `backend/src/ApprovalService.Domain/Reporting/ReportingFacts.cs`：为最小化事实增加锁定版本的保密标记。
- `backend/src/ApprovalService.Infrastructure/Reporting/ReportingFactsRepository.cs`：在同一读事务内从锁定流程版本取得保密标记。
- `backend/src/ApprovalService.Application/Reporting/`：内部安全查询应用服务和响应模型。
- `backend/tests/ApprovalService.Backend.Checks/ReportingQueryChecks.cs`：真实 SQLite 授权与保密场景。
- `backend/README.md`：说明内部用例及剩余接口边界。
- `docs/tasks/T-057-reporting-query-service.md`：任务交接。

## 共享文件

- `docs/tasks/README.md`：仅追加索引，单独提交。
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`：仅追加应用服务登记，单独提交。
- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：仅追加事实仓储登记，单独提交。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：仅追加场景检查登记，单独提交。

## 验收标准

- [ ] 仅本地登录的当前在职组织管理员可获得内部报表结果，其他身份拒绝。
- [ ] 非保密流程的流程、节点和人员指标遵守双时间口径与范围筛选；保密流程仅提供匿名发起量和完成量。
- [ ] 锁定流程版本保密标记从可信持久化定义读取，缺损定义不默认为公开。
- [ ] 新场景及全量 `verify.ps1` 通过，文档和交接同步。

## 不做

- 不开放报表 HTTP；新的路由、响应契约及部门负责人对子部门的范围在 T-019 明确后接入。
- 不实现小时汇总、导出或管理页面。
- 不新增依赖、数据库字段或迁移。

## 交接记录

- 2026-10-24 · Codex · 认领内部安全查询切片。设计书只写“部门负责人看本部门”，未定义下级部门；保密流程只显示数量，当前最小事实缺少锁定版本保密标记；报表 HTTP 契约也未定义。按 AGENTS.md R13 暂不开放接口或猜测部门层级，先完成权限语义已确定的本地组织管理员内部查询，并对保密流程仅返回匿名数量。
