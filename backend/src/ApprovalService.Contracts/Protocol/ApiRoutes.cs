namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 接口地址。开放接口都在 /api/v1 下，不兼容的改动升版本号（v2），老版本保留一段时间（设计书第 10 节）。
/// </summary>
public static class ApiRoutes
{
    /// <summary>
    /// 本地组织管理员工作日历配置。
    /// </summary>
    public const string AdminWorkCalendar = "/api/v1/admin/work-calendar";

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
    /// 仅本机可读取的 Prometheus 请求聚合指标。
    /// </summary>
    public const string Metrics = "/metrics";

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
    /// 当前流程管理员人工重排达到自动尝试上限的业务动作。
    /// </summary>
    public const string AdminActionRetry = OpenApiPrefix + "/admin/actions/retry";

    /// <summary>
    /// 组织管理员本地会话核验。
    /// </summary>
    public const string AdminSession = OpenApiPrefix + "/admin/session";

    /// <summary>
    /// 组织管理员读取或修改打印页眉的单位名称。
    /// </summary>
    public const string AdminSystemBranding = OpenApiPrefix + "/admin/system-info";

    /// <summary>
    /// 组织管理员读取、上传或移除打印页眉的 Logo。
    /// </summary>
    public const string AdminSystemLogo = AdminSystemBranding + "/logo";

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
    /// 组织管理员读取待发队列的安全摘要。
    /// </summary>
    public const string AdminOutbox = OpenApiPrefix + "/admin/outbox";

    /// <summary>
    /// 管理范围内的报表概览。
    /// </summary>
    public const string AdminReportOverview = OpenApiPrefix + "/admin/reports/overview";

    /// <summary>
    /// 管理范围内的流程指标。
    /// </summary>
    public const string AdminReportFlows = OpenApiPrefix + "/admin/reports/flows";

    /// <summary>
    /// 管理范围内的节点指标。
    /// </summary>
    public const string AdminReportNodes = OpenApiPrefix + "/admin/reports/nodes";

    /// <summary>
    /// 管理范围内的人员指标。
    /// </summary>
    public const string AdminReportPeople = OpenApiPrefix + "/admin/reports/people";

    /// <summary>
    /// 管理范围内的流程、节点和人员汇总工作簿。
    /// </summary>
    public const string AdminReportExport = OpenApiPrefix + "/admin/reports/export";

    /// <summary>
    /// 组织管理员按原事件 ID 手动重发回调。
    /// </summary>
    public const string AdminCallbackRetry = OpenApiPrefix + "/admin/callbacks/{eventId}/retry";

    /// <summary>
    /// 组织管理员读取不含密钥的接入应用摘要。
    /// </summary>
    public const string AdminClientApps = OpenApiPrefix + "/admin/apps";

    /// <summary>
    /// 组织管理员启停接入应用。
    /// </summary>
    public const string AdminAppEnabled = AdminClientApps + "/{code}/enabled";

    /// <summary>
    /// 组织管理员轮换接入应用密钥。
    /// </summary>
    public const string AdminAppRotateSecrets = AdminClientApps + "/{code}/rotate-secrets";

    /// <summary>
    /// 管理员读取或修改指定接入应用允许嵌入审批中心的网站来源。
    /// </summary>
    public const string AdminAppEmbedOrigins = AdminClientApps + "/{code}/embed-origins";
    /// <summary>
    /// 管理员配置某应用的只读原单钩子和 HTTPS 子页面来源。
    /// </summary>
    public const string AdminAppDocumentSettings = AdminClientApps + "/{code}/document-settings";

    /// <summary>
    /// 组织管理员维护接入应用管理员的内部人员 ID 名单。
    /// </summary>
    public const string AdminAppAdministrators = AdminClientApps + "/{code}/administrators";

    /// <summary>
    /// 有权管理该应用的本地人员排入签名测试回调。
    /// </summary>
    public const string AdminAppTestCallback = AdminClientApps + "/{code}/test-callback";

    /// <summary>
    /// 读取或创建有权限的流程草稿。
    /// </summary>
    public const string AdminFlows = OpenApiPrefix + "/admin/flows";

    /// <summary>
    /// 当前本地流程管理员可选择的直属流程所属部门。
    /// </summary>
    public const string AdminFlowDepartments = AdminFlows + "/departments";

    /// <summary>
    /// 按稳定编码读取流程和当前草稿。
    /// </summary>
    public const string AdminFlowByCode = AdminFlows + "/{code}";

    /// <summary>
    /// 按预期修订保存流程草稿。
    /// </summary>
    public const string AdminFlowDraft = AdminFlowByCode + "/draft";

    /// <summary>
    /// 校验草稿并原子发布新版本。
    /// </summary>
    public const string AdminFlowPublish = AdminFlowByCode + "/publish";

    /// <summary>
    /// 停用或恢复流程的新审批单发起。
    /// </summary>
    public const string AdminFlowEnabled = AdminFlowByCode + "/enabled";

    /// <summary>
    /// 仅检查本服务兼容 BPMN 文件与同编码冲突。
    /// </summary>
    public const string AdminFlowImportPreview = AdminFlows + "/import/preview";

    /// <summary>
    /// 预检通过后把兼容 BPMN 内容保存为草稿。
    /// </summary>
    public const string AdminFlowImport = AdminFlows + "/import";

    /// <summary>
    /// 显式导出草稿或指定不可变发布版本。
    /// </summary>
    public const string AdminFlowExport = AdminFlowByCode + "/export";

    /// <summary>
    /// 读取不可变发布版本摘要。
    /// </summary>
    public const string AdminFlowVersions = AdminFlowByCode + "/versions";

    /// <summary>
    /// 把旧发布版内容复制为待重新发布的草稿。
    /// </summary>
    public const string AdminFlowVersionRestore = AdminFlowVersions + "/{version}/restore";

    /// <summary>
    /// 创建仅驻留内存的流程模拟会话。
    /// </summary>
    public const string AdminFlowSimulations = AdminFlows + "/simulations";

    /// <summary>
    /// 读取或删除一场流程模拟。
    /// </summary>
    public const string AdminFlowSimulationById = AdminFlowSimulations + "/{id}";

    /// <summary>
    /// 对流程模拟执行一步纯引擎命令。
    /// </summary>
    public const string AdminFlowSimulationSteps = AdminFlowSimulationById + "/steps";

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
    /// 已登录人员的提醒渠道偏好。
    /// </summary>
    public const string NotificationPreferences = OpenApiPrefix + "/notifications/preferences";

    /// <summary>
    /// 已登录人员按提醒类别修改渠道偏好。
    /// </summary>
    public const string NotificationPreferenceByCategory = NotificationPreferences + "/{category}";

    /// <summary>
    /// 组织管理员设置每日待办汇总的本地发送时刻。
    /// </summary>
    public const string AdminDailyDigest = OpenApiPrefix + "/admin/notifications/daily-digest";

    /// <summary>
    /// 接入应用签发一次性嵌入令牌。
    /// </summary>
    public const string EmbedTokens = OpenApiPrefix + "/embed-tokens";

    /// <summary>
    /// 一次性令牌换登录态。
    /// </summary>
    public const string EmbedTokenExchange = OpenApiPrefix + "/embed-tokens/exchange";
}
