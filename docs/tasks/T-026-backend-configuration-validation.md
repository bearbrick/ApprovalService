# T-026 后端数据配置启动校验

状态：进行中
负责人：Codex 2026-10-15
里程碑：框架完善
依赖：T-000
设计书：§2、§9、§10

## 目标

后端在启动时发现数据库提供程序、数据目录或外部数据库连接串配置错误，并给出可定位的中文说明，避免错误配置下仍报告服务就绪。

## 范围

- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- `backend/tests/ApprovalService.Backend.Checks/ServerChecks.cs`
- `backend/README.md`
- 本任务文件
- `docs/tasks/README.md` 的任务索引追加行

## 共享文件

- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：在现有数据配置登记处追加启动校验，单独提交。
- `docs/tasks/README.md`：仅追加 T-026 索引，单独提交。

## 验收标准

- [ ] 默认 SQLite 配置能正常启动，已知提供程序的有效配置通过校验。
- [ ] 未知提供程序、空数据目录、外部数据库缺少连接串时启动失败，说明含配置键和值约束，不输出连接串。
- [ ] 现有存活、就绪、服务信息接口行为不变；不新增依赖或审批业务代码。
- [ ] `verify.ps1 -Part backend` 通过。
- [ ] 后端 README 已同步。

## 不做

- 不接数据库、不探测数据库连通性；数据库就绪探测留给 T-005。
- 不改变开放接口契约和错误码。

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

- 2026-10-15 · Codex · 已预登记两个共享文件：基础设施注册入口只补配置校验，任务索引只追加新行；实现和验证完成后更新结果。
