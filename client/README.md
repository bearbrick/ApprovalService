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

## 用法（现在只有服务信息）

```csharp
using var client = new ApprovalServiceClient(new ApprovalServiceClientOptions
{
    BaseAddress = new Uri("http://localhost:5280/"),
    ApiKey = Environment.GetEnvironmentVariable("APPROVAL_API_KEY"),
});
var info = await client.GetInfoAsync();   // ApprovalService 0.1.0，接口 v1
```

出错时抛 `ApprovalServiceException`，`ErrorCode` 是服务端的错误码，或者 SDK 自己的 `NETWORK_ERROR`（连不上）、`TIMEOUT`（超时）。

## 命令

```powershell
dotnet build client/ApprovalService.Client.slnx
dotnet format client/ApprovalService.Client.slnx --verify-no-changes --no-restore
dotnet run --project client/tests/ApprovalService.Client.Checks
```
