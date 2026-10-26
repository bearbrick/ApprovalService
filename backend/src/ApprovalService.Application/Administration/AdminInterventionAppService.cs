using System.Security.Cryptography;
using System.Text;
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

namespace ApprovalService.Application.Administration;

/// <summary>
/// 已授权管理员的安全查询与统一运行命令用例。
/// </summary>
/// <param name="repository">当前范围查询。</param>
/// <param name="runtime">统一运行入口。</param>
/// <param name="contexts">最新组织和节点上下文。</param>
/// <param name="timeProvider">固定操作时间。</param>
public sealed class AdminInterventionAppService(IAdminInterventionRepository repository,
    IApprovalRuntimeAppService runtime, OpenStartContextBuilder contexts, TimeProvider timeProvider)
{
    /// <summary>
    /// 列出最近一百张有权干预的在途单据。
    /// </summary>
    /// <param name="actorId">可信人员 ID。</param>
    /// <param name="isLocal">本地会话标识。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>不含业务快照的摘要。</returns>
    public async Task<IReadOnlyList<AdminInterventionResponse>> ListAsync(string actorId, bool isLocal,
        CancellationToken cancellationToken = default) =>
        (await repository.ReadAsync(actorId, isLocal, null, cancellationToken)).Select(Project).ToArray();

    /// <summary>
    /// 重新授权读取指定单据及操作选项。
    /// </summary>
    /// <param name="id">审批单 ID。</param>
    /// <param name="actorId">可信人员 ID。</param>
    /// <param name="isLocal">本地会话标识。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>真实运行详情。</returns>
    public async Task<AdminInterventionResponse> GetAsync(string id, string actorId, bool isLocal,
        CancellationToken cancellationToken = default) =>
        Project(await RequireAsync(id, actorId, isLocal, cancellationToken));

    /// <summary>
    /// 提交改派、跳转或终止，运行时和最终事务再次核对状态及授权。
    /// </summary>
    /// <param name="id">审批单 ID。</param>
    /// <param name="actorId">可信人员 ID。</param>
    /// <param name="isLocal">本地会话标识。</param>
    /// <param name="requestId">客户端稳定请求 ID。</param>
    /// <param name="input">干预参数。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>命令已执行；刷新详情仍需重新授权。</returns>
    public async Task ExecuteAsync(string id, string actorId, bool isLocal, string requestId,
        AdminInterventionRequest input, CancellationToken cancellationToken = default)
    {
        if (input is null || input.Reason?.Trim().Length is not (>= 2 and <= 200) ||
            string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64 || input.ExpectedVersion < 1 ||
            input.Action is not (OperationKinds.Reassign or OperationKinds.Jump or EngineCommandKinds.Terminate))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "干预类型、原因、请求 ID 或运行修订不正确。");
        }
        var record = await RequireAsync(id, actorId, isLocal, cancellationToken);
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(record.FlowVersion.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("发布执行图为空。");
        var definition = JsonSerializer.Deserialize<FlowDefinition>(record.FlowVersion.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("发布定义为空。");
        if (input.Action == OperationKinds.Reassign &&
            (string.IsNullOrWhiteSpace(input.TaskId) || string.IsNullOrWhiteSpace(input.TargetPersonId) ||
                input.TargetPersonId.Length > 100 || input.TargetNodeId is not null))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请选择当前任务并填写接手人的内部人员 ID。");
        }
        if (input.Action == OperationKinds.Jump && (input.TaskId is not null || input.TargetPersonId is not null ||
            !graph.Nodes.Any(node => node.Id == input.TargetNodeId && IsManual(node.Type))))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请选择锁定发布版本中的人工节点。");
        }
        if (input.Action == EngineCommandKinds.Terminate &&
            (input.TaskId is not null || input.TargetPersonId is not null || input.TargetNodeId is not null))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "终止审批不接受任务或人员参数。");
        }
        var context = await contexts.BuildContinuationAsync(record.Instance, definition, graph,
            timeProvider.GetUtcNow(), cancellationToken);
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            ExpectedInstanceVersion = input.ExpectedVersion,
            InterventionActorId = actorId,
            InterventionTargetId = input.TargetPersonId,
            Command = new EngineCommand
            {
                Kind = input.Action,
                InstanceId = id,
                TaskId = input.TaskId,
                // 人员隔离避免两位管理员碰巧复用同一个客户端请求 ID。
                RequestId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actorId + ":" + requestId))),
                Operation = new OperationInput
                {
                    ActorId = actorId,
                    IsAdministrator = true,
                    Reason = input.Reason.Trim(),
                    TargetUserId = input.TargetPersonId,
                    TargetNodeId = input.TargetNodeId,
                },
            },
            Context = context,
        }, cancellationToken);
    }

    /// <summary>
    /// 统一隐藏不可见与不存在记录。
    /// </summary>
    /// <param name="id">审批单 ID。</param>
    /// <param name="actorId">人员 ID。</param>
    /// <param name="isLocal">本地会话标识。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>有权限的运行快照。</returns>
    private async Task<RuntimeSnapshot> RequireAsync(string id, string actorId, bool isLocal,
        CancellationToken cancellationToken) =>
        (await repository.ReadAsync(actorId, isLocal, id, cancellationToken)).SingleOrDefault()
        ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在或当前账号无权干预。");

    /// <summary>
    /// 将运行快照裁剪成干预需要的元数据，不发送业务字段和完整执行图。
    /// </summary>
    /// <param name="record">已授权记录。</param>
    /// <returns>安全元数据。</returns>
    private static AdminInterventionResponse Project(RuntimeSnapshot record)
    {
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(record.FlowVersion.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("发布执行图为空。");
        return new AdminInterventionResponse
        {
            Id = record.Instance.Id,
            Title = record.Instance.Title,
            SerialNo = record.Instance.SerialNo,
            FlowCode = record.Instance.FlowCode,
            FlowVersionId = record.Instance.FlowVersionId,
            Status = record.Instance.Status,
            Version = record.Instance.Version,
            Tasks = record.Tasks.Where(task => task.Status is EngineTaskStatuses.Pending or EngineTaskStatuses.Queued)
                .Select(task => new AdminInterventionTask
                {
                    Id = task.Id,
                    NodeId = task.NodeId,
                    AssigneeId = task.AssigneeId,
                    Status = task.Status,
                    Name = graph.Nodes.Single(node => node.Id == task.NodeId).Name,
                }).ToArray(),
            Nodes = graph.Nodes.Where(node => IsManual(node.Type)).Select(node => new AdminInterventionNode
            {
                Id = node.Id,
                Name = node.Name,
            }).ToArray(),
        };
    }

    /// <summary>
    /// 本入口只允许明确的人工节点跳转，不暴露网关、自动或子流程跳转。
    /// </summary>
    /// <param name="type">节点类型。</param>
    /// <returns>人工节点时为真。</returns>
    private static bool IsManual(string type) =>
        type is FlowNodeTypes.Approval or FlowNodeTypes.Handling or FlowNodeTypes.Cc;
}
