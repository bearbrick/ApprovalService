using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Center;

/// <summary>
/// 用已验证的人员会话办理任务，所有状态变更仍经统一运行时入口。
/// </summary>
/// <param name="tasks">仅定位任务所属审批单。</param>
/// <param name="center">按人员和嵌入应用授权详情的仓储。</param>
/// <param name="organizations">可信内部人员与字段值查询。</param>
/// <param name="apps">任务所属业务应用查询。</param>
/// <param name="contextBuilder">最新组织和矩阵的引擎上下文。</param>
/// <param name="runtime">唯一引擎命令入口。</param>
/// <param name="hooks">同意前业务校验钩子。</param>
/// <param name="timeProvider">固定本次操作时间。</param>
public sealed class CenterActionAppService(IApprovalOpenQueryRepository tasks,
    ICenterQueryRepository center, IOrganizationRepository organizations, IClientAppRepository apps,
    OpenStartContextBuilder contextBuilder, IApprovalRuntimeAppService runtime,
    BusinessHookAppService hooks, TimeProvider timeProvider)
{
    /// <summary>
    /// 人工任务可提交的命令白名单；实例级和管理员命令不在这里办理。
    /// </summary>
    private static readonly HashSet<string> TaskActions =
    [
        EngineCommandKinds.Approve, EngineCommandKinds.Reject, EngineCommandKinds.Handle,
        EngineCommandKinds.Acknowledge, OperationKinds.Return, OperationKinds.Transfer,
        OperationKinds.AddSigner, OperationKinds.RemoveSigner, OperationKinds.RevokeApproval,
        OperationKinds.Comment, OperationKinds.SaveDraft, OperationKinds.CopyTo,
        OperationKinds.Consult, OperationKinds.Reply, OperationKinds.RequestSupplement,
    ];

    /// <summary>
    /// 独立执行一项人工命令，服务端再次核对当前状态和人员许可。
    /// </summary>
    /// <param name="scope">认证中间件生成的人员与应用范围。</param>
    /// <param name="taskId">人工任务 ID。</param>
    /// <param name="input">操作参数和内部人员 ID。</param>
    /// <param name="requestId">稳定请求 ID。</param>
    /// <param name="isBulk">批量同意时为真。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>操作后再次授权读取的详情。</returns>
    public async Task<CenterTaskActionResponse> ExecuteAsync(CenterScope scope, string taskId,
        CenterTaskActionRequest input, string requestId, bool isBulk = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(scope.PersonId) || string.IsNullOrWhiteSpace(taskId) || taskId.Length > 100 ||
            input is null || !TaskActions.Contains(input.Action) ||
            string.IsNullOrWhiteSpace(requestId) || input.MentionedPersonIds is null)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "任务操作参数或类型不正确。");
        }
        var record = await tasks.LoadTaskAsync(taskId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批任务不存在或当前人员无权查看。");
        // 先用查询仓储的 SQL 边界授权，不能仅凭任务 ID 绕过保密或嵌入应用范围。
        if (await center.FindAsync(scope, record.Instance.Id, cancellationToken) is null)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "审批任务不存在或当前人员无权查看。");
        }
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(record.FlowVersion.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单锁定的执行图为空。");
        var definition = JsonSerializer.Deserialize<FlowDefinition>(record.FlowVersion.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单锁定的流程定义为空。");
        var state = JsonSerializer.Deserialize<EngineState>(record.Instance.StateJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单运行状态为空。");
        var allowed = TaskActionAvailability.GetAllowedActions(state, graph, taskId, scope.PersonId);
        if (!allowed.Contains(input.Action, StringComparer.Ordinal) ||
            (isBulk && !allowed.Contains(OperationKinds.BulkApprove, StringComparer.Ordinal)))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "当前人员或节点不允许此操作。");
        }
        var node = graph.Nodes.SingleOrDefault(item => item.Id == record.Task.NodeId)
            ?? throw new InvalidOperationException("任务引用的执行节点不存在。");
        var targetId = await ActivePersonAsync(input.TargetPersonId, cancellationToken);
        var mentioned = new List<string>();
        foreach (var personId in input.MentionedPersonIds)
        {
            mentioned.Add(await ActivePersonAsync(personId, cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed, "提及的人员不存在或已停用。"));
        }
        var fields = await CenterActionFieldValidator.ValidateAsync(definition, node, input.Action,
            input.FieldValues, organizations, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var context = await contextBuilder.BuildContinuationAsync(record.Instance, definition, graph,
            now, cancellationToken);
        if (input.Action == EngineCommandKinds.Approve)
        {
            var app = await apps.FindByCodeAsync(record.Instance.ClientAppCode, cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.Conflict, "审批单所属接入应用不存在。");
            using var data = JsonDocument.Parse(record.Instance.ApprovalDataJson);
            await hooks.ValidateAsync(app, new ValidationHookRequest
            {
                Phase = "approve",
                FlowCode = record.Instance.FlowCode,
                BusinessKey = record.Instance.BusinessKey,
                InstanceId = record.Instance.Id,
                TaskId = taskId,
                // 中心登录人员不一定在该业务应用存在反向绑定；这里明确传审批服务内部人员 ID。
                Actor = scope.PersonId,
                Data = data.RootElement,
            }, requestId, cancellationToken);
        }
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
                    ActorId = scope.PersonId,
                    Opinion = input.Opinion,
                    TargetUserId = targetId,
                    TargetNodeId = input.TargetNodeId,
                    RelatedId = input.RelatedId,
                    Mode = input.Mode,
                    MentionedUserIds = mentioned,
                    FieldValues = fields,
                    IsBulkApproval = isBulk,
                },
            },
            Context = context,
        }, cancellationToken);
        var updated = await center.FindAsync(scope, record.Instance.Id, cancellationToken)
            ?? throw new InvalidOperationException("操作成功后当前人员失去了审批单读取权限。");
        return new CenterTaskActionResponse
        {
            TaskId = taskId,
            InstanceId = record.Instance.Id,
            Detail = CenterProjection.Detail(updated, scope.PersonId),
        };
    }

    /// <summary>
    /// 批量逐项执行；可预期的单项业务错误不回滚已成功任务。
    /// </summary>
    /// <param name="scope">已认证人员范围。</param>
    /// <param name="input">任务列表和意见。</param>
    /// <param name="requestId">批次稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>与任务顺序一致的逐项结果。</returns>
    public async Task<CenterBatchApproveResponse> BatchApproveAsync(CenterScope scope,
        CenterBatchApproveRequest input, string requestId, CancellationToken cancellationToken = default)
    {
        if (input?.TaskIds is null || input.TaskIds.Count is < 1 or > 100 ||
            input.TaskIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 100) ||
            input.TaskIds.Distinct(StringComparer.Ordinal).Count() != input.TaskIds.Count ||
            string.IsNullOrWhiteSpace(requestId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "批量任务列表不正确，最多一百项且不能重复。");
        }
        var results = new List<CenterBatchApproveItemResponse>();
        for (var index = 0; index < input.TaskIds.Count; index++)
        {
            var taskId = input.TaskIds[index];
            try
            {
                var completed = await ExecuteAsync(scope, taskId, new CenterTaskActionRequest
                {
                    Action = EngineCommandKinds.Approve,
                    Opinion = input.Opinion,
                }, requestId + ":" + index, true, cancellationToken);
                results.Add(new CenterBatchApproveItemResponse
                {
                    TaskId = taskId,
                    Succeeded = true,
                    InstanceId = completed.InstanceId,
                });
            }
            catch (UserFriendlyException error)
            {
                results.Add(new CenterBatchApproveItemResponse
                {
                    TaskId = taskId,
                    Code = error.Code,
                    Message = error.Message,
                });
            }
        }
        return new CenterBatchApproveResponse { Items = results };
    }

    /// <summary>
    /// 验证可选内部人员目标必须仍在职。
    /// </summary>
    /// <param name="personId">目标内部人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>有效目标 ID，空输入返回空。</returns>
    private async Task<string?> ActivePersonAsync(string? personId, CancellationToken cancellationToken)
    {
        if (personId is null)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(personId) ||
            await organizations.GetPersonAsync(personId, cancellationToken) is not { IsActive: true })
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "目标人员不存在或已停用。");
        }
        return personId;
    }
}
