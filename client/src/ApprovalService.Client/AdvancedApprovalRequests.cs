namespace ApprovalService.Client;

/// <summary>
/// 根据业务数据与当前组织预览发布流程。
/// </summary>
public sealed class ApprovalPreviewRequest
{
    /// <summary>
    /// 本应用已绑定的发起人员 ID。
    /// </summary>
    public string Initiator { get; set; } = string.Empty;

    /// <summary>
    /// 可选的本应用发起部门 ID。
    /// </summary>
    public string? InitiatingDepartment
    {
        get; set;
    }

    /// <summary>
    /// 可选业务单号，供业务取人钩子识别。
    /// </summary>
    public string? BusinessKey
    {
        get; set;
    }

    /// <summary>
    /// 与发起接口相同的审批数据对象。
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// 节点 ID 到本应用已绑定人员 ID 的自选映射。
    /// </summary>
    public Dictionary<string, List<string>> SelectedAssignees { get; set; } = new();
}

/// <summary>
/// 审批中仅更新流程显式授权的部分业务字段。
/// </summary>
public sealed class ApprovalDataPatchRequest
{
    /// <summary>
    /// 本应用已绑定的实际操作人员 ID。
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// 只包含本次变动字段的对象。
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}

/// <summary>
/// 退回或补充后由发起人重新提交完整业务数据。
/// </summary>
public sealed class ApprovalResubmitRequest
{
    /// <summary>
    /// 本应用已绑定的原发起人员 ID。
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// 完整业务审批数据。
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}

/// <summary>
/// 一张审批单的逐项批量发起结果。
/// </summary>
public sealed class ApprovalBatchItem
{
    /// <summary>
    /// 零起始请求下标。
    /// </summary>
    public int Index
    {
        get; set;
    }

    /// <summary>
    /// 成功时的审批单；失败时为空。
    /// </summary>
    public ApprovalInstance? Instance
    {
        get; set;
    }

    /// <summary>
    /// 可修正的逐项错误；成功时为空。
    /// </summary>
    public ApprovalBatchError? Error
    {
        get; set;
    }
}

/// <summary>
/// 批量发起某一项的业务错误。
/// </summary>
public sealed class ApprovalBatchError
{
    /// <summary>
    /// 服务端统一错误码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 中文错误说明。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 不落库的流程预览结果。
/// </summary>
public sealed class ApprovalPreview
{
    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 发布版本号。
    /// </summary>
    public int Version
    {
        get; set;
    }

    /// <summary>
    /// 按预计到达顺序排列的节点。
    /// </summary>
    public IReadOnlyList<ApprovalPreviewNode> Nodes { get; set; } = Array.Empty<ApprovalPreviewNode>();

    /// <summary>
    /// 假定成功推进时的预计状态。
    /// </summary>
    public string ProjectedStatus { get; set; } = string.Empty;

    /// <summary>
    /// 实际到达节点时重新计算的说明。
    /// </summary>
    public string Notice { get; set; } = string.Empty;
}

/// <summary>
/// 一次预计到达的执行节点。
/// </summary>
public sealed class ApprovalPreviewNode
{
    /// <summary>
    /// 发布节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 节点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 节点类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 当前候选内部人员 ID。
    /// </summary>
    public IReadOnlyList<string> CandidateAssigneeIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 当前候选内部人员与姓名。
    /// </summary>
    public IReadOnlyList<ApprovalPreviewCandidate> CandidateAssignees { get; set; } = Array.Empty<ApprovalPreviewCandidate>();
}

/// <summary>
/// 当前组织中的一个候选审批人。
/// </summary>
public sealed class ApprovalPreviewCandidate
{
    /// <summary>
    /// 审批服务内部人员 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 当前显示姓名。
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
