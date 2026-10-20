namespace ApprovalService.Contracts.Flows;

/// <summary>
/// 流程定义格式版本。
/// </summary>
public static class FlowSchema
{
    /// <summary>
    /// 当前自由画布格式版本。
    /// </summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// 自由画布支持的节点种类。
/// </summary>
public static class FlowNodeTypes
{
    /// <summary>
    /// 开始节点。
    /// </summary>
    public const string Start = "start";

    /// <summary>
    /// 结束节点。
    /// </summary>
    public const string End = "end";

    /// <summary>
    /// 审批节点。
    /// </summary>
    public const string Approval = "approval";

    /// <summary>
    /// 办理节点。
    /// </summary>
    public const string Handling = "handling";

    /// <summary>
    /// 抄送节点。
    /// </summary>
    public const string Cc = "cc";

    /// <summary>
    /// 排他网关。
    /// </summary>
    public const string ExclusiveGateway = "exclusiveGateway";

    /// <summary>
    /// 包容网关。
    /// </summary>
    public const string InclusiveGateway = "inclusiveGateway";

    /// <summary>
    /// 并行网关。
    /// </summary>
    public const string ParallelGateway = "parallelGateway";

    /// <summary>
    /// 子流程节点。
    /// </summary>
    public const string Subprocess = "subprocess";

    /// <summary>
    /// 自动节点。
    /// </summary>
    public const string Automatic = "automatic";
}

/// <summary>
/// 审批数据字段种类。
/// </summary>
public static class FlowFieldTypes
{
    /// <summary>
    /// 文本。
    /// </summary>
    public const string Text = "text";

    /// <summary>
    /// 数字。
    /// </summary>
    public const string Number = "number";

    /// <summary>
    /// 金额。
    /// </summary>
    public const string Money = "money";

    /// <summary>
    /// 日期。
    /// </summary>
    public const string Date = "date";

    /// <summary>
    /// 人员。
    /// </summary>
    public const string Person = "person";

    /// <summary>
    /// 部门。
    /// </summary>
    public const string Department = "department";

    /// <summary>
    /// 附件。
    /// </summary>
    public const string Attachment = "attachment";

    /// <summary>
    /// 明细表；子字段存放于 children。
    /// </summary>
    public const string DetailTable = "detailTable";
}

/// <summary>
/// 网关在一对分叉与汇合中的方向。
/// </summary>
public static class FlowGatewayDirections
{
    /// <summary>
    /// 分叉。
    /// </summary>
    public const string Split = "split";

    /// <summary>
    /// 汇合。
    /// </summary>
    public const string Join = "join";
}

/// <summary>
/// 节点对字段的可见与填写权限。
/// </summary>
public static class FlowFieldAccess
{
    /// <summary>
    /// 隐藏。
    /// </summary>
    public const string Hidden = "hidden";

    /// <summary>
    /// 只读。
    /// </summary>
    public const string ReadOnly = "readOnly";

    /// <summary>
    /// 允许当前节点审批人填写。
    /// </summary>
    public const string Editable = "editable";
}
