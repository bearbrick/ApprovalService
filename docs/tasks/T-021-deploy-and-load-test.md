# T-021 部署、监控、备份与压测

状态：阻塞
负责人：Codex 2026-10-24
里程碑：M5
依赖：T-005、T-006
设计书：§platform、§engine

## 目标

能用 Docker 或 Windows 服务部署；有健康检查和指标；SQLite 每日备份、升级前快照；压测确认性能目标。做法参照标签服务。

## 范围

可以新建、修改的文件：

- `backend/deploy/`：Dockerfile、Windows 服务、部署说明
- `/metrics`（参照标签服务自己输出 Prometheus 文本）
- SQLite 每日备份和升级前快照
- `backend/tests/load/`：k6 脚本（发起、审批、待办数量）和报告模板
- `backend/src/ApprovalService.Api/`、`backend/src/ApprovalService.Infrastructure/Hosting/`、`backend/tests/ApprovalService.Backend.Checks/`：健康与指标、在线快照、每天执行与恢复检查
- `backend/src/ApprovalService.Api/ApprovalService.Api.csproj`：引用本机已缓存、与标签服务一致的 `Microsoft.Extensions.Hosting.WindowsServices` 10.0.12；让进程可被 Windows 服务控制器正确启停，决定见 ADR 0020
- `docs/adr/0020-deployment-and-backup.md`：记录单实例部署及备份取舍

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/src/ApprovalService.Api/Program.cs`
- `backend/Directory.Packages.props`、`backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`
- `docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`

## 验收标准

- [ ] 部署说明按步骤能装起来
- [x] 压测报告写进交接记录：发起、审批 P95 ≤ 300 ms，待办数量 P95 ≤ 50 ms
- [x] 备份恢复演练一次，写进部署说明
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 多实例部署（换 SQL Server、PostgreSQL 以后再做）

## 交接记录

- 2026-10-24 · Codex · 已实现单实例 Docker 多阶段构建稿、Windows 官方服务宿主、Prometheus 0.0.4 HTTP 聚合指标（路由模板固定维度，本机或长 Bearer 密钥访问）、SQLite 每日在线 `VACUUM INTO`、迁移前快照、完整目录中的本地附件与登录签名密钥备份及保留策略；另有手工 `backup-now`、恢复说明、k6 发起/审批/待办脚本和 ADR 0020。后端场景在 WAL 模式写入后备份，修改原库、恢复副本并确认旧值、附件和密钥，另用只应用首个迁移的旧库验证升级前快照；`k6 inspect` 通过。后端分部验证 954 项，全量 `verify.ps1` 通过：后端 954、调用端 68、前端 164 项；本机无钩子 HTTP+SQLite，预热 4、各 20 个样本，P95 发起 22.7ms、审批 23.6ms、待办数量 5.4ms，均低于目标。**R13 阻塞项**：本机无 Docker，且本轮未以管理员身份安装 Windows 服务，无法实际执行部署说明并确认在目标机可装；k6 脚本已静态解析，但还没有两个业务人员绑定和专用测试流程的独立压测环境。下一位在目标 Linux/Windows 环境实装、按模板跑真实 k6 并恢复演练后才能勾第一项、转待验收。无需重做已经通过的数据库场景。
- 2026-10-24 · Codex · 从已验证整合提交 `4bdfc4a` 认领，先核对服务现有就绪检查、SQLite 启动迁移、部署脚本和压测工具。计划在本任务范围增加安全的 Prometheus 指标、SQLite 在线备份与升级前快照、Docker/Windows 部署说明、k6 压测。共享文件已在上方登记；Windows 服务引用本机已缓存的微软官方包 `Microsoft.Extensions.Hosting.WindowsServices` 10.0.12（和标签服务同版），写 ADR 0020。当前机器有 k6 2.1.0，但无 Docker 命令；容器镜像只能做静态检查，不能在本机实际运行。
<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
