using ApprovalService.Contracts.Flows;

namespace ApprovalService.Engine.Definitions;

/// <summary>
/// 流程画布的编译结果；有发布问题时不生成可运行的执行图。
/// </summary>
public sealed class FlowCompilationResult
{
    /// <summary>
    /// 校验通过后生成的执行图；存在问题时为空。
    /// </summary>
    public FlowExecutionGraph? Graph
    {
        get; set;
    }

    /// <summary>
    /// 全部发布问题，按画布和字段定义中的顺序列出。
    /// </summary>
    public List<FlowValidationIssue> Issues { get; set; } = [];
}

/// <summary>
/// 可定位到画布元素或数据字段的一条发布问题。
/// </summary>
public sealed class FlowValidationIssue
{
    /// <summary>
    /// 出问题的节点、连线或字段标识；整体问题为空。
    /// </summary>
    public string? ElementId
    {
        get; set;
    }

    /// <summary>
    /// 给设计人员看的中文说明。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
