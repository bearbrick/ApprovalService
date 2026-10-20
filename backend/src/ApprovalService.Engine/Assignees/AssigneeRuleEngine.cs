using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 到节点时找人并应用缺人、本人、重复及停用规则。
/// </summary>
public static class AssigneeRuleEngine
{
    /// <summary>
    /// 从当前组织快照产生人工节点的最终处理人计划。
    /// </summary>
    /// <param name="node">已发布节点。</param>
    /// <param name="state">当前审批单状态。</param>
    /// <param name="snapshot">到节点时的组织及数据快照。</param>
    /// <param name="extensions">自定义纯来源。</param>
    /// <returns>有序人员和自动通过原因。</returns>
    public static AssigneePlan Resolve(FlowExecutionNode node, EngineState state, AssigneeSnapshot snapshot,
        IReadOnlyList<IAssigneeResolver>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        var settings = node.Approval ?? new FlowApprovalSettings();
        ValidateSettings(settings);
        var request = new AssigneeRequest
        {
            NodeId = node.Id,
            Configuration = node.Assignee ?? new FlowAssignee(),
            LimitToInitiatingDepartment = node.Assignee?.LimitToInitiatingDepartment ?? false,
            StopAtGrade = node.Assignee?.StopAtGrade,
        };
        var ids = AssigneeResolverRegistry.Resolve(request, snapshot, extensions).AssigneeIds;
        if (ids.Count == 0)
        {
            return Missing(settings, snapshot);
        }

        var result = new List<string>();
        var skipped = false;
        foreach (var originalId in ids)
        {
            var id = ResolveInactive(originalId, settings, snapshot);
            if (id == snapshot.InitiatorId)
            {
                if (settings.SelfApprovalBehavior == FlowSelfApprovalBehaviors.Skip)
                {
                    skipped = true;
                    continue;
                }

                if (settings.SelfApprovalBehavior == FlowSelfApprovalBehaviors.Manager)
                {
                    id = ActiveManager(id, snapshot) ?? string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (settings.DuplicateAssigneeBehavior == FlowDuplicateAssigneeBehaviors.Once && state.ApprovedAssigneeIds.Contains(id) ||
                settings.DuplicateAssigneeBehavior == FlowDuplicateAssigneeBehaviors.AdjacentOnce && state.LastApprovedAssigneeId == id)
            {
                skipped = true;
                continue;
            }

            if (!result.Contains(id, StringComparer.Ordinal))
            {
                result.Add(id);
            }
        }

        if (result.Count > 0)
        {
            return new AssigneePlan { AssigneeIds = result };
        }

        return skipped ? new AssigneePlan { AutoApproveReason = "assigneeSkipped" } : Missing(settings, snapshot);
    }

    /// <summary>
    /// 拒绝未知的异常处理编码，避免把配置错误当成默认行为执行。
    /// </summary>
    /// <param name="settings">节点规则。</param>
    private static void ValidateSettings(FlowApprovalSettings settings)
    {
        if (settings.MissingAssigneeBehavior is not (FlowMissingAssigneeBehaviors.Block or
            FlowMissingAssigneeBehaviors.Approve or FlowMissingAssigneeBehaviors.Administrator or
            FlowMissingAssigneeBehaviors.Specified) ||
            settings.SelfApprovalBehavior is not (FlowSelfApprovalBehaviors.Skip or
                FlowSelfApprovalBehaviors.Self or FlowSelfApprovalBehaviors.Manager) ||
            settings.DuplicateAssigneeBehavior is not (FlowDuplicateAssigneeBehaviors.AdjacentOnce or
                FlowDuplicateAssigneeBehaviors.Once or FlowDuplicateAssigneeBehaviors.EveryTime) ||
            settings.InactiveAssigneeBehavior is not (FlowInactiveAssigneeBehaviors.Manager or
                FlowInactiveAssigneeBehaviors.Administrator))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "审批人异常处理设置无效。");
        }
    }

    /// <summary>
    /// 对找不到处理人的节点应用配置的后备措施。
    /// </summary>
    /// <param name="settings">节点规则。</param>
    /// <param name="snapshot">组织快照。</param>
    /// <returns>人员计划或自动通过。</returns>
    private static AssigneePlan Missing(FlowApprovalSettings settings, AssigneeSnapshot snapshot)
    {
        if (settings.MissingAssigneeBehavior == FlowMissingAssigneeBehaviors.Approve)
        {
            return new AssigneePlan { AutoApproveReason = "missingAssigneeApproved" };
        }

        var fallback = settings.MissingAssigneeBehavior switch
        {
            FlowMissingAssigneeBehaviors.Administrator => snapshot.AdministratorId,
            FlowMissingAssigneeBehaviors.Specified => settings.MissingAssigneeUserId,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(fallback))
        {
            throw new EngineException(ErrorCodes.ValidationFailed, "未找到审批人，请检查节点配置或组织数据。");
        }

        return new AssigneePlan { AssigneeIds = [fallback] };
    }

    /// <summary>
    /// 停用人员按节点设置转给直属上级或流程管理员。
    /// </summary>
    /// <param name="id">原处理人 ID。</param>
    /// <param name="settings">节点规则。</param>
    /// <param name="snapshot">组织快照。</param>
    /// <returns>可用人员 ID，找不到时为空。</returns>
    private static string ResolveInactive(string id, FlowApprovalSettings settings, AssigneeSnapshot snapshot)
    {
        var person = snapshot.People.SingleOrDefault(item => item.Id == id);
        if (person is null)
        {
            return string.Empty;
        }

        if (person.IsActive)
        {
            return id;
        }

        return settings.InactiveAssigneeBehavior == FlowInactiveAssigneeBehaviors.Administrator
            ? snapshot.AdministratorId ?? string.Empty : ActiveManager(id, snapshot) ?? string.Empty;
    }

    /// <summary>
    /// 向上寻找第一个在职且启用的直属上级，防止组织环路。
    /// </summary>
    /// <param name="id">起点人员。</param>
    /// <param name="snapshot">组织快照。</param>
    /// <returns>上级 ID，找不到时为空。</returns>
    private static string? ActiveManager(string id, AssigneeSnapshot snapshot)
    {
        for (var level = 1; level <= snapshot.People.Count; level++)
        {
            var manager = AssigneeLookup.ManagerAt(snapshot, id, level);
            if (manager is null)
            {
                return null;
            }

            if (manager.IsActive)
            {
                return manager.Id;
            }
        }

        return null;
    }
}

/// <summary>
/// 人工节点的最终人员计划。
/// </summary>
public sealed class AssigneePlan
{
    /// <summary>
    /// 按处理顺序排列的人员。
    /// </summary>
    public List<string> AssigneeIds { get; set; } = [];
    /// <summary>
    /// 跳过或缺人自动通过的事件原因；为空表示要创建任务。
    /// </summary>
    public string? AutoApproveReason
    {
        get; set;
    }
}
