# 调用端（client/）

给业务系统用的 .NET SDK，以及示例业务系统。规则见根目录 [AGENTS.md](../AGENTS.md) 的 R4、R14。

## 项目

| 项目 | 目标框架 | 说明 |
| --- | --- | --- |
| `src/ApprovalService.Client` | net461、netstandard2.0、net8.0 | SDK：把开放接口包成 C# 方法。不依赖任何 NuGet 包（JSON 读写是自带的） |
| `samples/ApprovalService.Demo.Integration` | net48、net10.0 | 示例业务系统的对接类库：调审批接口、处理回调、取数接口、钩子都在这里，不依赖界面框架，业务系统照抄它 |
| `samples/ApprovalService.Demo.WinUI` | net10.0-windows | 示例业务系统的 WinUI 3 界面（还没建：要先下载 Windows App SDK，见任务文件） |
| `tests/ApprovalService.Client.Checks` | net10.0 | 检查程序：SDK 行为、架构约定、注释；设置 `APPROVAL_SERVICE_URL` 时加跑端到端检查 |

审批服务对业务系统**只提供数据接口**（开放接口和这个 SDK），不提供界面组件；待办角标、审批窗口这类界面由业务系统自己做，或者跳转、内嵌审批中心的页面。示例业务系统把这些界面做了一遍给大家参考。

公司暂时没有私有的 NuGet 源，SDK 先不发布：业务系统按接口文档直接调，或者把 SDK 的 DLL 拷进项目引用。

## 用法（已实现开放接口）

```csharp
using var client = new ApprovalServiceClient(new ApprovalServiceClientOptions
{
    BaseAddress = new Uri("http://localhost:5280/"),
    ApiKey = Environment.GetEnvironmentVariable("APPROVAL_API_KEY"),
});
var info = await client.GetInfoAsync();   // ApprovalService 0.1.0，接口 v1
var flows = await client.GetFlowsAsync();
var schema = await client.GetFlowSchemaAsync("PURCHASE_REQUEST");
var instance = await client.StartAsync(new StartApprovalRequest
{
    FlowCode = "PURCHASE_REQUEST", BusinessKey = order.No,
    Title = order.Title, Initiator = currentUser.Code,
    Data = new Dictionary<string, object?> { ["amount"] = order.Amount },
});
var current = await client.GetInstanceAsync(instance.Id);
var history = await client.FindInstancesAsync("PURCHASE_REQUEST", order.No);
var count = await client.GetTaskCountAsync(currentUser.Code);
var tasks = await client.GetTasksAsync(currentUser.Code, skip: 0, take: 20);
// 下列操作按业务需要分别调用；示例不表示要顺序执行。
await client.ActOnTaskAsync(tasks[0].Id, new ApprovalTaskActionRequest
{
    User = currentUser.Code, Action = "approve", Opinion = "同意",
    FieldValues = new Dictionary<string, object?> { ["approvalNote"] = "已核对" },
});
await client.RemindAsync(instance.Id, currentUser.Code);
await client.WithdrawAsync(instance.Id, currentUser.Code);
```

以上为各方法的独立用法示例；催办、撤回和审批操作仍受服务端当前状态与人员权限限制。`StartAsync` 可以提供 `Data` 快照，也可以在接入应用已登记取数钩子时省略；重试时可使用接受稳定 `requestId` 的重载。`GetTaskCountAsync` 与 `GetTasksAsync` 默认只看本应用，接入应用获授权时可传 `allApps: true`。任务 ID 可能包含斜杠，SDK 会正确编码。`FieldValues` 仅提交当前节点允许审批人填写的顶层字段，不修改原业务数据快照。错误响应统一抛 `ApprovalServiceException`。

SDK 还提供 `PreviewAsync`、`StartBatchAsync`（逐张独立返回结果）、`PatchDataAsync`（只改流程显式允许的字段）、`ResubmitAsync`、`TerminateAsync`、`CreateEmbedTokenAsync`、`PushOrganizationChangesAsync`、`BindOrganizationIdAsync`。这些方法分别遵守后端的流程、人员绑定和接入应用权限；批量、PATCH、重新提交和终止均可传稳定 `requestId` 用于网络失败重试。后端尚未实现的打印 PDF 接口不在 SDK 中。

附件使用 `UploadFileAsync(fileName, stream)` 发送原始字节，服务器按内容识别文件类型；上传流仍由调用方释放。`DownloadUnlinkedFileAsync(fileId)` 只允许当前 API Key 获取本应用上传且尚未关联审批单的附件；已关联附件必须调用 `DownloadFileAsync(fileId, accessToken)`，由服务端按人员会话验权。服务器返回对象存储预签名跳转时，SDK 跟随一次跳转且不向目标转发 API Key 或人员令牌。

业务系统收到回调时，把 HTTP 原始正文、唯一 `X-Approval-Signature` 头值和登记的签名密钥交给 `ApprovalCallbackVerifier.VerifyAndParse(body, signature, secret)`；验签后按 `EventId` 去重。不要先把正文重新序列化。自动业务动作结果位于 `ActionResults`，不覆盖 `Data`。

出错时抛 `ApprovalServiceException`，`ErrorCode` 是服务端的错误码，或者 SDK 自己的 `NETWORK_ERROR`（连不上）、`TIMEOUT`（超时）。

创建客户端时，服务地址必须是完整的 HTTP 或 HTTPS 地址，可以带路径前缀，不能带账号、查询参数或片段；超时默认 30 秒，可设置为正数或 `Timeout.InfiniteTimeSpan`。API Key 可以不填（只查服务信息），填了就不能是空白或包含换行符。客户端会固定创建时的地址、Key 和超时，修改原来的 `ApprovalServiceClientOptions` 不影响已有客户端；一个客户端可供多个调用共享，使用完释放。

调用方主动取消 `GetInfoAsync(cancellationToken)` 时保留 `OperationCanceledException`；客户端自身超时返回 `TIMEOUT`。若代理返回非 JSON 错误页，SDK 仍按 HTTP 状态归类常见的 400、401、403、404、405、409；服务信息缺少必填字段会报 `INTERNAL_ERROR`，便于发现地址指错或服务协议不匹配。

## 示例业务系统的独立对接类库

`ApprovalService.Demo.Integration` 现有费用报销和采购申请的业务原单、独立本机 JSON 数据文件，以及用 SDK 发起、撤回、重提、待办操作和查询同步的薄对接层。业务后端用 `DemoBusinessHttpHost` 在回环地址提供 `/data`、`/validate`、`/assignees`、`/action`、`/callback` 五个完整 URL；请求先对原始字节验 HMAC，响应签名并回显请求 ID。回调按 `eventId` 在业务文件中持久去重，自动采购动作按同一事件 ID 重试和复用订单号。关闭本机后端时，客户端仍可调用 `DemoApprovalGateway.RefreshAsync` 在打开单据时同步状态。

示例 `/callback` 收到 `integration.test` 时只对原始正文验签并返回确认，不查业务单号或修改原单；SDK 的 `ApprovalCallbackEventCodes.IntegrationTest` 与可选 `ClientAppCode` 可供业务系统识别测试事件。真实审批事件仍按原事件 ID 持久去重和同步业务状态。

启动本机后端时只使用回环地址，例如 `http://127.0.0.1:6017/`；将五个完整地址分别登记在演示接入应用配置中。API Key 和签名密钥应从运行环境读入，业务文件不存密钥。`DemoOrganizationSeed.Build()` 可先预览设计书中的 7 个部门、19 个人员和 26 项绑定，`ApplyAsync` 通过 SDK 顺序推送；当前演示流程的角色配置与兼容 BPMN 文件导入仍需管理后台完成，不能把组织推送视为已经发布流程。

“对接改动”按源码文件统计（不含 SDK 和界面）：

| 文件 | 作用 | 行数 |
| --- | --- | ---: |
| `ApprovalConnection.cs` | 创建 SDK 客户端与连接状态 | 45 |
| `ExpenseClaim.cs` | 报销原单和审批数据映射 | 144 |
| `PurchaseRequest.cs` | 采购原单和审批数据映射 | 163 |
| `DemoDocumentStore.cs` | 独立业务文件、查询与回调同步 | 423 |
| `DemoDocumentStore.Actions.cs` | 采购动作事件幂等 | 137 |
| `DemoApprovalGateway.cs` | SDK 发起、重提、待办和状态刷新 | 201 |
| `DemoHmacSignature.cs` | 原始字节 HMAC 验签 | 62 |
| `DemoHookModels.cs` | 四类钩子的请求与响应 JSON | 363 |
| `DemoBusinessHttpHost.cs` | 本机业务 HTTP 后端与回调入口 | 443 |
| `DemoOrganizationSeed.cs` | 演示组织和应用绑定准备 | 190 |

调用端检查程序现用假审批服务和真实回环 HTTP 验证这些对接路径。完整 17 个演示场景仍需要流程管理的兼容 BPMN 导入、委托和打印接口，再做一键流程发布及场景串测。

## 命令

```powershell
dotnet build client/ApprovalService.Client.slnx
dotnet format client/ApprovalService.Client.slnx --verify-no-changes --no-restore
dotnet run --project client/tests/ApprovalService.Client.Checks
```

从仓库根目录运行完整 `verify.ps1` 时，脚本创建一次性测试接入应用和已发布流程，通过真实 HTTP 验证 SDK 的预览、发起、批量发起、查询、待办、审批、催办、撤回和附件上传下载；临时人员没有登录账号，因此令牌接口验证明确的未开通错误。API Key 只放在该次运行的临时文件中；单独运行调用端检查而未提供测试夹具时，会跳过需要已登记应用的成功链路。
