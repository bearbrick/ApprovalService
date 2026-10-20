# T-005 持久化：数据库、仓储、待发队列

状态：待验收
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
- 就绪检查加上数据库连通性；检查程序 `PersistenceChecks.cs`、`DatabaseProviderChecks.cs`、`EventAnchorChecks.cs`、`EventChainConsistencyChecks.cs`、`JobChecks.cs`、`JobReadbackRaceChecks.cs`
- `backend/src/ApprovalService.Infrastructure/ApprovalService.Infrastructure.csproj`：引用本任务已指定的数据库提供程序
- `backend/src/ApprovalService.Api/Endpoints/SystemEndpoints.cs`：就绪检查调用数据库探针
- `backend/tests/ApprovalService.Backend.Checks/ServerChecks.cs`、`ServerFactory.cs`：更新就绪语义并在删除临时 SQLite 库前清空连接池
- `backend/README.md`、`docs/architecture.md`、`docs/adr/`：同步实现、依赖选型

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/Directory.Packages.props`：EF Core（和标签服务同版本，本机缓存里有）
- `InfrastructureServiceCollectionExtensions.cs`
- `ApplicationServiceCollectionExtensions.cs`
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`

## 验收标准

- [x] 两个人同时同意（会签、或签）只推进一次（乐观锁），检查里真的并发提交
- [x] 操作记录只追加，每条带上一条的校验值，被改过能查出来
- [x] 三套迁移都和模型一致（检查程序核对）；设置环境变量 `APPROVAL_TEST_SQLSERVER`、`APPROVAL_TEST_POSTGRES` 时在真实数据库上跑一遍主要流程
- [x] 服务重启后待发队列和定时任务继续执行；同一项任务只有一个实例在做
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 开放接口（T-006）
- 每日备份（T-021）

## 交接记录

- 2026-10-20 · Codex · 用户同意从 nuget.org 下载并例外使用 2026-10-13 发布的 `System.Security.Cryptography.Pkcs` 10.0.13，已升级。合入已完成的 T-003 后，在真实 SQLite 上分别验证同一节点会签、或签两人并发：旧版本只提交一次，会签冲突方重试后完成，或签立即完成，两者最终事件均只写一次。独立审查发现的事件尾部删除、过期租约确认问题已修复并补场景；三套数据库的事件锚点迁移已生成。全量 `verify.ps1` 通过：后端 235 项、调用端 33 项、前端 54 项，构建零警告。SQL Server、PostgreSQL 环境变量未配置，实际连接测试未运行；对应迁移快照与模型一致。项目尚未上线，新事件锚点迁移对已有历史审批单默认值为 0/空串；如以后从旧版数据库升级，需要先回填历史末尾锚点。任务提交在独立分支，未合并主分支或推送远程。
- 2026-10-20 · Codex · 持久化实现及 SQLite 实跑、三套迁移模型检查、操作记录防篡改链、并行节点的两人真实并发提交与冲突重试、待发和定时任务重启恢复均已完成。全量 `verify.ps1` 退出码为 0：后端 168 项、调用端 33 项、前端 54 项通过；未配置 SQL Server 和 PostgreSQL 实例时，只运行迁移模型一致性检查，环境变量配置后会执行真实数据库流程。独立审查指出仍需补同一节点会签/或签并发场景、事件链尾部删除检测、执行者租约过期检查，正在补齐。零警告交付另受阻：本机缓存的 `System.Security.Cryptography.Pkcs` 10.0.11 触发 NU1902、NU1903；微软修复版 10.0.13 来自 nuget.org，约 1.09 MB，2026-10-13 发布。它尚未在本机缓存，且发布未满两周；按 AGENTS.md R8，需要用户同意下载并例外使用后才能升级、重跑验证、改为待验收。未抑制告警，也未合并或推送。
- 2026-10-20 · Codex · 认领任务，基于 main 创建独立任务分支并引入已完成的 T-002 分支。计划使用本机已缓存、与标签服务同版本的 EF Core SQLite 10.0.12、SQL Server 10.0.12、PostgreSQL 10.0.3、Design 10.0.12 及 Hosting.Abstractions 10.0.12（后台轮询需 `BackgroundService`）；在新增依赖的 ADR 中记录选型。修改共享文件 `backend/Directory.Packages.props`、`InfrastructureServiceCollectionExtensions.cs`、`ApplicationServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`、`docs/architecture.md`、`docs/adr/README.md` 前已登记；另需同步 `backend/README.md`。本轮先完成持久化核心与迁移，再验证并交接。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
