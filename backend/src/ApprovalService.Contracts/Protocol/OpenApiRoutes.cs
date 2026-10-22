namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 业务系统开放接口的稳定路径模板。
/// </summary>
public static class OpenApiRoutes
{
    /// <summary>
    /// 可用流程列表。
    /// </summary>
    public const string Flows = "/api/v1/flows";

    /// <summary>
    /// 流程审批数据定义。
    /// </summary>
    public const string FlowSchema = "/api/v1/flows/{code}/schema";

    /// <summary>
    /// 按流程编码与业务单号读取历史审批单。
    /// </summary>
    public const string Instances = "/api/v1/instances";

    /// <summary>
    /// 按审批单 ID 读取状态、任务和操作记录。
    /// </summary>
    public const string Instance = "/api/v1/instances/{id}";

    /// <summary>
    /// 发起人撤回在途审批。
    /// </summary>
    public const string InstanceWithdraw = "/api/v1/instances/{id}/withdraw";

    /// <summary>
    /// 发起人催办当前审批任务。
    /// </summary>
    public const string InstanceRemind = "/api/v1/instances/{id}/remind";

    /// <summary>
    /// 按已绑定人员查询待办数量。
    /// </summary>
    public const string TaskCount = "/api/v1/tasks/count";

    /// <summary>
    /// 按已绑定人员分页查询待办。
    /// </summary>
    public const string Tasks = "/api/v1/tasks";

    /// <summary>
    /// 人工任务的审批与协作操作。
    /// </summary>
    public const string TaskActions = "/api/v1/tasks/{id}/actions";

    /// <summary>
    /// 路由实现捕获带斜杠的引擎任务 ID 后再校验 actions 后缀。
    /// </summary>
    public const string TaskActionCatchAll = "/api/v1/tasks/{**path}";
}
