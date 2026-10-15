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
| `GET /health/ready` | 无 | 就绪检查，返回 `ready`（以后加上数据库连通性） |
| `GET /api/v1/info` | 无 | 服务信息 |

`GET /api/v1/info` 的响应：

```json
{ "name": "ApprovalService", "version": "0.1.0", "apiVersion": "v1" }
```

## 规划中的接口

按设计书第 13 节，在对应任务里实现，实现时把请求和响应的格式补进本文：

| 接口 | 用途 | 任务 |
| --- | --- | --- |
| `GET /api/v1/flows`、`GET /api/v1/flows/{code}/schema`、`POST /api/v1/flows/{code}/preview` | 能用的流程、要传的审批数据、预览会走哪些节点 | T-006 |
| `POST /api/v1/instances`、`POST /api/v1/instances/batch` | 发起审批（传数据，或只传单号让服务回业务系统取） | T-006 |
| `GET /api/v1/instances/{id}`、`PATCH /api/v1/instances/{id}/data` | 查询状态和审批记录、审批中更新允许更新的字段 | T-006 |
| `POST /api/v1/instances/{id}/withdraw`、`/resubmit`、`/remind`、`/terminate` | 撤回、重新提交、催办、业务单据作废时终止 | T-006 |
| `GET /api/v1/instances/{id}/print` | 审批单 PDF | T-015 |
| `GET /api/v1/tasks/count?user=`、`GET /api/v1/tasks?user=`、`POST /api/v1/tasks/{id}/actions` | 待办数量、待办列表、审批操作 | T-006 |
| `POST /api/v1/files`、`GET /api/v1/files/{id}` | 上传、下载附件 | T-013 |
| `POST /api/v1/embed-tokens` | 签发嵌入令牌，换成免登录打开地址 | T-007 |
| `POST /api/v1/org/changes`、`POST /api/v1/org/bindings` | 推送组织架构变更、绑定业务系统的用户和部门 | T-007 |

## 回调和业务系统提供的接口（规划中）

审批服务调业务系统的都经出站适配层，带 `X-Approval-Signature` 签名，默认 5 秒超时，失败按 1 分钟、5 分钟、30 分钟、2 小时……重试（T-006）。

- **结果回调**：审批状态变化时发 POST，请求体带 `eventId`（同一事件可能重发，按它去重）、`event`、`instanceId`、`serialNo`、`flowCode`、`businessKey`、`status`、`data`（审批人填写的字段）、`occurredAt`。事件：发起、节点完成（可选）、通过、拒绝、撤回、退回给发起人、终止、撤销、数据被更新，以及（可选订阅）新待办、待办已处理。
- **取数接口**：只传单号发起时，审批服务按单号回业务系统取审批数据。
- **校验钩子**：发起前、某个节点同意前问业务系统能不能过，不能过时返回原因。
- **取人接口**：审批人来源选“业务系统接口”时返回用户 ID 列表。
- **业务动作接口**：自动节点调用，如审批通过后生成采购订单。

## 流程定义 JSON（待定）

流程版本里保存的设计内容、编译后的执行图、审批数据定义、详情排版和打印设置，格式在 T-001 里定，定下后写进本节。原则：

- 带格式版本号（`schemaVersion`），老版本的流程一直能跑。
- 仅自由画布设计 JSON，发布时编译成执行图（节点、连线、网关）；不再定义树形设计或转换协议（ADR 0007）。此处仍为规划，已实现接口不变。
- 自由画布导出为 BPMN 2.0 文件，加本系统的扩展信息；只导入本系统导出的文件。

## 骨架阶段补充的约定

设计书没写明、骨架阶段先定下的，实现对应任务时可以提出修改：

- 方法不对（405）时错误码用 `NOT_FOUND`，不另设错误码。
- 健康检查返回纯文本（`live`、`ready`），不是 JSON。
- 调用方传的 `X-Request-Id` 最长 64 个字符，只允许字母数字和 `- _ . :`。
