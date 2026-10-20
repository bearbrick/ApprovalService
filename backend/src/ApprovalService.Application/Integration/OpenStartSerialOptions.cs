namespace ApprovalService.Application.Integration;

/// <summary>
/// 首版部署级审批编号格式；流程级可配置界面由管理任务接入。
/// </summary>
public sealed class OpenStartSerialOptions
{
    /// <summary>
    /// 编号前缀。
    /// </summary>
    public string Prefix { get; set; } = "SP";

    /// <summary>
    /// 日期格式：yyyy、yyyyMM 或 yyyyMMdd。
    /// </summary>
    public string DatePattern { get; set; } = "yyyyMM";

    /// <summary>
    /// 流水号位数。
    /// </summary>
    public int Digits { get; set; } = 6;
}
