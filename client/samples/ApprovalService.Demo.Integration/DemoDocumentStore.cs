using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using ApprovalService.Client;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 把演示业务单据保存在审批服务数据库之外的本机 JSON 文件中。
/// </summary>
public sealed class DemoDocumentStore
{
    /// <summary>本机业务文件的绝对路径。</summary>
    private readonly string path;

    /// <summary>保证同一进程内读取、去重与落盘为一个操作。</summary>
    private readonly object gate = new();

    /// <summary>当前业务单据和已处理的回调事件。</summary>
    private DemoStoreSnapshot snapshot;

    /// <summary>
    /// 打开本机业务数据文件；不存在时创建空业务库。
    /// </summary>
    /// <param name="filePath">只存示例业务数据的文件路径。</param>
    public DemoDocumentStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("必须指定示例业务数据文件。", nameof(filePath));
        }

        path = Path.GetFullPath(filePath);
        snapshot = File.Exists(path) ? ReadSnapshot(File.ReadAllBytes(path)) : new DemoStoreSnapshot();
    }

    /// <summary>
    /// 保存一张费用报销业务单；同单号再次保存表示业务系统修改原单。
    /// </summary>
    /// <param name="claim">报销单。</param>
    public void SaveExpense(ExpenseClaim claim)
    {
        if (claim == null)
        {
            throw new ArgumentNullException(nameof(claim));
        }
        RequireKey(claim.BusinessKey);
        lock (gate)
        {
            var index = snapshot.Expenses.FindIndex(item => item.BusinessKey == claim.BusinessKey);
            if (index < 0)
            {
                snapshot.Expenses.Add(Clone(claim));
            }
            else
            {
                snapshot.Expenses[index] = Clone(claim);
            }

            Persist();
        }
    }

    /// <summary>
    /// 保存一张采购业务单。
    /// </summary>
    /// <param name="purchase">采购单。</param>
    public void SavePurchase(PurchaseRequest purchase)
    {
        if (purchase == null)
        {
            throw new ArgumentNullException(nameof(purchase));
        }
        RequireKey(purchase.BusinessKey);
        lock (gate)
        {
            var index = snapshot.Purchases.FindIndex(item => item.BusinessKey == purchase.BusinessKey);
            if (index < 0)
            {
                snapshot.Purchases.Add(Clone(purchase));
            }
            else
            {
                snapshot.Purchases[index] = Clone(purchase);
            }

            Persist();
        }
    }

    /// <summary>
    /// 按业务单号读取费用报销原单，返回副本供界面编辑。
    /// </summary>
    /// <param name="businessKey">业务单号。</param>
    /// <returns>存在时返回报销单副本。</returns>
    public ExpenseClaim? FindExpense(string businessKey)
    {
        lock (gate)
        {
            var item = snapshot.Expenses.Find(value => value.BusinessKey == businessKey);
            return item == null ? null : Clone(item);
        }
    }

    /// <summary>
    /// 按业务单号读取采购原单，返回副本供界面编辑。
    /// </summary>
    /// <param name="businessKey">业务单号。</param>
    /// <returns>存在时返回采购单副本。</returns>
    public PurchaseRequest? FindPurchase(string businessKey)
    {
        lock (gate)
        {
            var item = snapshot.Purchases.Find(value => value.BusinessKey == businessKey);
            return item == null ? null : Clone(item);
        }
    }

    /// <summary>
    /// 记录 SDK 发起审批返回的实例 ID，业务单号仍属于本业务系统。
    /// </summary>
    /// <param name="instance">审批服务返回的实例。</param>
    public void RecordStart(ApprovalInstance instance) => UpdateFromQuery(instance);

    /// <summary>
    /// 无后端的客户端打开单据时查询并同步审批状态。
    /// </summary>
    /// <param name="instance">审批服务查询的可信实例。</param>
    public void UpdateFromQuery(ApprovalInstance instance)
    {
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }
        lock (gate)
        {
            if (instance.FlowCode == "EXPENSE")
            {
                var item = snapshot.Expenses.Find(value => value.BusinessKey == instance.BusinessKey)
                    ?? throw new InvalidOperationException("示例业务库中没有这张费用报销单。");
                CheckInstance(item.ApprovalInstanceId, instance.Id);
                item.ApprovalInstanceId = instance.Id;
                item.ApprovalStatus = instance.Status;
                ApplyExpenseValues(item, instance.ApproverFieldValues);
            }
            else if (instance.FlowCode == "PURCHASE_REQUEST")
            {
                var item = snapshot.Purchases.Find(value => value.BusinessKey == instance.BusinessKey)
                    ?? throw new InvalidOperationException("示例业务库中没有这张采购申请单。");
                CheckInstance(item.ApprovalInstanceId, instance.Id);
                item.ApprovalInstanceId = instance.Id;
                item.ApprovalStatus = instance.Status;
                ApplyPurchaseValues(item, instance.ApproverFieldValues);
            }
            else
            {
                throw new InvalidOperationException("示例业务系统只保存报销与采购单据。");
            }

            Persist();
        }
    }

    /// <summary>
    /// 按稳定事件 ID 去重后将已验签回调同步到原业务单据。
    /// </summary>
    /// <param name="notification">已经过 SDK 原文验签的回调。</param>
    /// <returns>首次处理返回 true；重发相同事件返回 false。</returns>
    public bool ApplyCallback(ApprovalCallback notification)
    {
        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }
        if (string.IsNullOrWhiteSpace(notification.EventId))
        {
            throw new ArgumentException("回调缺少事件 ID。", nameof(notification));
        }

        lock (gate)
        {
            if (snapshot.ProcessedEventIds.Contains(notification.EventId, StringComparer.Ordinal))
            {
                return false;
            }

            if (notification.FlowCode == "EXPENSE")
            {
                var item = snapshot.Expenses.Find(value => value.BusinessKey == notification.BusinessKey)
                    ?? throw new InvalidOperationException("示例业务库中没有这张费用报销单。");
                CheckInstance(item.ApprovalInstanceId, notification.InstanceId);
                item.ApprovalInstanceId = notification.InstanceId;
                item.ApprovalStatus = notification.Status;
                ApplyExpenseValues(item, notification.FieldValues);
            }
            else if (notification.FlowCode == "PURCHASE_REQUEST")
            {
                var item = snapshot.Purchases.Find(value => value.BusinessKey == notification.BusinessKey)
                    ?? throw new InvalidOperationException("示例业务库中没有这张采购申请单。");
                CheckInstance(item.ApprovalInstanceId, notification.InstanceId);
                item.ApprovalInstanceId = notification.InstanceId;
                item.ApprovalStatus = notification.Status;
                ApplyPurchaseValues(item, notification.FieldValues);
                ApplyOrderResult(item, notification.ActionResults);
            }
            else
            {
                throw new InvalidOperationException("示例业务系统只接收报销与采购流程回调。");
            }

            snapshot.ProcessedEventIds.Add(notification.EventId);
            Persist();
            return true;
        }
    }

    /// <summary>检查业务单号非空。</summary>
    /// <param name="businessKey">待检查单号。</param>
    private static void RequireKey(string businessKey)
    {
        if (string.IsNullOrWhiteSpace(businessKey))
        {
            throw new ArgumentException("业务单号不能为空。", nameof(businessKey));
        }
    }

    /// <summary>防止另一审批单的回调覆盖已关联业务单据。</summary>
    /// <param name="current">已有实例 ID。</param>
    /// <param name="incoming">收到的实例 ID。</param>
    private static void CheckInstance(string? current, string incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming) || (current != null && current != incoming))
        {
            throw new InvalidOperationException("审批单 ID 与业务原单已关联的实例不一致。");
        }
    }

    /// <summary>将报销审批人字段保存在独立列。</summary>
    /// <param name="item">业务原单。</param>
    /// <param name="values">审批人填写值。</param>
    private static void ApplyExpenseValues(ExpenseClaim item, Dictionary<string, object?>? values)
    {
        if (values == null)
        {
            return;
        }

        if (values.TryGetValue("verifiedAmount", out var amount) && amount != null)
        {
            item.VerifiedAmount = Convert.ToDecimal(amount, CultureInfo.InvariantCulture);
        }

        if (values.TryGetValue("paymentReference", out var reference))
        {
            item.PaymentReference = reference as string;
        }
    }

    /// <summary>将采购审批人字段保存在独立列。</summary>
    /// <param name="item">业务原单。</param>
    /// <param name="values">审批人填写值。</param>
    private static void ApplyPurchaseValues(PurchaseRequest item, Dictionary<string, object?>? values)
    {
        if (values == null)
        {
            return;
        }

        if (values.TryGetValue("approvedBudget", out var amount) && amount != null)
        {
            item.ApprovedBudget = Convert.ToDecimal(amount, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>从按动作事件 ID 索引的结果里读取订单号。</summary>
    /// <param name="item">采购业务单。</param>
    /// <param name="results">回调携带的独立动作结果。</param>
    private static void ApplyOrderResult(PurchaseRequest item, Dictionary<string, object?>? results)
    {
        if (results == null)
        {
            return;
        }

        foreach (var result in results.Values)
        {
            if (result is Dictionary<string, object?> values && values.TryGetValue("orderNo", out var orderNo) && orderNo is string text)
            {
                item.OrderNumber = text;
            }
        }
    }

    /// <summary>将内存快照写入临时文件后原子替换业务文件。</summary>
    private void Persist()
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("业务文件目录无效。");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, Serialize(snapshot));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>读取已保存的数据，不把损坏文件当成新空库。</summary>
    /// <param name="bytes">文件字节。</param>
    /// <returns>已验证快照。</returns>
    private static DemoStoreSnapshot ReadSnapshot(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var value = new DataContractJsonSerializer(typeof(DemoStoreSnapshot)).ReadObject(stream) as DemoStoreSnapshot;
            return value ?? throw new SerializationException("业务数据文件为空。");
        }
        catch (SerializationException ex)
        {
            throw new InvalidDataException("示例业务数据文件已损坏，未覆盖原文件。", ex);
        }
    }

    /// <summary>序列化一个文档或整个业务库。</summary>
    /// <typeparam name="T">数据类型。</typeparam>
    /// <param name="value">待序列化对象。</param>
    /// <returns>JSON 原始字节。</returns>
    private static byte[] Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        return stream.ToArray();
    }

    /// <summary>业务读取时返回副本，避免调用方绕过落盘。</summary>
    /// <typeparam name="T">数据类型。</typeparam>
    /// <param name="value">当前值。</param>
    /// <returns>独立副本。</returns>
    private static T Clone<T>(T value)
    {
        using var stream = new MemoryStream(Serialize(value));
        return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(stream)
            ?? throw new SerializationException("示例业务单据复制失败。"));
    }
}

/// <summary>
/// 本机业务库的存储结构。
/// </summary>
[DataContract]
internal sealed class DemoStoreSnapshot
{
    /// <summary>费用报销单。</summary>
    [DataMember(Name = "expenses")]
    public List<ExpenseClaim> Expenses { get; set; } = new();

    /// <summary>采购申请单。</summary>
    [DataMember(Name = "purchases")]
    public List<PurchaseRequest> Purchases { get; set; } = new();

    /// <summary>已经应用到原单的回调事件 ID。</summary>
    [DataMember(Name = "processedEventIds")]
    public List<string> ProcessedEventIds { get; set; } = new();
}
