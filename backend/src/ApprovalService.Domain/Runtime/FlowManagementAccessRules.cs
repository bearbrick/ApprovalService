namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 根据当前组织角色和直接部门关系判断流程管理范围。
/// </summary>
public static class FlowManagementAccessRules
{
    /// <summary>
    /// 部门流程管理员的组织角色编码。
    /// </summary>
    public const string FlowAdministratorRoleCode = "flow-admin";

    /// <summary>
    /// 判断在职本地人员是否可以管理指定流程。
    /// </summary>
    /// <param name="personIsActive">人员当前是否在职。</param>
    /// <param name="isLocalSession">会话是否由本地账号签发。</param>
    /// <param name="isOrganizationAdmin">是否拥有当前组织管理员角色。</param>
    /// <param name="hasFlowAdministratorRole">是否拥有当前部门流程管理员角色。</param>
    /// <param name="directDepartmentIds">人员直接加入的部门 ID。</param>
    /// <param name="flow">待管理流程。</param>
    /// <returns>可管理时为真。</returns>
    public static bool CanManage(bool personIsActive, bool isLocalSession, bool isOrganizationAdmin,
        bool hasFlowAdministratorRole, IReadOnlyCollection<string> directDepartmentIds, FlowEntity flow)
    {
        ArgumentNullException.ThrowIfNull(directDepartmentIds);
        ArgumentNullException.ThrowIfNull(flow);
        if (!personIsActive || !isLocalSession)
        {
            return false;
        }

        if (isOrganizationAdmin)
        {
            return true;
        }

        // 只比对直接部门关系；上级部门成员不会自动继承下级流程权限。
        return hasFlowAdministratorRole && !string.IsNullOrWhiteSpace(flow.OwnerDepartmentId) &&
            directDepartmentIds.Contains(flow.OwnerDepartmentId, StringComparer.Ordinal);
    }
}
