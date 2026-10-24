# T-041 接入应用身份代际与嵌入会话永久失效

状态：待验收
负责人：Codex 2026-10-22
里程碑：M4
依赖：T-006、T-007、T-039、T-042
设计书：§org、§integration、§api

## 目标

接入应用停用后再次启用或轮换密钥时，旧嵌入访问令牌、刷新令牌和未兑换的一次性令牌仍永久失效；新签发的嵌入会话正常使用，本地账号会话不受影响。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Domain/Integration/ClientAppEntity.cs`、`IClientAppRepository.cs`
- `backend/src/ApprovalService.Application/Integration/ClientAppAppService.cs`
- `backend/src/ApprovalService.Infrastructure/Integration/ClientAppRepository.cs`
- `backend/src/ApprovalService.Domain/Identity/IdentityEntities.cs`、`IIdentityRepository.cs`
- `backend/src/ApprovalService.Application/Identity/IIdentityAppService.cs`、`IdentityAppService.cs`、`IIdentityCryptography.cs`
- `backend/src/ApprovalService.Infrastructure/Identity/IdentityRepository.cs`、`JwtIdentityTokenService.cs`
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`、`Migrations/**`，以及 `backend/src/ApprovalService.Infrastructure/Identity/IdentityModelConfiguration.cs`
- `backend/src/ApprovalService.Api/Program.cs`
- `backend/tests/ApprovalService.Backend.Checks/IdentityAppServiceChecks.cs`、`IdentityEndpointChecks.cs`、`OpenApiChecksClientApps.cs`、`ClientAppAuthGenerationChecks.cs`、`Program.cs`
- `backend/README.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`、`docs/tasks/README.md`、本任务文件

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/tasks/README.md`：只追加 T-041 索引行。
- `docs/architecture.md`：补充应用身份代际和令牌验证关系。
- `backend/src/ApprovalService.Api/Program.cs`：JWT 验证时传入应用代际声明。
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：如新增检查类，只追加调用行。
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs` 与 `Migrations/**`：补充应用代际及令牌代际映射，用 EF 工具生成三库迁移；先等 T-042 稳定快照。

## 验收标准

- [x] 启停和密钥轮换原子递增应用身份代际，管理用例可安全调用。
- [x] 签发、兑换、刷新和访问嵌入令牌均检查应用代际，旧令牌或缺失代际均拒绝。
- [x] 停用后再启用、密钥轮换、新令牌可用和本地会话不受影响有场景检查。
- [x] 三种数据库迁移由 EF 工具生成，旧嵌入令牌不能因迁移复活。
- [x] `verify.ps1` 通过（见 AGENTS.md）。
- [x] 相关文档已更新。

## 不做

- 不增设匿名管理 HTTP 接口；受保护管理端路由留给 T-020。
- 不修改 T-006 的审批运行时和业务钩子代码。

## 交接记录

- 2026-10-22 · Codex · 在固定 T-042 快照 `4214c06` 上完成应用身份代际：启停及密钥轮换以单条条件 SQL 更新代际，嵌入签发、兑换、刷新与每次 JWT 访问均核对当前启用应用、人员绑定和代际。三库 `AddClientAppAuthGeneration` 迁移由 EF 工具生成，旧数据库的嵌入刷新/一次性令牌代际为空并拒绝；本地会话维持有效。真实 SQLite 与 HTTP 场景覆盖停用再启用、密钥轮换、旧令牌拒绝、新令牌正常、缺失声明、本地会话。全量 `verify.ps1` 通过：后端 684、客户端 50、前端 119 项，三库模型无待迁移变更，编译零警告；设计书 §org、架构与后端 README 已同步。受保护接入应用管理 HTTP 路由仍留给 T-020；不可直接改数据库的启用标记。用户要求此切片后暂停，不合并 main、不推送。
- 2026-10-22 · Codex · 认领并预登记上述共享文件。先等待 T-042 的稳定整合快照，再合入本任务分支；产品代码尚未编辑。旧数据库中的嵌入令牌代际留空并拒绝，本地刷新令牌继续有效。
