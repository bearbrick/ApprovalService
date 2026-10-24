using System.Runtime.Serialization;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 示例业务系统自己的费用报销单，审批服务只接收它的快照。
/// </summary>
[DataContract]
public sealed class ExpenseClaim
{
    /// <summary>
    /// 业务单号。
    /// </summary>
    [DataMember(Name = "businessKey")]
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 本应用已绑定的报销人 ID。
    /// </summary>
    [DataMember(Name = "initiator")]
    public string Initiator { get; set; } = string.Empty;

    /// <summary>
    /// 本应用已绑定的发起部门 ID。
    /// </summary>
    [DataMember(Name = "department")]
    public string Department { get; set; } = string.Empty;

    /// <summary>
    /// 报销事由。
    /// </summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 费用类别。
    /// </summary>
    [DataMember(Name = "category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 申请金额。
    /// </summary>
    [DataMember(Name = "claimAmount")]
    public decimal ClaimAmount { get; set; }

    /// <summary>
    /// 本应用预先上传的票据附件 ID。
    /// </summary>
    [DataMember(Name = "receiptFileId")]
    public string ReceiptFileId { get; set; } = string.Empty;

    /// <summary>
    /// 业务系统保存的费用明细。
    /// </summary>
    [DataMember(Name = "lines")]
    public List<ExpenseLine> Lines { get; set; } = new();

    /// <summary>
    /// 审批服务生成的审批单 ID。
    /// </summary>
    [DataMember(Name = "approvalInstanceId")]
    public string? ApprovalInstanceId { get; set; }

    /// <summary>
    /// 最近一次同步的审批状态。
    /// </summary>
    [DataMember(Name = "approvalStatus")]
    public string ApprovalStatus { get; set; } = "draft";

    /// <summary>
    /// 审批人核定的金额，和申请金额分开保存。
    /// </summary>
    [DataMember(Name = "verifiedAmount")]
    public decimal? VerifiedAmount { get; set; }

    /// <summary>
    /// 出纳填写的付款流水号。
    /// </summary>
    [DataMember(Name = "paymentReference")]
    public string? PaymentReference { get; set; }

    /// <summary>
    /// 生成和当前演示报销流程字段一致的审批数据快照。
    /// </summary>
    /// <returns>供 SDK 发起和重提使用的独立数据对象。</returns>
    public Dictionary<string, object?> ToApprovalData()
    {
        var lines = Lines.Select(line => new Dictionary<string, object?>
        {
            ["date"] = line.Date,
            ["description"] = line.Description,
            ["amount"] = line.Amount,
        }).ToList();
        return new Dictionary<string, object?>
        {
            ["title"] = Title,
            ["category"] = Category,
            ["claimAmount"] = ClaimAmount,
            ["receipt"] = ReceiptFileId,
            ["lines"] = lines,
        };
    }
}

/// <summary>
/// 费用报销单的一条原始业务明细。
/// </summary>
[DataContract]
public sealed class ExpenseLine
{
    /// <summary>
    /// 消费日期，使用 yyyy-MM-dd。
    /// </summary>
    [DataMember(Name = "date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// 费用说明。
    /// </summary>
    [DataMember(Name = "description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 本行金额。
    /// </summary>
    [DataMember(Name = "amount")]
    public decimal Amount { get; set; }
}
