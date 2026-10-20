namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 接口地址。开放接口都在 /api/v1 下，不兼容的改动升版本号（v2），老版本保留一段时间（设计书第 10 节）。
/// </summary>
public static class ApiRoutes
{
    /// <summary>
    /// 开放接口的前缀。
    /// </summary>
    public const string OpenApiPrefix = "/api/v1";

    /// <summary>
    /// 服务信息：名称、版本、接口版本。
    /// </summary>
    public const string Info = OpenApiPrefix + "/info";

    /// <summary>
    /// 上传附件：请求体是原始文件内容，fileName 查询参数为显示名。
    /// </summary>
    public const string Files = OpenApiPrefix + "/files";

    /// <summary>
    /// 按附件 ID 下载；调用者必须具有可信身份和审批单查看权限。
    /// </summary>
    public const string FileById = Files + "/{id}";

    /// <summary>
    /// 存活检查：进程在就返回 200。
    /// </summary>
    public const string HealthLive = "/health/live";

    /// <summary>
    /// 就绪检查：能对外服务时返回 200（以后加上数据库连通性）。
    /// </summary>
    public const string HealthReady = "/health/ready";
}
