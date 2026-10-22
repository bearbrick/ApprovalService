using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 接入应用为本应用任务提交已绑定人员的审批与协作命令。
/// </summary>
/// <param name="repository">任务归属查询。</param>
/// <param name="organization">本应用严格人员绑定。</param>
/// <param name="contextBuilder">最新组织与矩阵的引擎输入。</param>
/// <param name="runtime">唯一命令入口。</param>
/// <param name="queries">操作后的审批单详情。</param>
/// <param name="timeProvider">固定操作时间。</param>
public sealed class OpenTaskActionAppService(IApprovalOpenQueryRepository repository,
    IOrganizationLookupAppService organization, OpenStartContextBuilder contextBuilder,
    IApprovalRuntimeAppService runtime, OpenApprovalQueryAppService queries, TimeProvider timeProvider)
{
    /// <summary>
    /// 开放接口允许的人工任务命令；管理员和实例级命令须走各自权限入口。
    /// </summary>
    private static readonly HashSet<string> AllowedActions =
    [
        EngineCommandKinds.Approve,
        EngineCommandKinds.Reject,
        EngineCommandKinds.Handle,
        EngineCommandKinds.Acknowledge,
        OperationKinds.Return,
        OperationKinds.Transfer,
        OperationKinds.AddSigner,
        OperationKinds.RemoveSigner,
        OperationKinds.RevokeApproval,
        OperationKinds.Comment,
        OperationKinds.SaveDraft,
        OperationKinds.CopyTo,
        OperationKinds.Consult,
        OperationKinds.Reply,
        OperationKinds.RequestSupplement,
    ];

    /// <summary>
    /// 仅本应用任务、已绑定操作人和节点规则全部允许时执行人工命令。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="input">业务人员及命令参数。</param>
    /// <param name="requestId">稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>操作后的审批单详情。</returns>
    public async Task<OpenInstanceResponse> ExecuteAsync(ClientAppEntity app, string taskId,
        OpenTaskActionRequest input, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(taskId) || input is null || string.IsNullOrWhiteSpace(input.User) ||
            !AllowedActions.Contains(input.Action) || string.IsNullOrWhiteSpace(requestId) ||
            input.MentionedUsers is null)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "任务操作参数或操作类型不正确。");
        }

        var record = await repository.LoadTaskAsync(taskId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批任务不存在。");
        if (record.Instance.ClientAppCode != app.Code)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用不能操作其他应用的审批任务。");
        }

        var actorId = await RequireBoundPersonAsync(app.Code, input.User, cancellationToken);
        var targetId = input.TargetUser is null ? null :
            await RequireBoundPersonAsync(app.Code, input.TargetUser, cancellationToken);
        var mentioned = new List<string>();
        foreach (var externalId in input.MentionedUsers)
        {
            mentioned.Add(await RequireBoundPersonAsync(app.Code, externalId, cancellationToken));
        }

        var definition = JsonSerializer.Deserialize<FlowDefinition>(record.FlowVersion.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单锁定的流程定义为空。");
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(record.FlowVersion.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单锁定的执行图为空。");
        var node = graph.Nodes.SingleOrDefault(item => item.Id == record.Task.NodeId)
            ?? throw new InvalidOperationException("审批任务引用的执行图节点不存在。");
        var fieldValues = await OpenActionFieldValidator.ValidateAsync(app.Code, definition, node,
            input.FieldValues, organization, cancellationToken);
        var context = await contextBuilder.BuildContinuationAsync(record.Instance, definition, graph,
            timeProvider.GetUtcNow(), cancellationToken);
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = input.Action,
                InstanceId = record.Instance.Id,
                TaskId = taskId,
                RequestId = requestId,
                Operation = new OperationInput
                {
                    ActorId = actorId,
                    Opinion = input.Opinion,
                    TargetUserId = targetId,
                    TargetNodeId = input.TargetNodeId,
                    RelatedId = input.RelatedId,
                    Mode = input.Mode,
                    MentionedUserIds = mentioned,
                    FieldValues = fieldValues,
                },
            },
            Context = context,
        }, cancellationToken);
        return await queries.GetInstanceAsync(app, record.Instance.Id, cancellationToken);
    }

    /// <summary>
    /// 把业务人员 ID 映射为当前应用绑定的在职内部人员。
    /// </summary>
    /// <param name="appCode">已认证应用编码。</param>
    /// <param name="externalId">业务人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>内部人员 ID。</returns>
    private async Task<string> RequireBoundPersonAsync(string appCode, string? externalId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "任务操作的人员 ID 不能为空。");
        }

        return await organization.ResolveBoundPersonAsync(appCode, externalId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.Forbidden,
                "任务操作人员未在当前接入应用绑定或已离职。");
    }
}
