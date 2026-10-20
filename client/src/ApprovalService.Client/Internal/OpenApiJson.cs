using System.Globalization;

namespace ApprovalService.Client.Internal;

/// <summary>
/// 将 SDK 自带 JSON 解析器的值映射到稳定开放接口模型。
/// </summary>
internal static class OpenApiJson
{
    /// <summary>
    /// 解析可用流程列表。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>流程列表。</returns>
    public static IReadOnlyList<AvailableFlow> Flows(object? value) => Array(value, "flows")
        .Select(item =>
        {
            var row = Object(item, "flow");
            return new AvailableFlow { Code = String(row, "code"), Name = String(row, "name"), Version = Int(row, "version") };
        }).ToArray();

    /// <summary>
    /// 解析流程数据定义和样例。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>流程数据定义。</returns>
    public static FlowSchema Schema(object? value)
    {
        var row = Object(value, "schema");
        return new FlowSchema
        {
            FlowCode = String(row, "flowCode"),
            Version = Int(row, "version"),
            Data = Object(Field(row, "data"), "data"),
            Example = Object(Field(row, "example"), "example"),
        };
    }

    /// <summary>
    /// 解析审批单列表。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>审批单列表。</returns>
    public static IReadOnlyList<ApprovalInstance> Instances(object? value) => Array(value, "instances")
        .Select(Instance).ToArray();

    /// <summary>
    /// 解析一张审批单及其任务和操作记录。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>审批单详情。</returns>
    public static ApprovalInstance Instance(object? value)
    {
        var row = Object(value, "instance");
        return new ApprovalInstance
        {
            Id = String(row, "id"),
            SerialNo = String(row, "serialNo"),
            FlowCode = String(row, "flowCode"),
            BusinessKey = String(row, "businessKey"),
            Title = String(row, "title"),
            InitiatorId = String(row, "initiatorId"),
            InitiatorDepartmentId = OptionalString(row, "initiatorDepartmentId"),
            SubmittedById = OptionalString(row, "submittedById"),
            ClientAppCode = String(row, "clientAppCode"),
            Status = String(row, "status"),
            Data = Object(Field(row, "data"), "data"),
            StartedAt = Date(row, "startedAt"),
            EndedAt = OptionalDate(row, "endedAt"),
            Tasks = Array(Field(row, "tasks"), "tasks").Select(InstanceTask).ToArray(),
            Events = Array(Field(row, "events"), "events").Select(Operation).ToArray(),
        };
    }

    /// <summary>
    /// 解析一张审批单中的人工任务。
    /// </summary>
    /// <param name="value">JSON 任务值。</param>
    /// <returns>任务。</returns>
    private static ApprovalInstanceTask InstanceTask(object? value)
    {
        var row = Object(value, "task");
        return new ApprovalInstanceTask
        {
            Id = String(row, "id"), NodeId = String(row, "nodeId"),
            AssigneeId = String(row, "assigneeId"), Status = String(row, "status"),
            Opinion = OptionalString(row, "opinion"), DueAt = OptionalDate(row, "dueAt"),
            CompletedAt = OptionalDate(row, "completedAt"),
        };
    }

    /// <summary>
    /// 解析审批单操作记录。
    /// </summary>
    /// <param name="value">JSON 事件值。</param>
    /// <returns>操作记录。</returns>
    private static ApprovalOperation Operation(object? value)
    {
        var row = Object(value, "event");
        return new ApprovalOperation
        {
            EventId = String(row, "eventId"), Sequence = Long(row, "sequence"),
            Event = String(row, "event"), Detail = Object(Field(row, "detail"), "detail"),
            OccurredAt = Date(row, "occurredAt"),
        };
    }

    /// <summary>
    /// 解析待办数量。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>待办数量。</returns>
    public static ApprovalTaskCount TaskCount(object? value)
    {
        var row = Object(value, "taskCount");
        return new ApprovalTaskCount { Pending = Int(row, "pending"), Urgent = Int(row, "urgent"), Overdue = Int(row, "overdue") };
    }

    /// <summary>
    /// 解析待办列表。
    /// </summary>
    /// <param name="value">JSON 根值。</param>
    /// <returns>待办列表。</returns>
    public static IReadOnlyList<ApprovalTask> Tasks(object? value) => Array(value, "tasks")
        .Select(item =>
        {
            var row = Object(item, "task");
            return new ApprovalTask
            {
                Id = String(row, "id"), InstanceId = String(row, "instanceId"),
                FlowCode = String(row, "flowCode"), BusinessKey = String(row, "businessKey"),
                Title = String(row, "title"), NodeId = String(row, "nodeId"),
                IsUrgent = Bool(row, "isUrgent"), DueAt = OptionalDate(row, "dueAt"),
                Summary = Object(Field(row, "summary"), "summary"), OpenPath = String(row, "openPath"),
            };
        }).ToArray();

    /// <summary>
    /// 要求 JSON 值为对象。
    /// </summary>
    /// <param name="value">待验证值。</param>
    /// <param name="name">字段名。</param>
    /// <returns>对象。</returns>
    private static Dictionary<string, object?> Object(object? value, string name) =>
        value as Dictionary<string, object?> ?? Invalid<Dictionary<string, object?>>(name);

    /// <summary>
    /// 要求 JSON 值为数组。
    /// </summary>
    /// <param name="value">待验证值。</param>
    /// <param name="name">字段名。</param>
    /// <returns>数组。</returns>
    private static List<object?> Array(object? value, string name) =>
        value as List<object?> ?? Invalid<List<object?>>(name);

    /// <summary>
    /// 读取必填 JSON 字段。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>字段值。</returns>
    private static object? Field(Dictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) ? value : Invalid<object?>(name);

    /// <summary>
    /// 读取必填字符串。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>字符串。</returns>
    private static string String(Dictionary<string, object?> row, string name) =>
        Field(row, name) as string ?? Invalid<string>(name);

    /// <summary>
    /// 读取可空字符串。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>字符串或 null。</returns>
    private static string? OptionalString(Dictionary<string, object?> row, string name) =>
        !row.TryGetValue(name, out var value) || value == null ? null : value as string ?? Invalid<string>(name);

    /// <summary>
    /// 读取整数，拒绝小数和超出范围的值。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>整数。</returns>
    private static int Int(Dictionary<string, object?> row, string name)
    {
        var value = Long(row, name);
        return value >= int.MinValue && value <= int.MaxValue ? (int)value : Invalid<int>(name);
    }

    /// <summary>
    /// 读取长整数，拒绝小数和超出范围的值。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>长整数。</returns>
    private static long Long(Dictionary<string, object?> row, string name)
    {
        var value = Field(row, name);
        return value is decimal number && number == decimal.Truncate(number) && number >= long.MinValue && number <= long.MaxValue
            ? (long)number : Invalid<long>(name);
    }

    /// <summary>
    /// 读取布尔值。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>布尔值。</returns>
    private static bool Bool(Dictionary<string, object?> row, string name) =>
        Field(row, name) is bool value ? value : Invalid<bool>(name);

    /// <summary>
    /// 读取带时区的时间。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>时间。</returns>
    private static DateTimeOffset Date(Dictionary<string, object?> row, string name) =>
        DateTimeOffset.TryParse(String(row, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value : Invalid<DateTimeOffset>(name);

    /// <summary>
    /// 读取可空的带时区时间。
    /// </summary>
    /// <param name="row">对象。</param>
    /// <param name="name">字段名。</param>
    /// <returns>时间或 null。</returns>
    private static DateTimeOffset? OptionalDate(Dictionary<string, object?> row, string name) =>
        !row.TryGetValue(name, out var value) || value == null ? null : Date(row, name);

    /// <summary>
    /// 对协议结构错误统一报可诊断的服务异常。
    /// </summary>
    /// <typeparam name="T">调用方期望的类型。</typeparam>
    /// <param name="name">错误字段。</param>
    /// <returns>永远不返回。</returns>
    private static T Invalid<T>(string name) => throw new ApprovalServiceException(ApprovalErrorCodes.InternalError,
        "审批流服务响应字段 " + name + " 与接口约定不符。");
}
