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
| `Content-Security-Policy` | 页面响应 | 普通入口和直接静态路径为 `frame-ancestors 'none'`；应用专属入口为 `frame-ancestors 'self'` 加该应用登记来源 |

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
| `GET /metrics` | 本机或部署级 Bearer 密钥 | Prometheus 0.0.4 聚合文本；只含 HTTP 方法、路由模板、状态码和耗时桶，不含业务标识。远程密钥由 `ApprovalService:Metrics:BearerToken` 配置，至少 32 字符；无权返回 403 |
| `GET /api/v1/info` | 无 | 服务信息 |
| `GET /api/v1/center/instances`、`/center/instances/{id}`，`POST /api/v1/center/instances/{id}/read` | 本地或嵌入 JWT | 当前人员的四类列表、已授权详情和本人已读游标；不接受 API Key 代替人员登录 |
| `GET/POST /api/v1/center/opinions`、`PUT/DELETE /api/v1/center/opinions/{id}` | 读取为人员 JWT；写入仅本地 JWT | 本人常用意见最多 30 条，每条最多 500 字，嵌入会话只读 |
| `POST /api/v1/center/tasks/{id}/actions`、`/center/tasks/batch-approve` | 本地或嵌入 JWT | 当前人员任务操作、逐项独立的批量同意；同样不接受 API Key 代替人员登录 |
| `GET /api/v1/center/embed-context` | 仅嵌入 JWT | 返回已验证会话所属应用和当前允许的消息目标来源 |
| `GET /`、`GET /embed/{clientAppCode}` | 无 | 同源审批中心 HTML；普通入口禁止被嵌入，专属入口按应用登记来源限制 iframe |
| `POST /api/v1/identity/login`、`/identity/refresh`、`/embed-tokens/exchange` | 无 | 工号密码登录、刷新令牌轮换、一次性嵌入令牌交换 |
| `POST /api/v1/identity/change-password` | 本地 JWT | 修改自己的密码；首次登录时只允许调用此接口 |
| `POST /api/v1/identity/provision`、`/identity/revoke-all` | 本地 JWT + 当前组织管理员角色 | 为在职人员开通账号、作废人员全部令牌 |
| `GET /api/v1/notifications/preferences`、`PUT /api/v1/notifications/preferences/{category}` | 人员 JWT | 读取或修改本人的邮件、站内提醒开关 |
| `GET/PUT /api/v1/admin/notifications/daily-digest` | 本地 JWT + 当前组织管理员角色 | 读取或设置每日待办汇总的当地发送时刻 |
| `POST /api/v1/embed-tokens` | `X-Api-Key` | 为已绑定的在职人员签发一次性嵌入令牌 |
| `POST /api/v1/org/changes`、`/org/bindings` | `X-Api-Key` | 推送组织变更、绑定本应用的外部人员或部门 ID |
| `POST /api/v1/admin/org/import`、`/admin/org/bindings`、`GET /api/v1/admin/org/template` | 本地 JWT + 当前组织管理员角色 | 预检查/导入组织、手工绑定、下载模板 |
| `POST /api/v1/admin/actions/retry` | 本地 JWT + 原动作所属流程的当前流程管理员 | 人工重排达到自动上限的原业务动作 |
| `GET /api/v1/admin/session`、`/admin/org/directory`、`/admin/apps` | 本地 JWT + 当前组织管理员角色 | 核验管理会话、读取安全组织目录和接入应用摘要 |
| `POST /api/v1/admin/apps`、`PUT /admin/apps/{code}/enabled`、`POST /admin/apps/{code}/rotate-secrets` | 本地 JWT + 当前组织管理员角色 | 登记接入应用、启停应用或轮换 API Key 与签名密钥 |
| `GET/PUT /api/v1/admin/apps/{code}/embed-origins` | 本地 JWT + 当前组织管理员角色 | 读取或设置应用允许嵌入审批中心的完整网站来源 |
| `GET/PUT /api/v1/admin/apps/{code}/administrators` | 本地 JWT + 当前组织管理员角色 | 读取或替换该应用的内部人员管理员 ID 名单 |
| `POST /api/v1/admin/apps/{code}/test-callback` | 本地 JWT + 当前组织管理员或该应用管理员 | 带原因排入不关联审批单的签名测试回调 |
| `POST /api/v1/admin/flows/simulations`、`GET/DELETE /api/v1/admin/flows/simulations/{id}`、`POST /api/v1/admin/flows/simulations/{id}/steps` | 本地 JWT + 当前流程管理权限 | 完整草稿和假输入创建、读取、逐步推进及删除内存模拟会话 |
| `GET /api/v1/admin/bindings`、`/admin/audit` | 本地 JWT + 当前组织管理员角色 | 读取业务系统绑定及最近 200 条组织/接入应用操作日志的安全摘要 |
| `GET /api/v1/admin/outbox`、`POST /admin/callbacks/{eventId}/retry` | 本地 JWT + 当前组织管理员角色 | 读取最近 200 条待发项元数据；按原事件 ID 带原因手动重发回调 |
| `GET/POST /api/v1/admin/flows`、`GET /api/v1/admin/flows/{code}`、`PUT /api/v1/admin/flows/{code}/draft`、`POST /api/v1/admin/flows/{code}/publish` | 本地 JWT + 当前流程管理范围 | 读取、创建、按修订保存草稿及原子发布新版本 |
| `GET /api/v1/admin/flows/departments` | 本地 JWT + 当前流程管理范围 | 只列出当前管理员可设置为流程直接归属的现存部门 |
| `PUT /api/v1/admin/flows/{code}/enabled` | 本地 JWT + 当前流程管理范围 | 按预期启用状态停用或恢复新审批单发起 |
| `POST /api/v1/admin/flows/import/preview`、`POST /api/v1/admin/flows/import`、`GET /api/v1/admin/flows/{code}/export` | 本地 JWT + 当前流程管理范围 | 预检本服务兼容 BPMN、仅导入草稿及导出指定来源 |
| `GET /api/v1/admin/flows/{code}/versions`、`POST /api/v1/admin/flows/{code}/versions/{version}/restore` | 本地 JWT + 当前流程管理范围 | 读取不可变版本摘要、复制指定旧版为新草稿修订 |
| `GET /api/v1/admin/reports/overview`、`/flows`、`/nodes`、`/people` | 本地 JWT + 当前组织管理员或直接负责部门的负责人 | 查询管理范围内的匿名报表指标；保密流程仅计入匿名数量 |

四条报表 GET 共用查询参数：必填 `fromUtc`、`untilUtc` 是带零时区偏移的 ISO 8601 时间，区间为左闭右开；可选 `flowCode`、`clientAppCode`、`departmentId` 均为单个非空文本。重复参数、非 UTC 时间或空区间返回 `VALIDATION_FAILED`。组织管理员可查全部或指定发起部门；当前在职部门负责人只能查其直接负责的部门，可不传 `departmentId` 合并所有直属部门，不自动包含下级；越权返回 `FORBIDDEN`。嵌入会话和普通人员均返回 `FORBIDDEN`，未登录返回 `UNAUTHORIZED`。部门按审批发起时快照筛选。

`overview` 返回 `{visibleStartedCount, visibleCompletedCount, visiblePendingCount, visibleOverdueCount, confidentialStartedCount, confidentialCompletedCount}`；`flows` 返回 `{items, confidentialStartedCount, confidentialCompletedCount}`，公开流程每项含 `flowCode, startedCount, completedCount, approvedCount, rejectedCount, withdrawnCount, averageDurationMinutes, p90DurationMinutes`；`nodes` 返回 `{items}`，每项含 `flowCode, nodeId, completedCount, averageDurationMinutes, dueCount, overdueCount`；`people` 返回 `{items}`，每项含 `assigneeId, pendingCount, completedCount, averageDurationMinutes, overdueCount`。无耗时样本时平均值、P90 为 `null`，按通用 JSON 约定省略。保密流程只贡献匿名发起与结束量，不出现在流程、节点或人员项中。发起量按 `StartedAt`，结束量与历时按 `EndedAt`，P90 只用已结束审批单；`dueCount` 是区间内到期且查询时已到期任务数，`overdueCount` 为其中逾期任务数；当前待办以查询时刻为准。

  `GET /api/v1/admin/reports/export` 使用上述同一组查询参数、JWT 身份、直属部门范围和保密过滤，返回 `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`，下载名为 `approval-report.xlsx`。工作簿包含“流程汇总”“节点汇总”“人员汇总”“保密匿名总量”四张中文表；前三张只含公开流程的聚合指标，后一张只有匿名发起和结束数量，不包含保密流程、节点、人员或审批单原始字段。三张指标表累计最多 10,000 行，工作簿最多 16 MB，超出返回 `VALIDATION_FAILED`；导出维度标识作为文本写入，防止表格公式执行。逐单审批明细不在本接口导出。

`GET /api/v1/info` 的响应：

```json
{ "name": "ApprovalService", "version": "0.1.0", "apiVersion": "v1" }
```

个人提醒类别为 `newTask`、`urge`、`beforeDue`、`overdue`、`result`、`cc`、`mention`、`dailyDigest`。`GET /api/v1/notifications/preferences` 返回数组，每项为 `{category, emailEnabled, inAppEnabled}`；`PUT /api/v1/notifications/preferences/{category}` 请求 `{emailEnabled, inAppEnabled}`，返回更新后的同形对象。未保存的类别按默认开关显示，`dailyDigest` 仅支持邮件。人员 ID 只取已验证 JWT，不接受请求自报。

`GET/PUT /api/v1/admin/notifications/daily-digest` 的响应为 `{localTime}`；PUT 请求同形，`localTime` 为严格 `HH:mm` 或 `null`（停发）。未配置时不安排每日汇总。组织管理员权限在请求时重新验证，嵌入人员会话不能修改全局时间。

管理后台三个 GET 接口都要求本地密码会话和当前有效的 `organization-admin` 角色；匿名请求返回 401，普通人员或嵌入会话返回 403。`/admin/session` 返回 `{personId}`；`/admin/org/directory` 返回 `{departments, people}`，部门含 `id, code, name, parentName, managerName, source`，人员含 `id, employeeNumber, name, departments, position, isActive`，不包含联系方式和账号凭据；`/admin/apps` 返回应用数组，每项只含 `code, name, allowedFlowCodes, isEnabled`，不返回 API Key 摘要、签名密钥密文或出站地址。管理页使用访问令牌请求这些接口；组织 CSV/XLSX 导入继续使用既有 `/admin/org/import`，预检通过后正式导入时服务端再次校验并留变更记录。

流程管理接口只接受本地登录 JWT。当前在职组织管理员可管全部；有 `flow-admin` 角色的在职人员只能管理自己直接加入的 `ownerDepartmentId` 所属流程，不继承上级部门范围；无所属部门的流程仅组织管理员可管。`administratorId` 是运行期指定管理员的内部人员 ID，本身不授予管理权。列表仅返回可管理流程摘要，单个无权流程详情返回 404。`POST /admin/flows` 接受 `{definition, group?, ownerDepartmentId?, administratorId?, reason}`，创建草稿修订 1；`GET /admin/flows/{code}` 返回草稿及 `{code, name, group, ownerDepartmentId, administratorId, isEnabled, draftRevision, publishedVersionId, publishedVersion}`。`PUT /admin/flows/{code}/draft` 接受同样的定义和元数据，另带 `expectedRevision`，成功时修订号递增，过期返回 409。流程编码创建后不能更改；指定管理员必须是当前在职的内部人员 ID。管理写入的 `reason` 为 2–200 字。

`GET /admin/flows/departments` 只返回 `{id,name}[]`：组织管理员取得全部现存部门，部门流程管理员只取得当前本人直接加入且仍存在的部门；不会沿部门树扩展，也不返回部门编码、负责人、人员或其他部门细节。当前部门实体没有停用状态，只有现存记录可作为归属选项。无直属部门的流程管理员返回空数组，无在职本地管理权限返回 403；接口仍在流程创建和保存时按最新组织关系再次验权。

`POST /admin/flows/{code}/publish` 接受 `{expectedRevision, expectedPublishedVersionId, reason}`；首次发布的当前版本 ID 为 null。服务端先按完整定义编译并报告发布错误，再在同一事务内核对草稿修订及当前发布指针、追加不可变版本并切换当前版本。任一并发前提过期返回 409，已在途审批单仍使用原锁定版本。审计只存操作人、原因、修订/版本号及定义 SHA-256，不存完整流程 JSON。旧数据的流程管理员 ID 对账由服务器本机 `reconcile-flow-admins` 命令执行：先预览唯一修正与零/多匹配事项，输入 `APPLY` 后再次核对预览指纹并提交；没有自动启动修正。

`POST /admin/flows/import/preview` 接受 `{bpmnXml}`，仅解析本服务兼容文件并返回 `{code, name, nodeCount, hasCodeConflict, canOverwrite, existingDraftRevision, publishIssues}`，不写库；发布检查问题不阻止保存草稿。`POST /admin/flows/import` 接受 `{bpmnXml, targetCode?, expectedRevision?, group?, ownerDepartmentId?, administratorId?, reason}`。省略目标时只允许新建，同编码返回 409；覆盖时 `targetCode` 须与文件编码相同且必须填写 `expectedRevision`，过期返回 409，未填写的管理元数据沿用目标当前值。导入只保存草稿，不自动发布。`GET /admin/flows/{code}/export?source=draft` 导出当前草稿；`source=published&version=N` 导出指定不可变发布版。两种来源都再次核对当前流程管理范围；无权流程返回 404。文件内容为 `application/xml`，固定下载名 `approval-flow.bpmn`。

`GET /admin/flows/{code}/versions` 只返回按版本号倒序的 `{id, version, publishedAt, isCurrent}` 摘要，不返回定义 JSON 和执行图；无权目标返回 404。`POST /admin/flows/{code}/versions/{version}/restore` 接受 `{expectedRevision, expectedPublishedVersionId, reason}`，在同一事务内核对当前管理范围、草稿修订和当前发布版，把指定不可变版本内容复制成下一修订草稿，同时记录来源版本及内容 SHA-256；旧版和当前发布指针均不变。管理员随后须调用发布接口，新内容才成为下一不可变版本。过期前提返回 409，不存在的版本返回 404。

`PUT /admin/flows/{code}/enabled` 接受 `{expectedEnabled, enabled, reason}`，实际状态改变时按旧启用状态条件更新并留管理员、原因和当前发布版本号；重复提交相同状态不增加审计。停用后开放流程目录与新发起接口不再返回该流程，已在途实例仍按原 `FlowVersion` 快照运行；恢复启用不改发布版本指针。

`GET /admin/apps/{code}/embed-origins` 返回 `{code, origins}`；`PUT` 接受 `{origins, reason}` 并返回相同结构。`origins` 是完整来源列表，最多 20 项、每项不超过 256 字符，去重后保存；仅允许 HTTPS 网站来源，开发时 HTTP 只允许 `localhost` 或 `127.0.0.1`，不得包含路径、查询、片段、用户信息或通配符。`reason` 须为 2 至 200 字符。保存使用旧值条件更新防止覆盖并发修改，在同一事务记录管理员、原因和不含密钥的前后来源快照。

`GET /admin/apps/{code}/administrators` 返回 `{code, personIds}`，按内部人员 ID 排序；旧应用默认 `[]`。`PUT` 接受 `{expectedPersonIds, personIds, reason}`，两份名单均不得超过 50 人或含重复、空白 ID；目标名单仅接受当前在职的内部人员 ID，原因须为 2 至 200 字符。`expectedPersonIds` 必须等于读取时的旧名单，否则返回 409；写入在串行化事务中复核人员、条件替换并留只含人员 ID 与原因的审计。只有本地组织管理员能维护名单。后续测试回调用例会从可信本地 JWT 取得人员 ID，重新核对应用启用状态及当前在职的名单成员或组织管理员；本接口本身不发送回调。

`POST /admin/apps/{code}/test-callback` 接受 `{reason}`，原因须为 2 至 200 字符；仅当前本地会话中的组织管理员或该应用名单内在职人员可调用，入队事务再次核对权限、应用启用及非空回调地址。成功返回 `{eventId, status:"queued"}`，`eventId` 以 `test-` 开头，每次测试唯一。该接口只保存回调待发项及含操作者和原因的脱敏审计，不创建审批单、任务或业务动作。`integration.test` 正文沿用回调字段：`instanceId` 为与 `eventId` 相同的合成 ID，`serialNo`、`flowCode`、`businessKey` 为空串，`status` 为 `test`，`data` 为 `{}`，测试事件专有 `clientAppCode` 为目标应用编码；`occurredAt` 为排队时间。业务系统只验签确认，不按业务单号更新单据。发送复用 `X-Approval-Signature` 原始正文 HMAC、`X-Request-Id=eventId`、五秒超时、持久重试与发送日志；管理队列摘要可按应用查看该项。旧审批回调不带 `clientAppCode`。

流程模拟创建请求为 `{definition,data,initiatorId,fakeAssignees}`，其中 `definition.code` 必须是当前本地人员有权管理的已有流程，草稿可以包含尚未保存的修改。`fakeAssignees` 按人工节点 ID 提供假处理人内部 ID 数组。响应含 `id,flowCode,expiresAt,status,tasks,waits,events`；每步请求为 `{kind,taskId?,tokenId?,actorId?,success?,actionResult?}`，人工命令只允许当前假处理人操作当前任务，自动动作及子流程由调用方显式提供假结果。仅会话创建人能读取、推进和删除，读取也刷新 30 分钟闲置期；进程重启即丢失，不产生审批单、待发项、真实提醒、钩子或回调。

`POST /admin/apps` 接受 `{code, name, allowedFlowCodes, canViewAllTasks, callbackUrl?, dataUrl?, validationUrl?, assigneeUrl?, actionUrl?, reason}`。应用编码只含 ASCII 字母、数字、`-`、`_`，最长 128 字符；名称最长 256 字符，出站地址必须满足部署的 HTTPS/显式内网 HTTP 策略。`PUT /admin/apps/{code}/enabled` 接受 `{enabled, reason}` 并返回 204；`POST /admin/apps/{code}/rotate-secrets` 接受 `{reason}`。三种管理操作的原因均须为 2 至 200 字符，实际变更与不含凭据的管理员审计在同一事务提交。创建和轮换只在本次响应返回 `{code, apiKey, signingSecret}`；后续列表和日志均不能再次读取明文。轮换或实际启停使旧嵌入会话永久失效，停用时旧 API Key 不可调用；重新启用不会恢复旧嵌入会话。

`GET /admin/bindings` 返回绑定数组，每项只含 `clientAppCode, kind, externalId, internalId, targetName, method`；目标名称来自当前可信组织主数据，不含电话、邮箱。管理员写入继续使用 `POST /admin/org/bindings`：传 `kind, externalId, internalId, clientAppCode`，人员绑定可由管理员指定内部人员，服务端记录操作者并在换绑时作废旧身份令牌。`GET /admin/audit` 返回最近 200 条组织、接入应用及回调重发变更摘要，字段为 `occurredAt, actorId, action, target, reason`；不返回完整前后快照、联系方式或密钥。其他管理日志待相应写入功能接线。

`GET /admin/outbox` 返回最近 200 条待发项摘要，每项为 `eventId, clientAppCode, kind, status, attempts, availableAt, errorCode`，不返回业务正文、回调地址或密钥。`POST /admin/callbacks/{eventId}/retry` 接受 `{reason}`，原因须为 2 至 200 字符；只有回调事件可重排。它保留原事件 ID 和正文、重置待发时间及错误摘要，并在同一事务记录本地管理员和原因。正在有效租约中发送的回调返回冲突。重发记录也进入 `/admin/audit`。

`GET /api/v1/flows` 和 `GET /api/v1/flows/{code}/schema` 需要 `X-Api-Key`。列表只包含当前已发布、接入应用已获授权且流程定义允许该应用使用的版本；无权限读取单个流程时返回 `FORBIDDEN`。列表项为 `{ "code": "PURCHASE", "name": "采购申请", "version": 3 }`。数据定义接口返回 `{ "flowCode": "PURCHASE", "version": 3, "data": { "fields": [], "summaryFields": [] }, "example": {} }`；`example` 按当前定义生成，不含计算字段和审批人填写字段。

`GET /api/v1/instances/{id}` 与 `GET /api/v1/instances?flowCode=PURCHASE&businessKey=PR001` 也需要 `X-Api-Key`，只能读取由该接入应用发起的审批单；前者返回一张审批单，后者按发起时间倒序返回同业务单号的历史数组。审批单响应包含 `id`、`serialNo`、`flowCode`、`businessKey`、`title`、`initiatorId`、`initiatorDepartmentId`、`submittedById`、`clientAppCode`、`status`、`data`、`startedAt`、`endedAt`、`tasks`、`events`。详情中的任务包含 `id`、`nodeId`、`assigneeId`、`status`、`opinion`、`dueAt`、`completedAt`；操作记录包含 `eventId`、`sequence`、`event`、`detail`、`occurredAt`。按业务单号返回的历史摘要把 `tasks`、`events` 留空。缺少任一查询参数返回 `VALIDATION_FAILED`；用其他应用的 Key 查详情返回 `FORBIDDEN`，历史查询不会列出其他应用的记录。

在途审批仍按全局的 `(flowCode, businessKey)` 唯一。相同单号已由其他接入应用占用时，发起用例只能返回 `CONFLICT`，不能把对方的审批单当作幂等结果；本应用同号才可复用本应用现有在途审批单。

`GET /api/v1/tasks/count?user=alice` 和 `GET /api/v1/tasks?user=alice&skip=0&take=20` 需要 `X-Api-Key`，`user` 必须是当前接入应用已绑定的在职业务人员 ID，不按全局工号回退。默认只统计或列出本应用的待办；`scope=all` 要求该接入应用已获跨应用待办权限，否则返回 `FORBIDDEN`。数量响应为 `{pending, urgent, overdue}`；列表为任务数组，每项有 `id, instanceId, flowCode, businessKey, title, nodeId, isUrgent, dueAt, summary, openPath`。摘要仅取锁定流程版本指定的字段；`skip` 默认 0，`take` 默认 20、上限 100。

待办摘要还必须按锁定发布图中该待办的当前节点权限过滤：`fieldPermissions` 显式为 `hidden` 的字段不返回；未配置节点权限的字段沿用默认可见语义。同一节点重复配置同一字段权限时拒绝发布，查询旧异常图也拒绝投影，避免权限冲突导致泄露。

`POST /api/v1/instances` 需要 `X-Api-Key`，请求为 `{flowCode, businessKey, title, initiator, initiatingDepartment?, submittedBy?, data?, detailUrl?, isUrgent?, selectedAssignees?}`。`initiator`、`submittedBy` 及 `selectedAssignees` 中的人员 ID 必须已在当前应用绑定且在职；`initiatingDepartment` 必须是本应用已绑定部门且发起人属于该部门，不传时使用可信组织主数据中的主部门。流程同时检查应用授权、发起角色与选定发起部门范围；流程允许部门配置只按组织主数据的内部 ID 或编码解析，不接受应用外部部门绑定覆盖。`selectedAssignees` 按允许发起人自选的节点 ID 传人员列表，可用于自选审批人和抄送人。`data` 可直接提供审批数据对象；未传时要求当前应用配置取数地址，并在审批事务前调用取数钩子。服务端按已发布定义校验、重算公式与分支条件，再在写事务前调用已配置的发起校验钩子；拒绝时返回业务原因且不创建审批单。审批人填写值不接受业务系统自造；顶层及明细附件 ID 只能引用本应用上传且尚未关联的文件，文件归属与实例状态在同一事务内确认和保存，任何一项失败都回滚整笔发起。成功返回 200 和该审批单详情，已在途的本应用同号请求返回同一张，跨应用同号只返回 `CONFLICT`。取人与自动动作仍待按 ADR 0010 的节点到达和提交后持久队列接线。审批矩阵命中只从服务端可信提供者读取，未配置时返回可纠正错误。编号默认 `SP-yyyyMM-六位流水号`，部署可配置前缀、日期格式和位数；数据库分配号码后若发起失败允许留空号，流程级编号配置由管理后台接入。

`POST /api/v1/instances/batch` 需要同一 `X-Api-Key`，请求为 `{ "items": [单张发起请求, …] }`，每批 1 至 100 项。服务端按列表顺序逐张独立验证和提交，返回 `{ "results": [{ "index": 0, "instance": 审批单详情或 null, "error": { "code", "message" } 或 null }, …] }`；`index` 从 0 开始。单项业务校验失败只写该项 `error`，其他已成功的单据不回滚；无效的批次外形整体返回 400。每项使用由批次 `X-Request-Id` 和下标确定的独立请求 ID，重复批次中仍在途的同应用单据返回原审批单。程序缺陷不会被当作单项业务错误吞掉。

`POST /api/v1/instances/{id}/withdraw` 与 `POST /api/v1/instances/{id}/remind` 的 JSON 请求体为 `{user}`，`user` 必须是本应用已绑定的在职发起人；目标审批单也必须属于当前应用。前者按锁定流程的撤回规则执行，后者对当前待办催办，同一步每小时最多一次。`POST /api/v1/instances/{id}/terminate` 也要求 `X-Api-Key`，请求为 `{user, reason}`；审批单必须属于当前应用，`user` 必须是本应用已绑定的在职实际操作人，`reason` 为非空终止原因。审批单处于 `running` 或 `awaitingInitiator`（退回待重提、待补充）时均可终止；终止会取消未处理任务、清除暂停入口、释放在途业务单号占用，审计实际操作人和原因，并在同一事务固定 `instance.terminated` 回调；不要求操作人是发起人，也不授予其他管理员干预权限。`POST /api/v1/tasks/{id}/actions` 的请求体为 `{user, action, opinion?, targetUser?, targetNodeId?, relatedId?, mode?, mentionedUsers?, fieldValues?}`；`user`、`targetUser` 和 `mentionedUsers` 都须在本应用既有绑定且在职，目标任务须属于当前应用。`id` 是可能包含斜杠的不透明任务 ID，作为 URL 路径参数时应进行百分号编码。`action` 允许 `approve`、`reject`、`handle`、`acknowledge`、`return`、`transfer`、`addSigner`、`removeSigner`、`revokeApproval`、`comment`、`saveDraft`、`copyTo`、`consult`、`reply`、`requestSupplement`；节点权限、任务处理人、意见必填等仍由统一引擎校验。`fieldValues` 是按字段 key 的 JSON 对象，仅接受已发布定义标记 `approverInput=true` 且当前节点 `fieldPermissions` 显式为 `editable` 的顶层字段；类型沿用审批数据规则，人员和部门 ID 须在本应用既有绑定，附件 ID 暂不接受。明细子字段没有行索引协议，当前明确返回 `VALIDATION_FAILED`；业务原始字段、计算字段和无编辑权限字段不能提交。已提交值写入操作事件的 `detail.audit.fieldValues`；成功推进后合并到审批单详情的 `approverFieldValues` 当前值投影，相关 `task.completed`、`instance.approved`、`instance.rejected` 回调的可选 `fieldValues`，原审批数据 `data` 不变，由业务系统决定是否回写业务单据。当前节点显式可写且标记为必填的顶层审批人字段，只在 `approve`、`handle`、`acknowledge` 成功推进时必须提交；拒绝、评论、草稿和协作操作不强制填写，已提交的值仍按字段类型与授权校验。上述三个操作成功返回最新审批单详情。发起时选定部门、审批数据、自选审批/抄送人和业务数据找人标识保存为审批单快照；后续节点到达使用最新组织与审批矩阵重算，符合设计书第 4 节规则。

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

`GET /api/v1/center/instances` 参数：`section=todo|done|cc|started`（默认 `todo`）、可选 `flowCode`、`status`、`search`、`from`（发起时间含起点）、`to`（不含终点）、`skip=0`、`take=20`（1～100）。搜索支持标题、审批编号、业务单号和当前人员可见字段值（含非摘要字段及明细列），最长 200 字；字段名、隐藏字段和未定义业务键不参与匹配。时间范围必须递增。返回 `{total, skip, take, items}`，人员及嵌入应用授权先于搜索；字段权限裁剪先于搜索命中数与分页，绝不按原始业务 JSON 的隐藏值决定是否命中；加急优先，其后按发起时间倒序和 ID 稳定排序。同一单据有多项本人任务也只列一次。

- `todo`：当前 `pending` 任务的处理人或被代理的原审批人。
- `done`：可信持久参与者投影中的实际处理人，包括完成、转交、退回和要求补充；不会把代理前原审批人误当作实际处理人。
- `cc`：普通抄送节点实际送达或人工抄送的收件人；未到达的将来抄送不授予可见性。
- `started`：发起人或实际代发人，只供看进度。

摘要字段为 `id, serialNo, flowCode, flowName, sourceName, businessKey, title, status, clientAppCode, initiatorId, initiatorName, currentStep?, submittedById?, isUrgent, isUnread, startedAt, endedAt?, summary, summaryFieldDefinitions, pendingTaskIds, canBulkApprove, bulkApprovableTaskIds`。`isUnread` 只针对当前 JWT 人员，不返回其他人的读游标；列表纯读。显示名称来自可信人员与接入应用主数据；`currentStep` 为当前人工节点名称，多条并行待办时显示并行环节数量，无人工待办时省略。`pendingTaskIds` 仅说明当前任务关系；`bulkApprovableTaskIds` 只列当前人员可批量同意的实际任务 ID。全部操作提交时仍按最新状态和节点规则重验。`summary` 使用固定发布版本的数据定义及节点隐藏权限裁剪，包含明细列；未定义字段不透传。`summaryFieldDefinitions` 按发布顺序只描述实际返回的字段，每项含 `key, name, type, children`；隐藏字段及隐藏明细列的名称也不返回。

`GET /api/v1/center/instances/{id}` 返回 `{instance, data, fieldDefinitions, layout, attachments, tasks, events, activeNodeIds}`。其中 `instance` 使用同一摘要结构；`data` 只包含已发布定义中的可见字段，`fieldDefinitions` 只描述这些可见字段和明细子列。`layout` 为 `{mode:"auto"|"custom", groups:[{title, columns, items:[{fieldKey, columnSpan, detailColumnWidths, moneyUppercase?}]}]}`；自定义模式使用审批单锁定的发布版本，排版项与明细宽度只包含当前人员可见字段，全部隐藏的分组不返回，部分隐藏的分组标题改为通用标题。原单嵌入模式尚未接入，回退自动排版，不返回嵌入页面编码或地址。金额大写由服务端对可见金额计算。`tasks` 为 `{id, nodeId, nodeName, assigneeId, delegatedForId?, status, opinion?, dueAt?, completedAt?, allowedActions}`，`allowedActions` 从固定发布图与当前运行状态按认证人员计算；`events` 为 `{eventId, sequence, kind, nodeId?, actorId?, onBehalfOfId?, opinion?, occurredAt}`；`activeNodeIds` 只提供当前令牌节点，用于高亮。原始运行状态、审计 `fieldValues`、人员解析快照和私人暂存意见不进入响应。图布局仍待接入。

`POST /api/v1/center/instances/{id}/read` 请求 `{seenThroughSequence}`（非负整数，为本次详情时间线实际显示的最大事件序号；没有事件为零），成功返回 204。仅详情已显示的当前人员主动调用；服务端在同一事务重新核对该人员的参与关系、保密范围与嵌入应用，再把（人员、审批单）的持久游标单调推进到不超过当前最大事件序号的已显示序号。因此 GET 详情后新发生的活动不会被误标已读。无权与不存在统一 404。列表和详情 GET 不写游标；游标未保存成功时页面仍展示详情并提示可刷新重试。他人之后产生的新可见审计活动使 `isUnread=true`，本人动作与私人意见草稿不触发未读；完整原始事件和游标均不返回给前端。

`POST /api/v1/center/tasks/{id}/actions` 请求 `{action, opinion?, targetPersonId?, targetNodeId?, relatedId?, mode?, mentionedPersonIds?, fieldValues?}`，任务 ID 含斜杠时需将整个 ID 编码；人员参数使用审批服务内部 ID。响应 `{taskId, instanceId, detail}`，详情再次执行人员与应用授权。`action` 只允许人工任务命令，当前人、节点许可、意见及字段规则由服务端重新校验，变更经过统一引擎写入事件与任务状态。`fieldValues` 只接受当前节点显式可编辑的顶层审批人字段；人员、部门值须为现存内部 ID，必填仅在成功推进时强制；明细和附件字段目前没有安全提交协议，明确拒绝。节点同意前的业务校验钩子仍会运行，中心登录人员作为审批服务内部人员 ID 传给钩子的 `actor`。无权或失效任务不会因列表先前显示过按钮而获准。

`POST /api/v1/center/tasks/batch-approve` 请求 `{taskIds:[...], opinion?}`，1～100 项且 ID 不能重复，只接受明确拥有 `bulkApprove` 许可的同意任务。每项独立调用统一引擎，响应 `{items:[{taskId, succeeded, instanceId?, code?, message?}]}` 与输入同序；业务校验失败不回滚已成功项。两种写接口都只从已验证 JWT 获取操作人和嵌入应用范围，接收 `X-Request-Id` 作为幂等 ID；批量为各项追加稳定序号。浏览器从新服务响应更新许可，不根据旧按钮状态推断权限。

可见详情限发起人、实际代发人、当前本人或代理关系中的待办，以及持久化的历史实际处理人/已送达抄送人。保密流程不因管理员身份或仅有未到达、已取消任务自动开放；本切片不开放额外查看人或管理查询。嵌入会话的应用条件与人员条件共同下推到数据库查询，跨应用详情和其他不可见或不存在的单据统一返回 `NOT_FOUND`。列表与详情分别在一个一致读事务中读取计数、版本、任务和事件。

没有显式节点上下文的详情，按该查看人当前 pending 任务节点的隐藏字段并集裁剪；无当前待办的历史/抄送参与者采用发布图全部节点的隐藏集合，隐藏优先。发起人与实际代发人可以读取其提交的已定义快照字段。详情新增 `fieldDefinitions`，与已裁剪 `data` 一一对应，结构同摘要定义；前端用锁定发布版本的名称和已授权子列绘制明细表，不输出原始 JSON。详情还返回 `attachments: [{id,fileName,contentType,size}]`，只从可见附件字段（含明细行）提取 ID，且元数据必须仍关联本审批单；隐藏字段中的文件不列出。文件内容继续用当前人员 JWT 调 `GET /api/v1/files/{id}`，该下载接口独立验权，不把令牌放 URL。此保守读取策略不增加业务数据修改能力；桌面前端已以本地登录 JWT 接入四列表、详情、字段内容搜索、任务操作与逐项批量同意，`frontend/src/api/center-adapter.ts` 是中心响应副本。未读由后续中心切片实现。本切片不供业务 SDK 调用。

`GET /api/v1/center/opinions` 返回当前 JWT 人员的个人常用意见数组，每项 `{id,content,createdAtUtc}`，按创建时间和 ID 稳定排序；本地与嵌入会话均可读取。`POST /api/v1/center/opinions` 和 `PUT /api/v1/center/opinions/{id}` 请求 `{content}`，返回保存后的单项；`DELETE /api/v1/center/opinions/{id}` 成功返回 204。三种写请求仅允许本地人员会话，嵌入会话返回 `FORBIDDEN`；意见只按 JWT 人员 ID 归属，不接受请求中的人员参数。正文去除首尾空白后须为 1～500 字，每人最多 30 条，满额返回 `CONFLICT`；他人意见与不存在统一 `NOT_FOUND`。常用意见仅填入当前审批意见栏，任务提交仍走现有操作接口。

## 同源审批中心内嵌

业务系统后端先用本应用 API Key 调 `POST /api/v1/embed-tokens`，`targetPath` 示例为 `#/center/approvals/{instanceId}`。业务页面把收到的一次性令牌放在审批服务同源地址的片段中，例如 `/embed/{clientAppCode}#/center/approvals/{instanceId}?embedToken=...`。片段不发送给服务端；页面读取后立即从地址栏和历史条目清除，并调用令牌交换接口。路径中的 `clientAppCode` 只选择 HTML 的来源策略，不作为人员或应用认证。跳转新窗口也可用普通 `/` 入口。

普通 `GET /`、直接 `GET /index.html` 和其他静态 HTML 由同一服务返回 `Content-Security-Policy: frame-ancestors 'none'`；应用专属 `GET /embed/{clientAppCode}` 返回 `frame-ancestors 'self'` 加该应用登记的 `allowedOrigins`。未登记来源无法在 iframe 中打开，未配置来源时仅允许同源，停用或不存在应用返回 404。静态文件不会提供可绕过该策略的 HTML 入口。登记来源仅接受完整 HTTPS origin，开发环境允许本机 HTTP；读取持久化配置时再次验证，损坏值不进入 CSP。部署时前端 `dist` 必须由审批服务同源托管，反向代理不得另行暴露不带此响应头的 HTML 副本。

`GET /api/v1/center/embed-context` 必须带令牌交换后得到的嵌入 Bearer JWT。服务端每次重验人员、应用、外部绑定及身份代际，仅 `sessionKind=embed` 返回 `{clientAppCode, allowedOrigins:["https://business.example"]}`，来源按当前应用配置读取；匿名为 401，本地登录令牌为 403。响应不可缓存。前端只从 `document.referrer` 提取完整 origin 且与此列表严格匹配后，才以该 origin 为 `targetOrigin` 发送完成消息；空来源或未匹配绝不发送。消息不含令牌和审批数据。

## 开放接口补充与待接入项

下表汇集近期开放接口；已接入接口的细节以相邻正文为准，打印接口仍由独立任务实现：

| 接口 | 用途 | 任务 |
| --- | --- | --- |
| `POST /api/v1/flows/{code}/preview` | 按当前发布图、数据和组织预览预计路径及候选人 | T-006 |
| `POST /api/v1/instances/batch` | 批量发起审批 | T-006 |
| `PATCH /api/v1/instances/{id}/data` | 审批中更新发布版本显式授权的字段；协议见下文 | T-006 |
| `POST /api/v1/instances/{id}/withdraw`、`/remind`、`/terminate` | 撤回、催办、业务单据作废时终止；协议见上文 | T-006 |
| `GET /api/v1/instances/{id}/print` | 审批单 PDF | T-015 |
| `POST /api/v1/tasks/{id}/actions` | 审批操作 | T-006 |
| `POST /api/v1/files`、`GET /api/v1/files/{id}` | 上传、下载附件 | T-013 |

`PATCH /api/v1/instances/{id}/data` 需要所属接入应用的 `X-Api-Key` 与稳定 `X-Request-Id`，请求为 `{ "user": "alice", "data": { "amount": 5 } }`，`user` 必须是本应用已绑定的在职实际操作人。只接受 `running` 审批单，且仅允许修改锁定发布版本中显式设置 `businessUpdateAllowed=true` 的顶层字段或明细子字段；旧定义和未设置开关均视为禁止，计算字段及审批人填写字段不可改。顶层明细表若整体开放，可提交完整表值；仅开放子字段时，须提交与原表等长的数组，各项为该行的局部字段对象或 `null`，不能通过该形式增删行。服务在写入前校验字段类型、重算公式与可信找人映射；已到达任务不静默改派，后续节点和网关按新数据计算。数据快照、路由映射和 `businessDataUpdated` 事件在同一乐观版本事务中提交，事件 `detail.audit.dataChanges` 记录变化字段的 `before`/`after`，响应为更新后的审批单详情。同一请求 ID 重试不重复写入或追加事件；并发版本冲突返回 `CONFLICT`。

`POST /api/v1/instances/{id}/resubmit` 需要所属应用的 `X-Api-Key` 和稳定 `X-Request-Id`，请求为 `{ "user": "alice", "data": { ...完整新业务数据 } }`。`user` 必须是本应用已绑定且在职的原发起人；只接受退回发起人或要求补充后的 `awaitingInitiator` 审批单。服务按锁定发布定义重新校验完整业务数据、计算公式，并按当前可信组织重新解析数据找人映射；审批数据、映射、任务和事件通过同一引擎命令及乐观版本事务保存。退回重提按发起时锁定的 `returnMode` 从头或原节点恢复，清空审批人填写字段的当前值但保留历史事件；补充恢复原任务，保留当前值。审批单详情新增 `approverFieldValues` 对象承载成功推进后的顶层审批人填写值，与业务 `data` 分开；同一请求 ID 重试不追加事件。

`POST /api/v1/flows/{code}/preview` 需要 `X-Api-Key` 与 `X-Request-Id`，只预览当前应用获授权的已发布流程。请求为 `{ "initiator": "alice", "initiatingDepartment": "dept-one", "data": {...}, "selectedAssignees": {...} }`；部门和自选人员可省略，业务单号不必提供，可选 `businessKey` 仅供业务取人钩子识别。发起人及自选人员须按本应用现有在职绑定解析，数据沿用发起字段类型和公式校验。响应 `{flowCode, version, nodes:[{nodeId,name,type,candidateAssigneeIds,candidateAssignees:[{id,name}]}], projectedStatus, notice}`；`nodes` 按预计到达顺序排列，并行分支可交错出现，候选人是当前组织的内部人员 ID。预览在内存中假定人工操作同意、办理完成、自动动作和子流程成功；业务接口找人只在实际预计命中的节点调用，未提供业务单号时钩子收到空 `businessKey`。预览不发起审批、不占业务单号、不写事件或回调；实际到达节点时会按最新组织与业务数据重算，故路径和候选人不作最终承诺。

## 附件接口

`POST /api/v1/files?fileName=票据.png` 的请求体是文件原始字节，不是 JSON 或 multipart；返回 JSON：`{ "id": "...", "fileName": "票据.png", "size": 123, "contentType": "image/png", "sha256": "..." }`。服务按内容签名核对扩展名，默认单文件上限 20 MB。当前接受 PDF、PNG、JPEG、GIF、WebP，以及具有有效包清单、入口关系和主部件 XML 的 DOCX、XLSX、PPTX；Office 文件只提供下载，不在线预览。其他类型返回 `VALIDATION_FAILED`。上传支持接入应用 API Key 或可信人员会话；应用上传的文件 ID 可在发起审批的顶层或明细附件字段中引用。同一文件在请求内去重，仅当前应用上传、尚未关联且未被清理占用的文件可以关联；审批单和所有附件在同一事务提交，任一文件不符则整笔失败。审批意见中的附件字段暂不接受。

`GET /api/v1/files/{id}` 先校验可信身份及审批单可见性。本地会话按人员权限访问；嵌入会话还必须带已验证的接入应用编码，且只能下载同一应用审批单的已关联附件。未关联附件在元数据中记录上传主体：人员上传的仅本人本地会话可读，应用上传的仅同一 API Key 已认证应用可读；嵌入人员会话不能读取未关联附件。已关联附件不能仅凭 API Key 下载。上传 24 小时后若仍未关联则自动清理。本地文件与浏览器不可直连的 MinIO 由服务转发，浏览器可直连的 MinIO 返回最长五分钟有效的预签名地址的 302 跳转。应用编码从已验证 API Key 得出，不接受请求自报；客户端参数中的 `userId` 从不用于授权。

普通抄送到达时固定收件人，人工抄送和实际任务操作也在审批事务内写历史参与人投影；收件人不因后续组织调整失去已发生的查看事实。已关联附件允许发起人、实际代发人、当前待办人及已记录的历史参与人访问。保密流程由审批单锁定版本的 `isConfidential` 决定，不向管理员或额外查看人自动开放；排队、取消或转交的任务不能仅凭历史指派取得权限。旧版执行图省略该标记时按非保密处理。嵌入会话仍须属于来源应用。

## 回调和业务系统提供的接口

审批服务调业务系统的都经出站适配层，带 `X-Approval-Signature` 签名，默认 5 秒超时，失败按 1 分钟、5 分钟、30 分钟、2 小时……重试（T-006）。回调和钩子地址默认只能是 HTTPS；仅可信内网部署显式设置 `ApprovalService:Integration:AllowHttpUrls=true` 时接受 HTTP。签名只防篡改，不提供传输保密。出站客户端不跟随 HTTP 重定向，避免把审批数据和签名转发至未登记的地址。

- **结果回调（已实现基础发送）**：审批事务把需通知的事件固定成 JSON 快照并写入待发队列；后台任务对接入应用登记的完整回调地址发送 POST，使用 `X-Approval-Signature` 对原始 UTF-8 正文签名，`X-Request-Id` 为稳定 `eventId`。请求体包含 `eventId`、`event`、`instanceId`、`serialNo`、`flowCode`、`businessKey`、`status`、`data`（事件时的审批数据快照）、`occurredAt`；人工操作已填写字段另在可选 `fieldValues` 中，绝不覆盖 `data`。当前映射 `instance.started`、`instance.approved`、`instance.rejected`、`instance.withdrawn`、`instance.terminated`、`instance.cancelled`、`instance.returnedToInitiator`、`instance.supplementRequested`，以及 `task.created`、`task.completed`、`task.cancelled`；接入测试另外使用 `integration.test`。后两种实例事件表示已退回发起人或正等待发起人补充，`status` 为 `awaitingInitiator`；退回历史审批节点不使用 `instance.returnedToInitiator`。只接受 2xx；超时为 5 秒，失败按待发队列规则从 1 分钟、5 分钟、30 分钟、2 小时起重试。每次发送留状态和错误类别日志，不保存正文；管理用例可对原事件重新排队，重发仍使用相同 `eventId` 和正文。停用接入应用期间保留待发项且不出站；尚未登记回调地址时不调用业务系统，事件仍在操作记录中。
  接入应用被停用后，已有待发回调暂停发送并记录 `CLIENT_APP_DISABLED`，仍留在队列等待后续重试；重新启用后可送达同一事件。

撤销同意成功后，原审批任务从已处理恢复为待办，服务发兼容新增的 `task.restored` 回调；该回调独有可选 `taskId`，值为恢复的原任务 ID，`status` 为 `running`。待办订阅方收到它时须刷新原任务/待办列表；撤销期间被取消的下一步任务仍按既有 `task.cancelled` 通知。`task.restored` 与其他回调一样在审批事务中固定 `eventId` 和完整正文，失败重试及手动重发不生成新任务 ID 或事件 ID。审批单详情中恢复为 `pending` 的原任务清空 `opinion` 与 `completedAt`，此前的同意意见和撤销动作仍保留在只追加操作记录里。其他回调不新增 `taskId` 字段。
- **取数接口**：只传单号发起时，审批服务按单号回业务系统取审批数据。
- **校验钩子**：发起前、某个节点同意前问业务系统能不能过，不能过时返回原因。
- **取人接口**：审批人来源选“业务系统接口”时返回用户 ID 列表。
- **业务动作接口**：自动节点调用，如审批通过后生成采购订单。

四类钩子按已采纳的 [ADR 0010](adr/0010-outbound-hook-protocol.md) 使用各应用分别登记的完整 URL、POST 小驼峰 JSON，默认 5 秒超时；可选校验地址未配置时跳过，必需的取数、取人和动作地址未配置时返回可纠正错误或保留待发项。审批服务用 `X-Approval-Signature` 对原始 UTF-8 请求体签名，业务系统须以同一密钥对原始响应字节签名并原样回显 `X-Request-Id`；审批服务在解析 JSON 前恒定时间验签并核对请求 ID。非 2xx、超时、网络故障、无效签名、请求 ID 或 JSON 均返回 `BUSINESS_HOOK_FAILED`；钩子明确拒绝返回 `VALIDATION_FAILED` 和业务原因。取数请求 `{flowCode,businessKey,requestId}`，响应 `{data:{…},title?,detailUrl?}` 且 `data` 必须为对象；校验请求 `{phase:"start"/"approve",flowCode,businessKey,instanceId?,taskId?,actor,data}`，响应 `{allowed,reason?}`，拒绝时 `reason` 必填。取人请求 `{flowCode,businessKey,nodeId,sourceValue?,initiator,data}`、响应 `{userIds:[…]}`；动作请求 `{eventId,actionCode,instanceId,flowCode,businessKey,data}`、响应 `{success,message?,result?}`。取数和校验在审批写事务前同步调用；取人于目标节点前取得；业务动作经提交后持久待发队列执行。取人只在纯引擎实际到达目标节点时调用，返回人员仅按当前接入应用已有绑定解析；失败不落部分审批状态。自动节点在事务内固定 `actionPending` 待发项，提交后由后台调用；业务响应 `success:false` 或网络故障保留事件并退避重试，`success:true` 后用原 `eventId` 幂等提交引擎结果，业务端也须据此去重。

动作响应的可选 `result` 不改审批数据。成功时 `actionCompleted` 事件独立保存 `actionResult`，引擎状态按原动作 `eventId` 保存结果；后续审批终态回调在可选 `actionResults` 对象中按该 `eventId` 带出结果，`data` 仍是业务审批数据快照。无结果时省略 `actionResults`，重发回调使用首次入队时固定的正文。

自动动作待发项最多自动尝试 10 次；第 10 次失败后转为 `needsAttention`，后台不再领取，原审批令牌继续等待动作结果。流程管理员向 `POST /api/v1/admin/actions/retry` 提交 `{eventId}`；该事件 ID 可含斜杠，故放在 JSON 正文中。服务仅接受本地登录 JWT，并从当前在职人员的工号与原事件审批单所属流程的 `AdministratorId` 核对管理范围；组织管理员角色本身不授予其他流程权限。成功返回 204，仅将原事件排入一次人工重试，不产生新事件或正文；若这一次仍失败，立即回到待管理员处理。无令牌返回 401、无流程管理权返回 403、非自动动作事件返回 404、未处于待管理员处理返回 409。普通结果回调和其他待发事件不适用此上限。

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

`data.fields[]` 的字段包含 `key`、`name`、`type`、`required`、可选 `formula`、`approverInput`、`businessUpdateAllowed` 与 `children`。`businessUpdateAllowed` 缺省为 `false`，仅显式开启的字段允许业务系统在审批进行中更新。`type` 可为 `text`、`number`、`money`、`date`、`person`、`department`、`attachment`、`detailTable`；明细表的子字段放在 `children`。计算字段仅供审批判断与展示，不回写业务单据；`approverInput` 表示由获授权节点的审批人填写。`summaryFields` 存字段 key，通常选三至五个。节点 `fieldPermissions[]` 的每项包含 `fieldKey` 和 `access`（`hidden`、`readOnly`、`editable`）；明细子字段 key 写成 `tableKey.childKey`。

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
