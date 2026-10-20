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

}
