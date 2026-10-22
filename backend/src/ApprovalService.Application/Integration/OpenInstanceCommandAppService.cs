using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 在接入应用和真实操作人双重授权后执行实例级命令。
/// </summary>
/// <param name="repository">运行快照仓储。</param>
/// <param name="organization">本应用严格人员绑定查询。</param>
/// <param name="runtime">引擎唯一命令入口。</param>
/// <param name="queries">本应用审批单查询。</param>
/// <param name="timeProvider">固定操作时间。</param>
public sealed class OpenInstanceCommandAppService(IApprovalRuntimeRepository repository,
    IOrganizationLookupAppService organization, IApprovalRuntimeAppService runtime,
    OpenApprovalQueryAppService queries, TimeProvider timeProvider)
{
    /// <summary>
    /// 撤回本应用发起人的在途审批。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="input">业务人员标识。</param>
    /// <param name="requestId">链路中的稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>撤回后的审批单。</returns>
    public Task<OpenInstanceResponse> WithdrawAsync(ClientAppEntity app, string instanceId,
        OpenInstanceCommandRequest input, string requestId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(app, instanceId, EngineCommandKinds.Withdraw, input, requestId, cancellationToken);

    /// <summary>
    /// 对本应用审批单的当前任务催办。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="input">业务人员标识。</param>
    /// <param name="requestId">链路中的稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>催办后的审批单。</returns>
    public Task<OpenInstanceResponse> RemindAsync(ClientAppEntity app, string instanceId,
        OpenInstanceCommandRequest input, string requestId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(app, instanceId, OperationKinds.Remind, input, requestId, cancellationToken);

    /// <summary>
    /// 本应用以已绑定的实际操作人和原因终止在途审批。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="input">实际操作人和终止原因。</param>
    /// <param name="requestId">链路中的稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>终止后的审批单。</returns>
    public Task<OpenInstanceResponse> TerminateAsync(ClientAppEntity app, string instanceId,
        OpenInstanceCommandRequest input, string requestId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(app, instanceId, EngineCommandKinds.Terminate, input, requestId, cancellationToken);

    /// <summary>
    /// 核对审批单归属和人员绑定；撤回与催办还要求实际操作人是发起人。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="kind">已登记的实例级命令。</param>
    /// <param name="input">业务人员标识。</param>
    /// <param name="requestId">链路中的稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>操作后的同应用审批单详情。</returns>
    private async Task<OpenInstanceResponse> ExecuteAsync(ClientAppEntity app, string instanceId, string kind,
        OpenInstanceCommandRequest input, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (kind is not (EngineCommandKinds.Withdraw or EngineCommandKinds.Terminate or OperationKinds.Remind) ||
            string.IsNullOrWhiteSpace(instanceId) || input is null || string.IsNullOrWhiteSpace(input.User) ||
            string.IsNullOrWhiteSpace(requestId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批操作参数不正确。");
        }

        if (kind == EngineCommandKinds.Terminate && string.IsNullOrWhiteSpace(input.Reason))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "终止审批必须填写原因。");
        }

        var snapshot = await repository.LoadAsync(instanceId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在。");
        if (snapshot.Instance.ClientAppCode != app.Code)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用不能操作其他应用的审批单。");
        }

        var actorId = await organization.ResolveBoundPersonAsync(app.Code, input.User, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.Forbidden, "操作人员未在当前接入应用绑定或已离职。");
        if (kind != EngineCommandKinds.Terminate && actorId != snapshot.Instance.InitiatorId)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只有审批发起人可以执行此操作。");
        }

        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = kind,
                InstanceId = instanceId,
                RequestId = requestId,
                Operation = new OperationInput
                {
                    ActorId = actorId,
                    Reason = kind == EngineCommandKinds.Terminate ? input.Reason : null,
                    IsOwningClientApp = kind == EngineCommandKinds.Terminate,
                },
            },
            Context = new EngineContext
            {
                OccurredAt = timeProvider.GetUtcNow(),
                Operations = new OperationContext
                {
                    InitiatorId = snapshot.Instance.InitiatorId,
                    FlowCode = snapshot.Instance.FlowCode,
                },
            },
        }, cancellationToken);
        return await queries.GetInstanceAsync(app, instanceId, cancellationToken);
    }
}
