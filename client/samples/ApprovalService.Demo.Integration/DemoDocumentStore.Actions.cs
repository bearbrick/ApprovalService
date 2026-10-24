using System.Runtime.Serialization;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 本机业务库对自动采购动作的幂等处理。
/// </summary>
public sealed partial class DemoDocumentStore
{
    /// <summary>
    /// 根据原动作事件 ID 生成采购订单，演示前几次失败后的持久重试。
    /// </summary>
    /// <param name="eventId">审批服务重复投递时保持不变的动作事件 ID。</param>
    /// <param name="businessKey">采购业务单号。</param>
    /// <param name="failFirstAttempts">演示前几次失败；生产系统应使用真实业务校验。</param>
    /// <returns>本次尝试的成功状态与稳定订单号。</returns>
    internal DemoActionResult RunPurchaseAction(string eventId, string businessKey, int failFirstAttempts)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(businessKey) || failFirstAttempts < 0)
        {
            throw new ArgumentException("业务动作必须提供事件 ID、采购单号和有效的演示失败次数。");
        }

        lock (gate)
        {
            if (!snapshot.Purchases.Any(item => item.BusinessKey == businessKey))
            {
                throw new InvalidOperationException("采购原单不存在，不能生成订单。");
            }

            var record = snapshot.Actions.Find(item => item.EventId == eventId);
            if (record == null)
            {
                record = new DemoActionRecord { EventId = eventId, BusinessKey = businessKey };
                snapshot.Actions.Add(record);
            }
            else if (record.BusinessKey != businessKey)
            {
                throw new InvalidOperationException("同一业务动作事件不能用于另一张采购单。");
            }

            if (record.OrderNumber != null)
            {
                return new DemoActionResult(true, record.OrderNumber, record.Attempts);
            }

            record.Attempts++;
            if (record.Attempts > failFirstAttempts)
            {
                record.OrderNumber = "PO-" + businessKey;
            }

            Persist();
            return new DemoActionResult(record.OrderNumber != null, record.OrderNumber, record.Attempts);
        }
    }
}

/// <summary>
/// 业务动作一次调用的结果。
/// </summary>
internal sealed class DemoActionResult
{
    /// <summary>
    /// 创建调用结果。
    /// </summary>
    /// <param name="success">是否已生成订单。</param>
    /// <param name="orderNumber">成功时的订单号。</param>
    /// <param name="attempts">累计尝试次数。</param>
    internal DemoActionResult(bool success, string? orderNumber, int attempts)
    {
        Success = success;
        OrderNumber = orderNumber;
        Attempts = attempts;
    }

    /// <summary>
    /// 是否成功。
    /// </summary>
    internal bool Success
    {
        get;
    }

    /// <summary>
    /// 稳定订单号。
    /// </summary>
    internal string? OrderNumber
    {
        get;
    }

    /// <summary>
    /// 累计尝试次数。
    /// </summary>
    internal int Attempts
    {
        get;
    }
}

/// <summary>
/// 本机业务文件中持久化的自动动作记录。
/// </summary>
[DataContract]
internal sealed class DemoActionRecord
{
    /// <summary>
    /// 稳定事件 ID。
    /// </summary>
    [DataMember(Name = "eventId")]
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 采购业务单号。
    /// </summary>
    [DataMember(Name = "businessKey")]
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 已发生的尝试次数。
    /// </summary>
    [DataMember(Name = "attempts")]
    public int Attempts
    {
        get; set;
    }

    /// <summary>
    /// 成功后固定的订单号。
    /// </summary>
    [DataMember(Name = "orderNumber")]
    public string? OrderNumber
    {
        get; set;
    }
}
