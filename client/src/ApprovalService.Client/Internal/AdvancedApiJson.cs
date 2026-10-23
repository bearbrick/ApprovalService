namespace ApprovalService.Client.Internal;

/// <summary>
/// 将新增开放接口的 JSON 值映射为兼容 net461 的公开模型。
/// </summary>
internal static class AdvancedApiJson
{
    /// <summary>
    /// 解析预计路径和当前候选人。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>预览结果。</returns>
    internal static ApprovalPreview Preview(object? value)
    {
        var row = OpenApiJson.Object(value, "preview");
        return new ApprovalPreview
        {
            FlowCode = OpenApiJson.String(row, "flowCode"),
            Version = OpenApiJson.Int(row, "version"),
            ProjectedStatus = OpenApiJson.String(row, "projectedStatus"),
            Notice = OpenApiJson.String(row, "notice"),
            Nodes = OpenApiJson.Array(OpenApiJson.Field(row, "nodes"), "nodes").Select(item =>
            {
                var node = OpenApiJson.Object(item, "node");
                return new ApprovalPreviewNode
                {
                    NodeId = OpenApiJson.String(node, "nodeId"),
                    Name = OpenApiJson.String(node, "name"),
                    Type = OpenApiJson.String(node, "type"),
                    CandidateAssigneeIds = OpenApiJson.Array(OpenApiJson.Field(node, "candidateAssigneeIds"),
                        "candidateAssigneeIds").Select(entry => entry as string ?? Invalid<string>("candidateAssigneeIds")).ToArray(),
                    CandidateAssignees = OpenApiJson.Array(OpenApiJson.Field(node, "candidateAssignees"),
                        "candidateAssignees").Select(entry =>
                    {
                        var person = OpenApiJson.Object(entry, "candidateAssignee");
                        return new ApprovalPreviewCandidate
                        {
                            Id = OpenApiJson.String(person, "id"),
                            Name = OpenApiJson.String(person, "name"),
                        };
                    }).ToArray(),
                };
            }).ToArray(),
        };
    }

    /// <summary>
    /// 解析按请求顺序排列的逐项发起结果。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>成功审批单或逐项错误。</returns>
    internal static IReadOnlyList<ApprovalBatchItem> Batch(object? value)
    {
        var root = OpenApiJson.Object(value, "batch");
        return OpenApiJson.Array(OpenApiJson.Field(root, "results"), "results").Select(item =>
        {
            var row = OpenApiJson.Object(item, "batchItem");
            row.TryGetValue("instance", out var instance);
            row.TryGetValue("error", out var error);
            var errorRow = error == null ? null : OpenApiJson.Object(error, "error");
            return new ApprovalBatchItem
            {
                Index = OpenApiJson.Int(row, "index"),
                Instance = instance == null ? null : OpenApiJson.Instance(instance),
                Error = errorRow == null ? null : new ApprovalBatchError
                {
                    Code = OpenApiJson.String(errorRow, "code"),
                    Message = OpenApiJson.String(errorRow, "message"),
                },
            };
        }).ToArray();
    }

    /// <summary>
    /// 解析一次性嵌入令牌。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>令牌和有效期。</returns>
    internal static ApprovalEmbedToken EmbedToken(object? value)
    {
        var row = OpenApiJson.Object(value, "embedToken");
        return new ApprovalEmbedToken
        {
            Token = OpenApiJson.String(row, "token"),
            ExpiresAt = OpenApiJson.Date(row, "expiresAt"),
            TargetPath = OpenApiJson.String(row, "targetPath"),
        };
    }

    /// <summary>
    /// 解析组织批次写入和字段问题。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>组织推送结果。</returns>
    internal static OrganizationChangesResult OrganizationChanges(object? value)
    {
        var row = OpenApiJson.Object(value, "organizationChanges");
        return new OrganizationChangesResult
        {
            Applied = OpenApiJson.Bool(row, "applied"),
            Issues = OpenApiJson.Array(OpenApiJson.Field(row, "issues"), "issues").Select(item =>
            {
                var issue = OpenApiJson.Object(item, "issue");
                return new OrganizationIssue
                {
                    Field = OpenApiJson.String(issue, "field"),
                    Code = OpenApiJson.String(issue, "code"),
                    Message = OpenApiJson.String(issue, "message"),
                };
            }).ToArray(),
            DepartedPersonIds = OpenApiJson.Array(OpenApiJson.Field(row, "departedPersonIds"),
                "departedPersonIds").Select(item => item as string ?? Invalid<string>("departedPersonIds")).ToArray(),
        };
    }

    /// <summary>
    /// 解析成功建立的业务人员绑定。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>内部人员 ID。</returns>
    internal static string BoundPersonId(object? value) =>
        OpenApiJson.String(OpenApiJson.Object(value, "binding"), "personId");

    /// <summary>
    /// 解析上传文件的校验资料。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>上传文件。</returns>
    internal static ApprovalUploadedFile UploadedFile(object? value)
    {
        var row = OpenApiJson.Object(value, "file");
        return new ApprovalUploadedFile
        {
            Id = OpenApiJson.String(row, "id"),
            FileName = OpenApiJson.String(row, "fileName"),
            Size = OpenApiJson.Long(row, "size"),
            ContentType = OpenApiJson.String(row, "contentType"),
            Sha256 = OpenApiJson.String(row, "sha256"),
        };
    }

    /// <summary>
    /// JSON 结构错误时抛出统一 SDK 异常。
    /// </summary>
    /// <typeparam name="T">调用方期望的类型。</typeparam>
    /// <param name="name">错误字段。</param>
    /// <returns>永远不返回。</returns>
    private static T Invalid<T>(string name) => throw new ApprovalServiceException(ApprovalErrorCodes.InternalError,
        "审批流服务响应字段 " + name + " 与接口约定不符。");
}
