using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 业务系统代表已绑定发起人提交完整的新审批数据。
/// </summary>
public sealed class OpenResubmitRequest
{
    /// <summary>
    /// 当前应用已绑定的发起人外部 ID。
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// 完整的业务审批数据；计算字段由服务端重新生成。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }
}
