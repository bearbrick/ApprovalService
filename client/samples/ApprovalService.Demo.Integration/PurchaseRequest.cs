using System.Runtime.Serialization;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 示例业务系统自己的采购申请单。
/// </summary>
[DataContract]
public sealed class PurchaseRequest
{
    /// <summary>业务单号。</summary>
    [DataMember(Name = "businessKey")]
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>本应用已绑定的申请人 ID。</summary>
    [DataMember(Name = "initiator")]
    public string Initiator { get; set; } = string.Empty;

    /// <summary>本应用已绑定的申请部门 ID。</summary>
    [DataMember(Name = "department")]
    public string Department { get; set; } = string.Empty;

    /// <summary>采购事由。</summary>
    [DataMember(Name = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>未税金额。</summary>
    [DataMember(Name = "baseAmount")]
    public decimal BaseAmount { get; set; }

    /// <summary>税额。</summary>
    [DataMember(Name = "taxAmount")]
    public decimal TaxAmount { get; set; }

    /// <summary>是否在预算内；示例预算钩子使用。</summary>
    [DataMember(Name = "withinBudget")]
    public bool WithinBudget { get; set; }

    /// <summary>演示项目编码；取人钩子使用。</summary>
    [DataMember(Name = "projectCode")]
    public string? ProjectCode { get; set; }

    /// <summary>采购明细。</summary>
    [DataMember(Name = "items")]
    public List<PurchaseLine> Items { get; set; } = new();

    /// <summary>审批服务生成的审批单 ID。</summary>
    [DataMember(Name = "approvalInstanceId")]
    public string? ApprovalInstanceId { get; set; }

    /// <summary>最近一次同步的审批状态。</summary>
    [DataMember(Name = "approvalStatus")]
    public string ApprovalStatus { get; set; } = "draft";

    /// <summary>审批人核定的预算。</summary>
    [DataMember(Name = "approvedBudget")]
    public decimal? ApprovedBudget { get; set; }

    /// <summary>自动业务动作生成的采购订单号。</summary>
    [DataMember(Name = "orderNumber")]
    public string? OrderNumber { get; set; }

    /// <summary>
    /// 生成和当前演示采购流程字段一致的审批数据快照。
    /// </summary>
    /// <returns>供 SDK 发起和重提使用的独立数据对象。</returns>
    public Dictionary<string, object?> ToApprovalData()
    {
        var lines = Items.Select(line => new Dictionary<string, object?>
        {
            ["itemName"] = line.ItemName,
            ["quantity"] = line.Quantity,
            ["unitPrice"] = line.UnitPrice,
        }).ToList();
        return new Dictionary<string, object?>
        {
            ["title"] = Title,
            ["requester"] = Initiator,
            ["department"] = Department,
            ["baseAmount"] = BaseAmount,
            ["taxAmount"] = TaxAmount,
            ["items"] = lines,
        };
    }
}

/// <summary>
/// 采购申请单的一条物料明细。
/// </summary>
[DataContract]
public sealed class PurchaseLine
{
    /// <summary>物料名称。</summary>
    [DataMember(Name = "itemName")]
    public string ItemName { get; set; } = string.Empty;

    /// <summary>数量。</summary>
    [DataMember(Name = "quantity")]
    public decimal Quantity { get; set; }

    /// <summary>未税单价。</summary>
    [DataMember(Name = "unitPrice")]
    public decimal UnitPrice { get; set; }
}
