using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Notifications.Calendar;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 按任务实际开始时刻和发布节点设置计算处理期限。
/// </summary>
public sealed class TaskDeadlinePolicy
{
    /// <summary>
    /// 工作日及节假日规则快照。
    /// </summary>
    private readonly WorkCalendarRules calendar;

    /// <summary>
    /// 工作日历所属时区。
    /// </summary>
    private readonly TimeZoneInfo timeZone;

    /// <summary>
    /// 每天计入期限的工作时段。
    /// </summary>
    private readonly WorkCalendarWorkingHours workingHours;

    /// <summary>
    /// 建立一份固定的期限计算配置。
    /// </summary>
    /// <param name="calendar">节假日与调休快照。</param>
    /// <param name="timeZone">工作日历时区。</param>
    /// <param name="workingHours">每天的工作时段。</param>
    public TaskDeadlinePolicy(WorkCalendarRules calendar, TimeZoneInfo timeZone,
        WorkCalendarWorkingHours workingHours)
    {
        this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        this.timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        this.workingHours = workingHours ?? throw new ArgumentNullException(nameof(workingHours));
    }

    /// <summary>
    /// 建立中国时区、周一至周五九点到十八点的默认日历。
    /// </summary>
    /// <returns>默认期限策略。</returns>
    public static TaskDeadlinePolicy CreateDefault() => new(new WorkCalendarRules([]),
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"), WorkCalendarWorkingHours.Default);

    /// <summary>
    /// 按锁定的发布执行图计算一项刚开始处理的任务期限。
    /// </summary>
    /// <param name="graph">审批单锁定的执行图。</param>
    /// <param name="task">实际进入待办的任务。</param>
    /// <param name="startedAt">任务从排队转为待办的 UTC 时间点。</param>
    /// <returns>到期时间；节点未设时限时为空。</returns>
    public DateTimeOffset? Calculate(FlowExecutionGraph graph, EngineTask task, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(task);
        var node = graph.Nodes.SingleOrDefault(item => item.Id == task.NodeId)
            ?? throw new InvalidOperationException("任务对应的发布节点不存在。");
        var dueMinutes = node.Approval?.DueMinutes;
        if (dueMinutes is null)
        {
            return null;
        }

        // 正数已在发布校验确认；旧库若有非法定义，应暴露配置缺陷而不是产生无期限待办。
        if (dueMinutes <= 0)
        {
            throw new InvalidOperationException("发布节点的处理时限必须大于零。");
        }

        return calendar.GetDueAt(startedAt, dueMinutes.Value, timeZone, workingHours);
    }
}
