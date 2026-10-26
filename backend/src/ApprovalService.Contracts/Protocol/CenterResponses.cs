using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 一页已按人员和应用授权过滤的审批摘要。
/// </summary>
public sealed class CenterListResponse
{
    /// <summary>
    /// 过滤后的审批单总数；一张单在同一列表只出现一次。
    /// </summary>
    public int Total
    {
        get; set;
    }

    /// <summary>
    /// 本页跳过的审批单数量。
    /// </summary>
    public int Skip
    {
        get; set;
    }

    /// <summary>
    /// 本页最多返回的审批单数量。
    /// </summary>
    public int Take
    {
        get; set;
    }

    /// <summary>
    /// 已授权的列表内容。
    /// </summary>
    public IReadOnlyList<CenterInstanceResponse> Items { get; set; } = [];

}

/// <summary>
/// 审批中心使用的摘要，不携带原始运行状态或完整审批数据。
/// </summary>
public sealed class CenterInstanceResponse
{
    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 审批编号。
    /// </summary>
    public string SerialNo { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 锁定版本所属流程名称。
    /// </summary>
    public string FlowName { get; set; } = string.Empty;

    /// <summary>
    /// 来源接入应用的显示名称。
    /// </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// 发起人的显示名称。
    /// </summary>
    public string InitiatorName { get; set; } = string.Empty;

    /// <summary>
    /// 当前人工节点名称；无人工任务时为空。
    /// </summary>
    public string? CurrentStep
    {
        get; set;
    }

    /// <summary>
    /// 来源业务单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 审批标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 审批单状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 来源接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 发起人的内部人员 ID。
    /// </summary>
    public string InitiatorId { get; set; } = string.Empty;

    /// <summary>
    /// 实际代发人的内部人员 ID。
    /// </summary>
    public string? SubmittedById
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
    /// 当前登录人员有尚未看过的可见活动；不携带内部游标。
    /// </summary>
    public bool IsUnread
    {
        get; set;
    }

    /// <summary>
    /// 发起时间。
    /// </summary>
    public DateTimeOffset StartedAt
    {
        get; set;
    }

    /// <summary>
    /// 结束时间。
    /// </summary>
    public DateTimeOffset? EndedAt
    {
        get; set;
    }

    /// <summary>
    /// 按发布定义和节点字段权限裁剪后的摘要。
    /// </summary>
    public JsonElement Summary
    {
        get; set;
    }

    /// <summary>
    /// 与已裁剪摘要一一对应的发布版本字段名称和明细列，不含隐藏字段。
    /// </summary>
    public IReadOnlyList<CenterFieldDefinitionResponse> SummaryFieldDefinitions { get; set; } = [];

    /// <summary>
    /// 当前登录人员可处理的待办 ID，仅表达当前任务关系。
    /// </summary>
    public IReadOnlyList<string> PendingTaskIds { get; set; } = [];

    /// <summary>
    /// 当前人员是否有至少一项待办由服务端确认可批量同意。
    /// </summary>
    public bool CanBulkApprove
    {
        get; set;
    }

    /// <summary>
    /// 当前人员实际可批量同意的任务 ID，可能同一单有多个。
    /// </summary>
    public IReadOnlyList<string> BulkApprovableTaskIds { get; set; } = [];

}

/// <summary>
/// 当前人员可见的审批详情；本接口不提供业务数据修改。
/// </summary>
public sealed class CenterDetailResponse
{
    /// <summary>
    /// 审批单摘要。
    /// </summary>
    public CenterInstanceResponse Instance { get; set; } = new();

    /// <summary>
    /// 按当前查看人的字段权限裁剪后的审批快照。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }

    /// <summary>
    /// 与已裁剪详情数据一一对应的发布版本字段名称和明细列。
    /// </summary>
    public IReadOnlyList<CenterFieldDefinitionResponse> FieldDefinitions { get; set; } = [];

    /// <summary>
    /// 按当前人员可见字段裁剪后的锁定版本详情排版。
    /// </summary>
    public CenterDetailLayoutResponse Layout { get; set; } = new();

    /// <summary>
    /// 当前人员可见的附件字段所引用、且仍关联本审批单的文件元数据。
    /// </summary>
    public IReadOnlyList<CenterAttachmentResponse> Attachments { get; set; } = [];

    /// <summary>
    /// 当前与历史人工任务的只读展示。
    /// </summary>
    public IReadOnlyList<CenterTaskResponse> Tasks { get; set; } = [];

    /// <summary>
    /// 安全投影后的操作时间线，不返回原始审计 JSON。
    /// </summary>
    public IReadOnlyList<CenterEventResponse> Events { get; set; } = [];

    /// <summary>
    /// 当前运行令牌所在节点，仅用于进度高亮。
    /// </summary>
    public IReadOnlyList<string> ActiveNodeIds { get; set; } = [];

}

/// <summary>
/// 按可见数据键裁剪后的展示定义，名称取自本审批单锁定的发布版本。
/// </summary>
public sealed class CenterFieldDefinitionResponse
{
    /// <summary>
    /// 本层字段编码。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 审批人可见的发布字段名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 发布版本中的字段种类。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 仅含已返回明细列的子定义。
    /// </summary>
    public IReadOnlyList<CenterFieldDefinitionResponse> Children { get; set; } = [];
}

/// <summary>
/// 审批详情中已授权附件的展示元数据，内容通过独立验权接口下载。
/// </summary>
public sealed class CenterAttachmentResponse
{
    /// <summary>
    /// 文件 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 上传时的文件名称。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 按内容识别的媒体类型。
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 原始字节数。
    /// </summary>
    public long Size
    {
        get; set;
    }
}

/// <summary>
/// 人工任务的只读进度信息。
/// </summary>
public sealed class CenterTaskResponse
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 发布时的节点名称。
    /// </summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>
    /// 处理人的内部人员 ID。
    /// </summary>
    public string AssigneeId { get; set; } = string.Empty;

    /// <summary>
    /// 代理任务的原审批人 ID。
    /// </summary>
    public string? DelegatedForId
    {
        get; set;
    }

    /// <summary>
    /// 任务状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 已保存的处理意见。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }
    /// <summary>
    /// 当前任务成功办理时冻结的签名资产 ID。
    /// </summary>
    public string? SignatureFileId
    {
        get; set;
    }

    /// <summary>
    /// 任务到期时间。
    /// </summary>
    public DateTimeOffset? DueAt
    {
        get; set;
    }

    /// <summary>
    /// 任务完成时间。
    /// </summary>
    public DateTimeOffset? CompletedAt
    {
        get; set;
    }

    /// <summary>
    /// 当前登录人员在此任务上可尝试的命令；提交时引擎仍重新校验。
    /// </summary>
    public IReadOnlyList<string> AllowedActions { get; set; } = [];

    /// <summary>
    /// 此发布节点在同意、办理完成或确认已阅时是否要求本次手写签名。
    /// </summary>
    public bool SignatureRequired
    {
        get; set;
    }

}

/// <summary>
/// 时间线安全投影；不返回业务字段值、组织解析快照或管理员内部参数。
/// </summary>
public sealed class CenterEventResponse
{
    /// <summary>
    /// 事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 审批单内事件序号。
    /// </summary>
    public long Sequence
    {
        get; set;
    }

    /// <summary>
    /// 事件类型。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 相关节点 ID。
    /// </summary>
    public string? NodeId
    {
        get; set;
    }

    /// <summary>
    /// 实际操作人的内部 ID。
    /// </summary>
    public string? ActorId
    {
        get; set;
    }

    /// <summary>
    /// 被代理的原审批人 ID。
    /// </summary>
    public string? OnBehalfOfId
    {
        get; set;
    }

    /// <summary>
    /// 操作意见。
    /// </summary>
    public string? Opinion
    {
        get; set;
    }
    /// <summary>
    /// 本次操作审计冻结的签名资产 ID。
    /// </summary>
    public string? SignatureFileId
    {
        get; set;
    }

    /// <summary>
    /// 事件发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }

}
