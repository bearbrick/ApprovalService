using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 退回或补充后由所属业务系统代表发起人提交完整新数据。
/// </summary>
/// <param name="repository">审批单和锁定版本仓储。</param>
/// <param name="organization">当前应用严格人员绑定。</param>
/// <param name="contexts">最新可信组织和数据路由快照。</param>
/// <param name="runtime">唯一引擎命令入口。</param>
/// <param name="queries">更新后的审批单查询。</param>
/// <param name="timeProvider">固定命令时间。</param>
public sealed class OpenResubmitAppService(IApprovalRuntimeRepository repository,
    IOrganizationLookupAppService organization, OpenStartContextBuilder contexts,
    IApprovalRuntimeAppService runtime, OpenApprovalQueryAppService queries, TimeProvider timeProvider)
{
    /// <summary>
    /// 按锁定发布定义校验新数据，以同一命令原子恢复审批任务和快照。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="request">发起人与完整新数据。</param>
    /// <param name="requestId">稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>恢复后的审批单。</returns>
    public async Task<OpenInstanceResponse> SubmitAsync(ClientAppEntity app, string instanceId,
        OpenResubmitRequest request, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(requestId) || request is null ||
            string.IsNullOrWhiteSpace(request.User) || request.Data.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "重新提交必须包含已绑定发起人和完整数据对象。");
        }

        var snapshot = await repository.LoadAsync(instanceId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在。");
        var instance = snapshot.Instance;
        if (instance.ClientAppCode != app.Code)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用不能重新提交其他应用的审批单。");
        }

        var actorId = await organization.ResolveBoundPersonAsync(app.Code, request.User, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.Forbidden, "操作人员未在当前接入应用绑定或已离职。");
        if (actorId != instance.InitiatorId)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只有原发起人可以重新提交审批。");
        }

        var state = JsonSerializer.Deserialize<EngineState>(instance.StateJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单引擎状态为空。");
        if (state.ProcessedRequestIds.Contains(requestId))
        {
            return await queries.GetInstanceAsync(app, instanceId, cancellationToken);
        }

        var kind = state.Operations.Pause?.Kind switch
        {
            "return" when state.Status == EngineInstanceStatuses.AwaitingInitiator => OperationKinds.Resubmit,
            "supplement" when state.Status == EngineInstanceStatuses.AwaitingInitiator => OperationKinds.Supplement,
            _ => throw new UserFriendlyException(ErrorCodes.Conflict, "审批单当前没有待提交的退回或补充请求。"),
        };
        var definition = JsonSerializer.Deserialize<FlowDefinition>(snapshot.FlowVersion.DefinitionJson,
            ApprovalJson.Options) ?? throw new InvalidOperationException("审批单锁定的流程定义为空。");
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(snapshot.FlowVersion.GraphJson,
            ApprovalJson.Options) ?? throw new InvalidOperationException("审批单锁定的执行图为空。");
        var prepared = OpenApprovalDataPreparer.Prepare(definition.Data, request.Data);
        var inputs = await contexts.RefreshRoutingInputsAsync(app.Code, graph, prepared,
            instance.RoutingInputsJson, cancellationToken);
        var routingJson = JsonSerializer.Serialize(inputs, ApprovalJson.Options);
        // 只修改无追踪读取快照以构造本次路由；引擎成功后仓储才原子写入。
        instance.ApprovalDataJson = prepared.GetRawText();
        instance.RoutingInputsJson = routingJson;
        var context = await contexts.BuildContinuationAsync(instance, definition, graph,
            timeProvider.GetUtcNow(), cancellationToken);
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = kind,
                InstanceId = instanceId,
                RequestId = requestId,
                Operation = new OperationInput { ActorId = actorId },
            },
            Context = context,
            UpdatedApprovalDataJson = instance.ApprovalDataJson,
            UpdatedRoutingInputsJson = routingJson,
        }, cancellationToken);
        return await queries.GetInstanceAsync(app, instanceId, cancellationToken);
    }
}
