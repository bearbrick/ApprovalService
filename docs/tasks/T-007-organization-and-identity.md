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

- [x] 导入前检查工号重复、上级不存在、上下级绕圈，问题一次列全
- [x] 绑定：按工号或手机号自动匹配、导入、接口推送、手工补；绑定后接口里能用业务系统自己的用户 ID 和部门 ID
- [x] 推送变更含离职：提醒管理员做离职交接，在途待办按“审批人离职”的设置处理
- [x] 可信来源规则：其他来源不覆盖可信来源的数据；组织的每次改动留记录
- [x] 嵌入令牌几分钟有效、只能用一次、绑定用户和接入应用；首次登录要改密码
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 管理后台页面（T-020）
- 接企业微信、钉钉通讯录（只预留）

## 交接记录

- 2026-10-22 · Codex · 本轮完成服务器本机一次性 `bootstrap-admin` 命令（`e224021`、`fcef1c0`、`581988f`）：交互式无回显读取并确认临时密码，先应用数据库迁移，串行化事务中创建或选用在职人员、开通首次需改密账号、授予 `organization-admin` 并记录人员和角色变更；已有管理员时拒绝重复运行，没有匿名 HTTP 入口。真实空 SQLite 库经控制台正向开通成功；真实 SQLite 场景验证重复开通拒绝、整事务写入、已有人员、审计不含明文密码。通用人员解析的全局工号回退已移除（`80817c4`），本应用已绑定人员仍可解析，工号和跨应用冒用被拒绝；ADR 0013 与索引改为已采纳（`c0b25c6`）。最终全量 `verify.ps1` 通过：后端 440、客户端 33、前端 54 项，编译零警告；工作树干净。仍需 T-006 在其接入应用实体增加单调 `AuthGeneration` 并与此分支身份令牌签发/兑换/刷新/校验联动，防止应用停用再启用使旧嵌入会话复活；开放人员 ID 的契约和 SDK 文字也由 T-006 集成同步。本任务保持进行中，不合并 main、不推送。控制台正向验证产生的约 295 KB 临时 SQLite 库位于 `C:\Users\dukun\AppData\Local\Temp\approval-t007-bootstrap-b888325798674568b636784a11a9441d`；本机自动策略拒绝删除命令，未继续尝试清理。
- 2026-10-22 · Codex · 首位管理员命令已通过真实空库迁移与交互式开通；为同步既有文档，登记共享文件 `docs/contracts.md` 的首位管理员运维入口说明，以及 `backend/src/ApprovalService.Api/Program.cs` 的命令行日志过滤和错误用法提示。开放人员标识的契约文字由 T-006 集成处理，不在本次改动中抢改。
- 2026-10-22 · Codex · 已同 T-006 实现者确认当前开放发起、撤回、催办、任务操作及所属应用终止均调用 `ResolveBoundPersonAsync`；本轮在 T-007 内移除旧通用 `ResolvePersonAsync` 的全局工号回退，并补跨应用及工号冒用场景。ADR 0013 按用户确认改为已采纳；共享文件 `docs/adr/README.md` 仅同步该条状态。T-006 负责开放接口契约与 SDK 的语义同步，双方不交叉编辑端点文件。
- 2026-10-22 · Codex · 已获用户确认首次管理员使用服务器本机一次性 CLI。本轮先在 `Infrastructure/Identity` 增加原子初始化，并在 `Api/Hosting` 提供交互式命令；共享文件 `backend/src/ApprovalService.Api/Program.cs` 与 `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs` 只做 CLI 注册，`backend/tests/ApprovalService.Backend.Checks/Program.cs` 只登记新增真实 SQLite 场景。完成后同步 `backend/README.md` 与设计书 §org 的实际用法。此项不接触 T-006 认领的开放接口文件。
- 2026-10-20 · Codex · 用户已确认两项原阻塞方案：空库首位管理员使用服务器本机一次性 CLI，原子创建人员、账号与组织管理员角色，不开放匿名 HTTP；所有代表人员的开放接口必须要求当前接入应用已有绑定，工号只用于管理员确认绑定或应用自有人员自动匹配。按用户“只收尾现有工作”要求，本轮记录决定但暂停新增实现；下次从 CLI 与严格绑定回退清理继续。另需与 T-006 协同处理应用停用再启用后的 AuthGeneration 令牌代际。产品代码未在本条修改，最近最终完整验证为后端 433、客户端 33、前端 54 项。
- 2026-10-20 · Codex · 已修复身份令牌在状态恢复后复活：可信导入/推送人员在职状态转换和外部人员绑定换人，均在同一组织事务单调递增受影响双方已有账号的 `TokenVersion`；一次性嵌入令牌新增签发版本，兑换时核对。三种数据库的 `AddEmbedTokenGeneration` 迁移均由 EF 工具生成，未手改。真实 SQLite 场景覆盖离职→复职、手工绑定及导入绑定甲→乙→甲、旧本地/嵌入访问、刷新与未兑换令牌，以及无账号拒绝签发。完整 `verify.ps1` 在最终补测前通过：后端 432、客户端 33、前端 54 项，编译 0 警告；补测后后端定向检查 433 项与格式检查通过。未改接入应用管理文件；接入应用停用再启用仍可能使旧嵌入访问令牌恢复有效，须由 T-006 加应用代际并在身份校验中核对。首位管理员引导与全局工号回退仍待用户决定，本任务保持阻塞，不合入主分支、不推送。
- 2026-10-20 · Codex · 身份生命周期安全复核：可信人员停用/复职和外部人员改绑若只检查当前状态，旧令牌可在恢复为原状态后复活。本轮将在既有 T-007 范围内用账号单调令牌版本与嵌入令牌签发版本修复，并由工具生成三种数据库迁移；登记共享文件 `docs/architecture.md` 仅同步令牌代际语义。不触碰 T-006 认领的接入应用文件。应用停用再启用的代际缺口交 T-006 协调。任务仍因首次管理员引导与全局工号回退语义待确认而阻塞。
- 2026-10-20 · Codex · T-007 当前已无可独立推进的未完成实现；依 AGENTS.md R13 标记阻塞，等待两项已提交给用户的设计选择：空库首位组织管理员是否用本机一次性 CLI 原子创建人员、账号和角色；开放接口人员标识是否一律要求当前应用绑定、移除旧全局工号回退。已完成内容和全量验证结果见下文；收到答复后恢复本任务并补相应场景、文档及完整验证。
- 2026-10-20 · Codex · 已在本任务分支合入验收完成的 T-028（合入点 `d99481a`，来源 `37aeb34`），无内容冲突；离职变更持久排程后按节点 `InactiveAssigneeBehavior` 处理在途待办，复职会清理旧提醒。全量 `verify.ps1` 通过：后端 427 项、客户端 33 项、前端 54 项，编译零警告；因此勾选离职验收项。任务仍保持“进行中”：空库首位组织管理员安全引导、开放接口全局人员 ID 回退语义尚待用户决定，未擅自改变其行为或宣告整项完成。未合入 main、未推送。
- 2026-10-20 · Codex · 为 T-006 发起接口新增严格部门绑定解析 `ResolveBoundDepartmentAsync(clientAppCode, externalDepartmentId)`，只对该应用现有绑定且仍存在的部门返回内部 ID，不沿用旧 `ResolveDepartmentAsync` 的全局部门编码/ID 回退；旧方法语义未变。补真实 SQLite 场景覆盖正常、未绑定、跨应用、全局编码/ID 和孤儿绑定，契约注释已同步。T-006 应在发起部门及部门找人场景调用严格入口，缺绑定时映射为 `VALIDATION_FAILED`。
- 2026-10-20 · Codex · 核对离职在途待办：本分支仅含 T-005 运行时，`EngineRuntime` 尚无改派命令，`IApprovalRuntimeRepository` 也未提供按处理人枚举在途审批单的接口；当前 `AddDepartureNoticeAsync` 只统计待办并记提醒，不改变审批状态。已完成的 T-004 独立分支有统一入口的 `OperationKinds.Handover`，但它要求管理员明确填写接手人/原因，只按指定人员改派，不能自动依据各在途节点的 `InactiveAssigneeBehavior` 选择直属上级或流程管理员。完整接入至少需先局部合入 T-004，再在运行时/仓储提供按离职人员枚举并逐审批单乐观锁处理的用例，读取锁定流程版本的节点设置及最新组织快照，按节点策略择在职上级/管理员，统一命令产生事件、任务投影与出站项；并处理缺少可用接手人的异常与批量部分失败。这涉及 T-004/T-005 的 Engine/Runtime/Persistence 文件和跨实例事务语义，超出本任务已登记范围，未在 `OrganizationRepository` 直接改写任务表以免绕过事件链。与 T-006 实现者确认其未认领这些运行时文件；后续应由协调者新建/扩展任务并登记文件范围后实现与验收。本任务该验收项仍未勾选。
- 2026-10-20 · Codex · 最终复核发现外部绑定导入可引用未登记的接入应用；现于预览与正式导入的事务内均验证 `clientAppCode` 已登记，错误定位到绑定行并拒绝整批写入，已补真实 SQLite 场景。此改动后重新运行全量 `verify.ps1` 已通过：后端 301 项、调用端 33 项、前端 54 项，编译 0 警告。
- 2026-10-20 · Codex · 全量 `verify.ps1` 已通过：后端 299 项、调用端 33 项、前端 54 项，编译 0 警告。首次尝试恢复三份早期 `AddClientApps` 迁移原始 BOM 后，`dotnet format --verify-no-changes` 明确报 `CHARSET`；因此保留仅移除 BOM 的三处格式修复，不包含业务差异。待验收缺口是首位管理员安全引导方案未获确认，以及离职在途待办仍仅留管理员交接提醒，尚未按节点离职策略实际处理；任务保持“进行中”，不把对应验收项标记完成。
- 2026-10-20 · Codex · 本轮补齐 CSV/XLSX 外部人员/部门绑定行，和部门、人员、所属关系一起在可信导入事务中保存及审计。正式导入与来源推送均在可串行化事务中基于当前数据库快照重验，预览后的组织变化不能绕过环/唯一性校验；找人快照改为一致读。新增重复部门编码、字段列宽预检和无效 UTF-8 CSV 的可修正错误，真实 SQLite 场景共 299 项通过，`dotnet format --verify-no-changes` 通过。设计书 §org、契约、架构、后端 README 已同步。并发场景以两个预览先通过、第一次提交后第二次事务重验被拒来固定旧预检竞态；不同数据库真正的并发锁等待/死锁行为仍待集成环境专项压测。首位管理员 CLI 和开放接口人员标识收紧仍等用户设计答复，故保持“进行中”，相关验收项暂不勾选。嵌入会话的 `clientAppCode` 作用域还应覆盖后续 T-011 查询/详情/操作及 T-013 附件下载，不能仅核对人员 ID。
- 2026-10-20 · Codex · 开放人员标识语义待用户确认：设计允许直传工号，但全局工号回退会使任一 API Key 代表其他应用人员访问待办。已写提议 ADR 0013，确认前不改 `ResolvePersonAsync`；T-006 暂不应以该方法开放任务操作。嵌入签发已先要求本应用既有绑定，API Key 自助绑定只允许本应用来源且在职的人员/部门，跨来源须本地组织管理员确认。后续 T-011 审批中心需按嵌入会话 JWT 内 `clientAppCode` 限定查询、详情和操作到该应用审批单；本地登录可按人员权限查看跨应用待办。
- 2026-10-20 · Codex · ADR 编号与并行任务冲突，已把本任务两篇改为 0011/0012。改号提交 `e3bff18` 误包含此前暂存的 8 个新增 API/Contracts/检查文件，因此该提交未与 `docs/contracts.md`、设计书形成 AGENTS.md §6.4 要求的原子提交；不擅自改写已提交历史，后续将补同步文档并运行完整验证，留给验收者核对最终一致性。
- 2026-10-20 · Codex · 首位管理员引导仍待设计确认：受保护的管理接口不能在空库创建首位人员和账号。建议部署人员在服务器本机运行一次性 CLI，原子创建首位人员（工号、姓名）、本地账号和 `organization-admin` 角色；若人员已由其他受信任途径导入，可指定现有人员 ID。CLI 不提供匿名 HTTP 入口，也不保存明文初始密码。已请协调者向用户确认，确认前暂停此小段，其余明确功能继续。
- 2026-10-20 · Codex · 嵌入令牌签发及组织推送必须通过接入应用 API Key 鉴权，因此经协调将在本任务分支局部合入 T-006 已完成的认证基础提交 `4a1f76b`、`b234d49`、`93b0e04`，不合并主分支、不推送。交叉修改涉及 `Domain/Integration/ClientAppEntity.cs`、`Application/Integration/ClientAppAppService.cs`、`Infrastructure/Integration/ClientAppRepository.cs`、`Infrastructure/Persistence/ApprovalDbContext.cs` 与三套 EF 迁移/快照、`InfrastructureServiceCollectionExtensions.cs`、检查 `Program.cs`；保留组织和接入应用双方模型，迁移由 EF 工具移除并重生。此处先登记后合入，后续开放路由只信任已鉴权的应用编码及组织绑定解析结果。
- 2026-10-20 · Codex · 用户已同意使用本机缓存的 MiniExcel 1.33.0。恢复开发前登记共享 `docs/adr/README.md`，将分别记录 JWT 认证依赖与 Excel 读写库的选型 ADR；仅在 Infrastructure 使用 MiniExcel，主数据校验仍在 Domain。
- 2026-10-20 · Codex · 用户要求暂停节省流量，状态保持“进行中”。工作树 `C:\Users\dukun\.codex\worktrees\organization-identity\ApprovalService`，分支 `ai/codex/T-007-organization-identity`；从 main 认领后已合入完成的 T-005，仅在任务分支内。已提交：`212db63` 组织实体/仓储/应用查询及导入规则、`e5b34f3` 组织 DI、`261d7de` 身份 Application/Infrastructure 核心、`d2ac749`/`cac8226` JWT 包版本和项目引用；前置认领与范围登记见 `2f9eda2`/`8de06ae`。暂停时未提交：组织 CSV 解析器、Domain Identity 仓储接口与账号失败锁定字段、三套 `AddOrganizationIdentity` 迁移及快照、Contracts 的路由和请求响应模型、注释格式修正、IdentityAppServiceChecks.cs、MiniExcel 提案记录（本文件）。三套迁移在账号锁定字段加入前生成，恢复后须用 EF 工具移除并重生，不要手改。身份登录锁定仓储/应用逻辑、首次开通账号用例、组织与身份场景检查、API/认证/DI、契约与设计文档同步、全量 verify 均未完成；当前工作树因并行新增尚未完成的 Identity 接口和检查，不保证可编译。T-006 的 API Key 认证基础提交 `4a1f76b`/`b234d49`/`93b0e04` 尚未合入，嵌入签发接口不得匿名开放。用户已同意使用本机缓存的 MiniExcel 1.33.0，但尚未引入；恢复后按 R8 写 ADR，再实现 Excel 导入。所有已列验收项仍未勾选，未合并 main、未推送。
- 2026-10-20 · Codex · Excel 导入候选包提案（尚未引用或下载）：`MiniExcel` 1.33.0，来源 [NuGet](https://www.nuget.org/packages/MiniExcel/1.33.0) / [项目仓库](https://github.com/mini-software/MiniExcel)，本机 NuGet 缓存已有 373,138 字节 `.nupkg`，Apache-2.0；缓存的包元数据日期为 2024-07，满足发布满两周。本包同时可读取与导出 `.xlsx` 模板，且无 .NET 6 及以上的外部传递依赖。标签服务未使用 Excel 读写库，故无同包版本需对齐。备选 `NPOI` 2.7.6 亦已缓存，但包体约 13 MB、接口面更大；`ExcelDataReader` 3.6.0 只能读，不能导出模板；CSV 无依赖，已先实现解析但不能满足 Excel 验收项。拟在 Infrastructure/Organization 引入 MiniExcel 并写 ADR，先等用户同意，当前未添加依赖。
- 2026-10-20 · Codex · T-007 原范围未列 Contracts、EF 模型和迁移，但新增开放接口及持久化实体必须改这些文件；已获协调者同意在本任务登记后按 AGENTS.md §6.4 实施。计划同步 `docs/contracts.md` 和设计书 §11/§13 中受影响的部分；共享 DI、Program、包版本和检查入口单独提交。JWT Bearer 10.0.12 已在本机缓存，与标签服务一致。Excel 库尚未引入，先做 CSV 导入；引入提案需另行征得用户同意。
- 2026-10-20 · Codex · 从 `main` 创建独立任务工作树并认领。依赖 T-005 已完成；认领提交后将合入 T-005 任务分支，继续组织、账号、嵌入令牌开发。共享文件和新增依赖在修改前逐项登记。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->
