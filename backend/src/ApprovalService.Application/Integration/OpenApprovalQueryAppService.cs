using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 以接入应用授权和已解析的内部人员 ID 查询审批单与待办。
/// </summary>
/// <param name="repository">开放接口只读仓储。</param>
/// <param name="timeProvider">可替换时钟。</param>
public sealed class OpenApprovalQueryAppService(IApprovalOpenQueryRepository repository, TimeProvider timeProvider)
{
    /// <summary>
    /// 读取本接入应用发起的一张审批单及其任务、审计记录。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>审批单详情。</returns>
    public async Task<OpenInstanceResponse> GetInstanceAsync(ClientAppEntity app, string instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var instance = await repository.LoadInstanceAsync(instanceId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在。");
        RequireOwner(app, instance);
        var details = await repository.LoadDetailsAsync(instanceId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在。");
        RequireOwner(app, details.Instance);
        return ProjectInstance(details.Instance, details);
    }

    /// <summary>
    /// 按流程编码和业务单号读取本接入应用的历史审批单，最新的排在前面。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>审批单摘要列表。</returns>
    public async Task<IReadOnlyList<OpenInstanceResponse>> FindInstancesAsync(ClientAppEntity app,
        string flowCode, string businessKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(flowCode) || string.IsNullOrWhiteSpace(businessKey))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "查询时必须同时提供流程编码和业务单号。");
        }

        var records = await repository.ListInstancesAsync(flowCode, businessKey, app.Code, cancellationToken);
        return records.Select(item => ProjectInstance(item, null)).ToArray();
    }

    /// <summary>
    /// 发起前查全局在途单号；别的应用占用时只返回冲突，不泄露审批单。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本应用现有在途单；未占用时为空。</returns>
    public async Task<OpenInstanceResponse?> FindActiveForStartAsync(ClientAppEntity app, string flowCode,
        string businessKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(flowCode) || string.IsNullOrWhiteSpace(businessKey))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "流程编码和业务单号不能为空。");
        }

        var active = await repository.LoadActiveAsync(flowCode, businessKey, cancellationToken);
        if (active is null)
        {
            return null;
        }

        if (active.ClientAppCode != app.Code)
        {
            // 在途唯一键按流程和业务单号全局生效，但不能把其他应用的单据当成幂等结果。
            throw new UserFriendlyException(ErrorCodes.Conflict, "该流程的业务单号已有在途审批，请核对单号。");
        }

        return ProjectInstance(active, null);
    }

    /// <summary>
    /// 在数据库直接汇总某人当前待办数量。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="personId">服务端解析出的内部人员 ID。</param>
    /// <param name="allApps">是否请求跨接入应用统计。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>待办、加急、超时数量。</returns>
    public async Task<OpenTaskCountResponse> CountTasksAsync(ClientAppEntity app, string personId, bool allApps,
        CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope(app, personId, allApps);
        var counts = await repository.CountPendingTasksAsync(personId, scope, timeProvider.GetUtcNow(), cancellationToken);
        return new OpenTaskCountResponse
        {
            Pending = counts.Pending,
            Urgent = counts.Urgent,
            Overdue = counts.Overdue,
        };
    }

    /// <summary>
    /// 分页读取某人当前待办和锁定版本定义的摘要字段。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="personId">服务端解析出的内部人员 ID。</param>
    /// <param name="allApps">是否请求跨接入应用列表。</param>
    /// <param name="skip">跳过数量。</param>
    /// <param name="take">每页数量，一至一百。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>待办列表。</returns>
    public async Task<IReadOnlyList<OpenTaskResponse>> ListTasksAsync(ClientAppEntity app, string personId,
        bool allApps, int skip, int take, CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope(app, personId, allApps);
        if (skip < 0 || take is < 1 or > 100)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "待办分页参数不正确，每页最多一百条。");
        }

        var records = await repository.ListPendingTasksAsync(personId, scope, skip, take, cancellationToken);
        return records.Select(ProjectTask).ToArray();
    }

    /// <summary>
    /// 限定待办查询范围，跨应用权限只由接入应用配置决定。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="personId">可信内部人员 ID。</param>
    /// <param name="allApps">是否请求跨应用范围。</param>
    /// <returns>接入应用过滤编码；空表示全部。</returns>
    private static string? ResolveScope(ClientAppEntity app, string personId, bool allApps)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(personId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "待办人员不能为空。");
        }

        if (allApps && !app.CanViewAllTasks)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用没有查看全部待办的权限。");
        }

        return allApps ? null : app.Code;
    }

    /// <summary>
    /// 审批单查询只向发起它的接入应用返回业务数据。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instance">待读取审批单。</param>
    private static void RequireOwner(ClientAppEntity app, InstanceEntity instance)
    {
        if (instance.ClientAppCode != app.Code)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用没有查看该审批单的权限。");
        }
    }

    /// <summary>
    /// 将运行实体转为开放接口的只读快照。
    /// </summary>
    /// <param name="instance">审批单。</param>
    /// <param name="details">任务与事件；摘要查询时为空。</param>
    /// <returns>开放响应。</returns>
    private static OpenInstanceResponse ProjectInstance(InstanceEntity instance, OpenInstanceDetails? details)
    {
        using var data = JsonDocument.Parse(instance.ApprovalDataJson);
        return new OpenInstanceResponse
        {
            Id = instance.Id,
            SerialNo = instance.SerialNo,
            FlowCode = instance.FlowCode,
            BusinessKey = instance.BusinessKey,
            Title = instance.Title,
            InitiatorId = instance.InitiatorId,
            InitiatorDepartmentId = instance.InitiatorDepartmentId,
            SubmittedById = instance.SubmittedById,
            ClientAppCode = instance.ClientAppCode,
            Status = instance.Status,
            Data = data.RootElement.Clone(),
            StartedAt = instance.StartedAt,
            EndedAt = instance.EndedAt,
            Tasks = details?.Tasks.Select(task => new OpenInstanceTaskResponse
            {
                Id = task.Id,
                NodeId = task.NodeId,
                AssigneeId = task.AssigneeId,
                Status = task.Status,
                Opinion = task.Opinion,
                DueAt = task.DueAt,
                CompletedAt = task.CompletedAt,
            }).ToArray() ?? [],
            Events = details?.Events.Select(ProjectEvent).ToArray() ?? [],
        };
    }

    /// <summary>
    /// 克隆事件详情后释放临时 JSON 文档。
    /// </summary>
    /// <param name="item">持久操作记录。</param>
    /// <returns>开放事件。</returns>
    private static OpenOperationResponse ProjectEvent(OperationEventEntity item)
    {
        using var detail = JsonDocument.Parse(item.PayloadJson);
        return new OpenOperationResponse
        {
            EventId = item.Id,
            Sequence = item.Sequence,
            Event = item.Kind,
            Detail = detail.RootElement.Clone(),
            OccurredAt = item.OccurredAt,
        };
    }

    /// <summary>
    /// 按流程版本的字段 key 过滤摘要，其他业务数据不进入待办列表。
    /// </summary>
    /// <param name="record">待办及锁定流程版本。</param>
    /// <returns>一条开放待办。</returns>
    private static OpenTaskResponse ProjectTask(OpenTaskRecord record)
    {
        var definition = JsonSerializer.Deserialize<FlowDefinition>(record.FlowVersion.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("已发布流程定义为空。");
        using var data = JsonDocument.Parse(record.Instance.ApprovalDataJson);
        var summary = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var key in definition.Data.SummaryFields)
        {
            if (data.RootElement.TryGetProperty(key, out var value))
            {
                summary[key] = value.Clone();
            }
        }

        return new OpenTaskResponse
        {
            Id = record.Task.Id,
            InstanceId = record.Instance.Id,
            FlowCode = record.Instance.FlowCode,
            BusinessKey = record.Instance.BusinessKey,
            Title = record.Instance.Title,
            NodeId = record.Task.NodeId,
            IsUrgent = record.Instance.IsUrgent,
            DueAt = record.Task.DueAt,
            Summary = JsonSerializer.SerializeToElement(summary, ApprovalJson.Options),
            OpenPath = "#/center/approvals/" + Uri.EscapeDataString(record.Instance.Id),
        };
    }
}
