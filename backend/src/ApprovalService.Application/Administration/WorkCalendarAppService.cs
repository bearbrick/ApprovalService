using System.Globalization;
using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Notifications.Calendar;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 维护工作日历，并为每次运行命令建立独立期限快照。
/// </summary>
/// <param name="repository">在线日历仓储。</param>
/// <param name="defaults">启动时验证的部署默认值。</param>
public sealed class WorkCalendarAppService(IWorkCalendarRepository repository, WorkCalendarDefaults defaults)
    : ITaskDeadlinePolicyProvider
{
    /// <summary>
    /// 读取数据库优先的当前完整日历。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>当前日历与只读部署时区。</returns>
    public async Task<WorkCalendarResponse> ReadAsync(CancellationToken cancellationToken = default)
    {
        var response = defaults.Read();
        var saved = await repository.GetAsync(cancellationToken);
        if (saved is not null)
        {
            response.Revision = saved.Revision;
            response.WorkingStart = saved.WorkingStart;
            response.WorkingEnd = saved.WorkingEnd;
            response.Overrides = JsonSerializer.Deserialize<List<WorkCalendarOverride>>(saved.OverridesJson,
                ApprovalJson.Options) ?? throw new InvalidOperationException("已保存工作日历为空。");
        }
        return response;
    }

    /// <inheritdoc />
    public async Task<TaskDeadlinePolicy> GetAsync(CancellationToken cancellationToken = default) =>
        CreatePolicy(await ReadAsync(cancellationToken));

    /// <summary>
    /// 校验全部输入，并按旧修订原子保存日历及审计。
    /// </summary>
    /// <param name="request">完整日历及审计原因。</param>
    /// <param name="actorId">已验证管理员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本次成功提交的完整日历。</returns>
    public async Task<WorkCalendarResponse> SaveAsync(UpdateWorkCalendarRequest request, string actorId,
        CancellationToken cancellationToken = default)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue ||
            reason.Length is < 2 or > 200 || reason.Any(char.IsControl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "修订无效，或修改原因不是 2–200 个不换行的文字。");
        }
        if (!TimeOnly.TryParseExact(request.WorkingStart, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(request.WorkingEnd, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end) || end <= start)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "工作时段须为 HH:mm，结束必须晚于开始，不支持跨夜。");
        }
        var dates = new HashSet<string>(StringComparer.Ordinal);
        if (request.Overrides is null || request.Overrides.Any(item => item is null ||
            item.IsWorkingDay is null || !DateOnly.TryParseExact(item.Date, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || !dates.Add(item.Date)))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "逐日覆盖须填写有效且不重复的 yyyy-MM-dd 日期和工作状态。");
        }
        var response = defaults.Read();
        response.Revision = request.ExpectedRevision + 1;
        response.WorkingStart = request.WorkingStart;
        response.WorkingEnd = request.WorkingEnd;
        response.Overrides = request.Overrides.OrderBy(item => item.Date, StringComparer.Ordinal)
            .Select(item => new WorkCalendarOverride { Date = item.Date, IsWorkingDay = item.IsWorkingDay }).ToList();
        var value = new WorkCalendarEntity
        {
            Revision = response.Revision,
            WorkingStart = response.WorkingStart,
            WorkingEnd = response.WorkingEnd,
            OverridesJson = JsonSerializer.Serialize(response.Overrides, ApprovalJson.Options),
        };
        if (!await repository.SaveAsync(value, request.ExpectedRevision, actorId, reason, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "工作日历已被其他管理员修改，请刷新后重新核对并保存。");
        }
        return response;
    }

    /// <summary>
    /// 从完整快照同时建立期限和提醒规则。
    /// </summary>
    /// <param name="snapshot">已验证的完整日历。</param>
    /// <returns>不可变日历策略。</returns>
    public static TaskDeadlinePolicy CreatePolicy(WorkCalendarResponse snapshot) => new(
        new WorkCalendarRules(snapshot.Overrides.Select(item => new WorkCalendarDayOverride(
            DateOnly.ParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.IsWorkingDay ?? throw new InvalidOperationException("工作日历缺少工作状态。")))),
        TimeZoneInfo.FindSystemTimeZoneById(snapshot.TimeZoneId),
        new WorkCalendarWorkingHours(TimeOnly.ParseExact(snapshot.WorkingStart, "HH:mm", CultureInfo.InvariantCulture),
            TimeOnly.ParseExact(snapshot.WorkingEnd, "HH:mm", CultureInfo.InvariantCulture)));
}
