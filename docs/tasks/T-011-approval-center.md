# T-011 审批中心（电脑网页）

状态：进行中
负责人：Codex 2026-10-23
里程碑：M3
依赖：T-004、T-005、T-007
设计书：§center、§rules

## 目标

审批人登录后在审批中心处理审批：列表、详情、全部审批操作、批量同意、委托设置、签名和常用语；能被业务系统内嵌（免登录）。

## 范围

可以新建、修改的文件：

- 后端审批中心接口（登录用户）和应用服务
- `backend/src/ApprovalService.Domain/Center/`、`Application/Center/`、`Infrastructure/Center/`：中心查询模型与授权仓储
- `backend/src/ApprovalService.Api/Endpoints/Center/`、`Contracts/Protocol/Center*.cs`：JWT 中心路由和向后兼容响应
- `backend/tests/ApprovalService.Backend.Checks/Center*.cs`：真实 SQLite/JWT/HTTP 检查
- `backend/README.md`、`docs/contracts.md`、`docs/architecture.md`、设计书 §center/§api：实际接口行为同步
- `frontend/src/pages/center/`：待我处理、我已处理、抄送我的、我发起的（只看进度）；详情（按排版显示业务数据，或嵌入业务系统的原单；意见栏；时间线加流程图高亮；操作按钮）
- `frontend/src/pages/CenterPage.vue`、`frontend/src/api/center-adapter.ts`、`frontend/src/api/center-session.ts`、`frontend/src/model/center.ts`、`frontend/tests/center-checks.ts`、`frontend/src/styles/center.css`：登录会话、中心接口映射、真实列表与详情、场景检查及页面样式
- `frontend/src/api/center-actions.ts`：拆出中心 JWT 写请求，保持适配器单文件低于 500 行
- `frontend/src/api/center-embed.ts`：内嵌令牌片段消费、登记来源读取及最小完成通知
- `frontend/src/api/center-files.ts`：用当前 JWT 经现有权限下载接口读取附件内容，不向链接或 URL 写入令牌
- `frontend/src/api/center-layout.ts`、`frontend/src/api/center-fields.ts`：裁剪后的排版与字段展示映射，保持中心适配器单文件低于规模上限
- `frontend/src/api/center-settings.ts`、`frontend/src/pages/center/CenterSettings.vue`：按已稳定的 T-014 个人通知偏好接口提供真实 JWT 设置页
- `frontend/src/model/mobile.ts`、`frontend/tests/mobile-checks.ts`：中心共享操作类型增加后，最小同步手机端标签与检查，不扩展手机业务界面
- `frontend/README.md`：记录实际可用的登录和只读中心页面
- `docs/adr/0016-embed-origin-and-token-handshake.md`、`docs/adr/0017-center-jwt-actions.md`：已确认的内嵌来源及人员会话操作协议
- `backend/src/ApprovalService.Domain/Integration/ClientAppEntity.cs`、`backend/src/ApprovalService.Infrastructure/Persistence/ApprovalDbContext.cs`、`backend/src/ApprovalService.Infrastructure/Persistence/Migrations/`：接入应用允许嵌入来源的存储与三库迁移
- `backend/src/ApprovalService.Engine/Commands/TaskActionAvailability.cs`、`backend/src/ApprovalService.Application/Center/`、`backend/src/ApprovalService.Api/Endpoints/Center/`、`backend/src/ApprovalService.Contracts/Protocol/Center*.cs`、`backend/tests/ApprovalService.Backend.Checks/Center*.cs`：纯引擎任务许可、JWT 中心写用例与路由、场景检查
- 内嵌模式：用嵌入令牌打开，审批完用 postMessage 通知外面

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `frontend/src/App.vue`、`frontend/src/model/routes.ts`
- `backend/src/ApprovalService.Api/Program.cs`：登记中心查询和任务操作路由
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：登记中心查询、任务操作用例与仓储
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：登记中心检查
- `backend/src/ApprovalService.Contracts/Protocol/Center*.cs`、`docs/contracts.md`：新增中心查询及任务操作协议
- `docs/architecture.md`：同步查询层安全边界
- `docs/adr/README.md`：登记已采纳的内嵌与中心操作协议

## 验收标准

- [x] 不能发起审批、不能改业务数据（R14）
- [x] 列表能筛选、搜索，显示摘要字段，加急排前、未读加粗；批量同意（流程不允许时不出现）
- [ ] 第 4 节审批人能做的操作在详情页都能做；每个操作做完列表和角标数量刷新
- [ ] 在 iframe 里内嵌能用；只有登记过的网站能嵌入
- [x] `verify.ps1` 通过（当前后端只读切片，见交接记录）
- [x] 相关文档已更新（当前切片的 README、架构、契约、设计书）

## 不做

- 手机网页（T-012）
- 打印（T-015）

## 交接记录

- 2026-10-25 · Codex · 本段预登记常用意见：用户已确认本人 JWT 持久 CRUD、每人至多 30 条、每条至多 500 字，嵌入会话只读，办理仍使用现有任务操作接口。新增中心领域实体/仓储、应用服务、路由与真实场景检查，前端设置页管理并在详情意见栏选择。共享改动包括 Contracts/Protocol/CenterRoutes.cs 与新增 CenterOpinion 模型、Api/Program.cs、Application/Infrastructure 注册、Persistence/ApprovalDbContext.cs 与三库工具生成迁移、检查 Program.cs、docs/contracts.md、docs/architecture.md、设计书 §center/§api；无新增依赖。T-022 正同步生成三库迁移，本分支独立，集成时须在最新基线重新生成。
- 2026-10-24 · Codex · 完成审批中心按人员+审批单的持久已读游标和列表 `isUnread`：列表、详情 GET 均不写游标；桌面详情实际显示后，以本人 JWT 和该次详情时间线最大序号显式 POST 标记，服务端在同一事务重查人员参与、保密及嵌入应用范围，将游标单调推进至不超过数据库当前序号的已显示序号。详情 GET 与标记之间发生的新活动保持未读；本人动作和私人草稿不触发本人未读。三种数据库均由 EF 工具生成迁移。真实 SQLite/JWT/HTTP 检查覆盖越权、重复标记、非法序号、竞态、本人/他人新活动与 GET 无写入；前端检查覆盖 JWT 写请求和列表状态映射。全量 `verify.ps1` 通过：后端 745、调用端 50、前端 136 项，生产构建通过，本机发起/审批/待办数量 P95 分别 20.8/17.8/5.1 ms。任务仍进行中；常用意见/签名/委托及原单嵌入安全地址尚无已定协议，未做假实现；外置 Chrome 未连接，未做视觉复核。
- 2026-10-24 · Codex · 本段预登记未读游标：用户已确认按人员与审批单持久已读序号，详情成功显示后显式标记；本人动作和私人草稿不触发未读，新可见活动再次触发。需在中心领域/应用/仓储与前端中实现，并修改共享 `Contracts/Protocol/CenterRoutes.cs`、`CenterResponses.cs`、`Infrastructure/Persistence/ApprovalDbContext.cs`、三库迁移、`docs/contracts.md`、`docs/architecture.md` 和设计书 §center。服务端标记入口必须重查当前人员、保密参与关系及嵌入来源，列表读取不得写入状态；新增真实 JWT/SQLite 场景检查。上述共享文件改动将在单独提交中列明，无新依赖。
- 2026-10-24 · Codex · 完成当前人员可见字段内容搜索：领域层把与详情相同的字段与明细列裁剪供查询复用；仓储先限定人员参与和嵌入应用，再分批按锁定发布版本裁剪每张单据，仅用可见字段值与标题/编号/业务单号判断命中，随后计算总数和分页。桌面搜索 300 ms 防抖后带本人 JWT 发服务端请求，非摘要字段也可命中，前端不再按摘要二次排除服务端命中。真实 SQLite/JWT 检查覆盖非摘要字段、嵌入范围、分页和隐藏字段/明细列/未定义键不命中；全量 `verify.ps1` 通过：后端 741、调用端 50、前端 135，生产构建通过，本机发起 P95 8.6 ms。字段搜索目前按已授权单据分批扫描，数据量很大时仍需专门的性能观察。未读语义已由用户确认：按人员+审批单游标，详情成功显示后显式标记，其他人新的可见活动重新未读，本人操作与私人草稿不触发；下一切片实现持久化和列表展示。
- 2026-10-24 · Codex · 本段预登记字段内容搜索：修改中心仓储、应用字段投影和真实 HTTP 检查，以及中心列表前端适配与场景检查；在同一授权及可见字段规则下匹配业务数据，命中总数和分页在字段权限裁剪后计算。需同步共享 `docs/contracts.md`、`docs/architecture.md` 与设计书 §center，修改前已登记。未读加粗尚无已读写入时机、按人/任务/审批单粒度、重新变未读活动范围的约定；建议按（人员，审批单）保存可见活动序号，列表只读，详情成功显示后显式标记，其他人产生的新可见活动使其重新未读，待人确认后实现。
- 2026-10-24 · Codex · 完成本人通知偏好设置页，对接 T-014 已定的八类提醒 GET/PUT：每次进入从服务端读取，使用当前人员 JWT，页面不传人员 ID，邮件与站内开关只在服务端确认后更新；失败保持原勾选且不显示“已保存”，401 退出登录，每日待办汇总仅显示邮件。前端测试使用模拟 HTTP 验证持久值重新读取、失败响应和 JWT；全量 `verify.ps1` 通过：后端 739、调用端 50、前端 134 项，生产构建通过，本机发起 P95 9.5 ms。T-014 后端不在此工作树，须在集成分支做真实接口联机验证。常用意见、个人签名和委托无已定持久协议，本段未伪造保存；审批单读标记、字段内容搜索和原单嵌入仍待完成。外置 Chrome 未连接，未做视觉复核。
- 2026-10-24 · Codex · 本段预登记：T-014 已在稳定集成分支提供 `GET/PUT /api/v1/notifications/preferences` 的本人 JWT 与持久化协议；本 T-011 分支尚未合入 T-014，当前只新增前端 `center-settings.ts`、`CenterSettings.vue`，修改 `CenterPage.vue`、`center.css`、`center-checks.ts` 和 `frontend/README.md`，集成时以 T-014 服务端为准复验。失败时不显示“已保存”，重新进入设置页向服务端读取。常用意见、签名与委托尚无已定接口，不做虚假保存。
- 2026-10-24 · Codex · 完成锁定发布版本的自定义详情排版切片：应用投影只从已授权的字段定义选排版项和明细列宽，整个隐藏分组不返回，部分隐藏分组使用通用标题；可见金额由纯引擎转换人民币大写。前端按分组、列数与跨度绘制并在旧响应缺少排版时回退自动排版，字段展示映射拆入 `center-fields.ts` 以保持文件规模。真实 SQLite/JWT/HTTP 场景检查覆盖旧版本分组、隐藏字段及宽度不泄露；前端检查覆盖已授权映射。全量 `verify.ps1` 通过：后端 739、调用端 50、前端 133 项，生产构建通过；先前并发执行的本机发起 P95 336.6 ms 性能波动在独占复验时为 7.9 ms。原单嵌入安全地址、审批单读标记、字段内容搜索和个人设置仍待完成。外置 Chrome 扩展在本轮环境未连接，未做页面视觉复核。
- 2026-10-24 · Codex · 本段预登记：将发布版本的自定义详情排版按当前登录人员已授权字段裁剪后，通过共享 `Contracts/Protocol/CenterResponses.cs` 向后兼容返回；前端 `center-adapter.ts`、`center-layout.ts`、`center-fields.ts`、`model/center.ts`、`CenterDetailView.vue` 和样式据此排版，抽出字段展示逻辑以满足单文件规模上限。同步共享 `docs/contracts.md`、`docs/architecture.md`，以及前后端 README 与设计书 §center；在 CenterQueryChecks 与 center-checks 增加隐藏字段、分组顺序和列宽检查。不改业务系统嵌入原单模式，它仍需独立安全地址协议。
- 2026-10-24 · Codex · 接手续写已暂存的附件展示切片，修正增加明细附件后摘要展示列检查的预期。中心详情只从已授权字段取附件 ID，逐项核对文件仍关联当前审批单；前端以人员 JWT 经文件权限接口读取，图片/PDF 可预览，其他文件下载，令牌不放 URL。全量 `verify.ps1` 通过：后端 738、调用端 50、前端 131 项，构建通过。任务仍进行中；未完成读标记、字段内容搜索、完整自定义详情排版及个人设置，未合并主分支或推送。
- 2026-10-23 · Codex · 下一段范围登记：仅复用 T-013 已公开的 IStoredFileRepository 与 GET /api/v1/files/{id}，不修改其在认领文件。中心详情从已授权且已裁剪的附件字段提取文件 ID，再核对元数据仍属于本审批单，向后兼容新增附件元数据；前端新增 center-files.ts，用当前 JWT 下载及预览图片/PDF，删除附件占位提示。同步 Contracts、contracts.md、前后端 README、设计书 §center 和中心场景检查。尚不处理附件上传或审批人字段附件写入。
- 2026-10-23 · Codex · 完成已授权字段展示切片：中心列表新增 `summaryFieldDefinitions`、详情新增 `fieldDefinitions`，只从审批单锁定的发布版本及当前人员已裁剪的数据生成名称、种类和明细子列；即使流程新版本更名、当前查看者为历史参与人，隐藏字段名称不泄露。前端摘要显示发布名称，详情将明细数据绘成只读表格，不把明细对象直接渲染为 JSON；缺失展示定义时仍安全回退到已有可见字段编码。真实 SQLite/JWT 检查验证锁定版本、顶层及明细列隐藏，前端检查验证映射与表格数据。contracts、设计书 §center、前后端 README 同步。全量 `verify.ps1` 通过：后端 737、调用端 50、前端 130 项，构建零警告。完整自定义详情排版、附件、已读和个人设置仍未接入；没有新增依赖。
- 2026-10-23 · Codex · 下一段共享文件登记：为发布版本中的已授权字段增加安全展示元数据，向后兼容地扩展 Contracts/Protocol/CenterResponses.cs，投影由 CenterProjection.cs 生成；同步 docs/contracts.md、设计书 §center/§api，前端 model/center.ts、center-adapter.ts 与 CenterDetailView.vue 接入字段名称及结构化明细，并在 CenterQueryChecks.cs/center-checks.ts 验证隐藏字段名称不泄露。T-052 的内嵌共享文档修改与本段按不同小节集成，暂不改其 HTML/CSP 文件。
- 2026-10-23 · Codex · 完成 T-011 前端内嵌会话切片：合法中心地址中的 `embedToken` 在网络请求前从当前地址及历史条目同步清除，重复或无效令牌也先清除；兑换结果必须绑定清理后的目标路由，随后用嵌入 JWT 读取 `/api/v1/center/embed-context`。任务操作和逐项批量成功后，用服务端返回的审批单与任务 ID，仅向 referrer 精确匹配当前接入应用登记来源的父窗口发送 `{instanceId,taskId,status:"changed"}`，不传业务数据或令牌；普通登录、无 referrer 与未登记来源不发送。对应前端场景检查及 README 已同步；全量 `verify.ps1` 通过：后端 735、调用端 50、前端 129 项，前端类型检查与打包成功。T-052 独立分支负责后端 HTML 入口、CSP、同源托管与上述 embed-context 接口，本分支未实现或验收最终 iframe；字段标签、附件、已读、设置仍待续做。无新依赖、未用真实账号或密钥测试。
- 2026-10-23 · Codex · 本轮追加范围登记：新增 frontend/src/api/center-embed.ts，改 CenterPage.vue/center-actions.ts/center-adapter.ts 和 center-checks.ts，完成 URL 片段中的一次性令牌即时清除、兑换、嵌入上下文来源核对、成功办理后的最小 postMessage；T-052 独立负责服务端 HTML 入口、CSP 与同源托管，双方约定 GET /api/v1/center/embed-context 仅嵌入 JWT 可读。无新增依赖。
- 2026-10-23 · Codex · 本轮共享文件登记补充：Api/Program.cs 登记中心写路由、ApplicationServiceCollectionExtensions.cs 登记写用例、Contracts 的 Center 路由及响应添加向后兼容字段/模型，docs/contracts.md 与设计书 §center/§api 同步；前端 App.vue 不再改，CenterPage.vue 连接真实写适配。中心人员任务操作先按认证 JWT、嵌入应用、任务当前许可三重核对，再查可信组织及当前节点可编辑字段、执行同意前业务钩子，最终调用统一引擎；批量按任务独立返回结果。真实 SQLite/JWT/HTTP 检查覆盖评论成功、批量一成一败、无关人员/跨应用/未许可动作拒绝，前端检查覆盖只映射服务端许可和 JWT 写请求。当前仍未实现安全 iframe 最终 HTML 响应、嵌入令牌片段兑换、附件与字段标签/排版、读标记及个人设置；其中 iframe 的最终文档由独立 Vite 静态前端而非后端提供，仅 API 响应设置 CSP 无效，必须明确生产静态托管与开发服务如何对最终 HTML 施加每应用动态 frame-ancestors，不能假称仅靠 API CSP 已满足。新命令没有新增第三方依赖。
- 2026-10-23 · Codex · 中心写接口使用的操作类型扩至办理完成、确认已阅、回复征询和暂存意见；T-012 尚待认领，已与总任务负责人确认可在此仅最小更新手机共享标签和对应检查，避免跨端类型检查失败，不扩展手机 UI。
- 2026-10-23 · Codex · 用户已明确确认两项后续协议，解除此前 R13 阻塞：每应用登记允许嵌入来源，服务端设置 frame-ancestors；一次性令牌放 URL # 片段、兑换后立即清除、完成消息只发登记来源；详情由服务端返回当前人可执行操作，JWT 写入口调用统一引擎，批量逐项返回结果，前端只按许可显示。新增 ADR 0016/0017 已批准的落地范围，并与 T-020 负责人约定：本任务只改 ClientAppEntity 的 AllowedEmbedOriginsJson、EF 配置与迁移和中心页面安全使用，T-020 负责管理配置 API/UI；不碰其 IClientAppRepository / ClientAppRepository。
- 2026-10-23 · Codex · 桌面正式中心已接真实本地工号密码登录、首次改密、15 分钟访问令牌到期前轮换、JWT 四列表与详情；页面内存保存令牌，刷新页后需重新登录，不向匿名中心端点取假数据。摘要契约向后兼容新增发起人名称、来源应用名称和当前人工环节，后端在已授权记录投影中从可信主数据与发布图生成；前端协议副本、契约文档和设计书 §center 同步。测试覆盖匿名拒绝、人员令牌请求、真实显示字段与无操作许可禁用；全量 `verify.ps1` 通过：后端 729、调用端 50、前端 122 项，构建零警告。未连接真实 SQL Server/PostgreSQL，未使用真实账号或密码回放测试。剩余：读标记、真实详情排版/附件、审批动作与批量、委托/签名/常用语、嵌入兑换入口与 iframe 限制。阻塞原因（R13）：设计只说“登记过的网站能嵌入”，当前接入应用模型没有允许来源字段，也未规定令牌如何进入浏览器、发送 postMessage 的可信目标来源；写操作的逐单可用权限也未定义中心响应协议。建议先写 ADR：应用登记允许来源列表，服务端按当前应用签发 `frame-ancestors`，令牌放 URL 片段并兑换后立即清除，完成后仅向登记来源 postMessage；中心写入口从已验证 JWT 及应用范围调用统一引擎并返回逐项许可，前端不推断。请确认协议后继续，不应放宽到任意 iframe 来源或仅凭前端判断操作权限。此分支未合并 main、未推送。
- 2026-10-23 · Codex · 继续前端接线前登记必要范围：中心页面入口、API 适配与会话、只读展示模型、场景检查和样式均属于本任务目标；本轮先接本地登录与真实 JWT 列表/详情。后端只读契约尚无每条任务的操作许可、附件元数据与字段展示标签，不会从前端猜测许可或伪造附件；审批写操作待后续稳定接口实现。此轮不需要新增依赖。
- 2026-10-23 · Codex · 完成首个后端只读切片：新增 JWT `GET /api/v1/center/instances` 四列表和 `/{id}` 详情，人员只取认证声明，嵌入应用范围与参与关系先在 SQL 过滤，再筛选、计数、去重、排序和分页；详情不存在或不可见统一 404。待办覆盖当前代理人及原审批人，已办/抄送复用 T-043 可信参与投影，保密查询不给无关管理员或未来/取消任务自动授权。固定版本摘要和详情递归裁剪隐藏字段及明细列，不返回未定义键、运行状态、原始审计字段或私人暂存意见；历史参与者不能借 future queued 节点放宽字段权限。新增 20 项真实 SQLite/JWT/HTTP 及仓储边界检查，最终全量 `verify.ps1` 通过：后端 728、调用端 50（含真实服务端到端）、前端 120 项，构建零警告；三库模型一致性通过，未连接真实 SQL Server/PostgreSQL。同步契约、后端 README、架构及设计书 §center/§api，只描述已实现切片；没有新增依赖，初次工作树还原均用本机 NuGet/npm 缓存。下一步接前端登录/刷新/嵌入兑换与中心 adapter、读标记、字段搜索、排版/流程图、操作及批量审批、个人设置、iframe 允许来源和 postMessage；额外查看人/管理查询未在本入口开放。没有新增 SDK 或前端协议副本，因为中心客户端尚未接线；前端不持有 API Key。任务保持进行中，未合并 main、未推送。
- 2026-10-23 · Codex · 为四列表和保密详情复用持久参与者，合入 T-043 固定已验证提交 `b3e3e8e`。唯一合并冲突为架构文档相邻段落，保留 T-044 已实现取人/自动动作说明，同时保留 T-043 参与者和附件可见性说明；不更改两者实现。当前代理原审批人依据设计书仍可查看和处理 pending 任务，已与 T-043 验证口径统一。
- 2026-10-23 · Codex · 从 main 创建独立工作树和任务分支，认领后合入已验收 T-007 的稳定快照 `537c200`（包含 T-004、T-005），本轮先实现审批中心 JWT 只读四列表与详情。预登记范围：新增 Domain/Application/Infrastructure 的 Center 查询模块、Api/Endpoints/Center、Contracts/Protocol 的中心路由与响应、后端中心场景检查；共享登记涉及 Api/Program.cs、Application 与 Infrastructure 的 ServiceCollectionExtensions、检查 Program.cs、docs/contracts.md、docs/architecture.md；同步 backend/README.md 与设计书 §center/§api。不修改 Runtime/Persistence 实现，与 T-043 协调参与者投影，后续接入稳定版本。前端和审批写操作留待后续切片，不向前端提供 API Key。
