using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 本应用已绑定人员对人工任务执行审批或协作命令。
/// </summary>
public sealed class OpenTaskActionRequest
{
    /// <summary>
    /// 当前接入应用已绑定的业务人员 ID。
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// 命令编码，例如 approve、reject、return、transfer 或 addSigner。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 审批意见或评论。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }

    /// <summary>
    /// 转交、加签、抄送或征询目标的本应用业务人员 ID。
    /// </summary>
    public string? TargetUser
    {
        get; set;
    }

    /// <summary>
    /// 退回等操作的目标节点 ID。
    /// </summary>
    public string? TargetNodeId
    {
        get; set;
    }

    /// <summary>
    /// 征询或加签关联 ID。
    /// </summary>
    public string? RelatedId
    {
        get; set;
    }

    /// <summary>
    /// 退回或加签位置等操作模式。
    /// </summary>
    public string? Mode
    {
        get; set;
    }

    /// <summary>
    /// 被提及的本应用业务人员 ID。
    /// </summary>
    public List<string> MentionedUsers { get; set; } = [];

    /// <summary>
    /// 本节点允许审批人填写的顶层字段值；不修改发起时的业务数据快照。
    /// </summary>
    public JsonElement? FieldValues
    {
        get; set;
    }
}
