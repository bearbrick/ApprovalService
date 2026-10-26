namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 登录人员使用的审批中心接口，不接受接入应用密钥代替人员身份。
/// </summary>
public static class CenterRoutes
{
    /// <summary>
    /// 普通审批中心页面入口，不允许被其他页面嵌入。
    /// </summary>
    public const string Home = "/";
    /// <summary>
    /// 按接入应用编码限制内嵌来源的审批中心页面入口。
    /// </summary>
    public const string EmbedDocument = "/embed/{clientAppCode}";
    /// <summary>
    /// 已验证嵌入会话可读取本应用允许的消息目标来源。
    /// </summary>
    public const string EmbedContext = "/api/v1/center/embed-context";
    /// <summary>
    /// 按分组分页查询当前登录人员的审批单。
    /// </summary>
    public const string Instances = "/api/v1/center/instances";
    /// <summary>
    /// 读取当前登录人员有权查看的一张审批单。
    /// </summary>
    public const string Instance = Instances + "/{id}";
    /// <summary>
    /// 当前登录人员显式确认已显示的一张审批详情。
    /// </summary>
    public const string MarkRead = Instances + "/{id}/read";
    /// <summary>
    /// 当前人员的个人常用意见列表与新增入口。
    /// </summary>
    public const string Opinions = "/api/v1/center/opinions";
    /// <summary>
    /// 当前人员修改或删除一条个人常用意见。
    /// </summary>
    public const string Opinion = Opinions + "/{id}";
    /// <summary>
    /// 本人委托规则列表和新增入口。
    /// </summary>
    public const string Delegations = "/api/v1/center/delegations";
    /// <summary>
    /// 删除本人一项委托规则。
    /// </summary>
    public const string Delegation = Delegations + "/{id}";
    /// <summary>
    /// 按有效规则逐项将已有待办转给代理人。
    /// </summary>
    public const string DelegateTasks = Delegations + "/tasks";
    /// <summary>
    /// 组织管理员维护指定人员的委托规则。
    /// </summary>
    public const string AdminDelegations = "/api/v1/admin/delegations/{principalId}";
    /// <summary>
    /// 组织管理员逐项转移指定人员已有待办。
    /// </summary>
    public const string AdminDelegateTasks = AdminDelegations + "/tasks";
    /// <summary>
    /// 组织管理员删除指定人员的一项委托规则。
    /// </summary>
    public const string AdminDelegation = AdminDelegations + "/{id}";
    /// <summary>
    /// 使用当前登录人员身份办理一项人工任务。
    /// </summary>
    public const string TaskActions = "/api/v1/center/tasks/{taskId}/actions";
    /// <summary>
    /// 可接受包含斜杠的引擎任务 ID 的路由模板。
    /// </summary>
    public const string TaskActionCatchAll = "/api/v1/center/tasks/{**path}";
    /// <summary>
    /// 逐项独立办理当前登录人员的批量同意任务。
    /// </summary>
    public const string BatchApprove = "/api/v1/center/tasks/batch-approve";
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
