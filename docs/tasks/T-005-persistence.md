# T-005 持久化：数据库、仓储、待发队列

状态：进行中
负责人：Codex 2026-10-20
里程碑：M2
依赖：T-002
设计书：§engine

## 目标

流程和版本、审批单、任务、操作记录、待发队列、定时任务存进数据库；SQLite、SQL Server、PostgreSQL 三套迁移；乐观锁；后台任务抢锁执行，重启不丢。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Domain/`：实体和仓储接口
- `backend/src/ApprovalService.Infrastructure/Persistence/`：EF Core 上下文、配置、三种数据库的迁移；`backend/tools/add-migration.ps1`（参照标签服务）
- `backend/src/ApprovalService.Infrastructure/Jobs/`：待发队列的发送器（抢锁）、定时任务
- `backend/src/ApprovalService.Application/Runtime/`：加载状态 → 调引擎 → 在一个事务里存状态和事件
- 就绪检查加上数据库连通性；检查程序 `PersistenceChecks.cs`、`DatabaseProviderChecks.cs`

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/Directory.Packages.props`：EF Core（和标签服务同版本，本机缓存里有）
- `InfrastructureServiceCollectionExtensions.cs`
- `ApplicationServiceCollectionExtensions.cs`
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`

## 验收标准

- [ ] 两个人同时同意（会签、或签）只推进一次（乐观锁），检查里真的并发提交
- [ ] 操作记录只追加，每条带上一条的校验值，被改过能查出来
- [ ] 三套迁移都和模型一致（检查程序核对）；设置环境变量 `APPROVAL_TEST_SQLSERVER`、`APPROVAL_TEST_POSTGRES` 时在真实数据库上跑一遍主要流程
- [ ] 服务重启后待发队列和定时任务继续执行；同一项任务只有一个实例在做
- [ ] `verify.ps1` 通过
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 开放接口（T-006）
- 每日备份（T-021）

## 交接记录

- 2026-10-20 · Codex · 认领任务，基于 main 创建独立任务分支并引入已完成的 T-002 分支。计划使用本机已缓存、与标签服务同版本的 EF Core SQLite 10.0.12、SQL Server 10.0.12、PostgreSQL 10.0.3 及 Design 10.0.12；在新增依赖的 ADR 中记录选型。修改共享文件 `backend/Directory.Packages.props`、`InfrastructureServiceCollectionExtensions.cs`、`ApplicationServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`、`docs/architecture.md`、`docs/adr/README.md` 前已登记；另需同步 `backend/README.md`。本轮先完成持久化核心与迁移，再验证并交接。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
