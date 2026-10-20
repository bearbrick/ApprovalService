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
- `backend/src/ApprovalService.Domain/Runtime/InstanceEntity.cs`：保存发起时规范化的自选审批/抄送人与审批数据找人标识；后续节点到达重新读取最新组织与矩阵时仍保留业务选择
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

- 2026-10-20 · Codex：增加无外部钩子的真实 HTTP + SQLite 本机性能基线，预热 4 次、每项测量 20 次并用最近秩法计算 P95；本次发起 22.4 ms、审批 17.7 ms、待办数量 3.0 ms，前一次独立运行分别 52.2/41.3/7.2 ms，均低于 300/300/50 ms 初定目标。该基线包含本机 TestServer 和 SQLite，不代表生产网络、数据库或钩子耗时。回调场景补齐九类外部事件编码、内部事件不外发、开始事件状态/数据快照稳定，以及待发项与正文身份不一致时拒绝出站；原有签名、失败重试、手动重发同正文同事件 ID、发送日志和停用应用隔离场景继续通过。完整 `verify.ps1` 通过：后端 498、调用端 33、前端 65 项，构建零警告。
- 2026-10-20 · Codex：运行时重提/补充可把经应用层校验的审批数据和可信找人映射与状态、任务、事件、回调快照在同一事务提交，非重提命令拒绝替换；组织绑定变更后的数据人员/部门找人标识可重新解析，并保留发起时自选人员。真实 SQLite 场景覆盖无效命令不改数据、退回重提原子保存及本应用人员映射；`verify.ps1 -Part backend` 通过 492 项，零构建警告。这里只是内部基础，开放 PATCH/重新提交路由仍待用户确认字段级更新权限和 ApproverInput 保留规则；未把它们标成完成。
- 2026-10-20 · Codex：开放撤回、催办、人工任务操作已接入统一命令入口；先以 API Key 核对审批单/任务来源应用，再严格解析实际操作人、目标人员及提及人员的本应用绑定。任务 ID 含斜杠，路由尾部捕获并兼容百分号编码；真实 HTTP 检查覆盖跨应用、未绑定工号、错误任务人、一小时催办限制、撤回和同意。发起自选审批/抄送人与审批数据找人标识已入审批单路由快照，后续节点按最新组织和矩阵重算；新增三库 `AddRoutingInputs` 迁移，HTTP 场景覆盖自选抄送跨节点与组织变更后部门负责人变化。完整 `verify.ps1` 通过：后端 489、调用端 33、前端 65 项，构建零警告。尚待实现的数据更新必须与路由映射同事务重算；开放终止管理员语义仍待用户确认。
- 2026-10-20 · Codex：审批操作接入前核对设计书 #rules：审批人到达节点时按最新组织/矩阵求值，但发起自选人员、发起部门和业务数据保持发起快照。因此在本任务范围登记 `InstanceEntity.cs`，计划持久化发起时规范化的路由输入，后续人工命令用最新组织快照重算并复用这些输入，三库迁移由 EF 工具生成。设计 §api 的开放终止与现有引擎“仅管理员、必填原因”权限不一致，已向主任务报告，确认前暂不接该路由；先完成撤回/催办及明确的审批操作。
- 2026-10-20 · Codex：开放发起 POST 已接入已发布流程、可信组织快照、审批数据校验与公式重算、分支条件、自选审批/抄送人员、持久流水号和运行时原子在途键。请求新增本应用严格绑定的选定发起部门与代发人；发起部门核对人员归属，管理员配置的流程允许部门只用可信组织主数据解析，避免应用伪造同名绑定绕过。新增三库 `AddSerialNumberCounters` 迁移、HTTP 场景和契约/设计同步；流程级编号配置仍由 T-020 管理后台接入，当前为部署级默认 `SP-yyyyMM-六位`。取数/校验/取人/动作钩子仍待 ADR 0010 确认，附件引用在同应用归属与原子关联接通前拒绝；矩阵命中依赖可信提供者，未配置即明确报错。完整 `verify.ps1` 已通过：后端 477、调用端 33、前端 65 项。其余更新数据、撤回、重提、催办、终止、审批操作与批量发起继续实现。
- 2026-10-20 · Codex：待办数量和列表 HTTP 路由已接入现有数据库聚合与摘要投影；`user` 仅以本接入应用既有绑定解析，默认只看本应用，获授权才可传 `scope=all`。共享的 Contracts 路径、Api/检查 Program、`docs/contracts.md` 同步登记和更新，真实 HTTP 场景覆盖跨应用、未绑定工号及无效分页。完整 `verify.ps1` 后端 465、调用端 33、前端 65 项通过。发起审批的编号格式遵循设计 §form 的可配置方向；本任务不把示例前缀/日期/位数写死为最终规则。
- 2026-10-20 · Codex：局部合入 T-007 已提交基线 `04045e2`，保留双方的开放路由、身份/组织路由、DI、模型配置及契约说明。三库 `AddOrganizationIdentity` 迁移通过 EF 工具基于合并后模型重新生成；SQLite 从空库迁移和模型快照检查、后端 460 项、调用端 33 项、前端 65 项及完整 `verify.ps1` 均通过。发起和待办人员身份只使用 `ResolveBoundPersonAsync`，不能使用全局工号回退；未获确认的四类钩子协议和附件绑定仍保持隔离，后续继续实现明确的开放接口。
- 2026-10-20 · Codex：为继续发起/待办接口，计划局部合入 T-007 已提交基线 `04045e2`，只使用严格本应用绑定的 `ResolveBoundPersonAsync`，不调用待确认的全局工号回退。合并预计涉及共享 `Program.cs`、应用/基础设施 DI、`ApprovalDbContext.cs`、三库模型快照、`docs/contracts.md`、`docs/architecture.md` 与设计书；冲突只保留两任务的登记和已确认条款。离职在途重分配由独立任务处理，T-006 不修改其 Engine/Runtime/Persistence 文件。
- 2026-10-20 · Codex：共享文件增量登记：`InfrastructureServiceCollectionExtensions.cs` 增加部署级 HTTPS/内网 HTTP 策略并关闭 HTTP 自动重定向，`docs/contracts.md` 和 `docs/architecture.md` 同步全局在途键的跨应用冲突、回调停用与传输边界；这些共享改动单独提交。审批单查询产品与 HTTP 隔离场景已提交 `eba0029`、`f5a65a7`、`00728fe`、`7ca8835`；全量验证后端 392、调用端 33、前端 65 项通过。审阅修复 `3dfcec2` 补了跨应用同号只报 409、HTTPS 默认、停用应用不发送回调、审批详情一致快照；新场景后端 401 项通过，待 T-007 释放 5289 后重跑全量。
- 2026-10-20 · Codex：回调和流程目录已能独立运行，后端完整检查 383 项通过。发起接口要求同一业务单号结束后可再次发起，而现有 `Instances(FlowCode,BusinessKey)` 为全生命周期唯一索引；为跨 SQLite、SQL Server、PostgreSQL 保持一致事务语义，登记独立在途键实体 `Domain/Runtime/ActiveInstanceKeyEntity.cs`，在运行仓储的同一次提交中增删，之后才实现重复发起返回已有审批单。
- 2026-10-20 · Codex：恢复后完成流程目录与数据定义接口提交 `a0ca17a`、共享 DI/路由/检查登记 `d11fd63`。设计书 §api 对取数、校验、取人、业务动作钩子只写用途，未定义精确 URL/HTTP 方法和 JSON 请求响应；按 R13 登记提议 ADR，待确认前不实施这四类钩子协议。签名 HTTP 出站适配层和结果回调属于已明确部分，继续开发。
- 2026-10-20 · Codex：按用户要求在可恢复节点暂停，状态保持“进行中”。独立工作树 `C:\Users\dukun\.codex\worktrees\open-api\ApprovalService`、分支 `ai/codex/T-006-open-api`，未合入 main、未推送。已合入已完成的 T-005、T-004、T-010；认领 `dda795f`，范围登记 `f783d68`，接入应用实体/API Key SHA-256/签名密钥 AES 加密及三库迁移 `4a1f76b`，共享 DI `b234d49`，检查入口 `93b0e04`。接入应用后端构建零警告、检查程序 373 项通过；密钥只在应用层签发，尚无未鉴权管理接口。暂停时工作区另有尚未提交的流程目录 GET 接口、服务、仓储、Contracts 模型、场景检查、契约/设计书同步和 DI/Program 登记；产品新文件及契约文档已暂存，共享登记文件仍未暂存，恢复时先看 `git status` 和 `git diff --cached`，核对并提交。流程目录改动后 `dotnet build` 零警告，定向检查的三条新场景通过；尚未跑完整 verify。后续依次完成预览、发起/批量发起、查询/数据更新、撤回/重新提交/催办/终止、待办及审批操作、钩子和回调可靠发送与重发，并补三库迁移、完整场景和文档。T-007 组织身份任务在并行开发，已约定 `IOrganizationLookupAppService` 解析可信人员/部门和组织快照；其负责人可合入独立的接入应用认证提交，T-006 恢复后需合入完成的 T-007，绝不信任请求自造的组织快照。矩阵命中由可信 `IMatrixMatchProvider` 接口提供，持久配置由 T-020 实施；未配置时要明确报可纠正错误。完整交付前需全量 `verify.ps1`，验收未勾选。
- 2026-10-20 · Codex：基于 main 建立独立 T-006 工作树，合入已完成的 T-005、T-004 与 T-010 依赖；共享的检查入口与后端 README 冲突已保留两边内容。接入应用需落库并加密密钥，实例与任务接口需持久化查询和在途唯一性，已与主任务协调并在范围中显式登记 Domain/Integration、Application/Runtime、Runtime 仓储、Persistence 上下文与三库迁移及共享登记文件，之后实施开放接口。
