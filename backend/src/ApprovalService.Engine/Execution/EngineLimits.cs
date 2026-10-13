namespace ApprovalService.Engine.Execution;

/// <summary>
/// 引擎的硬性上限。超过时引擎停下并标记异常交给管理员，而不是无限运行（设计书第 9 节“防死循环”）。
/// </summary>
public static class EngineLimits
{
    /// <summary>
    /// 一张审批单累计推进的最多步数。自由画布允许回路，超过这个数就认为流程画错了。
    /// </summary>
    public const int MaxStepsPerInstance = 200;

    /// <summary>
    /// 子流程最多嵌套几层（审批里调审批，再调审批……）。
    /// </summary>
    public const int MaxSubprocessDepth = 5;
}
