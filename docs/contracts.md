# 契约

开放接口、错误码、HTTP 头、回调和流程定义的约定。权威定义在 `backend/src/ApprovalService.Contracts`，调用端 SDK 有副本，改动流程见 [AGENTS.md](../AGENTS.md) §6.4。设计说明见设计书第 12、13 节。

## 通用约定

- 开放接口都在 `/api/v1` 下。不兼容的改动升版本号（`/api/v2`），老版本保留一段时间。
- 请求和响应都是 JSON：属性名小驼峰；枚举写成小驼峰字符串；值为 null 的属性不输出；时间用带时区的 ISO 8601（如 `2026-10-13T09:30:00+08:00`）；金额用数字，不用字符串。
- 开放接口代表人员时必须使用当前接入应用已绑定的业务用户 ID，不按全局工号回退；工号仅用于本地账号登录或管理员确认人员绑定。
- 每个响应都带 `X-Request-Id` 和 `X-Trace-Id`。

## HTTP 头

| 头 | 方向 | 说明 |
| --- | --- | --- |
| `X-Api-Key` | 请求 | 接入应用的 API Key（管理后台“接入应用”里生成，服务端只存 SHA-256） |
| `X-Request-Id` | 请求、响应 | 调用方生成的请求 ID，原样返回并记进日志；没传或不合规（超过 64 个字符，或含字母数字和 `- _ . :` 以外的字符）时服务生成一个 32 位的 |
| `X-Trace-Id` | 响应 | 本次请求的链路 ID，和错误体里的 `traceId` 相同，反馈问题时提供 |
| `X-Approval-Signature` | 回调与钩子请求、钩子响应 | `sha256=` 加上用接入应用的签名密钥对原始 UTF-8 正文做 HMAC-SHA256 的十六进制小写；钩子双方都验签 |

## 错误格式

所有接口出错都返回：

```json
{
  "code": "VALIDATION_FAILED",
  "message": "审批数据不完整。",
  "errors": [ { "field": "data.items[0].qty", "code": "VALIDATION_FAILED", "message": "数量必须大于 0。" } ],
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

`errors` 只在有逐项错误时出现。

| 错误码 | HTTP 状态 | 说明 |
| --- | --- | --- |
| `UNAUTHORIZED` | 401 | 没带 API Key 或 Key 不对 |
| `FORBIDDEN` | 403 | Key 有效，但不允许做这件事，如用了没授权的流程 |
| `NOT_FOUND` | 404 | 地址不存在，或流程、审批单、任务不存在；方法不对时状态码是 405，错误码也是它 |
| `INVALID_JSON` | 400 | 请求体不是合法的 JSON |
| `VALIDATION_FAILED` | 400 | 数据不符合要求，具体字段在 `errors` 里 |
| `BUSINESS_HOOK_FAILED` | 400 | 业务系统钩子超时、网络或 HTTP 失败、响应签名、请求 ID 或 JSON 格式不正确 |
| `CONFLICT` | 409 | 和当前状态冲突，如审批单已经结束、任务刚被别人处理 |
| `INTERNAL_ERROR` | 500 | 服务内部错误（程序缺陷），带 `traceId` 反馈 |
| `NETWORK_ERROR` | — | SDK 自己的：连不上服务 |
| `TIMEOUT` | — | SDK 自己的：等待响应超时 |

流程、审批操作、接入相关的错误码在对应任务里追加，追加时同步 SDK 的 `ApprovalErrorCodes.cs` 和本表（后端检查程序会核对）。

## 现有接口

| 接口 | 鉴权 | 说明 |
| --- | --- | --- |
| `GET /health/live` | 无 | 存活检查，返回 `live` |
| `GET /health/ready` | 无 | 数据库可连接时返回 `ready`，不可连接时返回 503 |
| `GET /api/v1/info` | 无 | 服务信息 |
| `GET /api/v1/center/instances`、`/center/instances/{id}` | 本地或嵌入 JWT | 当前人员的四类列表和已授权详情；不接受 API Key 代替人员登录 |
| `POST /api/v1/center/tasks/{id}/actions`、`/center/tasks/batch-approve` | 本地或嵌入 JWT | 当前人员任务操作、逐项独立的批量同意；同样不接受 API Key 代替人员登录 |
| `POST /api/v1/identity/login`、`/identity/refresh`、`/embed-tokens/exchange` | 无 | 工号密码登录、刷新令牌轮换、一次性嵌入令牌交换 |
| `POST /api/v1/identity/change-password` | 本地 JWT | 修改自己的密码；首次登录时只允许调用此接口 |
| `POST /api/v1/identity/provision`、`/identity/revoke-all` | 本地 JWT + 当前组织管理员角色 | 为在职人员开通账号、作废人员全部令牌 |
| `POST /api/v1/embed-tokens` | `X-Api-Key` | 为已绑定的在职人员签发一次性嵌入令牌 |
| `POST /api/v1/org/changes`、`/org/bindings` | `X-Api-Key` | 推送组织变更、绑定本应用的外部人员或部门 ID |
| `POST /api/v1/admin/org/import`、`/admin/org/bindings`、`GET /api/v1/admin/org/template` | 本地 JWT + 当前组织管理员角色 | 预检查/导入组织、手工绑定、下载模板 |

`GET /api/v1/info` 的响应：

```json
{ "name": "ApprovalService", "version": "0.1.0", "apiVersion": "v1" }
```

`GET /api/v1/flows` 和 `GET /api/v1/flows/{code}/schema` 需要 `X-Api-Key`。列表只包含当前已发布、接入应用已获授权且流程定义允许该应用使用的版本；无权限读取单个流程时返回 `FORBIDDEN`。列表项为 `{ "code": "PURCHASE", "name": "采购申请", "version": 3 }`。数据定义接口返回 `{ "flowCode": "PURCHASE", "version": 3, "data": { "fields": [], "summaryFields": [] }, "example": {} }`；`example` 按当前定义生成，不含计算字段和审批人填写字段。

`GET /api/v1/instances/{id}` 与 `GET /api/v1/instances?flowCode=PURCHASE&businessKey=PR001` 也需要 `X-Api-Key`，只能读取由该接入应用发起的审批单；前者返回一张审批单，后者按发起时间倒序返回同业务单号的历史数组。审批单响应包含 `id`、`serialNo`、`flowCode`、`businessKey`、`title`、`initiatorId`、`initiatorDepartmentId`、`submittedById`、`clientAppCode`、`status`、`data`、`startedAt`、`endedAt`、`tasks`、`events`。详情中的任务包含 `id`、`nodeId`、`assigneeId`、`status`、`opinion`、`dueAt`、`completedAt`；操作记录包含 `eventId`、`sequence`、`event`、`detail`、`occurredAt`。按业务单号返回的历史摘要把 `tasks`、`events` 留空。缺少任一查询参数返回 `VALIDATION_FAILED`；用其他应用的 Key 查详情返回 `FORBIDDEN`，历史查询不会列出其他应用的记录。

在途审批仍按全局的 `(flowCode, businessKey)` 唯一。相同单号已由其他接入应用占用时，发起用例只能返回 `CONFLICT`，不能把对方的审批单当作幂等结果；本应用同号才可复用本应用现有在途审批单。

`GET /api/v1/tasks/count?user=alice` 和 `GET /api/v1/tasks?user=alice&skip=0&take=20` 需要 `X-Api-Key`，`user` 必须是当前接入应用已绑定的在职业务人员 ID，不按全局工号回退。默认只统计或列出本应用的待办；`scope=all` 要求该接入应用已获跨应用待办权限，否则返回 `FORBIDDEN`。数量响应为 `{pending, urgent, overdue}`；列表为任务数组，每项有 `id, instanceId, flowCode, businessKey, title, nodeId, isUrgent, dueAt, summary, openPath`。摘要仅取锁定流程版本指定的字段；`skip` 默认 0，`take` 默认 20、上限 100。

待办摘要还必须按锁定发布图中该待办的当前节点权限过滤：`fieldPermissions` 显式为 `hidden` 的字段不返回；未配置节点权限的字段沿用默认可见语义。同一节点重复配置同一字段权限时拒绝发布，查询旧异常图也拒绝投影，避免权限冲突导致泄露。

`POST /api/v1/instances` 需要 `X-Api-Key`，请求为 `{flowCode, businessKey, title, initiator, initiatingDepartment?, submittedBy?, data?, detailUrl?, isUrgent?, selectedAssignees?}`。`initiator`、`submittedBy` 及 `selectedAssignees` 中的人员 ID 必须已在当前应用绑定且在职；`initiatingDepartment` 必须是本应用已绑定部门且发起人属于该部门，不传时使用可信组织主数据中的主部门。流程同时检查应用授权、发起角色与选定发起部门范围；流程允许部门配置只按组织主数据的内部 ID 或编码解析，不接受应用外部部门绑定覆盖。`selectedAssignees` 按允许发起人自选的节点 ID 传人员列表，可用于自选审批人和抄送人。`data` 可直接提供审批数据对象；未传时要求当前应用配置取数地址，并在审批事务前调用取数钩子。服务端按已发布定义校验、重算公式与分支条件，再在写事务前调用已配置的发起校验钩子；拒绝时返回业务原因且不创建审批单。审批人填写值不接受业务系统自造；顶层及明细附件 ID 只能引用本应用上传且尚未关联的文件，文件归属与实例状态在同一事务内确认和保存，任何一项失败都回滚整笔发起。成功返回 200 和该审批单详情，已在途的本应用同号请求返回同一张，跨应用同号只返回 `CONFLICT`。取人与自动动作仍待按 ADR 0010 的节点到达和提交后持久队列接线。审批矩阵命中只从服务端可信提供者读取，未配置时返回可纠正错误。编号默认 `SP-yyyyMM-六位流水号`，部署可配置前缀、日期格式和位数；数据库分配号码后若发起失败允许留空号，流程级编号配置由管理后台接入。

`POST /api/v1/instances/batch` 需要同一 `X-Api-Key`，请求为 `{ "items": [单张发起请求, …] }`，每批 1 至 100 项。服务端按列表顺序逐张独立验证和提交，返回 `{ "results": [{ "index": 0, "instance": 审批单详情或 null, "error": { "code", "message" } 或 null }, …] }`；`index` 从 0 开始。单项业务校验失败只写该项 `error`，其他已成功的单据不回滚；无效的批次外形整体返回 400。每项使用由批次 `X-Request-Id` 和下标确定的独立请求 ID，重复批次中仍在途的同应用单据返回原审批单。程序缺陷不会被当作单项业务错误吞掉。

`POST /api/v1/instances/{id}/withdraw` 与 `POST /api/v1/instances/{id}/remind` 的 JSON 请求体为 `{user}`，`user` 必须是本应用已绑定的在职发起人；目标审批单也必须属于当前应用。前者按锁定流程的撤回规则执行，后者对当前待办催办，同一步每小时最多一次。`POST /api/v1/instances/{id}/terminate` 也要求 `X-Api-Key`，请求为 `{user, reason}`；审批单必须属于当前应用，`user` 必须是本应用已绑定的在职实际操作人，`reason` 为非空终止原因。审批单处于 `running` 或 `awaitingInitiator`（退回待重提、待补充）时均可终止；终止会取消未处理任务、清除暂停入口、释放在途业务单号占用，审计实际操作人和原因，并在同一事务固定 `instance.terminated` 回调；不要求操作人是发起人，也不授予其他管理员干预权限。`POST /api/v1/tasks/{id}/actions` 的请求体为 `{user, action, opinion?, targetUser?, targetNodeId?, relatedId?, mode?, mentionedUsers?, fieldValues?}`；`user`、`targetUser` 和 `mentionedUsers` 都须在本应用既有绑定且在职，目标任务须属于当前应用。`id` 是可能包含斜杠的不透明任务 ID，作为 URL 路径参数时应进行百分号编码。`action` 允许 `approve`、`reject`、`handle`、`acknowledge`、`return`、`transfer`、`addSigner`、`removeSigner`、`revokeApproval`、`comment`、`saveDraft`、`copyTo`、`consult`、`reply`、`requestSupplement`；节点权限、任务处理人、意见必填等仍由统一引擎校验。`fieldValues` 是按字段 key 的 JSON 对象，仅接受已发布定义标记 `approverInput=true` 且当前节点 `fieldPermissions` 显式为 `editable` 的顶层字段；类型沿用审批数据规则，人员和部门 ID 须在本应用既有绑定，附件 ID 暂不接受。明细子字段没有行索引协议，当前明确返回 `VALIDATION_FAILED`；业务原始字段、计算字段和无编辑权限字段不能提交。已核准值只写操作事件 `detail.audit.fieldValues` 及相关 `task.completed`、`instance.approved`、`instance.rejected` 回调的可选 `fieldValues`，原审批数据 `data` 不变，由业务系统决定是否回写业务单据。当前节点显式可写且标记为必填的顶层审批人字段，只在 `approve`、`handle`、`acknowledge` 成功推进时必须提交；拒绝、评论、草稿和协作操作不强制填写，已提交的值仍按字段类型与授权校验。上述三个操作成功返回最新审批单详情。发起时选定部门、审批数据、自选审批/抄送人和业务数据找人标识保存为审批单快照；后续节点到达使用最新组织与审批矩阵重算，符合设计书第 4 节规则。

`reply` 必须带原发问任务 ID 和该任务产生的 `relatedId` 征询 ID，实际受询人可以回复意见，但不能携带 `fieldValues`；不能借另一任务的节点权限写入征询审计；原发问任务一旦完成，即使审批单其他节点仍在运行，也拒绝迟到回复。审批人实际处理任务时的意见进入任务详情的 `opinion` 并在后续状态更新中保留。退回发起人或要求补充操作若提交了合法 `fieldValues`，对应状态回调也会携带独立的填写值快照。

## 组织和身份接口

`POST /api/v1/identity/login` 接受 `{ "employeeNumber": "E001", "password": "..." }`；
`POST /api/v1/identity/refresh` 接受 `{ "refreshToken": "..." }`；
`POST /api/v1/embed-tokens/exchange` 接受 `{ "token": "..." }`。三者成功后都返回
`{personId, accessToken, accessExpiresAt, refreshToken, refreshExpiresAt, mustChangePassword, targetPath}`，普通登录的 `targetPath` 为 null 并按通用约定省略。访问令牌有效 15 分钟，刷新令牌有效 30 天、每次使用后立即轮换；数据库只存刷新令牌摘要。登录失败连续 5 次暂时锁定 15 分钟。管理员开通账号时通过 `POST /api/v1/identity/provision` 传 `{personId, temporaryPassword}`，首次登录必须改密码；`POST /api/v1/identity/change-password` 传 `{currentPassword, newPassword}`；管理员作废人员全部令牌时向 `/identity/revoke-all` 传 `{personId}`。改密和作废会使旧访问及刷新令牌失效。

JWT 由审批服务签发并绑定内部人员 ID，每次受保护请求都核对账号、在职状态和令牌版本。首次登录尚未改密码的访问令牌只能调用 `/identity/change-password`。JWT 内部 `sessionKind` 为 `local` 或 `embed`；嵌入交换和后续刷新保持 `embed` 来源、`clientAppCode` 及 `externalUserId`，只用于审批中心。每次交换、刷新和访问都重新核对接入应用仍启用且原绑定仍指向同一人员；改绑或停用应用立即使旧令牌失效。组织管理授权要求 `sessionKind=local` 且人员当前具有 `organization-admin` 角色；业务系统的外部用户绑定不能提升为管理员。首位管理员在服务器本机交互式运行 `dotnet run --project backend/src/ApprovalService.Api -- bootstrap-admin` 开通；命令在串行化事务内创建或选用在职人员、开通需首次改密的账号并授予组织管理员角色，已有管理员时拒绝再次运行，不提供匿名管理 HTTP 路由。

`POST /api/v1/embed-tokens` 由接入应用后端带 `X-Api-Key` 调用，请求为 `{userId, targetPath}`；`userId` 必须已在该应用自己的绑定表对应在职内部人员，嵌入签发不按全局工号回退，也不能使用别的应用的绑定。`targetPath` 只能是本站相对路由。响应为 `{token, expiresAt, targetPath}`；令牌绑定已认证接入应用和人员，5 分钟有效，只能原子交换一次，数据库只存摘要。嵌入令牌无效或已消费时返回 `UNAUTHORIZED`。

`POST /api/v1/org/changes` 的 JSON 请求有 `departments`、`people`、`memberships` 三个数组；部门含 `id, code, name, parentId, headPersonId`，人员含 `id, employeeNumber, name, phone, email, managerId, position, grade, gradeRank, isActive`，所属部门含 `personId, departmentId, isPrimary`。响应 `{applied, issues, departedPersonIds}`；`issues` 每项按通用错误字段结构给出路径、码和中文提示。服务一次收集重复工号、缺失上级及组织环等问题；验证通过才写入。接入应用只能覆盖本应用来源的数据，不能覆盖审批服务主数据。人员转离职时在 `departedPersonIds` 返回需交接的内部人员 ID，并持久留下管理员交接提醒。

`POST /api/v1/org/bindings` 请求 `{kind, externalId, internalId, employeeNumber, phone, method, clientAppCode}`：`kind` 为 `person` 或 `department`。开放接口忽略请求里的 `clientAppCode` 和 `method`，始终使用 API Key 所属应用及 `push` 来源；人员不允许直接指定 `internalId`，只能按唯一工号或手机号匹配本接入应用来源且在职的人员，部门也仅能绑定本应用来源。审批服务或其他应用来源的人员和部门须由本地组织管理员在 `/api/v1/admin/org/bindings` 手工确认，管理员在请求体指定 `clientAppCode`，来源固定为 `manual`。人员绑定成功返回 `{personId}`，部门绑定成功返回 204。之后该接入应用可在开放接口中使用自己的人员和部门 ID。

开放发起接口解析业务部门 ID 时必须使用该应用已建立且仍指向现有部门的绑定；未绑定、跨应用绑定或失效绑定返回可纠正的输入错误，不将该值回退为全局部门编码或内部 ID。既有的通用部门查询语义保持不变，严格查询由应用服务 `ResolveBoundDepartmentAsync` 单独提供。

组织管理员以 `multipart/form-data` 的 `file` 字段调用 `POST /api/v1/admin/org/import?preview=true` 预检查，或省略 `preview` 真正导入。仅接受 UTF-8 CSV、XLSX，文件不超过 5 MB；Excel 最多 10,000 行且解压后字段总量不超过 10 MiB 字符。无效 UTF-8 返回 `VALIDATION_FAILED`。预检查不写库，响应仍为 `{applied:false, issues, departedPersonIds:[]}`。`GET /api/v1/admin/org/template?format=xlsx` 下载 Excel 模板，`format=csv` 下载同字段 CSV 模板；模板首列 `kind` 为 `department`、`person`、`membership`、`personBinding` 或 `departmentBinding`。绑定行使用 `clientAppCode`、`externalId`、`internalId` 三列，绑定方式记为 `import`；`clientAppCode` 必须是已登记的接入应用。导入会检查重复部门编码与字段列宽，正式写入在可串行化事务内按当前组织快照重验，绑定和主数据及每项来源、操作者、时间与前后快照一起提交。


## 审批中心人员查询与任务操作

中心接口使用 `Authorization: Bearer <访问令牌>`；人员 ID 和嵌入来源应用只从已验证 JWT 读取，不提供 `user`、`personId`、`scope=all` 或 `clientAppCode` 切换身份的能力。只有 API Key、未改初始密码、账号或应用代际失效的请求均返回 `UNAUTHORIZED`。响应设置 `Cache-Control: no-store`。

`GET /api/v1/center/instances` 参数：`section=todo|done|cc|started`（默认 `todo`）、可选 `flowCode`、`status`、`search`、`from`（发起时间含起点）、`to`（不含终点）、`skip=0`、`take=20`（1～100）。搜索当前支持标题、审批编号和业务单号，最长 200 字；不搜索原始业务 JSON，避免隐藏字段通过是否命中搜索泄露。时间范围必须递增。返回 `{total, skip, take, items}`，总数、筛选、去重和分页均在人员及应用授权之后计算；加急优先，其后按发起时间倒序和 ID 稳定排序。同一单据有多项本人任务也只列一次。

- `todo`：当前 `pending` 任务的处理人或被代理的原审批人。
- `done`：可信持久参与者投影中的实际处理人，包括完成、转交、退回和要求补充；不会把代理前原审批人误当作实际处理人。
- `cc`：普通抄送节点实际送达或人工抄送的收件人；未到达的将来抄送不授予可见性。
- `started`：发起人或实际代发人，只供看进度。

摘要字段为 `id, serialNo, flowCode, flowName, sourceName, businessKey, title, status, clientAppCode, initiatorId, initiatorName, currentStep?, submittedById?, isUrgent, startedAt, endedAt?, summary, pendingTaskIds, canBulkApprove, bulkApprovableTaskIds`。显示名称来自可信人员与接入应用主数据；`currentStep` 为当前人工节点名称，多条并行待办时显示并行环节数量，无人工待办时省略。`pendingTaskIds` 仅说明当前任务关系；`bulkApprovableTaskIds` 只列当前人员可批量同意的实际任务 ID。全部操作提交时仍按最新状态和节点规则重验。`summary` 使用固定发布版本的数据定义及节点隐藏权限裁剪，包含明细列；未定义字段不透传。

`GET /api/v1/center/instances/{id}` 返回 `{instance, data, tasks, events, activeNodeIds}`。其中 `instance` 使用同一摘要结构；`data` 只包含已发布定义中的可见字段；`tasks` 为 `{id, nodeId, nodeName, assigneeId, delegatedForId?, status, opinion?, dueAt?, completedAt?, allowedActions}`，`allowedActions` 从固定发布图与当前运行状态按认证人员计算；`events` 为 `{eventId, sequence, kind, nodeId?, actorId?, onBehalfOfId?, opinion?, occurredAt}`；`activeNodeIds` 只提供当前令牌节点，用于高亮。原始运行状态、审计 `fieldValues`、人员解析快照和私人暂存意见不进入响应。该切片尚未提供图布局、详情排版和读标记协议。

`POST /api/v1/center/tasks/{id}/actions` 请求 `{action, opinion?, targetPersonId?, targetNodeId?, relatedId?, mode?, mentionedPersonIds?, fieldValues?}`，任务 ID 含斜杠时需将整个 ID 编码；人员参数使用审批服务内部 ID。响应 `{taskId, instanceId, detail}`，详情再次执行人员与应用授权。`action` 只允许人工任务命令，当前人、节点许可、意见及字段规则由服务端重新校验，变更经过统一引擎写入事件与任务状态。`fieldValues` 只接受当前节点显式可编辑的顶层审批人字段；人员、部门值须为现存内部 ID，必填仅在成功推进时强制；明细和附件字段目前没有安全提交协议，明确拒绝。节点同意前的业务校验钩子仍会运行，中心登录人员作为审批服务内部人员 ID 传给钩子的 `actor`。无权或失效任务不会因列表先前显示过按钮而获准。

`POST /api/v1/center/tasks/batch-approve` 请求 `{taskIds:[...], opinion?}`，1～100 项且 ID 不能重复，只接受明确拥有 `bulkApprove` 许可的同意任务。每项独立调用统一引擎，响应 `{items:[{taskId, succeeded, instanceId?, code?, message?}]}` 与输入同序；业务校验失败不回滚已成功项。两种写接口都只从已验证 JWT 获取操作人和嵌入应用范围，接收 `X-Request-Id` 作为幂等 ID；批量为各项追加稳定序号。浏览器从新服务响应更新许可，不根据旧按钮状态推断权限。

可见详情限发起人、实际代发人、当前本人或代理关系中的待办，以及持久化的历史实际处理人/已送达抄送人。保密流程不因管理员身份或仅有未到达、已取消任务自动开放；本切片不开放额外查看人或管理查询。嵌入会话的应用条件与人员条件共同下推到数据库查询，跨应用详情和其他不可见或不存在的单据统一返回 `NOT_FOUND`。列表与详情分别在一个一致读事务中读取计数、版本、任务和事件。

没有显式节点上下文的详情，按该查看人当前 pending 任务节点的隐藏字段并集裁剪；无当前待办的历史/抄送参与者采用发布图全部节点的隐藏集合，隐藏优先。发起人与实际代发人可以读取其提交的已定义快照字段。此保守读取策略不增加业务数据修改能力；桌面前端已以本地登录 JWT 接入四列表、详情、任务操作与逐项批量同意，`frontend/src/api/center-adapter.ts` 是中心响应副本。字段搜索和未读仍由后续中心切片实现。本切片不供业务 SDK 调用。

## 规划中的接口

按设计书第 13 节，在对应任务里实现，实现时把请求和响应的格式补进本文：

| 接口 | 用途 | 任务 |
| --- | --- | --- |
| `POST /api/v1/flows/{code}/preview` | 预览会走哪些节点 | T-006 |
| `POST /api/v1/instances/batch` | 批量发起审批 | T-006 |
| `PATCH /api/v1/instances/{id}/data` | 审批中更新允许更新的字段 | T-006 |
| `POST /api/v1/instances/{id}/withdraw`、`/resubmit`、`/remind`、`/terminate` | 撤回、重新提交、催办、业务单据作废时终止 | T-006 |
| `GET /api/v1/instances/{id}/print` | 审批单 PDF | T-015 |
| `POST /api/v1/tasks/{id}/actions` | 审批操作 | T-006 |
| `POST /api/v1/files`、`GET /api/v1/files/{id}` | 上传、下载附件 | T-013 |

## 附件接口

`POST /api/v1/files?fileName=票据.png` 的请求体是文件原始字节，不是 JSON 或 multipart；返回 JSON：`{ "id": "...", "fileName": "票据.png", "size": 123, "contentType": "image/png", "sha256": "..." }`。服务按内容签名核对扩展名，默认单文件上限 20 MB。当前接受 PDF、PNG、JPEG、GIF、WebP，以及具有有效包清单、入口关系和主部件 XML 的 DOCX、XLSX、PPTX；Office 文件只提供下载，不在线预览。其他类型返回 `VALIDATION_FAILED`。上传支持接入应用 API Key 或可信人员会话；应用上传的文件 ID 可在发起审批的顶层或明细附件字段中引用。同一文件在请求内去重，仅当前应用上传、尚未关联且未被清理占用的文件可以关联；审批单和所有附件在同一事务提交，任一文件不符则整笔失败。审批意见中的附件字段暂不接受。

`GET /api/v1/files/{id}` 先校验可信身份及审批单可见性。本地会话按人员权限访问；嵌入会话还必须带已验证的接入应用编码，且只能下载同一应用审批单的已关联附件。未关联附件在元数据中记录上传主体：人员上传的仅本人本地会话可读，应用上传的仅同一 API Key 已认证应用可读；嵌入人员会话不能读取未关联附件。已关联附件不能仅凭 API Key 下载。上传 24 小时后若仍未关联则自动清理。本地文件与浏览器不可直连的 MinIO 由服务转发，浏览器可直连的 MinIO 返回最长五分钟有效的预签名地址的 302 跳转。应用编码从已验证 API Key 得出，不接受请求自报；客户端参数中的 `userId` 从不用于授权。

普通抄送到达时固定收件人，人工抄送和实际任务操作也在审批事务内写历史参与人投影；收件人不因后续组织调整失去已发生的查看事实。已关联附件允许发起人、实际代发人、当前待办人及已记录的历史参与人访问。保密流程由审批单锁定版本的 `isConfidential` 决定，不向管理员或额外查看人自动开放；排队、取消或转交的任务不能仅凭历史指派取得权限。旧版执行图省略该标记时按非保密处理。嵌入会话仍须属于来源应用。

## 回调和业务系统提供的接口（规划中）

审批服务调业务系统的都经出站适配层，带 `X-Approval-Signature` 签名，默认 5 秒超时，失败按 1 分钟、5 分钟、30 分钟、2 小时……重试（T-006）。回调和钩子地址默认只能是 HTTPS；仅可信内网部署显式设置 `ApprovalService:Integration:AllowHttpUrls=true` 时接受 HTTP。签名只防篡改，不提供传输保密。出站客户端不跟随 HTTP 重定向，避免把审批数据和签名转发至未登记的地址。

- **结果回调（已实现基础发送）**：审批事务把需通知的事件固定成 JSON 快照并写入待发队列；后台任务对接入应用登记的完整回调地址发送 POST，使用 `X-Approval-Signature` 对原始 UTF-8 正文签名，`X-Request-Id` 为稳定 `eventId`。请求体包含 `eventId`、`event`、`instanceId`、`serialNo`、`flowCode`、`businessKey`、`status`、`data`（事件时的审批数据快照）、`occurredAt`；人工操作已填写字段另在可选 `fieldValues` 中，绝不覆盖 `data`。当前映射 `instance.started`、`instance.approved`、`instance.rejected`、`instance.withdrawn`、`instance.terminated`、`instance.cancelled`、`instance.returnedToInitiator`、`instance.supplementRequested`，以及 `task.created`、`task.completed`、`task.cancelled`。后两种实例事件表示已退回发起人或正等待发起人补充，`status` 为 `awaitingInitiator`；退回历史审批节点不使用 `instance.returnedToInitiator`。只接受 2xx；超时为 5 秒，失败按待发队列规则从 1 分钟、5 分钟、30 分钟、2 小时起重试。每次发送留状态和错误类别日志，不保存正文；管理用例可对原事件重新排队，重发仍使用相同 `eventId` 和正文。停用接入应用期间保留待发项且不出站；尚未登记回调地址时不调用业务系统，事件仍在操作记录中。
  接入应用被停用后，已有待发回调暂停发送并记录 `CLIENT_APP_DISABLED`，仍留在队列等待后续重试；重新启用后可送达同一事件。

撤销同意成功后，原审批任务从已处理恢复为待办，服务发兼容新增的 `task.restored` 回调；该回调独有可选 `taskId`，值为恢复的原任务 ID，`status` 为 `running`。待办订阅方收到它时须刷新原任务/待办列表；撤销期间被取消的下一步任务仍按既有 `task.cancelled` 通知。`task.restored` 与其他回调一样在审批事务中固定 `eventId` 和完整正文，失败重试及手动重发不生成新任务 ID 或事件 ID。审批单详情中恢复为 `pending` 的原任务清空 `opinion` 与 `completedAt`，此前的同意意见和撤销动作仍保留在只追加操作记录里。其他回调不新增 `taskId` 字段。
- **取数接口**：只传单号发起时，审批服务按单号回业务系统取审批数据。
- **校验钩子**：发起前、某个节点同意前问业务系统能不能过，不能过时返回原因。
- **取人接口**：审批人来源选“业务系统接口”时返回用户 ID 列表。
- **业务动作接口**：自动节点调用，如审批通过后生成采购订单。

四类钩子按已采纳的 [ADR 0010](adr/0010-outbound-hook-protocol.md) 使用各应用分别登记的完整 URL、POST 小驼峰 JSON，默认 5 秒超时；可选校验地址未配置时跳过，必需的取数、取人和动作地址未配置时返回可纠正错误或保留待发项。审批服务用 `X-Approval-Signature` 对原始 UTF-8 请求体签名，业务系统须以同一密钥对原始响应字节签名并原样回显 `X-Request-Id`；审批服务在解析 JSON 前恒定时间验签并核对请求 ID。非 2xx、超时、网络故障、无效签名、请求 ID 或 JSON 均返回 `BUSINESS_HOOK_FAILED`；钩子明确拒绝返回 `VALIDATION_FAILED` 和业务原因。取数请求 `{flowCode,businessKey,requestId}`，响应 `{data:{…},title?,detailUrl?}` 且 `data` 必须为对象；校验请求 `{phase:"start"/"approve",flowCode,businessKey,instanceId?,taskId?,actor,data}`，响应 `{allowed,reason?}`，拒绝时 `reason` 必填。取人请求 `{flowCode,businessKey,nodeId,sourceValue?,initiator,data}`、响应 `{userIds:[…]}`；动作请求 `{eventId,actionCode,instanceId,flowCode,businessKey,data}`、响应 `{success,message?,result?}`。取数和校验在审批写事务前同步调用；取人于目标节点前取得；业务动作经提交后持久待发队列执行。取人只在纯引擎实际到达目标节点时调用，返回人员仅按当前接入应用已有绑定解析；失败不落部分审批状态。自动节点在事务内固定 `actionPending` 待发项，提交后由后台调用；业务响应 `success:false` 或网络故障保留事件并退避重试，`success:true` 后用原 `eventId` 幂等提交引擎结果，业务端也须据此去重。

## 流程定义 JSON

流程顶层有三个可省略的流程级策略，`schemaVersion` 仍为 `1`，旧定义与旧执行图不需要迁移：

| JSON 属性 | 值 | 省略时的行为 |
| --- | --- | --- |
| `withdrawBeforeFirstDecision` | `false` / `true` | `false`：发起人可在审批结束前撤回；`true`：仅首位人工处理人操作前可撤回 |
| `returnMode` | `restart` / `returnNode` | `restart`：退回后重新提交从头推进；`returnNode`：回到发起退回的节点 |
| `isConfidential` | `false` / `true` | `false`：普通流程；`true`：附件只向实际参与人开放，旧版本默认为 `false` |

发布编译后的执行图保留这些值。业务系统发起时，应用层须把已发布值固定到审批单操作状态，后续流程版本修改不影响在途单据；保密标记直接读取审批单锁定版本的执行图。开放接口接线由 T-006 完成。

权威模型在 `backend/src/ApprovalService.Contracts/Flows/`，前端镜像在 `frontend/src/model/flow.ts`。当前 `schemaVersion` 为 `1`。流程版本保存一份 `FlowDefinition`，其 `canvas` 是唯一设计方式；发布时编译成独立的 `FlowExecutionGraph`，运行图不含 `x`、`y`。发布后的版本锁定，已运行审批单始终使用发起时的版本。接口开放时仍按上文 `/api/v1` 约定，不在本节预设尚未实现的请求路径。

顶层属性如下，JSON 属性名一律小驼峰；可空属性为 `null` 时按通用约定省略，集合默认为空集合。

| 属性 | 类型 | 含义 |
| --- | --- | --- |
| `schemaVersion` | 整数 | 当前为 `1`；读取方必须按版本解释，不静默猜测未来版本 |
| `code`、`name` | 字符串 | 业务系统使用的流程编码、显示名称 |
| `canvas` | 对象 | `nodes`、`edges` 组成的自由画布 |
| `data` | 对象 | `fields` 与 `summaryFields`，只定义审批数据，不创建业务单据 |
| `detailLayout` | 对象 | 只读详情的自动、拖拽或原单嵌入排版 |
| `printSettings` | 对象 | A4 审批单的打印配置 |
| `allowedClientApps`、`allowedInitiatorDepartments`、`allowedInitiatorRoles` | 字符串数组 | 接入应用与发起人范围限制；空数组表示不额外限制，接入授权仍单独校验 |

`canvas.nodes[]` 每项有 `id`、`type`、`name`、`x`、`y`；位置仅供设计器使用。可选的 `gatewayPairId`、`gatewayDirection`、`assignee`、`approval`、`subprocess`、`automation` 承载语义；`fieldPermissions` 是当前节点的字段权限数组。`canvas.edges[]` 每项有 `id`、`sourceId`、`targetId`、`priority`、`isDefault`，可选 `condition`。条件分支按 `priority` 从小到大尝试，兜底线的 `isDefault` 为 `true` 且不写条件。网关方向为 `split` 或 `join`，分叉与汇合共用 `gatewayPairId`。

| `type` | 用途 |
| --- | --- |
| `start`、`end` | 开始、结束 |
| `approval`、`handling`、`cc` | 审批、办理、抄送 |
| `exclusiveGateway`、`inclusiveGateway`、`parallelGateway` | 排他、包容、并行网关 |
| `subprocess`、`automatic` | 子流程、自动节点 |

条件与计算字段使用受限表达式，不执行任意代码。审批数据字段引用 `${fieldKey}`，明细子字段如 `${items.amount}`；`${initiator.department}`、`${initiator.role}`、`${initiator.level}` 保留给发起人上下文，不是审批数据字段。表达式可使用四则运算、比较、SUM、AVG、MAX、MIN、IF 和日期差等受支持函数；具体求值规则由引擎提供。发布校验解析表达式、检查函数参数数量与字段引用是否存在，并对纯常量表达式求值，以拒绝除零等错误。

表达式的确定语义：数字为十进制数，不隐式转换字符串；字段缺失、类型不符、除零和数字溢出都会返回可纠正的中文错误。支持括号、一元负号与非、`* / + -`、`> >= < <= == !=`、`AND`/`OR`（也可写 `&&`/`||`），优先级按上述顺序递减；布尔运算和 `IF` 未选中分支短路。字段引用先按完整顶层 key 查找，因此 `${采购.金额}` 可以引用名为 `采购.金额` 的顶层字段；没有同名顶层 key 时才按最长匹配的明细表 key 查找子字段。字符串用单引号或双引号包围，日期字段用 `yyyy-MM-dd` 字符串。`SUM`、`AVG`、`MAX`、`MIN` 接受数字或明细列数组；空集合报错。`ANY(集合)` 在集合内有布尔真值时为真，`ANY(集合, 值)` 在集合内有同类型相等值时为真。`DATEDIFF(起始日期, 结束日期)` 返回后者减前者的自然日数；`RMBUPPER(金额)` 返回人民币财务大写。只接受这些函数，不执行脚本或任意方法。表达式最多 2048 字符、32 层括号/函数/一元运算嵌套、512 个语法单元、每次聚合最多 1000 行；平铺的二元运算不占嵌套层数，超过限制时拒绝。

发起时数据快照是 JSON 对象；必传字段和明细子字段按类型检查，必传文本不能只含空白，明细错误路径带零起始行号，例如 `data.items[0].qty`。计算字段和审批人填写字段无需业务系统在发起时提交。`number` 和 `money` 必须是 JSON 数字，日期必须是 `yyyy-MM-dd`，人员和部门是字符串 ID，附件为一个文件 ID 字符串或文件 ID 字符串数组；数组内每个文件 ID 必须为非空白字符串。必传明细表至少一行，单表最多 1000 行。`flows/{code}/schema` 可按同一字段定义生成 JSON 样例，样例不包含计算字段和审批人填写字段。审批编号由服务分配序号后按 `前缀-日期-流水号` 格式生成；日期格式支持 `yyyy`、`yyyyMM`、`yyyyMMdd`，流水号按配置位数补零，发号的并发唯一性由持久化层保证。

`data.fields[]` 的字段包含 `key`、`name`、`type`、`required`、可选 `formula`、`approverInput` 与 `children`。`type` 可为 `text`、`number`、`money`、`date`、`person`、`department`、`attachment`、`detailTable`；明细表的子字段放在 `children`。计算字段仅供审批判断与展示，不回写业务单据；`approverInput` 表示由获授权节点的审批人填写。`summaryFields` 存字段 key，通常选三至五个。节点 `fieldPermissions[]` 的每项包含 `fieldKey` 和 `access`（`hidden`、`readOnly`、`editable`）；明细子字段 key 写成 `tableKey.childKey`。

人工节点的 `assignee` 至少有 `source`；可选 `value`、`fieldKey`、`level` 分别存成员/角色/矩阵/接口等编码、数据字段和上级层级。来源编码为 `member`、`role`、`position`、`grade`、`manager`、`managerChain`、`departmentHead`、`dataDepartmentHead`、`divisionLeader`、`dataPerson`、`matrix`、`businessApi`、`previousSelection`、`initiatorSelection`、`initiator`；岗位和职级在设计书同一类中。可选的 `limitToInitiatingDepartment` 限定角色只在发起部门内找人，`stopAtGrade` 是连续多级上级的截止职级。`approval` 存多人方式（`sequential`、`all`、`any`、`vote`）、票签门槛与权重；会签拒绝处理为 `immediate` 或 `waitAll`，无处理人为 `block`、`approve`、`administrator` 或 `specified`，选 `specified` 时由 `missingAssigneeUserId` 保存目标人员 ID；本人审批为 `skip`、`self` 或 `manager`，重复审批人为 `adjacentOnce`、`once` 或 `everyTime`，离职人员为 `manager` 或 `administrator`。其余还有允许的操作、意见及已阅要求、时限；新增可选字段不改变既有流程定义的默认行为。`subprocess` 存目标 `flowCode`、`waitForCompletion` 与 `dataMappings`；`automation` 存由出站适配层执行的 `actionCode` 和失败后交管理员的设置。

`detailLayout.mode` 为 `auto`、`custom` 或 `embed`。自定义排版的 `groups` 包含标题、列数与字段项；字段项可指定跨列、明细子列宽及金额大写。嵌入原单仅保存 `embedPageCode`，实际只读地址由业务系统提供。`printSettings` 指定 A4 方向、字段顺序、意见、签名、票据页、Logo、二维码与水印；打印用的中文字体由使用单位上传，不放入流程 JSON。

例如一条最小顺序审批的设计片段：

```json
{
  "schemaVersion": 1,
  "code": "SIMPLE_APPROVAL",
  "name": "简单审批",
  "canvas": {
    "nodes": [
      { "id": "start", "type": "start", "name": "开始", "x": 0, "y": 0, "fieldPermissions": [] },
      { "id": "manager", "type": "approval", "name": "主管审批", "x": 200, "y": 0, "assignee": { "source": "manager", "level": 1 }, "fieldPermissions": [] },
      { "id": "end", "type": "end", "name": "结束", "x": 400, "y": 0, "fieldPermissions": [] }
    ],
    "edges": [
      { "id": "e1", "sourceId": "start", "targetId": "manager", "priority": 0, "isDefault": false },
      { "id": "e2", "sourceId": "manager", "targetId": "end", "priority": 0, "isDefault": false }
    ]
  },
  "data": { "fields": [], "summaryFields": [] },
  "detailLayout": { "mode": "auto", "groups": [] },
  "printSettings": { "orientation": "portrait", "fieldKeys": [], "showComments": true, "showSignatures": true, "includeReceiptPages": false, "showLogo": true, "showQrCode": true, "showWatermark": false },
  "allowedClientApps": [],
  "allowedInitiatorDepartments": [],
  "allowedInitiatorRoles": []
}
```

自由画布导出为带本系统扩展信息的 BPMN 2.0 文件，仅导入带兼容本系统扩展格式的文件；其他来源会给出明确错误。扩展中保存完整流程定义 JSON（UTF-8 后再 Base64 编码）、格式版本、生产者标记和 JSON 的 SHA-256 摘要。摘要只用于发现文件损坏或意外改动，未使用密钥，**不提供来源认证或防伪**；导入后的流程仍须经过发布校验与授权检查。完整示例流程存放在 `backend/samples/flows/`。

## 骨架阶段补充的约定

设计书没写明、骨架阶段先定下的，实现对应任务时可以提出修改：

- 方法不对（405）时错误码用 `NOT_FOUND`，不另设错误码。
- 健康检查返回纯文本（`live`、`ready`），不是 JSON。
- 调用方传的 `X-Request-Id` 最长 64 个字符，只允许字母数字和 `- _ . :`。
