namespace ApprovalService.Contracts.Flows;

/// <summary>
/// 自由画布节点属性里使用、与执行命令不同的动作编码。
/// </summary>
public static class FlowActionKinds
{
    /// <summary>
    /// 办理节点的设计器“办理提交”；运行时使用 handle 命令。
    /// </summary>
    public const string Submit = "submit";
}
