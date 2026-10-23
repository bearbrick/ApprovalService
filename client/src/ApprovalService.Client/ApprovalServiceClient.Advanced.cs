using System.Net.Http;
using ApprovalService.Client.Internal;

namespace ApprovalService.Client;

/// <summary>
/// 已实现的预览、批量、审批数据、身份与组织开放接口。
/// </summary>
public sealed partial class ApprovalServiceClient
{
    /// <summary>
    /// 按当前业务数据和组织预览预计路径，不创建审批单。
    /// </summary>
    /// <param name="flowCode">已授权流程编码。</param>
    /// <param name="input">发起人、数据和可选自选人员。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>预计路径和当前候选人。</returns>
    public async Task<ApprovalPreview> PreviewAsync(string flowCode, ApprovalPreviewRequest input,
        CancellationToken cancellationToken = default)
    {
        RequireNotNull(input, nameof(input));
        var body = new Dictionary<string, object?>
        {
            ["initiator"] = Required(input.Initiator, nameof(input.Initiator)),
            ["initiatingDepartment"] = input.InitiatingDepartment,
            ["businessKey"] = input.BusinessKey,
            ["data"] = input.Data ?? throw new ArgumentException("流程预览必须提供审批数据。", nameof(input)),
            ["selectedAssignees"] = input.SelectedAssignees,
        };
        return AdvancedApiJson.Preview(await SendAsync(HttpMethod.Post, ForCode(OpenApiPaths.FlowPreview, flowCode),
            cancellationToken, body).ConfigureAwait(false));
    }

    /// <summary>
    /// 按原顺序逐张独立发起审批，单项业务错误不回滚其他项。
    /// </summary>
    /// <param name="items">一到一百张审批单；null 项由服务端逐项报告错误。</param>
    /// <param name="requestId">批次重试时保持不变的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按零起始下标排列的逐项结果。</returns>
    public async Task<IReadOnlyList<ApprovalBatchItem>> StartBatchAsync(IReadOnlyList<StartApprovalRequest?> items,
        string? requestId = null, CancellationToken cancellationToken = default)
    {
        RequireNotNull(items, nameof(items));
        if (items.Count < 1 || items.Count > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(items), "批量发起须包含一到一百张审批单。");
        }

        var body = new Dictionary<string, object?>
        {
            ["items"] = items.Select(item => item == null ? null : StartBody(item)).ToArray(),
        };
        return AdvancedApiJson.Batch(await SendAsync(HttpMethod.Post, Relative(OpenApiPaths.InstancesBatch),
            cancellationToken, body, requestId).ConfigureAwait(false));
    }

    /// <summary>
    /// 审批中更新发布流程显式允许修改的业务字段。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="input">本应用实际操作人和局部变动字段。</param>
    /// <param name="requestId">重试时保持不变的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的审批单。</returns>
    public async Task<ApprovalInstance> PatchDataAsync(string instanceId, ApprovalDataPatchRequest input,
        string? requestId = null, CancellationToken cancellationToken = default)
    {
        RequireNotNull(input, nameof(input));
        var body = new Dictionary<string, object?>
        {
            ["user"] = Required(input.User, nameof(input.User)),
            ["data"] = input.Data ?? throw new ArgumentException("更新审批数据必须提供变动字段。", nameof(input)),
        };
        return OpenApiJson.Instance(await SendAsync(new HttpMethod("PATCH"), ForId(OpenApiPaths.InstanceData, instanceId),
            cancellationToken, body, requestId).ConfigureAwait(false));
    }

    /// <summary>
    /// 由原发起人重新提交完整业务数据。
    /// </summary>
    /// <param name="instanceId">退回或等待补充的审批单 ID。</param>
    /// <param name="input">发起人和完整业务数据。</param>
    /// <param name="requestId">重试时保持不变的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复后的审批单。</returns>
    public async Task<ApprovalInstance> ResubmitAsync(string instanceId, ApprovalResubmitRequest input,
        string? requestId = null, CancellationToken cancellationToken = default)
    {
        RequireNotNull(input, nameof(input));
        var body = new Dictionary<string, object?>
        {
            ["user"] = Required(input.User, nameof(input.User)),
            ["data"] = input.Data ?? throw new ArgumentException("重新提交必须提供完整业务数据。", nameof(input)),
        };
        return OpenApiJson.Instance(await SendAsync(HttpMethod.Post, ForId(OpenApiPaths.InstanceResubmit, instanceId),
            cancellationToken, body, requestId).ConfigureAwait(false));
    }

    /// <summary>
    /// 由审批单所属应用代表已绑定实际操作人终止审批。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="user">本应用已绑定的实际操作人员 ID。</param>
    /// <param name="reason">必填终止原因。</param>
    /// <param name="requestId">重试时保持不变的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>终止后的审批单。</returns>
    public async Task<ApprovalInstance> TerminateAsync(string instanceId, string user, string reason,
        string? requestId = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["user"] = Required(user, nameof(user)),
            ["reason"] = Required(reason, nameof(reason)),
        };
        return OpenApiJson.Instance(await SendAsync(HttpMethod.Post, ForId(OpenApiPaths.InstanceTerminate, instanceId),
            cancellationToken, body, requestId).ConfigureAwait(false));
    }

    /// <summary>
    /// 为本应用已绑定人员签发几分钟有效的一次性嵌入令牌。
    /// </summary>
    /// <param name="userId">本应用的外部人员 ID。</param>
    /// <param name="targetPath">站内相对目标路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌、到期时间和目标路径。</returns>
    public async Task<ApprovalEmbedToken> CreateEmbedTokenAsync(string userId, string targetPath,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["userId"] = Required(userId, nameof(userId)),
            ["targetPath"] = Required(targetPath, nameof(targetPath)),
        };
        return AdvancedApiJson.EmbedToken(await SendAsync(HttpMethod.Post, Relative(AuxiliaryPaths.EmbedTokens),
            cancellationToken, body).ConfigureAwait(false));
    }

    /// <summary>
    /// 由接入应用推送组织主数据变化。
    /// </summary>
    /// <param name="input">部门、人员及所属关系批次。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入结果、问题和待交接人员。</returns>
    public async Task<OrganizationChangesResult> PushOrganizationChangesAsync(OrganizationChangesRequest input,
        CancellationToken cancellationToken = default)
    {
        RequireNotNull(input, nameof(input));
        var body = new Dictionary<string, object?>
        {
            ["departments"] = input.Departments.Select(item => new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["code"] = item.Code,
                ["name"] = item.Name,
                ["parentId"] = item.ParentId,
                ["headPersonId"] = item.HeadPersonId,
            }).ToArray(),
            ["people"] = input.People.Select(item => new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["employeeNumber"] = item.EmployeeNumber,
                ["name"] = item.Name,
                ["phone"] = item.Phone,
                ["email"] = item.Email,
                ["managerId"] = item.ManagerId,
                ["position"] = item.Position,
                ["grade"] = item.Grade,
                ["gradeRank"] = item.GradeRank,
                ["isActive"] = item.IsActive,
            }).ToArray(),
            ["memberships"] = input.Memberships.Select(item => new Dictionary<string, object?>
            {
                ["personId"] = item.PersonId,
                ["departmentId"] = item.DepartmentId,
                ["isPrimary"] = item.IsPrimary,
            }).ToArray(),
        };
        return AdvancedApiJson.OrganizationChanges(await SendAsync(HttpMethod.Post, Relative(AuxiliaryPaths.OrgChanges),
            cancellationToken, body).ConfigureAwait(false));
    }

    /// <summary>
    /// 建立本应用外部人员或部门 ID 绑定。
    /// </summary>
    /// <param name="input">人员按工号或手机号匹配；部门提交内部部门 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>人员绑定返回内部人员 ID；部门绑定成功时为空。</returns>
    public async Task<string?> BindOrganizationIdAsync(OrganizationBinding input,
        CancellationToken cancellationToken = default)
    {
        RequireNotNull(input, nameof(input));
        var kind = Required(input.Kind, nameof(input.Kind));
        if (kind != "person" && kind != "department")
        {
            throw new ArgumentException("绑定类型只能是 person 或 department。", nameof(input));
        }

        if (kind == "person" && input.InternalId != null)
        {
            throw new ArgumentException("接入应用不能直接指定内部人员 ID。", nameof(input));
        }

        var body = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["externalId"] = Required(input.ExternalId, nameof(input.ExternalId)),
            ["internalId"] = input.InternalId,
            ["employeeNumber"] = input.EmployeeNumber,
            ["phone"] = input.Phone,
            ["method"] = "push",
        };
        var result = await SendAsync(HttpMethod.Post, Relative(AuxiliaryPaths.OrgBindings), cancellationToken,
            body).ConfigureAwait(false);
        return kind == "person" ? AdvancedApiJson.BoundPersonId(result) : null;
    }

    /// <summary>
    /// 将发起输入映射为后端 OpenStartRequest 的小驼峰对象。
    /// </summary>
    /// <param name="input">单张发起输入。</param>
    /// <returns>可由 SDK 自带 JSON 写入器编码的对象。</returns>
    private static Dictionary<string, object?> StartBody(StartApprovalRequest input)
    {
        RequireNotNull(input, nameof(input));
        return new Dictionary<string, object?>
        {
            ["flowCode"] = Required(input.FlowCode, nameof(input.FlowCode)),
            ["businessKey"] = Required(input.BusinessKey, nameof(input.BusinessKey)),
            ["title"] = Required(input.Title, nameof(input.Title)),
            ["initiator"] = Required(input.Initiator, nameof(input.Initiator)),
            ["initiatingDepartment"] = input.InitiatingDepartment,
            ["submittedBy"] = input.SubmittedBy,
            ["data"] = input.Data,
            ["detailUrl"] = input.DetailUrl,
            ["isUrgent"] = input.IsUrgent,
            ["selectedAssignees"] = input.SelectedAssignees,
        };
    }

    /// <summary>
    /// 兼容 .NET Framework 4.6.1 的参数非空检查。
    /// </summary>
    /// <param name="value">待检查对象。</param>
    /// <param name="name">参数名称。</param>
    private static void RequireNotNull(object? value, string name)
    {
        if (value == null)
        {
            throw new ArgumentNullException(name);
        }
    }
}
