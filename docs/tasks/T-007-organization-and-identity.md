# T-007 组织架构、账号与嵌入令牌

状态：进行中
负责人：Codex 2026-10-20
里程碑：M3
依赖：T-005
设计书：§org

## 目标

审批服务自己维护组织架构（Excel 导入、业务系统绑定、推送变更）；工号加密码登录；嵌入令牌换免登录地址；登录层按统一登录预留（ADR 0006）。

## 范围

可以新建、修改的文件：

- `Domain`、`Application`、`Infrastructure` 的 `Organization`、`Identity` 模块；接口层的管理接口和开放接口（`org/changes`、`org/bindings`、`embed-tokens`）
- 给引擎用的组织查询实现（T-003 定义的抽象）
- 登录参照标签服务（JWT，包在本机缓存里），短期访问令牌加刷新令牌，可以作废某人的全部令牌
- Excel 导入要新增读写库：按 R8 写理由、征得同意并写 ADR；同意前可以先支持 CSV
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs` 与三种数据库迁移：新增组织和账号实体的持久化模型
- `backend/src/ApprovalService.Contracts/Protocol/`：新增组织、登录、嵌入令牌接口的向后兼容路由及请求响应契约
- `backend/README.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`：仅同步已实现的接口和安全行为
- `backend/tests/ApprovalService.Backend.Checks/`：组织、登录、令牌和接口场景检查

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/Directory.Packages.props`
- 登记文件
- `docs/contracts.md`
- `backend/src/ApprovalService.Contracts/Protocol/`：开放接口的权威路径与模型，按 AGENTS.md §6.4 同步
- `backend/src/ApprovalService.Api/Program.cs`：登记认证与新接口
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`：登记应用服务
- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：登记仓储及令牌实现
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：登记场景检查
- `backend/Directory.Packages.props`：登记与标签服务一致且本机已缓存的 JWT 包
- `docs/adr/README.md`：登记本任务引入 JWT 与 MiniExcel 的架构决定

## 验收标准

- [ ] 导入前检查工号重复、上级不存在、上下级绕圈，问题一次列全
- [ ] 绑定：按工号或手机号自动匹配、导入、接口推送、手工补；绑定后接口里能用业务系统自己的用户 ID 和部门 ID
- [ ] 推送变更含离职：提醒管理员做离职交接，在途待办按“审批人离职”的设置处理
- [ ] 可信来源规则：其他来源不覆盖可信来源的数据；组织的每次改动留记录
- [ ] 嵌入令牌几分钟有效、只能用一次、绑定用户和接入应用；首次登录要改密码
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 管理后台页面（T-020）
- 接企业微信、钉钉通讯录（只预留）

## 交接记录

- 2026-10-20 · Codex · 用户已同意使用本机缓存的 MiniExcel 1.33.0。恢复开发前登记共享 `docs/adr/README.md`，将分别记录 JWT 认证依赖与 Excel 读写库的选型 ADR；仅在 Infrastructure 使用 MiniExcel，主数据校验仍在 Domain。
- 2026-10-20 · Codex · 用户要求暂停节省流量，状态保持“进行中”。工作树 `C:\Users\dukun\.codex\worktrees\organization-identity\ApprovalService`，分支 `ai/codex/T-007-organization-identity`；从 main 认领后已合入完成的 T-005，仅在任务分支内。已提交：`212db63` 组织实体/仓储/应用查询及导入规则、`e5b34f3` 组织 DI、`261d7de` 身份 Application/Infrastructure 核心、`d2ac749`/`cac8226` JWT 包版本和项目引用；前置认领与范围登记见 `2f9eda2`/`8de06ae`。暂停时未提交：组织 CSV 解析器、Domain Identity 仓储接口与账号失败锁定字段、三套 `AddOrganizationIdentity` 迁移及快照、Contracts 的路由和请求响应模型、注释格式修正、IdentityAppServiceChecks.cs、MiniExcel 提案记录（本文件）。三套迁移在账号锁定字段加入前生成，恢复后须用 EF 工具移除并重生，不要手改。身份登录锁定仓储/应用逻辑、首次开通账号用例、组织与身份场景检查、API/认证/DI、契约与设计文档同步、全量 verify 均未完成；当前工作树因并行新增尚未完成的 Identity 接口和检查，不保证可编译。T-006 的 API Key 认证基础提交 `4a1f76b`/`b234d49`/`93b0e04` 尚未合入，嵌入签发接口不得匿名开放。用户已同意使用本机缓存的 MiniExcel 1.33.0，但尚未引入；恢复后按 R8 写 ADR，再实现 Excel 导入。所有已列验收项仍未勾选，未合并 main、未推送。
- 2026-10-20 · Codex · Excel 导入候选包提案（尚未引用或下载）：`MiniExcel` 1.33.0，来源 [NuGet](https://www.nuget.org/packages/MiniExcel/1.33.0) / [项目仓库](https://github.com/mini-software/MiniExcel)，本机 NuGet 缓存已有 373,138 字节 `.nupkg`，Apache-2.0；缓存的包元数据日期为 2024-07，满足发布满两周。本包同时可读取与导出 `.xlsx` 模板，且无 .NET 6 及以上的外部传递依赖。标签服务未使用 Excel 读写库，故无同包版本需对齐。备选 `NPOI` 2.7.6 亦已缓存，但包体约 13 MB、接口面更大；`ExcelDataReader` 3.6.0 只能读，不能导出模板；CSV 无依赖，已先实现解析但不能满足 Excel 验收项。拟在 Infrastructure/Organization 引入 MiniExcel 并写 ADR，先等用户同意，当前未添加依赖。
- 2026-10-20 · Codex · T-007 原范围未列 Contracts、EF 模型和迁移，但新增开放接口及持久化实体必须改这些文件；已获协调者同意在本任务登记后按 AGENTS.md §6.4 实施。计划同步 `docs/contracts.md` 和设计书 §11/§13 中受影响的部分；共享 DI、Program、包版本和检查入口单独提交。JWT Bearer 10.0.12 已在本机缓存，与标签服务一致。Excel 库尚未引入，先做 CSV 导入；引入提案需另行征得用户同意。
- 2026-10-20 · Codex · 从 `main` 创建独立任务工作树并认领。依赖 T-005 已完成；认领提交后将合入 T-005 任务分支，继续组织、账号、嵌入令牌开发。共享文件和新增依赖在修改前逐项登记。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
