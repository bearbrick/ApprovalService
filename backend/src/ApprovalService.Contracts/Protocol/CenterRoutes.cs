namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 登录人员使用的审批中心只读接口，不接受接入应用密钥代替人员身份。
/// </summary>
public static class CenterRoutes
{
    /// <summary>
    /// 按分组分页查询当前登录人员的审批单。
    /// </summary>
    public const string Instances = "/api/v1/center/instances";
    /// <summary>
    /// 读取当前登录人员有权查看的一张审批单。
    /// </summary>
    public const string Instance = Instances + "/{id}";
}

/// <summary>
/// 审批中心四类列表的稳定编码。
/// </summary>
public static class CenterSections
{
    /// <summary>
    /// 当前本人或代理给本人的待办。
    /// </summary>
    public const string Todo = "todo";
    /// <summary>
    /// 本人实际处理过的审批单。
    /// </summary>
    public const string Done = "done";
    /// <summary>
    /// 已经抄送给本人的审批单。
    /// </summary>
    public const string Copy = "cc";
    /// <summary>
    /// 本人发起或实际代发的审批单，只供查看进度。
    /// </summary>
    public const string Started = "started";
}
