# T-040 SDK 真实服务临时种库夹具

状态：进行中
负责人：Codex 2026-10-22
里程碑：M4
依赖：T-038 已验收集成快照；T-037 SDK 首批接口
设计书：§integration、§api

## 目标

让统一验证脚本在隔离的临时 SQLite 库中创建接入应用、已发布流程和本应用人员绑定，用真实 HTTP 检查 SDK 发起、查询与审批的成功链路。

## 范围

可以新建、修改的文件：

- `docs/tasks/T-040-sdk-live-fixture.md`、`docs/tasks/README.md`：任务登记、验收与交接。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs` 及新增 `SdkLiveFixture*.cs`：仅供测试程序调用的种库命令和检查。
- `client/tests/ApprovalService.Client.Checks/LiveServerChecks.cs`：真实 HTTP 成功链路检查。
- `verify.ps1`：为独立临时库运行种库命令，把一次性 Key 只经临时文件与环境变量交给调用端检查并在结束时清理。
- `backend/README.md`、`client/README.md`：说明本地验证与测试夹具边界。

## 共享文件

修改前在此登记，尽量独立提交：

- `docs/tasks/README.md`：只追加 T-040 索引行。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：独立命令分支，不改原检查顺序；与 T-039 合并时保留双方检查登记。
- `verify.ps1`：全量验证启动服务前对同一临时库执行测试种库，保留原清理流程。

## 验收标准

- [ ] 临时库可重复生成应用、发布流程、人员绑定及随机 Key；无生产匿名初始化路由或仓库内固定密钥。
- [ ] SDK 通过真实 HTTP 完成流程/结构读取、发起、实例与历史查询、待办查询和审批操作；无 Key 鉴权检查仍通过。
- [ ] 临时 Key 和 SQLite 库在验证结束后清理，失败时亦清理。
- [ ] `verify.ps1` 全量通过，相关 README 与交接同步。

## 不做

- 不修改生产开放接口、契约或 SDK 公开 API。
- 不把接入应用测试创建入口暴露为 HTTP 路由。
- 不合并主分支，不推送远程。

## 交接记录

- 2026-10-22 · Codex · 从 `main` 创建独立分支并合入已验收 T-038 快照作为开发基线；预登记任务索引、后端检查入口与统一验证脚本的共享文件。T-039 独立分支也可能改后端检查入口，集成时须保留双方登记；本任务不接入产品路由。
