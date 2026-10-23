namespace ApprovalService.Client.Internal;

/// <summary>
/// 后端 ApiRoutes 中已实现的身份、组织和文件开放路径副本。
/// </summary>
internal static class AuxiliaryPaths
{
    /// <summary>
    /// 签发一次性嵌入令牌。
    /// </summary>
    public const string EmbedTokens = "/api/v1/embed-tokens";

    /// <summary>
    /// 推送组织变化。
    /// </summary>
    public const string OrgChanges = "/api/v1/org/changes";

    /// <summary>
    /// 建立本应用人员或部门外部 ID 绑定。
    /// </summary>
    public const string OrgBindings = "/api/v1/org/bindings";

    /// <summary>
    /// 上传原始文件字节。
    /// </summary>
    public const string Files = "/api/v1/files";

    /// <summary>
    /// 下载已授权的附件。
    /// </summary>
    public const string FileById = "/api/v1/files/{id}";
}
