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

    /// <summary>
    /// 接入应用推送组织变更。
    /// </summary>
    public const string OrgChanges = OpenApiPrefix + "/org/changes";

    /// <summary>
    /// 接入应用绑定自身人员或部门 ID。
    /// </summary>
    public const string OrgBindings = OpenApiPrefix + "/org/bindings";

    /// <summary>
    /// 组织管理员预检查或导入 CSV、Excel。
    /// </summary>
    public const string OrgImport = OpenApiPrefix + "/admin/org/import";

    /// <summary>
    /// 下载 CSV 或 Excel 导入模板。
    /// </summary>
    public const string OrgTemplate = OpenApiPrefix + "/admin/org/template";

    /// <summary>
    /// 组织管理员手工维护外部 ID 绑定。
    /// </summary>
    public const string AdminOrgBindings = OpenApiPrefix + "/admin/org/bindings";

    /// <summary>
    /// 组织管理员本地会话核验。
    /// </summary>
    public const string AdminSession = OpenApiPrefix + "/admin/session";

    /// <summary>
    /// 组织管理员读取部门、人员和所属关系。
    /// </summary>
    public const string AdminDirectory = OpenApiPrefix + "/admin/org/directory";

    /// <summary>
    /// 组织管理员读取业务系统人员与部门绑定。
    /// </summary>
    public const string AdminBindings = OpenApiPrefix + "/admin/bindings";

    /// <summary>
    /// 组织管理员读取最近组织和接入应用变更的安全审计摘要。
    /// </summary>
    public const string AdminAudit = OpenApiPrefix + "/admin/audit";

    /// <summary>
    /// 组织管理员读取不含密钥的接入应用摘要。
    /// </summary>
    public const string AdminClientApps = OpenApiPrefix + "/admin/apps";

    /// <summary>
    /// 管理员读取或修改指定接入应用允许嵌入审批中心的网站来源。
    /// </summary>
    public const string AdminAppEmbedOrigins = AdminClientApps + "/{code}/embed-origins";

    /// <summary>
    /// 工号密码登录。
    /// </summary>
    public const string IdentityLogin = OpenApiPrefix + "/identity/login";

    /// <summary>
    /// 轮换刷新令牌。
    /// </summary>
    public const string IdentityRefresh = OpenApiPrefix + "/identity/refresh";

    /// <summary>
    /// 修改本地密码。
    /// </summary>
    public const string IdentityChangePassword = OpenApiPrefix + "/identity/change-password";

    /// <summary>
    /// 管理员作废人员的全部访问与刷新令牌。
    /// </summary>
    public const string IdentityRevokeAll = OpenApiPrefix + "/identity/revoke-all";

    /// <summary>
    /// 组织管理员为在职人员开通本地账号。
    /// </summary>
    public const string IdentityProvision = OpenApiPrefix + "/identity/provision";

    /// <summary>
    /// 接入应用签发一次性嵌入令牌。
    /// </summary>
    public const string EmbedTokens = OpenApiPrefix + "/embed-tokens";

    /// <summary>
    /// 一次性令牌换登录态。
    /// </summary>
    public const string EmbedTokenExchange = OpenApiPrefix + "/embed-tokens/exchange";
}
