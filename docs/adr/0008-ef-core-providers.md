# 0008 运行库使用 EF Core 和三种独立迁移

状态：已采纳
日期：2026-10-20
关联：T-005

## 背景

审批状态、任务投影、只追加操作记录和待发队列必须在同一事务内持久化。初版用 SQLite 部署，后续要切换 SQL Server 或 PostgreSQL；运行规则不能依赖某一种数据库。标签服务已经使用 EF Core 和这三种提供程序。

## 决定

采用 EF Core 10.0.12 的 SQLite、SQL Server 提供程序和设计工具，PostgreSQL 使用 Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3；版本与标签服务一致。运行库模型只在 `ApprovalDbContext` 定义一次，三种提供程序各有派生上下文和独立迁移。启动时 SQLite 自动执行迁移；外部数据库使用各自迁移，部署时显式升级。仓储与后台任务只注入统一的 `ApprovalDbContext` 或工厂。

所有 `DateTimeOffset` 按 UTC `DateTime` 存储，确保 SQLite 的到期和租约比较可由数据库执行。审批单的 `Version` 是乐观锁标记；一次 `SaveChanges` 原子提交状态、任务投影、操作记录和待发项。新增 `Microsoft.Extensions.Hosting.Abstractions` 10.0.12 承载后台任务和启动迁移。SQL Server 提供程序的传递依赖曾解析到有漏洞告警的 `System.Security.Cryptography.Pkcs` 9.0.11；本机缓存的 9.0.18 和 10.0.11 仍受 [微软安全公告](https://github.com/advisories/GHSA-846f-f764-76mx)影响。当前显式固定到 10.0.11 只是避免解析更旧版本，告警尚未解决；必须在获得未缓存包下载和两周选型规则例外的同意后，改用 10.0.13 并重新验证。

## 备选方案

手写三套 SQL 会使模型和迁移分叉，也难以保证事务与乐观锁语义一致。共享单套迁移不能覆盖三种提供程序的列类型和数据库差异，因此不用。

## 影响

基础设施层增加数据库包和三套生成的迁移；Contracts、引擎和领域层仍不引用 EF Core。流程版本发布后不可修改，操作记录只追加。切换数据库前要迁移数据并运行对应提供程序的迁移，不通过修改配置自动搬运已有数据。
