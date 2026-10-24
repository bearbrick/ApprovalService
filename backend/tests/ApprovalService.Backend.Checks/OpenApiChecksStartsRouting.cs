using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 开放发起检查的跨节点路由场景。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 发起时选择固定，后续节点到达按最新组织找人。
    /// </summary>
    /// <param name="services">已迁移的检查服务。</param>
    /// <param name="http">真实 HTTP 客户端。</param>
    /// <param name="apiKey">示例应用 API Key。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunRoutingAsync(IServiceProvider services, HttpClient http, string apiKey,
        Action<string, bool> check)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        var selectedGraph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Cc);
        selectedGraph.Nodes.Single(node => node.Id == "n1").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.Member,
            Value = "approver",
        };
        selectedGraph.Nodes.Single(node => node.Id == "n2").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.InitiatorSelection,
        };
        selectedGraph.Nodes.Single(node => node.Id == "n2").Approval = new FlowApprovalSettings
        {
            ReadReceiptRequired = true,
        };
        var dynamicGraph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval, FlowNodeTypes.Approval);
        dynamicGraph.Nodes.Single(node => node.Id == "n1").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.Member,
            Value = "approver",
        };
        dynamicGraph.Nodes.Single(node => node.Id == "n2").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.DepartmentHead,
        };
        var dataDefinition = new FlowDataDefinition
        {
            Fields = [new FlowFieldDefinition { Key = "amount", Name = "金额", Type = FlowFieldTypes.Money, Required = true }],
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Flows.AddRange(
                new FlowEntity { Id = "selected-flow", Code = "SELECTED", Name = "自选抄送", PublishedVersionId = "selected-version" },
                new FlowEntity { Id = "dynamic-flow", Code = "DYNAMIC", Name = "动态部门", PublishedVersionId = "dynamic-version" });
            db.FlowVersions.AddRange(
                new FlowVersionEntity
                {
                    Id = "selected-version",
                    FlowId = "selected-flow",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition
                    {
                        Code = "SELECTED",
                        Name = "自选抄送",
                        Data = dataDefinition,
                    }, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(selectedGraph, ApprovalJson.Options),
                },
                new FlowVersionEntity
                {
                    Id = "dynamic-version",
                    FlowId = "dynamic-flow",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition
                    {
                        Code = "DYNAMIC",
                        Name = "动态部门",
                        Data = dataDefinition,
                    }, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(dynamicGraph, ApprovalJson.Options),
                });
            var department = await db.Departments.SingleAsync(item => item.Id == "department-1");
            department.HeadPersonId = "approver";
            await db.SaveChangesAsync();
        }

        using var selectedStartRequest = Request(new OpenStartRequest
        {
            FlowCode = "SELECTED",
            BusinessKey = "SEL-1",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { amount = 1 }),
            SelectedAssignees = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["n2"] = ["secretary"],
            },
        }, apiKey, "selected-start");
        using var selectedStartResponse = await http.SendAsync(selectedStartRequest);
        var selectedStart = await selectedStartResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("发起时自选抄送节点先保存首节点任务", selectedStartResponse.StatusCode == HttpStatusCode.OK &&
            selectedStart?.Tasks.Single().AssigneeId == "approver");
        using var selectedApproveRequest = ActionRequest(selectedStart!.Tasks.Single().Id,
            new OpenTaskActionRequest { User = "bob", Action = EngineCommandKinds.Approve }, apiKey, "selected-approve");
        using var selectedApproveResponse = await http.SendAsync(selectedApproveRequest);
        var selectedApproved = await selectedApproveResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("跨节点推进复用发起时自选抄送人而不丢失快照",
            selectedApproveResponse.StatusCode == HttpStatusCode.OK &&
            selectedApproved?.Tasks.Any(task => task.NodeId == "n2" && task.AssigneeId == "proxy" &&
                task.Status == EngineTaskStatuses.Pending) == true);

        using var dynamicStartRequest = Request(new OpenStartRequest
        {
            FlowCode = "DYNAMIC",
            BusinessKey = "DYN-1",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { amount = 1 }),
        }, apiKey, "dynamic-start");
        using var dynamicStartResponse = await http.SendAsync(dynamicStartRequest);
        var dynamicStart = await dynamicStartResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("组织调整前动态流程首节点分配成功", dynamicStartResponse.StatusCode == HttpStatusCode.OK &&
            dynamicStart?.Tasks.Single().AssigneeId == "approver");
        await using (var db = await factory.CreateDbContextAsync())
        {
            var department = await db.Departments.SingleAsync(item => item.Id == "department-1");
            department.HeadPersonId = "proxy";
            await db.SaveChangesAsync();
        }

        using var dynamicApproveRequest = ActionRequest(dynamicStart!.Tasks.Single().Id,
            new OpenTaskActionRequest { User = "bob", Action = EngineCommandKinds.Approve }, apiKey, "dynamic-approve");
        using var dynamicApproveResponse = await http.SendAsync(dynamicApproveRequest);
        var dynamicApproved = await dynamicApproveResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("后续节点到达时按最新组织主数据分配部门负责人",
            dynamicApproveResponse.StatusCode == HttpStatusCode.OK &&
            dynamicApproved?.Tasks.Any(task => task.NodeId == "n2" && task.AssigneeId == "proxy" &&
                task.Status == EngineTaskStatuses.Pending) == true);
    }
}
