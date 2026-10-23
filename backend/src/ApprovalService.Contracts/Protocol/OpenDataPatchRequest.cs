using System.Text.Json;

namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 所属业务系统在审批中提交部分字段值；明细子字段按现有行顺序逐行提交。
/// </summary>
public sealed class OpenDataPatchRequest
{
    /// <summary>
    /// 当前接入应用已绑定的实际操作人员 ID。
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// 只包含本次变动字段的 JSON 对象；明细表可提交同长度的逐行对象数组。
    /// </summary>
    public JsonElement Data
    {
        get; set;
    }
}
