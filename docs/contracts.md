# 契约

开放接口、错误码、HTTP 头、回调和流程定义的约定。权威定义在 `backend/src/ApprovalService.Contracts`，调用端 SDK 有副本，改动流程见 [AGENTS.md](../AGENTS.md) §6.4。设计说明见设计书第 12、13 节。

## 通用约定

- 开放接口都在 `/api/v1` 下。不兼容的改动升版本号（`/api/v2`），老版本保留一段时间。
- 请求和响应都是 JSON：属性名小驼峰；枚举写成小驼峰字符串；值为 null 的属性不输出；时间用带时区的 ISO 8601（如 `2026-10-13T09:30:00+08:00`）；金额用数字，不用字符串。
- 人员用工号，或者业务系统自己的用户 ID（在审批服务里绑定过的）。
- 每个响应都带 `X-Request-Id` 和 `X-Trace-Id`。

## HTTP 头

| 头 | 方向 | 说明 |
| --- | --- | --- |
| `X-Api-Key` | 请求 | 接入应用的 API Key（管理后台“接入应用”里生成，服务端只存 SHA-256） |
| `X-Request-Id` | 请求、响应 | 调用方生成的请求 ID，原样返回并记进日志；没传或不合规（超过 64 个字符，或含字母数字和 `- _ . :` 以外的字符）时服务生成一个 32 位的 |
| `X-Trace-Id` | 响应 | 本次请求的链路 ID，和错误体里的 `traceId` 相同，反馈问题时提供 |
| `X-Approval-Signature` | 回调请求 | 回调请求体的签名：`sha256=` 加上用接入应用的签名密钥对请求体做 HMAC-SHA256 的十六进制小写 |

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

`GET /api/v1/info` 的响应：

```json
{ "name": "ApprovalService", "version": "0.1.0", "apiVersion": "v1" }
```

`GET /api/v1/flows` 和 `GET /api/v1/flows/{code}/schema` 需要 `X-Api-Key`。列表只包含当前已发布、接入应用已获授权且流程定义允许该应用使用的版本；无权限读取单个流程时返回 `FORBIDDEN`。列表项为 `{ "code": "PURCHASE", "name": "采购申请", "version": 3 }`。数据定义接口返回 `{ "flowCode": "PURCHASE", "version": 3, "data": { "fields": [], "summaryFields": [] }, "example": {} }`；`example` 按当前定义生成，不含计算字段和审批人填写字段。

`GET /api/v1/instances/{id}` 与 `GET /api/v1/instances?flowCode=PURCHASE&businessKey=PR001` 也需要 `X-Api-Key`，只能读取由该接入应用发起的审批单；前者返回一张审批单，后者按发起时间倒序返回同业务单号的历史数组。审批单响应包含 `id`、`serialNo`、`flowCode`、`businessKey`、`title`、`initiatorId`、`clientAppCode`、`status`、`data`、`startedAt`、`endedAt`、`tasks`、`events`。详情中的任务包含 `id`、`nodeId`、`assigneeId`、`status`、`opinion`、`dueAt`、`completedAt`；操作记录包含 `eventId`、`sequence`、`event`、`detail`、`occurredAt`。按业务单号返回的历史摘要把 `tasks`、`events` 留空。缺少任一查询参数返回 `VALIDATION_FAILED`；用其他应用的 Key 查详情返回 `FORBIDDEN`，历史查询不会列出其他应用的记录。

在途审批仍按全局的 `(flowCode, businessKey)` 唯一。相同单号已由其他接入应用占用时，发起用例只能返回 `CONFLICT`，不能把对方的审批单当作幂等结果；本应用同号才可复用本应用现有在途审批单。

## 规划中的接口

按设计书第 13 节，在对应任务里实现，实现时把请求和响应的格式补进本文：

| 接口 | 用途 | 任务 |
| --- | --- | --- |
| `POST /api/v1/flows/{code}/preview` | 预览会走哪些节点 | T-006 |
| `POST /api/v1/instances`、`POST /api/v1/instances/batch` | 发起审批（传数据，或只传单号让服务回业务系统取） | T-006 |
| `PATCH /api/v1/instances/{id}/data` | 审批中更新允许更新的字段 | T-006 |
| `POST /api/v1/instances/{id}/withdraw`、`/resubmit`、`/remind`、`/terminate` | 撤回、重新提交、催办、业务单据作废时终止 | T-006 |
| `GET /api/v1/instances/{id}/print` | 审批单 PDF | T-015 |
| `GET /api/v1/tasks/count?user=`、`GET /api/v1/tasks?user=`、`POST /api/v1/tasks/{id}/actions` | 待办数量、待办列表、审批操作 | T-006 |
| `POST /api/v1/files`、`GET /api/v1/files/{id}` | 上传、下载附件 | T-013 |
| `POST /api/v1/embed-tokens` | 签发嵌入令牌，换成免登录打开地址 | T-007 |
| `POST /api/v1/org/changes`、`POST /api/v1/org/bindings` | 推送组织架构变更、绑定业务系统的用户和部门 | T-007 |

## 回调和业务系统提供的接口

审批服务调业务系统的都经出站适配层，带 `X-Approval-Signature` 签名，默认 5 秒超时，失败按 1 分钟、5 分钟、30 分钟、2 小时……重试（T-006）。回调和钩子地址默认只能是 HTTPS；仅可信内网部署显式设置 `ApprovalService:Integration:AllowHttpUrls=true` 时接受 HTTP。签名只防篡改，不提供传输保密。出站客户端不跟随 HTTP 重定向，避免把审批数据和签名转发至未登记的地址。

- **结果回调（已实现基础发送）**：审批事务把需通知的事件固定成 JSON 快照并写入待发队列；后台任务对接入应用登记的完整回调地址发送 POST，使用 `X-Approval-Signature` 对原始 UTF-8 正文签名，`X-Request-Id` 为稳定 `eventId`。请求体包含 `eventId`、`event`、`instanceId`、`serialNo`、`flowCode`、`businessKey`、`status`、`data`（事件时的审批数据快照）、`occurredAt`。当前映射 `instance.started`、`instance.approved`、`instance.rejected`、`instance.withdrawn`、`instance.terminated`、`instance.cancelled`，以及 `task.created`、`task.completed`、`task.cancelled`。只接受 2xx；超时为 5 秒，失败按待发队列规则从 1 分钟、5 分钟、30 分钟、2 小时起重试。每次发送留状态和错误类别日志，不保存正文；管理用例可对原事件重新排队，重发仍使用相同 `eventId` 和正文。尚未登记回调地址时不调用业务系统，事件仍在操作记录中。
  接入应用被停用后，已有待发回调暂停发送并记录 `CLIENT_APP_DISABLED`，仍留在队列等待后续重试；重新启用后可送达同一事件。
- **取数接口**：只传单号发起时，审批服务按单号回业务系统取审批数据。
- **校验钩子**：发起前、某个节点同意前问业务系统能不能过，不能过时返回原因。
- **取人接口**：审批人来源选“业务系统接口”时返回用户 ID 列表。
- **业务动作接口**：自动节点调用，如审批通过后生成采购订单。

四类钩子的精确请求/响应与双向验签协议尚待确认，提议见 [ADR 0010](adr/0010-outbound-hook-protocol.md)，确认前不实现。

## 流程定义 JSON

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
