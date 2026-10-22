namespace ApprovalService.Engine.Assignees;

/// <summary>
/// 一个审批人来源的纯解析器。实现不得读取数据库、网络或系统时间。
/// </summary>
public interface IAssigneeResolver
{
    /// <summary>
    /// 所支持的来源编码。
    /// </summary>
    string Source
    {
        get;
    }

    /// <summary>
    /// 从调用方提供的快照中找出有序人员。
    /// </summary>
    /// <param name="request">节点配置和补充限制。</param>
    /// <param name="snapshot">人员、部门、数据和外部结果快照。</param>
    /// <returns>有序人员 ID。</returns>
    IReadOnlyList<string> Resolve(AssigneeRequest request, AssigneeSnapshot snapshot);
}
