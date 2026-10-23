namespace ApprovalService.Client.Internal;

/// <summary>
/// 已确认开放接口路径，是后端 Contracts 中 OpenApiRoutes 的调用端副本。
/// </summary>
internal static class OpenApiPaths
{
    /// <summary>
    /// 已授权的发布流程列表。
    /// </summary>
    public const string Flows = "/api/v1/flows";

    /// <summary>
    /// 流程审批数据定义。
    /// </summary>
    public const string FlowSchema = "/api/v1/flows/{code}/schema";

    /// <summary>
    /// 发起审批和按业务单号查询历史。
    /// </summary>
    public const string Instances = "/api/v1/instances";

    /// <summary>
    /// 按审批单 ID 查询详情。
    /// </summary>
    public const string Instance = "/api/v1/instances/{id}";

    /// <summary>
    /// 发起人撤回在途审批。
    /// </summary>
    public const string InstanceWithdraw = "/api/v1/instances/{id}/withdraw";

    /// <summary>
    /// 发起人催办当前任务。
    /// </summary>
    public const string InstanceRemind = "/api/v1/instances/{id}/remind";

    /// <summary>
    /// 人员待办数量。
    /// </summary>
    public const string TaskCount = "/api/v1/tasks/count";

    /// <summary>
    /// 人员待办列表。
    /// </summary>
    public const string Tasks = "/api/v1/tasks";

    /// <summary>
    /// 人工任务操作。
    /// </summary>
    public const string TaskActions = "/api/v1/tasks/{id}/actions";
}
