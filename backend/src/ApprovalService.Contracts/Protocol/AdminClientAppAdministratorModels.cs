namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 接入应用管理员名单响应。
/// </summary>
public sealed class AdminClientAppAdministratorsResponse
{
    /// <summary>
    /// 应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 按内部人员 ID 排序的管理员名单。
    /// </summary>
    public IReadOnlyList<string> PersonIds { get; set; } = [];
}

/// <summary>
/// 组织管理员替换应用管理员名单的请求。
/// </summary>
public sealed class UpdateAdminClientAppAdministratorsRequest
{
    /// <summary>
    /// 读取时看到的旧名单，用于并发检查。
    /// </summary>
    public IReadOnlyList<string> ExpectedPersonIds { get; set; } = [];

    /// <summary>
    /// 目标内部人员 ID 名单。
    /// </summary>
    public IReadOnlyList<string> PersonIds { get; set; } = [];

    /// <summary>
    /// 可审阅的变更原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
