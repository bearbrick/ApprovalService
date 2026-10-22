using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 撤销同意后恢复待办投影与回调的真实 HTTP 和 SQLite 场景。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 核对审批意见留痕、原待办恢复和稳定回调正文。
    /// </summary>
    /// <param name="services">已迁移服务。</param>
    /// <param name="http">真实 HTTP 客户端。</param>
    /// <param name="apiKey">示例应用 API Key。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunRevokeAsync(IServiceProvider services, HttpClient http, string apiKey,
        Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        graph.Nodes.Single(node => node.Id == "n1").Assignee =
            new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "approver" };
        graph.Nodes.Single(node => node.Id == "n2").Assignee =
            new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "proxy" };
        var definition = new FlowDefinition { Code = "REVOKE", Name = "撤销同意回归" };
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Flows.Add(new FlowEntity
            {
                Id = "revoke-flow",
                Code = definition.Code,
                Name = definition.Name,
                PublishedVersionId = "revoke-version",
            });
            db.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "revoke-version",
                FlowId = "revoke-flow",
                Version = 1,
                SchemaVersion = 1,
                DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
                GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
            });
            await db.SaveChangesAsync();
        }

        using var startRequest = Request(new OpenStartRequest
        {
            FlowCode = "REVOKE",
            BusinessKey = "REVOKE-1",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { }),
        }, apiKey, "revoke-start");
        using var startResponse = await http.SendAsync(startRequest);
        var started = await startResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("撤销同意场景先建立原审批任务",
            startResponse.StatusCode == HttpStatusCode.OK && started?.Tasks.Count == 1);
        var taskId = started!.Tasks.Single().Id;

        using var approveRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            Opinion = "批准但尚未进入下一步处理",
        }, apiKey, "revoke-approve");
        using var approveResponse = await http.SendAsync(approveRequest);
        var approved = await approveResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("同意后原任务记录意见和完成时间且下一步仍待办",
            approveResponse.StatusCode == HttpStatusCode.OK &&
            approved?.Tasks.Single(task => task.Id == taskId) is
            { Status: EngineTaskStatuses.Approved, Opinion: "批准但尚未进入下一步处理", CompletedAt: not null } &&
            approved.Tasks.Any(task => task.NodeId == "n2" && task.Status == EngineTaskStatuses.Pending));

        using var revokeRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = OperationKinds.RevokeApproval,
        }, apiKey, "revoke-action");
        using var revokeResponse = await http.SendAsync(revokeRequest);
        var restored = await revokeResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("撤销同意后原任务恢复待办且不显示已撤销意见与完成时间",
            revokeResponse.StatusCode == HttpStatusCode.OK &&
            restored?.Tasks.Single(task => task.Id == taskId) is
            { Status: EngineTaskStatuses.Pending, Opinion: null, CompletedAt: null } &&
            restored.Tasks.All(task => task.NodeId != "n2"));
        check("已撤销同意仍留在只追加事件链中",
            restored?.Events.Any(item => item.Event == "taskCompleted" &&
                item.Detail.GetProperty("audit").GetProperty("opinion").GetString() == "批准但尚未进入下一步处理") == true &&
            restored.Events.Any(item => item.Event == "approvalRevoked"));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var pending = await db.ApprovalTasks.AsNoTracking().SingleAsync(task => task.Id == taskId);
            var outbox = await db.OutboxItems.AsNoTracking().Where(item => item.InstanceId == started.Id &&
                item.Kind == "callback").ToArrayAsync();
            var restoredItem = outbox.Single(item => item.PayloadJson.Contains("task.restored", StringComparison.Ordinal));
            using var body = JsonDocument.Parse(restoredItem.PayloadJson);
            check("恢复待办投影落库且同事务回调携带原任务 ID 与稳定事件 ID",
                pending.Status == EngineTaskStatuses.Pending && pending.Opinion is null && pending.CompletedAt is null &&
                body.RootElement.GetProperty("taskId").GetString() == taskId &&
                body.RootElement.GetProperty("eventId").GetString() == restoredItem.EventId &&
                body.RootElement.GetProperty("status").GetString() == EngineInstanceStatuses.Running);
        }
    }
}
