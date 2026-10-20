using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Policies;

/// <summary>
/// 一名审批人的节点内投票快照。
/// </summary>
public sealed class ApprovalBallot
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>
    /// 审批人 ID。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;
    /// <summary>
    /// 任务状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// 节点的计算结果。
/// </summary>
public sealed class ApprovalPolicyDecision
{
    /// <summary>
    /// 节点结果：pending、approved 或 rejected。
    /// </summary>
    public string Outcome { get; set; } = "pending";
    /// <summary>
    /// 依次审批中下一项需要激活的任务 ID。
    /// </summary>
    public string? ActivateTaskId
    {
        get; set;
    }
}

/// <summary>
/// 可插拔的多人审批方式，只计算票数和节点结果。
/// </summary>
public interface IApprovalPolicy
{
    /// <summary>
    /// 方式编码。
    /// </summary>
    string Mode
    {
        get;
    }

    /// <summary>
    /// 计算节点结果，不能修改传入票据。
    /// </summary>
    /// <param name="settings">流程定义中的多人设置。</param>
    /// <param name="ballots">当前节点的所有任务。</param>
    /// <returns>确定的节点结果。</returns>
    ApprovalPolicyDecision Evaluate(FlowApprovalSettings settings, IReadOnlyList<ApprovalBallot> ballots);
}
