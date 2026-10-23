namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 审批服务向业务系统发送的稳定回调事件编码。
/// </summary>
public static class CallbackEventCodes
{
    /// <summary>
    /// 审批单已发起。
    /// </summary>
    public const string InstanceStarted = "instance.started";

    /// <summary>
    /// 审批单已通过。
    /// </summary>
    public const string InstanceApproved = "instance.approved";

    /// <summary>
    /// 审批单已拒绝。
    /// </summary>
    public const string InstanceRejected = "instance.rejected";

    /// <summary>
    /// 审批单已撤回。
    /// </summary>
    public const string InstanceWithdrawn = "instance.withdrawn";

    /// <summary>
    /// 审批单已终止。
    /// </summary>
    public const string InstanceTerminated = "instance.terminated";

    /// <summary>
    /// 审批单已撤销。
    /// </summary>
    public const string InstanceCancelled = "instance.cancelled";

    /// <summary>
    /// 审批单退回发起人等待重新提交。
    /// </summary>
    public const string InstanceReturnedToInitiator = "instance.returnedToInitiator";

    /// <summary>
    /// 审批单等待发起人补充数据。
    /// </summary>
    public const string InstanceSupplementRequested = "instance.supplementRequested";

    /// <summary>
    /// 审批任务已建立。
    /// </summary>
    public const string TaskCreated = "task.created";

    /// <summary>
    /// 审批任务已处理。
    /// </summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>
    /// 撤销同意后原审批任务恢复为待办。
    /// </summary>
    public const string TaskRestored = "task.restored";

    /// <summary>
    /// 审批任务已取消。
    /// </summary>
    public const string TaskCancelled = "task.cancelled";
}
