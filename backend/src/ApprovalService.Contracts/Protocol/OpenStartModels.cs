using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 业务系统发起审批所需的业务单号、已绑定发起人及审批数据快照。
/// </summary>
public sealed class OpenStartRequest
{
    /// <summary>
    /// 已授权的流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务系统自己的单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 审批标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 当前接入应用已绑定的发起人员 ID。
    /// </summary>
    public string Initiator { get; set; } = string.Empty;

    /// <summary>
    /// 可选的本应用已绑定发起部门 ID；未传时使用发起人的主部门。
    /// </summary>
    public string? InitiatingDepartment
    {
        get; set;
    }

    /// <summary>
    /// 可选的本应用已绑定代发人员 ID；审批责任仍归属于发起人。
    /// </summary>
    public string? SubmittedBy
    {
        get; set;
    }

    /// <summary>
    /// 业务审批数据；不传时需由已登记的取数钩子获取。
    /// </summary>
    public JsonElement? Data
    {
        get; set;
    }

    /// <summary>
    /// 业务系统原单详情地址。
    /// </summary>
    public string? DetailUrl
    {
        get; set;
    }

    /// <summary>
    /// 是否加急。
    /// </summary>
    public bool IsUrgent
    {
        get; set;
    }

    /// <summary>
    /// 发起人自选节点的业务人员 ID；服务端逐人核对本应用绑定。
    /// </summary>
    public Dictionary<string, List<string>> SelectedAssignees { get; set; } = [];
}
