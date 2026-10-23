namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 已通过管理员授权的当前本地会话。
/// </summary>
public sealed class AdminSessionResponse
{
    /// <summary>
    /// 管理员对应的内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;
}

/// <summary>
/// 管理后台可读取的组织目录，不含联系方式和账号凭据。
/// </summary>
public sealed class AdminDirectoryResponse
{
    /// <summary>
    /// 部门列表。
    /// </summary>
    public IReadOnlyList<AdminDepartmentResponse> Departments { get; set; } = [];

    /// <summary>
    /// 人员列表。
    /// </summary>
    public IReadOnlyList<AdminPersonResponse> People { get; set; } = [];
}

/// <summary>
/// 管理后台部门摘要。
/// </summary>
public sealed class AdminDepartmentResponse
{
    /// <summary>
    /// 内部部门 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 部门编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 部门名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 上级部门名称；根部门为空。
    /// </summary>
    public string? ParentName
    {
        get; set;
    }

    /// <summary>
    /// 负责人姓名；未设置时为空。
    /// </summary>
    public string? ManagerName
    {
        get; set;
    }

    /// <summary>
    /// 可信主数据来源。
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 管理后台人员摘要。
/// </summary>
public sealed class AdminPersonResponse
{
    /// <summary>
    /// 内部人员 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 工号。
    /// </summary>
    public string EmployeeNumber { get; set; } = string.Empty;

    /// <summary>
    /// 姓名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 所属部门名称。
    /// </summary>
    public IReadOnlyList<string> Departments { get; set; } = [];

    /// <summary>
    /// 岗位。
    /// </summary>
    public string? Position
    {
        get; set;
    }

    /// <summary>
    /// 在职状态。
    /// </summary>
    public bool IsActive
    {
        get; set;
    }
}

/// <summary>
/// 接入应用的安全摘要，不包含 API Key、签名密钥或地址。
/// </summary>
public sealed class AdminClientAppResponse
{
    /// <summary>
    /// 应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 应用名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 已授权流程编码。
    /// </summary>
    public IReadOnlyList<string> AllowedFlowCodes { get; set; } = [];

    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool IsEnabled
    {
        get; set;
    }
}

/// <summary>
/// 管理后台可见的业务系统外部 ID 绑定。
/// </summary>
public sealed class AdminBindingResponse
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;

    /// <summary>
    /// person 或 department。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// 业务系统外部 ID。
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// 审批服务内部人员或部门 ID。
    /// </summary>
    public string InternalId { get; set; } = string.Empty;

    /// <summary>
    /// 当前内部目标名称，不含联系方式。
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// 绑定来源。
    /// </summary>
    public string Method { get; set; } = string.Empty;
}

/// <summary>
/// 管理后台可见的组织、接入应用及回调重发操作日志摘要。
/// </summary>
public sealed class AdminAuditResponse
{
    /// <summary>
    /// 操作时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }

    /// <summary>
    /// 操作者 ID。
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// 操作类别。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 对象类型及 ID。
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// 明确填写的操作原因；其他变更为空。
    /// </summary>
    public string? Reason
    {
        get; set;
    }
}

/// <summary>
/// 管理员可读取的待发项摘要，不含发送正文和地址。
/// </summary>
public sealed class AdminOutboxResponse
{
    /// <summary>
    /// 稳定事件 ID。
    /// </summary>
    public string EventId { get; set; } = string.Empty;
    /// <summary>
    /// 所属接入应用编码。
    /// </summary>
    public string ClientAppCode { get; set; } = string.Empty;
    /// <summary>
    /// 待发项类型。
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>
    /// 队列状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>
    /// 已尝试次数。
    /// </summary>
    public int Attempts
    {
        get; set;
    }
    /// <summary>
    /// 下次可发送时间。
    /// </summary>
    public DateTimeOffset AvailableAt
    {
        get; set;
    }
    /// <summary>
    /// 最近错误类别。
    /// </summary>
    public string? ErrorCode
    {
        get; set;
    }
}

/// <summary>
/// 管理员手动重发回调所需的原因。
/// </summary>
public sealed class AdminCallbackRetryRequest
{
    /// <summary>
    /// 本次重发原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 接入应用的可信嵌入来源设置。
/// </summary>
public sealed class AdminEmbedOriginsResponse
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 完整可信网站来源列表。
    /// </summary>
    public IReadOnlyList<string> Origins { get; set; } = [];
}

/// <summary>
/// 管理员修改接入应用可信嵌入来源的请求。
/// </summary>
public sealed class UpdateAdminEmbedOriginsRequest
{
    /// <summary>
    /// 新的完整来源列表；空列表表示不允许外部网站嵌入。
    /// </summary>
    public IReadOnlyList<string>? Origins
    {
        get; set;
    }

    /// <summary>
    /// 必填的变更原因。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
