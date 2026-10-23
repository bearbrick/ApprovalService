using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Runtime;

/// <summary>
/// 读取发布版本和状态，计算引擎结果，再以乐观锁原子提交事件与待发项。
/// </summary>
/// <param name="repository">原子保存状态和事件的仓储。</param>
/// <param name="businessAssignees">实际节点到达前的业务取人服务。</param>
public sealed class ApprovalRuntimeAppService(IApprovalRuntimeRepository repository,
    BusinessAssigneeAppService? businessAssignees = null) : IApprovalRuntimeAppService
{
    /// <inheritdoc />
    public async Task<EngineResult> ExecuteAsync(RuntimeCommandInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Command);
        ArgumentNullException.ThrowIfNull(input.Context);

        var command = input.Command;
        RuntimeSnapshot? snapshot = null;
        FlowVersionEntity flowVersion;
        InstanceEntity instance;
        EngineState? current = null;
        if (command.Kind == EngineCommandKinds.Start)
        {
            snapshot = await repository.LoadAsync(command.InstanceId, cancellationToken);
            if (snapshot is not null)
            {
                // 同一请求 ID 再次发起时由引擎返回幂等重放；不同请求由引擎报状态冲突。
                instance = snapshot.Instance;
                flowVersion = snapshot.FlowVersion;
                current = JsonSerializer.Deserialize<EngineState>(instance.StateJson, ApprovalJson.Options)
                    ?? throw new InvalidOperationException("已保存的引擎状态为空。");
            }
            else
            {
                var proposed = input.StartInstance ??
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed, "发起审批缺少审批单信息。");
                if (proposed.Id != command.InstanceId || string.IsNullOrWhiteSpace(proposed.FlowVersionId))
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批单 ID 或流程版本不正确。");
                }

                flowVersion = await repository.LoadVersionAsync(proposed.FlowVersionId, cancellationToken)
                    ?? throw new UserFriendlyException(ErrorCodes.NotFound, "流程版本不存在。");
                instance = proposed;
                instance.FlowId = flowVersion.FlowId;
            }
        }
        else
        {
            snapshot = await repository.LoadAsync(command.InstanceId, cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在。");
            instance = snapshot.Instance;
            flowVersion = snapshot.FlowVersion;
            current = JsonSerializer.Deserialize<EngineState>(instance.StateJson, ApprovalJson.Options)
                ?? throw new InvalidOperationException("已保存的引擎状态为空。");
        }

        // 每张审批单始终使用发起时锁定版本的执行图，调用方不能替换它。
        input.Context.Graph = JsonSerializer.Deserialize<FlowExecutionGraph>(flowVersion.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("已发布的执行图为空。");
        if (input.UpdatedApprovalDataJson is not null || input.UpdatedRoutingInputsJson is not null)
        {
            if (command.Kind is not (ApprovalService.Engine.Commands.OperationKinds.Resubmit or
                    ApprovalService.Engine.Commands.OperationKinds.Supplement or
                    ApprovalService.Engine.Commands.OperationKinds.UpdateData) ||
                input.UpdatedApprovalDataJson is null || input.UpdatedRoutingInputsJson is null)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批数据与找人映射必须随允许的数据更新命令一起提交。");
            }

            // 数据只在引擎命令成功后随同状态、事件和回调快照进入同一事务。
            instance.ApprovalDataJson = input.UpdatedApprovalDataJson;
            instance.RoutingInputsJson = input.UpdatedRoutingInputsJson;
        }

        EngineResult result;
        var requestedNodes = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            try
            {
                result = EngineRuntime.Execute(current, command, input.Context);
            }
            catch (EngineException error)
            {
                throw new UserFriendlyException(error.Code, error.Message);
            }

            var requirement = result.RequiredBusinessAssignee;
            if (requirement is null)
            {
                break;
            }

            // 引擎只报告实际命中的节点；每次重算沿用原状态、请求 ID、时间及执行图。
            if (!requestedNodes.Add(requirement.NodeId) || requestedNodes.Count > input.Context.Graph.Nodes.Count)
            {
                throw new InvalidOperationException("业务取人节点预取出现重复或超出执行图范围。");
            }

            if (businessAssignees is null)
            {
                throw new InvalidOperationException("运行时缺少业务取人服务登记。");
            }

            await businessAssignees.PrepareAsync(instance, requirement, input.Context, command.RequestId,
                cancellationToken);
        }

        if (result.IsReplay)
        {
            return result;
        }

        var expectedVersion = snapshot?.Instance.Version;
        var nextVersion = expectedVersion.GetValueOrDefault() + 1;
        instance.Version = nextVersion;
        instance.Status = result.State.Status;
        instance.StateJson = JsonSerializer.Serialize(result.State, ApprovalJson.Options);
        if (command.Kind == EngineCommandKinds.Start)
        {
            instance.StartedAt = input.Context.OccurredAt;
        }

        if (result.State.Status is not (EngineInstanceStatuses.Running or EngineInstanceStatuses.AwaitingInitiator))
        {
            instance.EndedAt ??= input.Context.OccurredAt;
        }

        var previousStatuses = current?.Tasks.ToDictionary(task => task.Id, task => task.Status) ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        var tasks = result.State.Tasks.Select(task => new ApprovalTaskEntity
        {
            Id = task.Id,
            InstanceId = instance.Id,
            NodeId = task.NodeId,
            AssigneeId = task.AssigneeId,
            Status = task.Status,
            Opinion = command.TaskId == task.Id && task.Status != EngineTaskStatuses.Pending &&
                previousStatuses.TryGetValue(task.Id, out var previous) && previous == EngineTaskStatuses.Pending
                    ? command.Operation?.Opinion : null,
            CompletedAt = task.Status != EngineTaskStatuses.Pending &&
                (!previousStatuses.TryGetValue(task.Id, out var previousStatus) || previousStatus == EngineTaskStatuses.Pending)
                    ? input.Context.OccurredAt
                    : null,
        }).ToArray();
        var events = BuildEvents(instance.Id, command.RequestId, result.Events, snapshot?.LastEvent);
        var outbox = result.Events.Select(item =>
        {
            var notification = CallbackNotifications.Create(item, instance);
            return notification is not null ? new OutboxItemEntity
            {
                Id = item.Id + ":callback",
                InstanceId = instance.Id,
                EventId = item.Id,
                Kind = "callback",
                PayloadJson = CallbackNotifications.Serialize(notification),
                AvailableAt = item.OccurredAt,
            } : new OutboxItemEntity
            {
                Id = item.Id + ":dispatch",
                InstanceId = instance.Id,
                EventId = item.Id,
                Kind = "engineEvent",
                PayloadJson = JsonSerializer.Serialize(item, ApprovalJson.Options),
                AvailableAt = item.OccurredAt,
            };
        }).ToArray();

        // 仓储在同一个事务中更新版本、任务投影、事件链和待发项；抢输乐观锁时不重算命令。
        var saved = await repository.CommitAsync(new RuntimeCommit
        {
            Instance = instance,
            ExpectedVersion = expectedVersion,
            Tasks = tasks,
            Events = events,
            OutboxItems = outbox,
        }, cancellationToken);
        if (!saved)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "审批单刚被其他人处理，请刷新后重试。");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyEventChainAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var snapshot = await repository.LoadEventChainAsync(instanceId, cancellationToken);
        if (snapshot is null)
        {
            return false;
        }

        return OperationHash.Verify(snapshot.Events, snapshot.LastEventSequence, snapshot.LastEventHash);
    }

    /// <summary>
    /// 以上一条记录为起点构造连续的事件校验链。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="requestId">处理请求 ID。</param>
    /// <param name="source">引擎新产生的事件。</param>
    /// <param name="lastEvent">已保存的最后一条事件。</param>
    /// <returns>带顺序号和校验值的新记录。</returns>
    private static IReadOnlyList<OperationEventEntity> BuildEvents(
        string instanceId,
        string requestId,
        IReadOnlyList<EngineEvent> source,
        OperationEventEntity? lastEvent)
    {
        var entries = new List<OperationEventEntity>(source.Count);
        var sequence = lastEvent?.Sequence ?? 0;
        var previousHash = lastEvent?.Hash ?? string.Empty;
        foreach (var item in source)
        {
            var entry = new OperationEventEntity
            {
                Id = item.Id,
                InstanceId = instanceId,
                Sequence = ++sequence,
                RequestId = requestId,
                Kind = item.Kind,
                PayloadJson = JsonSerializer.Serialize(item, ApprovalJson.Options),
                PreviousHash = previousHash,
                OccurredAt = item.OccurredAt,
            };
            entry.Hash = OperationHash.Compute(entry);
            entries.Add(entry);
            previousHash = entry.Hash;
        }

        return entries;
    }
}
