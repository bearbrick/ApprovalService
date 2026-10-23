using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Jobs;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 事务外派发自动动作，并以同一事件 ID 幂等提交引擎结果。
/// </summary>
/// <param name="repository">审批单及发布版本快照。</param>
/// <param name="apps">所属接入应用登记。</param>
/// <param name="hooks">双向验签的业务钩子接口。</param>
/// <param name="contexts">后续节点的可信组织与路由快照。</param>
/// <param name="runtime">引擎命令入口。</param>
/// <param name="timeProvider">命令发生时间。</param>
public sealed class EngineEventOutboxHandler(IApprovalRuntimeRepository repository,
    IClientAppRepository apps, IApprovalHookClient hooks, OpenStartContextBuilder contexts,
    IApprovalRuntimeAppService runtime, TimeProvider timeProvider) : IOutboxHandler
{
    /// <inheritdoc />
    public string Kind => "engineEvent";

    /// <inheritdoc />
    public async Task DeliverAsync(OutboxItemEntity item, CancellationToken cancellationToken)
    {
        var source = JsonSerializer.Deserialize<EngineEvent>(item.PayloadJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("引擎待发项缺少事件正文。");
        if (source.Id != item.EventId)
        {
            throw new InvalidOperationException("引擎待发项事件身份不一致。");
        }

        if (source.Kind == "subprocessRequested")
        {
            throw new InvalidOperationException("子流程执行器尚未配置，待发项将在配置后重试。");
        }

        if (source.Kind != "actionPending")
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(source.TokenId) || string.IsNullOrWhiteSpace(source.TargetCode))
        {
            throw new InvalidOperationException("自动动作事件缺少令牌或动作编码。");
        }

        var snapshot = await repository.LoadAsync(item.InstanceId, cancellationToken)
            ?? throw new InvalidOperationException("自动动作对应的审批单不存在。");
        var state = JsonSerializer.Deserialize<EngineState>(snapshot.Instance.StateJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("自动动作对应的引擎状态为空。");
        if (state.ProcessedRequestIds.Contains(source.Id))
        {
            // 动作成功后确认待发项若失败，下一次领取不再发送第二次 HTTP 请求。
            return;
        }

        var token = state.Tokens.SingleOrDefault(candidate => candidate.Id == source.TokenId);
        if (token is null || token.WaitingFor != "action" || token.NodeId != source.NodeId)
        {
            // 审批单可能已被终止；失效的自动动作不应再发送。
            return;
        }

        var app = await apps.FindByCodeAsync(snapshot.Instance.ClientAppCode, cancellationToken);
        if (app is null || !app.IsEnabled)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "审批单所属接入应用不存在或已停用。");
        }

        if (string.IsNullOrWhiteSpace(app.ActionUrl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前接入应用未配置业务动作接口。");
        }

        var definition = JsonSerializer.Deserialize<FlowDefinition>(snapshot.FlowVersion.DefinitionJson,
            ApprovalJson.Options) ?? throw new InvalidOperationException("已发布流程定义为空。");
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(snapshot.FlowVersion.GraphJson,
            ApprovalJson.Options) ?? throw new InvalidOperationException("已发布执行图为空。");
        var context = await contexts.BuildContinuationAsync(snapshot.Instance, definition, graph,
            timeProvider.GetUtcNow(), cancellationToken);
        using var data = JsonDocument.Parse(snapshot.Instance.ApprovalDataJson);
        var response = await hooks.InvokeAsync(app, app.ActionUrl, new ActionHookRequest
        {
            EventId = source.Id,
            ActionCode = source.TargetCode,
            InstanceId = item.InstanceId,
            FlowCode = snapshot.Instance.FlowCode,
            BusinessKey = snapshot.Instance.BusinessKey,
            Data = data.RootElement,
        }, source.Id, cancellationToken);
        ActionHookResponse? result;
        try
        {
            result = response.Deserialize<ActionHookResponse>(ApprovalJson.Options);
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务系统动作响应格式不正确。");
        }

        if (result?.Success != true)
        {
            // 业务失败与网络失败同样保留原事件并按队列退避重试。
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed,
                "业务系统动作未成功，审批将在待发队列中重试。");
        }

        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = EngineCommandKinds.ActionSucceeded,
                InstanceId = item.InstanceId,
                TokenId = source.TokenId,
                RequestId = source.Id,
                ActionResult = result.Result?.Clone(),
            },
            Context = context,
        }, cancellationToken);
    }
}
