# T-006 开放接口、接入应用、回调和钩子

状态：进行中
负责人：Codex 2026-10-20
里程碑：M4
依赖：T-004、T-005
设计书：§integration、§api

## 目标

业务系统能发起（传数据或只传单号）、查询、更新数据、撤回、重新提交、催办、终止；拿待办数量和列表、做审批操作；回调和钩子经出站适配层可靠送达。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Contracts/Protocol/`：开放接口的请求和响应、新错误码
- `backend/src/ApprovalService.Api/Endpoints/OpenApi/`；`Application/Integration/`（接入应用、API Key 只存 SHA-256、流程授权）
- `backend/src/ApprovalService.Infrastructure/Integration/`：出站适配层（HMAC 签名、5 秒超时、按 1 分钟 / 5 分钟 / 30 分钟 / 2 小时重试、发送日志）、钩子调用（取数、校验、取人、业务动作）
- `backend/src/ApprovalService.Domain/Integration/`：接入应用、流程授权、加密密文与出站发送记录实体和仓储接口
- `backend/src/ApprovalService.Domain/Runtime/IApprovalRuntimeRepository.cs`、`backend/src/ApprovalService.Infrastructure/Persistence/ApprovalRuntimeRepository.cs`：审批单和任务查询、在途唯一性与数据更新所需仓储能力
- `backend/src/ApprovalService.Domain/Runtime/ActiveInstanceKeyEntity.cs`：跨三种数据库保证在途“流程编码 + 业务单号”唯一的独立键
- `backend/src/ApprovalService.Application/Runtime/`：现有运行应用服务的发起、查询和更新数据接入
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`、`Migrations/`：接入应用实体、索引和出站记录的三种数据库迁移
- `backend/src/ApprovalService.Infrastructure/Options/`：签名密钥加密主密钥等接入配置
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`：新错误码的副本
- 检查程序 `OpenApiChecks*.cs`：用一个假的业务系统后端
- `backend/README.md`、`docs/architecture.md`、`docs/design/approval-service-design.html`：同步开放接口和接入实现
- `docs/adr/0010-outbound-hook-protocol.md`：未定义的四类业务钩子协议提议，确认前不实施

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/contracts.md`：写全请求、响应、回调格式
- `backend/src/ApprovalService.Contracts/**`
- `ApprovalErrorCodes.cs`
- `Program.cs`（Api、检查程序）
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：接入服务登记
- `backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`：新增实体与索引所需模型登记
- `docs/architecture.md`：接入应用持久化和出站可靠发送说明
- `docs/adr/README.md`：登记 T-006 钩子协议提议 ADR

## 验收标准

- [ ] 设计书第 13 节的审批服务接口全部可用（嵌入令牌、附件、打印除外）
- [ ] “流程编码 + 业务单号”在进行中的审批里唯一，重复发起拿到已有的那张
- [ ] 只传单号发起时调取数接口；校验钩子不通过时挡住并返回原因；钩子超时有明确的错误
- [ ] 回调带签名、失败按规则重试、可以手动重发；同一事件重发时 `eventId` 不变
- [ ] `tasks/count` 能只算本接入应用的流程，也能算全部（按接入应用设置）
- [ ] 性能初定：发起、审批 P95 ≤ 300 ms（不含钩子），待办数量 P95 ≤ 50 ms，写进交接记录
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 嵌入令牌和免登录地址（T-007）
- 附件（T-013）
- 打印（T-015）
- SDK 方法（T-016）

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

- 2026-10-20 · Codex：回调和流程目录已能独立运行，后端完整检查 383 项通过。发起接口要求同一业务单号结束后可再次发起，而现有 `Instances(FlowCode,BusinessKey)` 为全生命周期唯一索引；为跨 SQLite、SQL Server、PostgreSQL 保持一致事务语义，登记独立在途键实体 `Domain/Runtime/ActiveInstanceKeyEntity.cs`，在运行仓储的同一次提交中增删，之后才实现重复发起返回已有审批单。
- 2026-10-20 · Codex：恢复后完成流程目录与数据定义接口提交 `a0ca17a`、共享 DI/路由/检查登记 `d11fd63`。设计书 §api 对取数、校验、取人、业务动作钩子只写用途，未定义精确 URL/HTTP 方法和 JSON 请求响应；按 R13 登记提议 ADR，待确认前不实施这四类钩子协议。签名 HTTP 出站适配层和结果回调属于已明确部分，继续开发。
- 2026-10-20 · Codex：按用户要求在可恢复节点暂停，状态保持“进行中”。独立工作树 `C:\Users\dukun\.codex\worktrees\open-api\ApprovalService`、分支 `ai/codex/T-006-open-api`，未合入 main、未推送。已合入已完成的 T-005、T-004、T-010；认领 `dda795f`，范围登记 `f783d68`，接入应用实体/API Key SHA-256/签名密钥 AES 加密及三库迁移 `4a1f76b`，共享 DI `b234d49`，检查入口 `93b0e04`。接入应用后端构建零警告、检查程序 373 项通过；密钥只在应用层签发，尚无未鉴权管理接口。暂停时工作区另有尚未提交的流程目录 GET 接口、服务、仓储、Contracts 模型、场景检查、契约/设计书同步和 DI/Program 登记；产品新文件及契约文档已暂存，共享登记文件仍未暂存，恢复时先看 `git status` 和 `git diff --cached`，核对并提交。流程目录改动后 `dotnet build` 零警告，定向检查的三条新场景通过；尚未跑完整 verify。后续依次完成预览、发起/批量发起、查询/数据更新、撤回/重新提交/催办/终止、待办及审批操作、钩子和回调可靠发送与重发，并补三库迁移、完整场景和文档。T-007 组织身份任务在并行开发，已约定 `IOrganizationLookupAppService` 解析可信人员/部门和组织快照；其负责人可合入独立的接入应用认证提交，T-006 恢复后需合入完成的 T-007，绝不信任请求自造的组织快照。矩阵命中由可信 `IMatrixMatchProvider` 接口提供，持久配置由 T-020 实施；未配置时要明确报可纠正错误。完整交付前需全量 `verify.ps1`，验收未勾选。
- 2026-10-20 · Codex：基于 main 建立独立 T-006 工作树，合入已完成的 T-005、T-004 与 T-010 依赖；共享的检查入口与后端 README 冲突已保留两边内容。接入应用需落库并加密密钥，实例与任务接口需持久化查询和在途唯一性，已与主任务协调并在范围中显式登记 Domain/Integration、Application/Runtime、Runtime 仓储、Persistence 上下文与三库迁移及共享登记文件，之后实施开放接口。
