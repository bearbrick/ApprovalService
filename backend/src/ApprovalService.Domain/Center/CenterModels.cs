using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;

namespace ApprovalService.Domain.Center;

/// <summary>
/// 由服务端从已验证 JWT 提取的查询身份，绝不能从查询参数赋值。
/// </summary>
public sealed class CenterScope
{
    /// <summary>
    /// 可信内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 嵌入会话的来源应用；本地登录为空。
    /// </summary>
    public string? ClientAppCode
    {
        get; set;
    }

}

/// <summary>
/// 审批中心分页筛选，不含可由调用方伪造的人员或应用范围。
/// </summary>
public sealed class CenterQuery
{
    /// <summary>
    /// 列表分组。
    /// </summary>
    public string Section { get; set; } = CenterSections.Todo;

    /// <summary>
    /// 可选流程编码。
    /// </summary>
    public string? FlowCode
    {
        get; set;
    }

    /// <summary>
    /// 可选审批状态。
    /// </summary>
    public string? Status
    {
        get; set;
    }

    /// <summary>
    /// 标题、审批编号或业务单号搜索词。
    /// </summary>
    public string? Search
    {
        get; set;
    }

    /// <summary>
    /// 发起时间起点，包含。
    /// </summary>
    public DateTimeOffset? From
    {
        get; set;
    }

    /// <summary>
    /// 发起时间终点，不包含。
    /// </summary>
    public DateTimeOffset? To
    {
        get; set;
    }

    /// <summary>
    /// 跳过数量。
    /// </summary>
    public int Skip
    {
        get; set;
    }

    /// <summary>
    /// 页大小。
    /// </summary>
    public int Take { get; set; } = 20;

}

/// <summary>
/// 在同一读取事务中获得的已授权审批记录及固定版本。
/// </summary>
public sealed class CenterRecord
{
    /// <summary>
    /// 已授权审批单。
    /// </summary>
    public InstanceEntity Instance { get; set; } = new();

    /// <summary>
    /// 发起时锁定的流程版本。
    /// </summary>
    public FlowVersionEntity FlowVersion { get; set; } = new();

    /// <summary>
    /// 流程显示名称。
    /// </summary>
    public string FlowName { get; set; } = string.Empty;

    /// <summary>
    /// 该审批单任务投影。
    /// </summary>
    public IReadOnlyList<ApprovalTaskEntity> Tasks { get; set; } = [];

    /// <summary>
    /// 详情所需的操作事件；列表为空。
    /// </summary>
    public IReadOnlyList<OperationEventEntity> Events { get; set; } = [];

}

/// <summary>
/// 同一权限和筛选条件下的总数与分页结果。
/// </summary>
public sealed class CenterPage
{
    /// <summary>
    /// 过滤后总数。
    /// </summary>
    public int Total
    {
        get; set;
    }

    /// <summary>
    /// 本页记录。
    /// </summary>
    public IReadOnlyList<CenterRecord> Items { get; set; } = [];

}
