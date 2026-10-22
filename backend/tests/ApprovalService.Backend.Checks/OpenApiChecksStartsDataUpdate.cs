using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 开放审批数据更新的运行时原子保存场景。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 验证重新提交时新审批数据、找人映射与状态一起落库，错误命令不会改动数据。
    /// </summary>
    /// <param name="services">已迁移的检查服务。</param>
    /// <param name="started">正在处理的审批单。</param>
    /// <param name="definition">审批单锁定的流程定义。</param>
    /// <param name="graph">审批单锁定的执行图。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunDataUpdateStorageAsync(IServiceProvider services, OpenInstanceResponse started,
        FlowDefinition definition, FlowExecutionGraph graph, Action<string, bool> check)
    {
        var repository = services.GetRequiredService<IApprovalRuntimeRepository>();
        var runtime = services.GetRequiredService<IApprovalRuntimeAppService>();
        var contexts = services.GetRequiredService<OpenStartContextBuilder>();
        var before = await repository.LoadAsync(started.Id)
            ?? throw new InvalidOperationException("测试审批单未持久化。");
        var oldData = before.Instance.ApprovalDataJson;
        var oldRouting = before.Instance.RoutingInputsJson;
        var changedData = JsonSerializer.Serialize(new
        {
            amount = 9,
            reviewer = "bob",
        }, ApprovalJson.Options);
        var dataPersonGraph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        dataPersonGraph.Nodes.Single(node => node.Id == "n1").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.DataPerson,
            FieldKey = "reviewer",
        };
        var trustedRouting = await contexts.RefreshRoutingInputsAsync("demo", dataPersonGraph,
            JsonSerializer.SerializeToElement(new
            {
                amount = 9,
                reviewer = "bob"
            }), oldRouting);
        var changedRouting = JsonSerializer.Serialize(trustedRouting, ApprovalJson.Options);
        check("业务数据找人字段更新时重新解析本应用可信人员绑定",
            trustedRouting.DataValues["reviewer"] == "approver");
        var currentContext = await contexts.BuildContinuationAsync(before.Instance, definition, graph,
            DateTimeOffset.UtcNow);
        var rejected = false;
        try
        {
            await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = new EngineCommand
                {
                    Kind = EngineCommandKinds.Approve,
                    InstanceId = started.Id,
                    TaskId = started.Tasks.Single().Id,
                    RequestId = "data-update-wrong-operation",
                    Operation = new OperationInput { ActorId = "approver" },
                },
                Context = currentContext,
                UpdatedApprovalDataJson = changedData,
                UpdatedRoutingInputsJson = changedRouting,
            });
        }
        catch (UserFriendlyException error) when (error.Code == ErrorCodes.ValidationFailed)
        {
            rejected = true;
        }

        var afterRejected = await repository.LoadAsync(started.Id)
            ?? throw new InvalidOperationException("测试审批单未持久化。");
        check("非重提命令不能偷偷替换审批数据和找人映射", rejected &&
            afterRejected.Instance.ApprovalDataJson == oldData &&
            afterRejected.Instance.RoutingInputsJson == oldRouting &&
            afterRejected.Instance.Version == before.Instance.Version);

        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = OperationKinds.Return,
                InstanceId = started.Id,
                TaskId = started.Tasks.Single().Id,
                RequestId = "data-update-return",
                Operation = new OperationInput { ActorId = "approver" },
            },
            Context = currentContext,
        });
        var returned = await repository.LoadAsync(started.Id)
            ?? throw new InvalidOperationException("退回后审批单未持久化。");
        returned.Instance.ApprovalDataJson = changedData;
        returned.Instance.RoutingInputsJson = changedRouting;
        var updatedContext = await contexts.BuildContinuationAsync(returned.Instance, definition, graph,
            DateTimeOffset.UtcNow);
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = OperationKinds.Resubmit,
                InstanceId = started.Id,
                RequestId = "data-update-resubmit",
                Operation = new OperationInput { ActorId = "requester" },
            },
            Context = updatedContext,
            UpdatedApprovalDataJson = changedData,
            UpdatedRoutingInputsJson = changedRouting,
        });
        var saved = await repository.LoadAsync(started.Id)
            ?? throw new InvalidOperationException("重提后审批单未持久化。");
        check("重提时审批数据与找人映射随状态在同一事务保存",
            saved.Instance.Status == EngineInstanceStatuses.Running &&
            saved.Instance.ApprovalDataJson == changedData &&
            saved.Instance.RoutingInputsJson == changedRouting &&
            saved.Instance.Version == returned.Instance.Version + 1);
    }
}
