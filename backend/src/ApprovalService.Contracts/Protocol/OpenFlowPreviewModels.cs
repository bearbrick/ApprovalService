using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 业务系统按发起输入预览发布流程；业务单号可以省略。
/// </summary>
public sealed class OpenFlowPreviewRequest
{
    /// <summary>
    /// 当前应用已绑定的发起人员 ID。
    /// </summary>
    public string Initiator { get; set; } = string.Empty;

    /// <summary>
    /// 可选的当前应用已绑定发起部门 ID。
    /// </summary>
    public string? InitiatingDepartment
    {
        get; set;
    }

    /// <summary>
    /// 可选业务单号，仅供业务取人钩子识别预览对象。
    /// </summary>
    public string? BusinessKey
    {
        get; set;
    }

    /// <summary>
    /// 与发起接口相同的审批数据对象。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }

    /// <summary>
    /// 发起人自选节点的本应用外部人员 ID。
    /// </summary>
    public Dictionary<string, List<string>> SelectedAssignees { get; set; } = [];
}

/// <summary>
/// 不落库的预计路径、当前候选处理人及计算边界。
/// </summary>
public sealed class OpenFlowPreviewResponse
{
    /// <summary>
    /// 发布流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 预览使用的发布版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }

    /// <summary>
    /// 按预计到达顺序排列的节点，可包含并行分支。
    /// </summary>
    public List<OpenPreviewNode> Nodes { get; set; } = [];

    /// <summary>
    /// 假定人工操作和自动动作成功时的预计实例状态。
    /// </summary>
    public string ProjectedStatus { get; set; } = string.Empty;

    /// <summary>
    /// 实际到达节点时会按最新组织和业务数据重新计算的提示。
    /// </summary>
    public string Notice { get; set; } = string.Empty;
}

/// <summary>
/// 一次预计到达的执行节点及当前候选人员。
/// </summary>
public sealed class OpenPreviewNode
{
    /// <summary>
    /// 发布执行图中的节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 节点显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 节点类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 本次快照计算出的内部候选人员 ID；非人工节点为空。
    /// </summary>
    public List<string> CandidateAssigneeIds { get; set; } = [];

    /// <summary>
    /// 当前组织中的候选人员 ID 和显示名称。
    /// </summary>
    public List<OpenPreviewCandidate> CandidateAssignees { get; set; } = [];
}

/// <summary>
/// 预览时当前组织中的一个候选审批人。
/// </summary>
public sealed class OpenPreviewCandidate
{
    /// <summary>
    /// 审批服务内部人员 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 当前组织显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
