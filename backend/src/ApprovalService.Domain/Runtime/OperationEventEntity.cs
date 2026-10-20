namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 只追加的审批操作记录，相邻记录的校验值首尾相接。
/// </summary>
public sealed class OperationEventEntity
{
    /// <summary>
    /// 引擎事件 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 所属审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 审批单内从一开始的顺序号。
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// 请求 ID，用于追踪与幂等处理。
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 事件类型。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 事件的规范 JSON。
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// 上一条记录的 SHA-256 校验值，第一条为空字符串。
    /// </summary>
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>
    /// 当前记录的 SHA-256 校验值。
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// 事件发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// 对外发送的事件；内容与审批状态同一事务入库。
/// </summary>
public sealed class OutboxItemEntity
{
    /// <summary>
    /// 待发项 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 来源审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 来源事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 发送类型：回调、自动动作或消息等。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 待发送的完整 JSON。
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// 队列状态。
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 已尝试次数。
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// 下次可发送时间。
    /// </summary>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>
    /// 当前租约持有者。
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// 租约失效时间。
    /// </summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>
    /// 发送完成时间。
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// 最近一次错误摘要。
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// 乐观锁版本。
    /// </summary>
    public long Version { get; set; }
}

/// <summary>
/// 可跨进程重启继续执行的定时任务。
/// </summary>
public sealed class ScheduledJobEntity
{
    /// <summary>
    /// 定时任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 相关审批单 ID；全局任务可以为空。
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// 任务类型。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 执行所需 JSON。
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// 定时任务状态。
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 到期时间。
    /// </summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>
    /// 当前租约持有者。
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// 租约失效时间。
    /// </summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>
    /// 执行次数。
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// 乐观锁版本。
    /// </summary>
    public long Version { get; set; }
}
