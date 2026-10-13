# T-021 部署、监控、备份与压测

状态：待认领
负责人：—
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

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/src/ApprovalService.Api/Program.cs`

## 验收标准

- [ ] 部署说明按步骤能装起来
- [ ] 压测报告写进交接记录：发起、审批 P95 ≤ 300 ms，待办数量 P95 ≤ 50 ms
- [ ] 备份恢复演练一次，写进部署说明
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 多实例部署（换 SQL Server、PostgreSQL 以后再做）

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
