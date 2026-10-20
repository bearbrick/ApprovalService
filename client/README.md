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

## 用法（首批开放接口）

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

以上为各方法的独立用法示例；催办、撤回和审批操作仍受服务端当前状态与人员权限限制。`StartAsync` 当前必须提供 `Data` 快照，只传单号取数的钩子协议尚待确认。`GetTaskCountAsync` 与 `GetTasksAsync` 默认只看本应用，接入应用获授权时可传 `allApps: true`。任务 ID 可能包含斜杠，SDK 会正确编码。`FieldValues` 仅提交当前节点允许审批人填写的顶层字段，不修改原业务数据快照。错误响应统一抛 `ApprovalServiceException`。

尚未确认的更新数据、重新提交、终止、批量、预览与钩子等接口由后续 SDK 任务补齐；本批方法不包含界面组件。

出错时抛 `ApprovalServiceException`，`ErrorCode` 是服务端的错误码，或者 SDK 自己的 `NETWORK_ERROR`（连不上）、`TIMEOUT`（超时）。

创建客户端时，服务地址必须是完整的 HTTP 或 HTTPS 地址，可以带路径前缀，不能带账号、查询参数或片段；超时默认 30 秒，可设置为正数或 `Timeout.InfiniteTimeSpan`。API Key 可以不填（只查服务信息），填了就不能是空白或包含换行符。客户端会固定创建时的地址、Key 和超时，修改原来的 `ApprovalServiceClientOptions` 不影响已有客户端；一个客户端可供多个调用共享，使用完释放。

调用方主动取消 `GetInfoAsync(cancellationToken)` 时保留 `OperationCanceledException`；客户端自身超时返回 `TIMEOUT`。若代理返回非 JSON 错误页，SDK 仍按 HTTP 状态归类常见的 400、401、403、404、405、409；服务信息缺少必填字段会报 `INTERNAL_ERROR`，便于发现地址指错或服务协议不匹配。

## 命令

```powershell
dotnet build client/ApprovalService.Client.slnx
dotnet format client/ApprovalService.Client.slnx --verify-no-changes --no-restore
dotnet run --project client/tests/ApprovalService.Client.Checks
```
